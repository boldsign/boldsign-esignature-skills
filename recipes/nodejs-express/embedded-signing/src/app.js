import express from 'express';
import { applyEvent, FINAL, verifyBoldSignSignature } from './webhook.js';
import { samplePdf } from './sample-pdf.js';
import { errorSummary } from './boldsign.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const page = (title, body, csp = "default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'") =>
  `<!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="${csp}"><title>${esc(title)}</title><style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}iframe{width:100%;height:80vh;border:1px solid #ccc}</style>${body}`;

// The signed-in user. This recipe uses one demo user from SIGNER_NAME and SIGNER_EMAIL: replace currentUser
// with your application's authentication. Signing links go to whoever this returns.
export function createApp({ cfg, boldsign, store, log = console, currentUser = () => cfg.signer }) {
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
    res.sendStatus(200);
  });

  app.use(express.json());
  app.use(express.urlencoded({ extended: false }));

  const owned = (req, res) => {
    const d = store.get(req.params.id);
    if (!d || d.signer?.email !== currentUser(req).email) { res.sendStatus(404); return null; }
    return d;
  };

  // Create the document for the signed-in user, then open the signing page.
  app.post('/documents', async (req, res) => {
    const signer = currentUser(req);
    if (!signer?.email) return res.status(400).json({ error: 'No signed-in user (set SIGNER_EMAIL)' });
    const title = req.body?.title || 'Policy acknowledgement';
    try {
      const documentId = await boldsign.createDocument({ title, pdf: samplePdf({ title, signerName: signer.name }), signer });
      store.save({ documentId, title, signer, status: 'Queued', createdAt: new Date().toISOString(), events: [] });
      const signingPage = `/documents/${encodeURIComponent(documentId)}/sign`;
      if (req.accepts(['json', 'html']) === 'html') return res.redirect(303, signingPage);
      res.status(201).json({ documentId, status: 'Queued', signingPage });
    } catch (err) {
      const e = errorSummary(err);
      log.error?.('create failed', e);
      res.status(502).json({ error: 'BoldSign create failed', ...e });
    }
  });

  // The signing page: BoldSign's signing UI in an iframe, with a fresh link on every visit.
  app.get('/documents/:id/sign', async (req, res) => {
    const d = owned(req, res);
    if (!d) return;
    if (FINAL.has(d.status)) return res.redirect(`/signing/complete?documentId=${encodeURIComponent(d.documentId)}`);
    try {
      const redirectUrl = `${cfg.appBaseUrl}/signing/complete?documentId=${encodeURIComponent(d.documentId)}`;
      const link = await boldsign.signLink(d.documentId, d.signer.email, redirectUrl);
      res.set('Cache-Control', 'no-store').set('Referrer-Policy', 'no-referrer');
      res.type('html').send(page(`Sign: ${d.title}`, `<h1>${esc(d.title)}</h1><p><a href="/documents">Back to documents</a></p>
<iframe src="${esc(link)}" title="BoldSign signing" allow="camera"></iframe>`, "default-src 'self'; style-src 'unsafe-inline'; frame-src https://app.boldsign.com https://*.boldsign.com"));
    } catch (err) { res.status(502).json({ error: 'Could not get a signing link', ...errorSummary(err) }); }
  });

  // Where BoldSign sends the signer when signing ends (redirectUrl). Breaks out of the iframe and shows the status.
  app.get('/signing/complete', (req, res) => {
    const d = store.get(String(req.query.documentId || ''));
    res.type('html').send(page('Signing finished', `<script>if (window.top !== window.self) window.top.location.href = window.location.href;</script>
<h1>Thank you</h1><p>${d ? `Document <code>${esc(d.documentId)}</code>: status <b>${esc(d.status)}</b>. It changes to Completed when BoldSign's webhook arrives.` : 'Signing finished.'}</p><p><a href="/documents">Back to documents</a></p>`));
  });

  // Status page.
  app.get('/documents', (req, res) => {
    const me = currentUser(req);
    const rows = store.all().filter((d) => d.signer?.email === me.email).map((d) => `<tr><td>${esc(d.title)}</td><td><b>${esc(d.status)}</b>${d.error ? ` (${esc(d.error)})` : ''}</td><td><code>${esc(d.documentId)}</code></td><td>${FINAL.has(d.status) ? '' : `<a href="/documents/${encodeURIComponent(d.documentId)}/sign">Open signing page</a>`}</td></tr>`).join('');
    res.type('html').send(page('My documents', `<h1>My documents</h1><p>Signed in as ${esc(me.name)} (${esc(me.email)})</p><form method="post" action="/documents"><button>Create a document and sign it here</button></form>
<table><tr><th>Title</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>${rows || '<tr><td colspan="4">No documents yet</td></tr>'}</table>`));
  });
  app.get('/documents.json', (req, res) => res.json(store.all().filter((d) => d.signer?.email === currentUser(req).email)));

  app.get('/', (req, res) => res.redirect('/documents'));
  return app;
}
