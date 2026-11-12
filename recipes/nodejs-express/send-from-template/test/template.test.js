import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TemplateApi } from 'boldsign';
import { createApp } from '../src/app.js';
import { createBoldSign, retrySoon } from '../src/boldsign.js';
import { parseRoles } from '../src/config.js';
import { compareRoles, mapRoles } from '../src/roles.js';
import { listen, quiet, tempStore, testConfig } from './helpers.js';

test('parseRoles reads Name:index pairs', () => {
  assert.deepEqual(parseRoles('Employee:1, Manager:2'), [{ name: 'Employee', index: 1 }, { name: 'Manager', index: 2 }]);
  assert.throws(() => parseRoles('Employee'));
});

test('mapRoles maps each template role to a signer from the app', () => {
  assert.deepEqual(mapRoles([{ name: 'Employee', index: 1 }], { user: { name: 'Ann', email: 'ann@boldsign.dev' } }),
    [{ roleIndex: 1, signerRole: 'Employee', signerName: 'Ann', signerEmail: 'ann@boldsign.dev' }]);
  assert.throws(() => mapRoles([{ name: 'Employee', index: 1 }], { user: { name: 'No email' } }), /Employee/);
});

test('compareRoles finds roles that do not match the template', () => {
  assert.deepEqual(compareRoles([{ name: 'Employee', index: 1 }], [{ name: 'employee', index: 1 }]), { missingInTemplate: [], notConfigured: [] });
  const d = compareRoles([{ name: 'Employee', index: 1 }], [{ name: 'Manager', index: 1 }]);
  assert.equal(d.missingInTemplate.length, 1);
  assert.equal(d.notConfigured.length, 1);
});

test('sendFromTemplate sends the template ID and Role objects, and retries the first 400', async (t) => {
  let n = 0, args;
  t.mock.method(TemplateApi.prototype, 'sendUsingTemplate', async (...a) => { args = a; if (++n < 2) throw { statusCode: 400 }; return { documentId: 'doc-9' }; });
  const roles = mapRoles([{ name: 'Employee', index: 1 }], { user: { name: 'Ann', email: 'ann@boldsign.dev' } });
  const id = await createBoldSign(testConfig()).sendFromTemplate({ templateId: 'tpl-1', title: 'Offer', roles });
  assert.equal(id, 'doc-9');
  assert.equal(n, 2);
  assert.equal(args[0], 'tpl-1');
  assert.equal(args[1].roles[0].roleIndex, 1);
  assert.equal(args[1].roles[0].signerEmail, 'ann@boldsign.dev');
  assert.equal(args[1].roles[0].signerRole, 'Employee');
});

test('POST /documents sends from the template and stores the document as Queued', async () => {
  const store = tempStore();
  const calls = [];
  const boldsign = { sendFromTemplate: async (a) => { calls.push(a); return 'doc-5'; } };
  const { url, close } = await listen(createApp({ cfg: testConfig(), boldsign, store, log: quiet }));
  try {
    const res = await fetch(`${url}/documents`, { method: 'POST', headers: { 'content-type': 'application/json', accept: 'application/json' }, body: '{}' });
    assert.equal(res.status, 201);
    assert.equal(calls[0].templateId, 'tpl-1');
    assert.equal(calls[0].roles[0].signerEmail, 'signer@boldsign.dev');
    assert.equal(store.get('doc-5').status, 'Queued');
  } finally { close(); }
});

test('retrySoon does not retry a 401', async () => {
  let m = 0;
  await assert.rejects(retrySoon(async () => { m++; throw { statusCode: 401 }; }, { waitMs: 1 }));
  assert.equal(m, 1);
});
