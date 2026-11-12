import { BrandingApi, DocumentApi, DocumentSigner, SendForSign } from 'boldsign';

// A new document is processed asynchronously: for a few seconds the next call on it can return
// 403 "Invalid Document ID" (or 400). Retry those briefly; anything else is a real error.
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
  const branding = new BrandingApi(cfg.baseUrl);
  branding.setApiKey(cfg.apiKey);

  return {
    // One read-only call: 200 means the key and the region's base URL match.
    checkConfig: () => branding.brandList(),

    // Sends the PDF to one signer. Fields come from the text tags in the PDF (useTextTags), so no template
    // and no coordinates. BoldSign emails the signer. Returns the document ID; the send itself completes
    // asynchronously, and the webhook reports Sent or SendFailed.
    async sendDocument({ title, pdf, signer }) {
      const s = new DocumentSigner();
      s.name = signer.name;
      s.emailAddress = signer.email;
      s.signerType = DocumentSigner.SignerTypeEnum.Signer;
      const request = new SendForSign();
      request.title = title;
      request.signers = [s];
      request.useTextTags = true;
      request.files = [{ value: pdf, options: { filename: 'document.pdf', contentType: 'application/pdf' } }];
      const { documentId } = await documents.sendDocument(request);
      return documentId;
    },

    // A signing link for the signer, for an in-app "Sign now" button (the signer can also use the email).
    async signLink(documentId, signerEmail, redirectUrl) {
      const r = await retrySoon(() => documents.getEmbeddedSignLink(documentId, signerEmail, undefined, undefined, undefined, redirectUrl));
      return r.signLink;
    },

    // Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour).
    properties: (documentId) => retrySoon(() => documents.getProperties(documentId)),
  };
}
