import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DocumentApi } from 'boldsign';
import { createApp } from '../src/app.js';
import { createBoldSign, retrySoon } from '../src/boldsign.js';
import { samplePdf, SIGN_TAG, DATE_TAG } from '../src/sample-pdf.js';
import { listen, quiet, tempStore, testConfig } from './helpers.js';

test('sample PDF is a PDF with the signature and date text tags', () => {
  const pdf = samplePdf().toString('latin1');
  assert.ok(pdf.startsWith('%PDF-'));
  assert.ok(pdf.includes(SIGN_TAG) && pdf.includes(DATE_TAG));
});

test('createDocument uses text tags and disables BoldSign emails (embedded signing)', async (t) => {
  let captured;
  t.mock.method(DocumentApi.prototype, 'sendDocument', async (req) => { captured = req; return { documentId: 'doc-42' }; });
  const id = await createBoldSign(testConfig()).createDocument({ title: 'T', pdf: samplePdf(), signer: { name: 'N', email: 'n@boldsign.dev' } });
  assert.equal(id, 'doc-42');
  assert.equal(captured.useTextTags, true);
  assert.equal(captured.disableEmails, true);
  assert.ok(!captured.signers[0].formFields?.length);
});

test('signLink asks for a redirect back to the app and retries the first 403', async (t) => {
  let n = 0, args;
  t.mock.method(DocumentApi.prototype, 'getEmbeddedSignLink', async (...a) => { args = a; if (++n < 2) throw { statusCode: 403 }; return { signLink: 'https://app.boldsign.com/document/sign/?documentId=x' }; });
  const link = await createBoldSign(testConfig()).signLink('doc-1', 'n@boldsign.dev', 'http://localhost/signing/complete?documentId=doc-1');
  assert.match(link, /^https:\/\/app\.boldsign\.com\//);
  assert.equal(args[0], 'doc-1');
  assert.equal(args[1], 'n@boldsign.dev');
  assert.equal(args[5], 'http://localhost/signing/complete?documentId=doc-1');
});

test('create, signing page in an iframe, only for the signed-in user, and the redirect page', async () => {
  const store = tempStore();
  const boldsign = { createDocument: async () => 'doc-7', signLink: async () => 'https://app.boldsign.com/document/sign/?documentId=doc-7&t=1' };
  const { url, close } = await listen(createApp({ cfg: testConfig(), boldsign, store, log: quiet }));
  try {
    const res = await fetch(`${url}/documents`, { method: 'POST', headers: { 'content-type': 'application/json', accept: 'application/json' }, body: '{}' });
    assert.equal(res.status, 201);
    const body = await res.json();
    assert.equal(body.signingPage, '/documents/doc-7/sign');
    const sign = await fetch(`${url}${body.signingPage}`);
    const html = await sign.text();
    assert.match(html, /<iframe src="https:\/\/app\.boldsign\.com\/document\/sign\/\?documentId=doc-7&amp;t=1"/);
    assert.match(sign.headers.get('cache-control'), /no-store/);
    store.save({ ...store.get('doc-7'), signer: { email: 'someone-else@boldsign.dev' } });
    assert.equal((await fetch(`${url}${body.signingPage}`)).status, 404);
    assert.match(await (await fetch(`${url}/signing/complete?documentId=doc-7`)).text(), /window\.top\.location/);
  } finally { close(); }
});

test('retrySoon does not retry a 401', async () => {
  let m = 0;
  await assert.rejects(retrySoon(async () => { m++; throw { statusCode: 401 }; }, { waitMs: 1 }));
  assert.equal(m, 1);
});
