# BoldSign — Node.js / TypeScript Integration Reference

## SDK Install

```bash
npm install boldsign@3.3.4 --save-exact
# or
yarn add boldsign@3.3.4 --exact
```

Pin the exact version you tested with (3.3.4 is the version these examples were checked against), so a new SDK release cannot change behaviour unnoticed.

Docs: https://developers.boldsign.com/sdks/node-sdk/

---

## Setup & Authentication

```typescript
import { DocumentApi, TemplateApi } from 'boldsign';
import 'dotenv/config';

// API Key auth (server-to-server)
const documentApi = new DocumentApi();
documentApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

// OAuth Bearer Token auth
documentApi.setAccessToken(process.env.BOLDSIGN_ACCESS_TOKEN!);

// Non-US region: set the base path (or pass it to the constructor: new DocumentApi('https://api-eu.boldsign.com'))
documentApi.basePath = 'https://api-eu.boldsign.com'; // EU; CA: https://api-ca.boldsign.com, AU: https://api-au.boldsign.com
```

Confirm the key and region with one read-only call before building anything:

```typescript
import { BrandingApi } from 'boldsign';

const brandingApi = new BrandingApi(process.env.BOLDSIGN_BASE_URL ?? 'https://api.boldsign.com');
brandingApi.setApiKey(process.env.BOLDSIGN_API_KEY!);
await brandingApi.brandList(); // throws with status 401 if the key or region is wrong
```

---

## Send a Document for Signature

```typescript
import {
  DocumentApi,
  SendForSign,
  DocumentSigner,
  FormField,
  Rectangle,
} from 'boldsign';
import * as fs from 'fs';

const documentApi = new DocumentApi();
documentApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

async function sendDocument() {
  const bounds = new Rectangle();
  bounds.x = 100;
  bounds.y = 200;
  bounds.width = 200;
  bounds.height = 50;

  const signatureField = new FormField();
  signatureField.fieldType = FormField.FieldTypeEnum.Signature;
  signatureField.pageNumber = 1;
  signatureField.bounds = bounds;
  signatureField.isRequired = true;

  const signer = new DocumentSigner();
  signer.name = 'John Doe';
  signer.emailAddress = 'john@example.com';
  signer.signerType = DocumentSigner.SignerTypeEnum.Signer;
  signer.formFields = [signatureField];

  const sendRequest = new SendForSign();
  sendRequest.title = 'Service Agreement';
  sendRequest.message = 'Please sign this agreement.';
  sendRequest.signers = [signer];
  sendRequest.files = [fs.createReadStream('contract.pdf')];
  // Optional: sendRequest.expiryDays = 30;
  // Optional: sendRequest.enableSigningOrder = true;

  const result = await documentApi.sendDocument(sendRequest);
  console.log('Document ID:', result.documentId);
  // NOTE: Document is still processing — use webhooks to confirm Sent status
  return result.documentId;
}
```

### Send with text tags (no coordinates, no template)

When the PDF contains text tags such as `{{sign|1|*}}` and `{{date|1|*}}` (see Text Tags in SKILL.md), set `useTextTags` and leave out `formFields`:

```typescript
async function sendWithTextTags(pdfPath: string, name: string, email: string) {
  const signer = new DocumentSigner();
  signer.name = name;
  signer.emailAddress = email;
  signer.signerType = DocumentSigner.SignerTypeEnum.Signer;

  const sendRequest = new SendForSign();
  sendRequest.title = 'Policy acknowledgement';
  sendRequest.signers = [signer];
  sendRequest.files = [fs.createReadStream(pdfPath)];
  sendRequest.useTextTags = true;
  // Embedded signing only: sendRequest.disableEmails = true;

  const { documentId } = await documentApi.sendDocument(sendRequest);
  return documentId;
}
```

### Retry the first call after a send

The document is processed asynchronously, so the next call on it (for example `getEmbeddedSignLink`) can return `403 Invalid Document ID` for a few seconds:

```typescript
async function retrySoon<T>(fn: () => Promise<T>, tries = 5): Promise<T> {
  for (let i = 1; ; i++) {
    try {
      return await fn();
    } catch (err: any) {
      const status = err?.statusCode ?? err?.response?.status; // SDK HttpError or Axios error
      if (i >= tries || (status !== 400 && status !== 403)) throw err;
      await new Promise((r) => setTimeout(r, 2000 * i));
    }
  }
}

const link = await retrySoon(() => documentApi.getEmbeddedSignLink(documentId, email));
```

---

## Send from Template

```typescript
import { TemplateApi, SendForSignFromTemplateForm, Role } from 'boldsign';

const templateApi = new TemplateApi();
templateApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

async function sendFromTemplate(templateId: string) {
  const role = new Role();
  role.roleIndex = 1;
  role.signerName = 'Jane Smith';
  role.signerEmail = 'jane@example.com';

  const sendRequest = new SendForSignFromTemplateForm();
  sendRequest.title = 'Contract for Jane Smith';
  sendRequest.roles = [role];

  const result = await templateApi.sendUsingTemplate(templateId, sendRequest);
  return result.documentId;
}
```

---

## Embedded Signing Link

Generate a URL to embed the signing page in an iframe inside your app:

```typescript
async function getEmbeddedSignLink(documentId: string, signerEmail: string) {
  const signLinkExpiry = new Date();
  signLinkExpiry.setHours(signLinkExpiry.getHours() + 24); // valid 24h

  const result = await documentApi.getEmbeddedSignLink(
    documentId,
    signerEmail,
    undefined,           // countryCode (optional, for SMS OTP)
    undefined,           // phoneNumber
    signLinkExpiry, // signLinkValidTill
    'https://yourapp.com/signing-complete' // redirectUrl after signing
  );

  return result.signLink; // load this in an <iframe>
}
```

---

## Embedded Send Link

Let users send documents without leaving your app:

```typescript
import { DocumentApi, EmbeddedDocumentRequest, DocumentSigner } from 'boldsign';

async function createEmbeddedSendRequest() {
  const signer = new DocumentSigner();
  signer.name = 'Recipient Name';
  signer.emailAddress = 'recipient@example.com';
  signer.signerType = DocumentSigner.SignerTypeEnum.Signer;

  const embeddedRequest = new EmbeddedDocumentRequest();
  embeddedRequest.title = 'New Contract';
  embeddedRequest.signers = [signer];
  embeddedRequest.files = [fs.createReadStream('contract.pdf')];
  embeddedRequest.showToolbar = true;
  embeddedRequest.showSaveButton = true;
  embeddedRequest.showSendButton = true;
  embeddedRequest.redirectUrl = 'https://yourapp.com/sent-complete';

  const result = await documentApi.createEmbeddedRequestUrlDocument(embeddedRequest);
  return result.sendUrl; // load in <iframe>
}
```

---

## Webhook Handler (Express)

The signature is `t=<unix seconds>, s0=<hex>[, s1=<hex>]`, computed as HMAC SHA256 over `t + "." + rawBody` (see Webhooks in SKILL.md). Mount the route with `express.raw` **before** any `express.json()`, so the body stays as raw bytes.

```typescript
import express from 'express';
import crypto from 'crypto';

const TOLERANCE_SECONDS = 300;

// Returns true only for an event signed with this webhook's secret, within the time tolerance.
export function verifyBoldSignSignature(rawBody: Buffer, header: string | undefined, secret: string): boolean {
  if (!header || !secret) return false;
  let t = '';
  const signatures: string[] = [];
  for (const part of header.split(',')) {
    const i = part.indexOf('=');
    if (i < 0) return false;
    const key = part.slice(0, i).trim();
    const value = part.slice(i + 1).trim();
    if (key === 't') t = value;
    else if (/^s\d+$/.test(key)) signatures.push(value.toLowerCase());
  }
  if (!/^\d+$/.test(t) || signatures.length === 0) return false;
  if (Math.abs(Math.floor(Date.now() / 1000) - Number(t)) > TOLERANCE_SECONDS) return false;

  const expected = crypto.createHmac('sha256', secret)
    .update(Buffer.concat([Buffer.from(`${t}.`), rawBody]))
    .digest();
  return signatures.some((sig) => {
    if (!/^[0-9a-f]{64}$/.test(sig)) return false; // also keeps timingSafeEqual lengths equal
    return crypto.timingSafeEqual(Buffer.from(sig, 'hex'), expected);
  });
}

const app = express();

app.post('/webhooks/boldsign', express.raw({ type: '*/*' }), (req, res) => {
  // BoldSign's URL check when the webhook is added: identified by the header, answered with 200
  if (req.get('X-BoldSign-Event') === 'Verification') return res.sendStatus(200);

  if (!verifyBoldSignSignature(req.body, req.get('X-BoldSign-Signature'), process.env.BOLDSIGN_WEBHOOK_SECRET!)) {
    return res.status(401).send('Invalid signature');
  }

  const payload = JSON.parse(req.body.toString('utf8')); // parse only after verifying
  const eventType: string = payload.event.eventType;      // event metadata: event.id, event.created, event.environment
  if (payload.data?.object !== 'document') return res.sendStatus(200);
  const { documentId } = payload.data;                    // the document is in data, not document

  switch (eventType) {
    case 'Completed':
      // All signers done — download PDF and audit trail
      handleDocumentComplete(documentId);
      break;
    case 'Signed':
      console.log(`Document ${documentId} signed by one signer`);
      break;
    case 'Declined':
      console.log(`Document ${documentId} was declined`);
      break;
    case 'SendFailed':
      console.error(`Send failed for ${documentId}:`, payload.data.errorMessage);
      break;
  }

  res.status(200).send('OK');
});

app.use(express.json()); // other routes, after the webhook route

async function handleDocumentComplete(documentId: string) {
  // Download signed PDF
  const pdf = await documentApi.downloadDocument(documentId);
  // pdf is a Buffer — save to storage
  fs.writeFileSync(`./signed/${documentId}.pdf`, pdf as Buffer);

  // Download audit trail
  const audit = await documentApi.downloadAuditLog(documentId);
  fs.writeFileSync(`./signed/${documentId}-audit.pdf`, audit as Buffer);
}
```

---

## Get Document Status

```typescript
async function getDocumentStatus(documentId: string) {
  const details = await documentApi.getProperties(documentId);
  console.log('Status:', details.status); // Draft, InProgress, Completed, Declined, Expired
  console.log('Signers:', details.signerDetails);
  return details;
}
```

---

## Environment Variables

```env
BOLDSIGN_API_KEY=your_api_key_here
BOLDSIGN_WEBHOOK_SECRET=your_webhook_secret_here
# For OAuth:
BOLDSIGN_CLIENT_ID=your_oauth_client_id
BOLDSIGN_CLIENT_SECRET=your_oauth_client_secret
```

---

## Sender Identity — Register and Send on Behalf of a Tenant

```typescript
import { SenderIdentitiesApi } from 'boldsign';

const senderApi = new SenderIdentitiesApi();
senderApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

// Step 1: Register identity
async function registerTenantIdentity(name: string, email: string) {
  await senderApi.createSenderIdentities({ name, email });
  await senderApi.resendInvitationSenderIdentities(email);
  // Tenant gets approval email — wait for SenderIdentityApproved webhook
}

// Step 2: Send on behalf after approval
async function sendOnBehalfOf(tenantEmail: string, docDetails: any) {
  const sendRequest = new SendForSign();
  sendRequest.onBehalfOf = tenantEmail; // ← key field
  sendRequest.title = docDetails.title;
  sendRequest.signers = docDetails.signers;
  sendRequest.files = docDetails.files;
  return documentApi.sendDocument(sendRequest);
}
```

---

## Rate Limit Handling

```typescript
// Wrap API calls to inspect headers and handle 429
async function callWithRateLimitAwareness<T>(fn: () => Promise<T>): Promise<T> {
  try {
    const result = await fn();
    // SDK may expose response headers — log remaining quota in dev
    return result;
  } catch (err: any) {
    if (err?.status === 429) {
      const retryAfter = parseInt(err.headers?.['retry-after'] ?? '60', 10);
      console.warn(`Rate limit hit. Retrying after ${retryAfter}s`);
      await new Promise(r => setTimeout(r, retryAfter * 1000));
      return fn(); // single retry
    }
    throw err;
  }
}

// Usage
const result = await callWithRateLimitAwareness(() =>
  documentApi.sendDocument(sendRequest)
);
```

**Sandbox limit: 50 req/hour. Production: 2,000 req/hour (account-level).**  
Use webhooks instead of polling to avoid unnecessary API calls.

---

## SDK Links
- npm: `npm install boldsign`
- GitHub: https://github.com/boldsign/boldsign-nodejs-sdk
- Full docs: https://developers.boldsign.com/sdks/node-sdk/
