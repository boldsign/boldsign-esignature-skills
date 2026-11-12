import express from 'express';
import { applyEvent, verifyBoldSignSignature } from './webhook.js';
import { mapRoles } from './roles.js';
import { errorSummary } from './boldsign.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

export function createApp({ cfg, boldsign, store, log = console }) {
  const app = express();

  // Webhook first, with the raw body: the signature is computed over the exact bytes BoldSign sent.
  app.post('/webhooks/boldsign', express.raw({ type: '*/*', limit: '1mb' }), (req, res) => {
    if (req.get('X-BoldSign-Event') === 'Verification') return res.sendStatus(200); // BoldSign's URL check
    if (!cfg.webhookSecret) return res.status(500).send('Webhook secret not configured');
    if (!verifyBoldSignSignature(req.body, req.get('X-BoldSign-Signature'), cfg.webhookSecret)) {
      log.warn?.('webhook: signature check failed');
      return res.status(401).send('Invalid signature');
    }
    let payload;
    try { payload = JSON.parse(req.body.toString('utf8')); } catch { return res.status(400).send('Invalid JSON'); }
    const result = applyEvent(store, payload);
    log.info?.(`webhook: ${payload?.event?.eventType} ${payload?.data?.documentId ?? ''} ${result}`);
    res.sendStatus(200); // also for unknown documents, so BoldSign does not retry them
  });

  app.use(express.json());
  app.use(express.urlencoded({ extended: false }));

  // Send from the template, mapping each template role to a signer from the app.
  app.post('/documents', async (req, res) => {
    if (!cfg.templateId) return res.status(400).json({ error: 'Set BOLDSIGN_TEMPLATE_ID' });
    const user = { name: req.body?.signerName || cfg.signer.name, email: req.body?.signerEmail || cfg.signer.email };
    let roles;
    try { roles = mapRoles(cfg.templateRoles, { user }); } catch (err) { return res.status(400).json({ error: err.message }); }
    const title = req.body?.title || 'Offer letter';
    try {
      const documentId = await boldsign.sendFromTemplate({ templateId: cfg.templateId, title, roles });
      const signer = { name: roles[0].signerName, email: roles[0].signerEmail };
      const record = store.save({ documentId, title, signer, roles, status: 'Queued', createdAt: new Date().toISOString(), events: [] });
      if (req.accepts(['json', 'html']) === 'html') return res.redirect(303, '/documents');
      res.status(201).json(record);
    } catch (err) {
      const e = errorSummary(err);
      log.error?.('send from template failed', e);
      res.status(502).json({ error: 'BoldSign send from template failed', ...e });
    }
  });

  // Status page.
  app.get('/documents', (req, res) => {
    const rows = store.all().map((d) => `<tr><td>${esc(d.title)}</td><td>${esc(d.signer?.email)}</td><td><b>${esc(d.status)}</b>${d.error ? ` (${esc(d.error)})` : ''}</td><td><code>${esc(d.documentId)}</code></td><td>${d.status === 'Completed' ? '' : `<a href="/documents/${encodeURIComponent(d.documentId)}/sign">Sign now</a> <form method="post" action="/documents/${encodeURIComponent(d.documentId)}/refresh" style="display:inline"><button>Refresh</button></form>`}</td></tr>`).join('');
    res.type('html').send(`<!doctype html><meta charset="utf-8"><title>Documents</title><style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}</style>
<h1>Documents</h1><form method="post" action="/documents"><button>Send the template to ${esc(cfg.signer.email || 'SIGNER_EMAIL')}</button></form>
<table><tr><th>Title</th><th>Signer</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>${rows || '<tr><td colspan="5">No documents yet</td></tr>'}</table>`);
  });
  app.get('/documents.json', (req, res) => res.json(store.all()));

  // Opens BoldSign's signing page for the signer (alternative to the email).
  app.get('/documents/:id/sign', async (req, res) => {
    const d = store.get(req.params.id);
    if (!d) return res.sendStatus(404);
    try { res.redirect(await boldsign.signLink(d.documentId, d.signer.email, `${cfg.appBaseUrl}/documents`)); }
    catch (err) { res.status(502).json({ error: 'Could not get a signing link', ...errorSummary(err) }); }
  });

  // Fallback while no webhook can reach this app: one status read from BoldSign.
  app.post('/documents/:id/refresh', async (req, res) => {
    const d = store.get(req.params.id);
    if (!d) return res.sendStatus(404);
    try {
      const p = await boldsign.properties(d.documentId);
      if (p?.status) store.save({ ...d, status: p.status === 'InProgress' && d.status !== 'Queued' ? d.status : p.status });
      res.redirect(303, '/documents');
    } catch (err) { res.status(502).json({ error: 'Could not read the status', ...errorSummary(err) }); }
  });

  app.get('/', (req, res) => res.redirect('/documents'));
  return app;
}
