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
  assert.match(SIGN_TAG, /^\{\{sign\|1\|\*/);
});

test('sendDocument builds a text-tag request (useTextTags, no form fields) for one signer', async (t) => {
  let captured;
  t.mock.method(DocumentApi.prototype, 'sendDocument', async (req) => { captured = req; return { documentId: 'doc-42' }; });
  const id = await createBoldSign(testConfig()).sendDocument({ title: 'T', pdf: samplePdf(), signer: { name: 'N', email: 'n@boldsign.dev' } });
  assert.equal(id, 'doc-42');
  assert.equal(captured.useTextTags, true);
  assert.equal(captured.signers.length, 1);
  assert.equal(captured.signers[0].emailAddress, 'n@boldsign.dev');
  assert.ok(!captured.signers[0].formFields?.length);
  assert.equal(captured.files[0].options.contentType, 'application/pdf');
});

test('POST /documents sends and stores the document as Queued', async () => {
  const store = tempStore();
  const calls = [];
  const boldsign = { sendDocument: async (a) => { calls.push(a); return 'doc-7'; } };
  const { url, close } = await listen(createApp({ cfg: testConfig(), boldsign, store, log: quiet }));
  try {
    const res = await fetch(`${url}/documents`, { method: 'POST', headers: { 'content-type': 'application/json', accept: 'application/json' }, body: '{}' });
    assert.equal(res.status, 201);
    assert.equal((await res.json()).documentId, 'doc-7');
    assert.equal(calls[0].signer.email, 'signer@boldsign.dev');
    assert.equal(store.get('doc-7').status, 'Queued');
  } finally { close(); }
});

test('retrySoon retries 403 and 400 briefly, but not other errors', async () => {
  let n = 0;
  assert.equal(await retrySoon(async () => { if (++n < 3) throw { statusCode: 403 }; return 'ok'; }, { waitMs: 1 }), 'ok');
  let m = 0;
  await assert.rejects(retrySoon(async () => { m++; throw { statusCode: 401 }; }, { waitMs: 1 }));
  assert.equal(m, 1);
});
