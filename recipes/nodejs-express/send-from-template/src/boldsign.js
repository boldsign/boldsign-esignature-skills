import { BrandingApi, DocumentApi, Role, SendForSignFromTemplateForm, TemplateApi } from 'boldsign';

// A new document (or template) is processed asynchronously: for a few seconds the next call on it can return
// 403 "Invalid Document ID" or 400 "Invalid template id.". Retry those briefly; anything else is a real error.
export async function retrySoon(fn, { tries = 5, waitMs = 2000 } = {}) {
  for (let i = 1; ; i++) {
    try {
      return await fn();
    } catch (err) {
      const status = statusOf(err);
      if (i >= tries || (status !== 400 && status !== 403)) throw err;
      await new Promise((r) => setTimeout(r, waitMs * i));
    }
  }
}

// SDK errors are an HttpError (statusCode) or an Axios error (response.status). Their full object carries
// request headers, including the API key, so log only this summary.
export function statusOf(err) { return err?.statusCode ?? err?.response?.status; }
export function errorSummary(err) {
  const body = err?.body ?? err?.response?.data;
  return { status: statusOf(err), error: body?.error || body?.message || body?.title || err?.message || 'Unknown error' };
}

export function createBoldSign(cfg) {
  const documents = new DocumentApi(cfg.baseUrl);
  documents.setApiKey(cfg.apiKey);
  const templates = new TemplateApi(cfg.baseUrl);
  templates.setApiKey(cfg.apiKey);
  const branding = new BrandingApi(cfg.baseUrl);
  branding.setApiKey(cfg.apiKey);

  return {
    // One read-only call: 200 means the key and the region's base URL match.
    checkConfig: () => branding.brandList(),

    // The template's roles as { name, index } (read-only), to check the role configuration.
    async templateRoles(templateId) {
      const p = await retrySoon(() => templates.getProperties(templateId));
      return (p.roles || []).map((r) => ({ name: r.name, index: r.index }));
    },

    // Sends from the template, with one signer per role. BoldSign emails each signer. Returns the document ID;
    // the send completes asynchronously, and the webhook reports Sent or SendFailed.
    async sendFromTemplate({ templateId, title, roles }) {
      const request = new SendForSignFromTemplateForm();
      request.title = title;
      request.roles = roles.map((r) => Object.assign(new Role(), r));
      const { documentId } = await retrySoon(() => templates.sendUsingTemplate(templateId, request));
      return documentId;
    },

    // A signing link for a signer, for an in-app "Sign now" button (the signer can also use the email).
    async signLink(documentId, signerEmail, redirectUrl) {
      const r = await retrySoon(() => documents.getEmbeddedSignLink(documentId, signerEmail, undefined, undefined, undefined, redirectUrl));
      return r.signLink;
    },

    // Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour).
    properties: (documentId) => retrySoon(() => documents.getProperties(documentId)),
  };
}
