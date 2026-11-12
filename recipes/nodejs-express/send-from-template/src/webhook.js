import crypto from 'node:crypto';

export const TOLERANCE_SECONDS = 300;

// X-BoldSign-Signature: t=<unix seconds>, s0=<hex>[, s1=<hex>]
// Signature: hex(HMAC_SHA256(webhook secret, t + "." + raw body)); s1 is present while an old secret is still valid.
// https://developers.boldsign.com/webhooks/verify-webhook-events/
export function verifyBoldSignSignature(rawBody, header, secret, nowSeconds = Math.floor(Date.now() / 1000)) {
  if (!header || !secret || !Buffer.isBuffer(rawBody)) return false;
  let t = '';
  const signatures = [];
  for (const part of header.split(',')) {
    const i = part.indexOf('=');
    if (i < 0) return false;
    const key = part.slice(0, i).trim();
    const value = part.slice(i + 1).trim();
    if (key === 't') t = value;
    else if (/^s\d+$/.test(key)) signatures.push(value.toLowerCase());
  }
  if (!/^\d+$/.test(t) || signatures.length === 0) return false;
  if (Math.abs(nowSeconds - Number(t)) > TOLERANCE_SECONDS) return false;
  const expected = crypto.createHmac('sha256', secret).update(Buffer.concat([Buffer.from(`${t}.`), rawBody])).digest();
  return signatures.some((sig) => /^[0-9a-f]{64}$/.test(sig) && crypto.timingSafeEqual(Buffer.from(sig, 'hex'), expected));
}

const STATUS = {
  Sent: 'Sent', Viewed: 'Viewed', Signed: 'Signed', Completed: 'Completed', Declined: 'Declined',
  Expired: 'Expired', Revoked: 'Revoked', SendFailed: 'SendFailed', DeliveryFailed: 'DeliveryFailed',
};
export const FINAL = new Set(['Completed', 'Declined', 'Expired', 'Revoked', 'SendFailed']);

// Applies a verified event to the stored document. Returns what happened, for logging and tests.
export function applyEvent(store, payload) {
  const data = payload?.data;
  const event = payload?.event || {};
  if (!data || data.object !== 'document') return 'ignored';
  const record = store.get(data.documentId);
  if (!record) return 'unknown-document';
  record.events ||= [];
  if (event.id && record.events.some((e) => e.id === event.id)) return 'duplicate';
  if (record.lastEventCreated && event.created && event.created < record.lastEventCreated) return 'stale';
  record.events.push({ id: event.id, type: event.eventType, created: event.created });
  record.lastEventCreated = event.created || record.lastEventCreated;
  const next = STATUS[event.eventType];
  if (next && !FINAL.has(record.status)) record.status = next;
  if (event.eventType === 'SendFailed') record.error = data.errorMessage || 'Send failed';
  store.save(record);
  return 'applied';
}
