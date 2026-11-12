import { test } from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { verifyBoldSignSignature, applyEvent } from '../src/webhook.js';
import { createApp } from '../src/app.js';
import { SECRET, event, listen, quiet, signedHeaders, tempStore, testConfig } from './helpers.js';

const body = Buffer.from(event('doc-1', 'Completed'));
const now = Math.floor(Date.now() / 1000);
const sig = (key, t = now, b = body) => crypto.createHmac('sha256', key).update(`${t}.${b}`).digest('hex');

test('verifier accepts s0, and s1 while a secret is rolled', () => {
  assert.equal(verifyBoldSignSignature(body, `t=${now}, s0=${sig(SECRET)}`, SECRET), true);
  assert.equal(verifyBoldSignSignature(body, `t=${now}, s0=${sig('old')}, s1=${sig(SECRET)}`, SECRET), true);
});

test('verifier rejects wrong secret, body-only HMAC, stale or malformed headers, tampered body', () => {
  const bodyOnly = crypto.createHmac('sha256', SECRET).update(body).digest('hex');
  for (const header of [`t=${now}, s0=${sig('wrong')}`, `t=${now}, s0=${bodyOnly}`, `t=${now - 600}, s0=${sig(SECRET, now - 600)}`, `t=${now}, s0=abc`, `s0=${sig(SECRET)}`, '', 'garbage']) {
    assert.equal(verifyBoldSignSignature(body, header, SECRET), false, header);
  }
  assert.equal(verifyBoldSignSignature(Buffer.from(body.toString().replace('doc-1', 'doc-2')), `t=${now}, s0=${sig(SECRET)}`, SECRET), false);
});

test('applyEvent: updates status, ignores duplicates, stale events and anything after a final status', () => {
  const store = tempStore();
  store.save({ documentId: 'doc-1', status: 'Queued', events: [] });
  const sent = JSON.parse(event('doc-1', 'Sent', { id: 'e1', created: 100 }));
  assert.equal(applyEvent(store, sent), 'applied');
  assert.equal(store.get('doc-1').status, 'Sent');
  assert.equal(applyEvent(store, sent), 'duplicate');
  assert.equal(applyEvent(store, JSON.parse(event('doc-1', 'Completed', { id: 'e2', created: 200 }))), 'applied');
  assert.equal(applyEvent(store, JSON.parse(event('doc-1', 'Viewed', { id: 'e3', created: 150 }))), 'stale');
  assert.equal(applyEvent(store, JSON.parse(event('doc-1', 'Revoked', { id: 'e4', created: 300 }))), 'applied');
  assert.equal(store.get('doc-1').status, 'Completed');
  assert.equal(applyEvent(store, JSON.parse(event('other', 'Completed'))), 'unknown-document');
});

test('webhook route: verification ping 200, forged 401, signed Completed updates the status page', async () => {
  const store = tempStore();
  store.save({ documentId: 'doc-1', title: 'T', signer: { email: 'signer@boldsign.dev' }, status: 'Queued', events: [], createdAt: '2026-01-01' });
  const { url, close } = await listen(createApp({ cfg: testConfig(), boldsign: {}, store, log: quiet }));
  try {
    const ping = await fetch(`${url}/webhooks/boldsign`, { method: 'POST', headers: { 'x-boldsign-event': 'Verification' }, body: '{}' });
    assert.equal(ping.status, 200);
    const raw = event('doc-1', 'Completed');
    const forged = await fetch(`${url}/webhooks/boldsign`, { method: 'POST', headers: signedHeaders(raw, 'not-the-secret'), body: raw });
    assert.equal(forged.status, 401);
    assert.equal(store.get('doc-1').status, 'Queued');
    const ok = await fetch(`${url}/webhooks/boldsign`, { method: 'POST', headers: signedHeaders(raw), body: raw });
    assert.equal(ok.status, 200);
    assert.equal(store.get('doc-1').status, 'Completed');
    assert.match(await (await fetch(`${url}/documents`)).text(), /<b>Completed<\/b>/);
  } finally { close(); }
});
