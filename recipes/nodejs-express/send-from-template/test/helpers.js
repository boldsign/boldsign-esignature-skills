import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createStore } from '../src/store.js';

export const SECRET = 'test-webhook-secret';
export function tempStore() { return createStore(path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'bs-recipe-')), 'documents.json')); }
export function testConfig(over = {}) {
  return { apiKey: 'test', webhookSecret: SECRET, region: 'US', baseUrl: 'https://api.boldsign.com', port: 0, appBaseUrl: 'http://localhost', signer: { name: 'Test Signer', email: 'signer@boldsign.dev' }, storeFile: '', templateId: 'tpl-1', templateRoles: [{ name: 'Employee', index: 1 }], ...over };
}
export function signedHeaders(body, secret = SECRET, t = Math.floor(Date.now() / 1000)) {
  const s0 = crypto.createHmac('sha256', secret).update(`${t}.${body}`).digest('hex');
  return { 'content-type': 'application/json', 'x-boldsign-event': 'Completed', 'x-boldsign-signature': `t=${t}, s0=${s0}` };
}
export function event(documentId, eventType, { id = crypto.randomUUID(), created = Math.floor(Date.now() / 1000) } = {}) {
  return JSON.stringify({ event: { id, created, eventType, environment: 'Test' }, data: { object: 'document', documentId, status: eventType } });
}
export async function listen(app) {
  const server = app.listen(0);
  await new Promise((r) => server.once('listening', r));
  return { url: `http://localhost:${server.address().port}`, close: () => server.close() };
}
export const quiet = { info() {}, warn() {}, error() {} };
