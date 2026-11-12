import express from 'express';
import { applyEvent, verifyBoldSignSignature } from './webhook.js';
import { samplePdf } from './sample-pdf.js';
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

  // Send the sample PDF for signature.
  app.post('/documents', async (req, res) => {
    const signer = { name: req.body?.signerName || cfg.signer.name, email: req.body?.signerEmail || cfg.signer.email };
    if (!signer.email) return res.status(400).json({ error: 'Set SIGNER_EMAIL or post signerEmail' });
    const title = req.body?.title || 'Policy acknowledgement';
    try {
      const documentId = await boldsign.sendDocument({ title, pdf: samplePdf({ title, signerName: signer.name }), signer });
      const record = store.save({ documentId, title, signer, status: 'Queued', createdAt: new Date().toISOString(), events: [] });
      if (req.accepts(['json', 'html']) === 'html') return res.redirect(303, '/documents');
      res.status(201).json(record);
    } catch (err) {
      const e = errorSummary(err);
      log.error?.('send failed', e);
      res.status(502).json({ error: 'BoldSign send failed', ...e });
    }
  });

  // Status page.
  app.get('/documents', (req, res) => {
    const rows = store.all().map((d) => `<tr><td>${esc(d.title)}</td><td>${esc(d.signer?.email)}</td><td><b>${esc(d.status)}</b>${d.error ? ` (${esc(d.error)})` : ''}</td><td><code>${esc(d.documentId)}</code></td><td>${d.status === 'Completed' ? '' : `<a href="/documents/${encodeURIComponent(d.documentId)}/sign">Sign now</a> <form method="post" action="/documents/${encodeURIComponent(d.documentId)}/refresh" style="display:inline"><button>Refresh</button></form>`}</td></tr>`).join('');
    res.type('html').send(`<!doctype html><meta charset="utf-8"><title>Documents</title><style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}</style>
<h1>Documents</h1><form method="post" action="/documents"><button>Send the sample PDF to ${esc(cfg.signer.email || 'SIGNER_EMAIL')}</button></form>
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
