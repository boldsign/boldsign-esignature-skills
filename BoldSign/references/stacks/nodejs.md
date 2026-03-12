# BoldSign — Node.js / TypeScript Integration Reference

## SDK Install

```bash
npm install boldsign
# or
yarn add boldsign
```

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

// EU region — set base path
documentApi.basePath = 'https://eu-api.boldsign.com';
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

---

## Send from Template

```typescript
import { TemplateApi, SendForSignFromTemplate, Roles } from 'boldsign';

const templateApi = new TemplateApi();
templateApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

async function sendFromTemplate(templateId: string) {
  const role = new Roles();
  role.roleIndex = 1;
  role.signerName = 'Jane Smith';
  role.signerEmail = 'jane@example.com';

  const sendRequest = new SendForSignFromTemplate();
  sendRequest.templateId = templateId;
  sendRequest.title = 'Contract for Jane Smith';
  sendRequest.roles = [role];

  const result = await templateApi.sendUsingTemplate(sendRequest);
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
    null,           // countryCode (optional, for SMS OTP)
    null,           // sendSMS
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

  const result = await documentApi.createEmbeddedRequestUrl(embeddedRequest);
  return result.sendUrl; // load in <iframe>
}
```

---

## Webhook Handler (Express)

```typescript
import express from 'express';
import crypto from 'crypto';

const app = express();
app.use(express.raw({ type: 'application/json' })); // raw body needed for HMAC

app.post('/webhooks/boldsign', (req, res) => {
  // 1. Verify HMAC signature
  const signature = req.headers['x-boldsign-signature'] as string;
  const secret = process.env.BOLDSIGN_WEBHOOK_SECRET!;
  const expectedSig = crypto
    .createHmac('sha256', secret)
    .update(req.body)
    .digest('hex');

  if (signature !== expectedSig) {
    return res.status(401).send('Invalid signature');
  }

  // 2. Parse and handle event
  const payload = JSON.parse(req.body.toString());
  const { eventType } = payload.event;
  const { documentId, status } = payload.document;

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
      console.error(`Send failed for ${documentId}:`, payload.document);
      break;
  }

  res.status(200).send('OK');
});

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
  const details = await documentApi.getDocumentProperties(documentId);
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
  await senderApi.requestForIdentityApproval({ email });
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
