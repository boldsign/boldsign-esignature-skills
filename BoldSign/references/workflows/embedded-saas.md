# BoldSign Workflow — Embedded Send & Sign for SaaS Applications

## Overview

For SaaS apps, the entire eSignature experience lives inside your product.
Users never visit boldsign.com. Two key decisions drive your architecture:

1. **Embedded UI** — send and sign via iframes inside your app
2. **Tenant identity** — whose name appears as sender on documents

This guide covers both.

---

## Multi-Tenant SaaS: OAuth vs Sender Identities

This is the most critical architecture decision for SaaS builders. Both let you send documents on behalf of customers. Choose based on whether your customers have BoldSign accounts.

---

### Option A — Sender Identities (`onBehalfOf`) ← Recommended for most SaaS

**How it works:**
1. Call the Sender Identity API to register a customer's name + email
2. BoldSign sends the customer a **one-time approval email** — no BoldSign account needed
3. After approval, include `onBehalfOf: "customer@theirdomain.com"` in any send request
4. Signers see the customer's name/email as the sender, not your app's identity

**Characteristics:**
- ✅ Customer does **not** need a BoldSign account — just an email click to approve
- ✅ Simple tenant onboarding — one API call + one approval click
- ✅ Cost-effective — pay per document sent, not per tenant seat
- ✅ Clean audit trails — clearly shows who acted on whose behalf
- ✅ Customer can revoke delegation at any time via email or BoldSign web app
- ✅ Webhook events for `SenderIdentityApproved`, `Denied`, `Revoked`
- ⚠️ All tenant activity counts against your single account's rate limit

**Core API endpoints:**
```
POST   /v1/senderIdentities/create    → Register a sender identity (name + email)
POST   /v1/senderIdentities/request   → Send approval email to the identity owner
POST   /v1/senderIdentities/resend    → Resend approval if not acted on
POST   /v1/senderIdentities/update    → Update notification preferences
DELETE /v1/senderIdentities/delete    → Remove an identity permanently
GET    /v1/senderIdentities/list      → List all identities for your account
```

**Send a document using Sender Identity:**
```json
POST /v1/document/send
{
  "onBehalfOf": "customer@theirdomain.com",
  "title": "...",
  "signers": [...],
  "files": [...]
}
```

**Lifecycle to implement:**
```
1. POST /senderIdentities/create       → Register customer identity
2. POST /senderIdentities/request      → Customer receives approval email
3. [Customer clicks Approve]
4. Webhook: SenderIdentityApproved     → Store approval status in your DB
5. All subsequent sends include onBehalfOf
6. Webhook: SenderIdentityRevoked      → Handle gracefully if customer revokes
```

Ref: https://developers.boldsign.com/sender-identities/create-identity/

---

### Option B — OAuth 2.0 Authorization Code Flow

**How it works:**
1. Register an OAuth App in BoldSign
2. Each customer authorizes via OAuth consent screen
3. You receive per-customer access + refresh tokens
4. API calls with their token act as their BoldSign account

**Characteristics:**
- ✅ Full API access as the customer's own BoldSign account
- ✅ Customer has full visibility in their own BoldSign dashboard
- ✅ Per-customer billing and rate limits
- ❌ Customer **must** have a BoldSign account and complete OAuth flow
- ❌ More complex — per-tenant token storage, refresh rotation
- ❌ Higher onboarding friction for customers

**Use when:** Your customers are BoldSign users themselves, they want their own dashboard visibility, or per-tenant billing is required.

---

### Decision Guide

```
Does your customer have (or need) their own BoldSign account?
│
├── NO  → Sender Identities (onBehalfOf)
│         Zero-friction, no account required, pay per document
│
└── YES → OAuth 2.0 Authorization Code Flow
          Full per-account access, customer manages their own docs
```

Ref: https://boldsign.com/blogs/oauth-vs-sender-identities-explained/

---

## Part 1 — Sender Identity Setup for Multi-Tenant SaaS

For most SaaS apps where customers don't have BoldSign accounts:
**use Sender Identities**.

### Tenant Onboarding Flow

```
Your app: New tenant signs up
│
├─ 1. POST /v1/senderIdentities/create
│       body: { name: "Acme Corp", email: "admin@acme.com" }
│
├─ 2. POST /v1/senderIdentities/request
│       body: { email: "admin@acme.com" }
│       → BoldSign sends approval email to admin@acme.com
│
├─ 3. [Tenant clicks "Approve" in email — no BoldSign account needed]
│
├─ 4. Webhook: SenderIdentityApproved
│       → Update your DB: tenant.boldSignStatus = 'approved'
│
└─ All subsequent sends: include onBehalfOf: "admin@acme.com"
```

### Implementation — Sender Identity Setup

```typescript
// Node.js: Register and request approval for a new tenant
import { SenderIdentitiesApi, CreateSenderIdentityRequest } from 'boldsign';

const senderApi = new SenderIdentitiesApi();
senderApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

async function onboardTenant(tenant: { name: string; email: string }) {
  // Step 1: Create the identity
  await senderApi.createSenderIdentities({
    name: tenant.name,
    email: tenant.email,
    // Customize which email notifications the identity owner receives
    notificationSettings: {
      viewed: true,
      signed: true,
      completed: true,
      declined: true,
    }
  });

  // Step 2: Request their approval
  await senderApi.requestForIdentityApproval({ email: tenant.email });

  // Store pending status — wait for SenderIdentityApproved webhook
  await db.tenants.update(tenant.email, { boldSignStatus: 'pending' });
}
```

### Handle Identity Webhook Events

```typescript
// In your webhook handler
case 'SenderIdentityApproved':
  const approvedEmail = payload.senderIdentity?.email;
  await db.tenants.update(approvedEmail, { boldSignStatus: 'approved' });
  break;

case 'SenderIdentityDenied':
  await db.tenants.update(payload.senderIdentity?.email, { boldSignStatus: 'denied' });
  // Notify your team or prompt re-request
  break;

case 'SenderIdentityRevoked':
  await db.tenants.update(payload.senderIdentity?.email, { boldSignStatus: 'revoked' });
  // Stop sending on behalf of this tenant until re-approved
  break;
```

### Send on Behalf of Tenant

```typescript
// Always include onBehalfOf for approved tenants
const sendRequest = new SendForSign();
sendRequest.onBehalfOf = tenant.email;   // ← key field
sendRequest.title = documentTitle;
sendRequest.signers = [signer];
sendRequest.files = [fileStream];

const result = await documentApi.sendDocument(sendRequest);
```

---

## Part 2 — Embedded Send UI (Sender Side)

Let your users prepare and send documents without leaving your app.

### Backend: Create Embedded Send Link

```typescript
// POST /api/boldsign/embedded-send
import { DocumentApi, EmbeddedDocumentRequest, DocumentSigner } from 'boldsign';

app.post('/api/boldsign/embedded-send', authenticate, async (req, res) => {
  const { recipientName, recipientEmail, documentTitle } = req.body;
  const tenant = await getTenantForUser(req.user);

  const documentApi = new DocumentApi();
  documentApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

  const signer = new DocumentSigner();
  signer.name = recipientName;
  signer.emailAddress = recipientEmail;
  signer.signerType = DocumentSigner.SignerTypeEnum.Signer;

  const embeddedRequest = new EmbeddedDocumentRequest();
  embeddedRequest.title = documentTitle;
  embeddedRequest.signers = [signer];
  embeddedRequest.files = [/* file stream from request */];
  embeddedRequest.onBehalfOf = tenant.email;   // Sender Identity

  // Control what the sender sees inside the iframe
  embeddedRequest.showToolbar = true;
  embeddedRequest.showSaveButton = true;
  embeddedRequest.showSendButton = true;
  embeddedRequest.showNavigationButtons = true;
  embeddedRequest.redirectUrl = `${process.env.APP_URL}/documents/sent`;

  const result = await documentApi.createEmbeddedRequestUrl(embeddedRequest);
  res.json({ sendUrl: result.sendUrl });
});
```

### Frontend: Render Embedded Send Iframe

```tsx
function EmbeddedSendUI({ recipientName, recipientEmail, documentTitle }) {
  const [sendUrl, setSendUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch('/api/boldsign/embedded-send', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ recipientName, recipientEmail, documentTitle })
    })
      .then(r => r.json())
      .then(data => { setSendUrl(data.sendUrl); setLoading(false); });
  }, []);

  if (loading) return <div>Preparing document editor...</div>;

  return (
    <iframe
      src={sendUrl!}
      style={{ width: '100%', height: '800px', border: 'none' }}
      title="Send Document"
    />
  );
}
```

---

## Part 3 — Embedded Sign UI (Recipient Side)

After a document is sent, signers can sign inside your app rather than following an email link.

### Backend: Generate Embedded Sign Link

```typescript
// GET /api/boldsign/sign-link?documentId=...&signerEmail=...
app.get('/api/boldsign/sign-link', authenticate, async (req, res) => {
  const { documentId, signerEmail } = req.query as Record<string, string>;

  // SECURITY: verify the requesting user is the intended signer
  if (req.user.email !== signerEmail) {
    return res.status(403).json({ error: 'Forbidden' });
  }

  const documentApi = new DocumentApi();
  documentApi.setApiKey(process.env.BOLDSIGN_API_KEY!);

  const expiry = new Date(Date.now() + 24 * 60 * 60 * 1000); // 24h

  const result = await documentApi.getEmbeddedSignLink(
    documentId,
    signerEmail,
    null,   // countryCode (for SMS OTP)
    null,   // sendSMS
    expiry,
    `${process.env.APP_URL}/documents/complete?id=${documentId}`
  );

  res.json({ signUrl: result.signLink });
});
```

### Frontend: Render Embedded Sign Iframe

```tsx
function EmbeddedSignUI({ documentId, signerEmail }: { documentId: string; signerEmail: string }) {
  const [signUrl, setSignUrl] = useState<string | null>(null);

  useEffect(() => {
    fetch(`/api/boldsign/sign-link?documentId=${documentId}&signerEmail=${signerEmail}`)
      .then(r => r.json())
      .then(data => setSignUrl(data.signUrl));
  }, [documentId, signerEmail]);

  if (!signUrl) return <div>Loading signing experience...</div>;

  return (
    <iframe
      src={signUrl}
      style={{ width: '100%', height: '800px', border: 'none' }}
      allow="camera"  // Required if using ID verification
      title="Sign Document"
    />
  );
}
```

---

## Part 4 — Webhook Handler for SaaS

```typescript
app.post('/webhooks/boldsign', express.raw({ type: 'application/json' }), async (req, res) => {
  // 1. Verify HMAC
  const signature = req.headers['x-boldsign-signature'] as string;
  const expected = crypto.createHmac('sha256', process.env.BOLDSIGN_WEBHOOK_SECRET!)
    .update(req.body).digest('hex');
  if (signature !== expected) return res.status(401).send('Invalid signature');

  const payload = JSON.parse(req.body.toString());
  const { eventType } = payload.event;
  const doc = payload.document;

  switch (eventType) {
    case 'Completed': {
      // Download signed PDF and audit trail — store in your own storage
      const pdf = await documentApi.downloadDocument(doc.documentId);
      const audit = await documentApi.downloadAuditLog(doc.documentId);
      await storage.save(`documents/${doc.documentId}/signed.pdf`, pdf);
      await storage.save(`documents/${doc.documentId}/audit.pdf`, audit);

      // Update your DB and notify relevant users
      await db.documents.update(doc.documentId, { status: 'completed', completedAt: new Date() });
      await notifyTenantUsers(doc.documentId, 'completed');
      break;
    }

    case 'Sent':
      await db.documents.update(doc.documentId, { status: 'sent' });
      break;

    case 'SendFailed':
      await db.documents.update(doc.documentId, { status: 'failed', error: doc.errorMessage });
      await alertOpsTeam(doc);
      break;

    case 'Declined':
      await db.documents.update(doc.documentId, { status: 'declined' });
      await notifyTenantUsers(doc.documentId, 'declined');
      break;

    case 'SenderIdentityApproved':
      await db.tenants.update(payload.senderIdentity?.email, { boldSignStatus: 'approved' });
      break;

    case 'SenderIdentityRevoked':
      await db.tenants.update(payload.senderIdentity?.email, { boldSignStatus: 'revoked' });
      break;
  }

  res.status(200).send('OK');
});
```

---

## Part 5 — Per-Tenant Branding

Apply your customer's brand to the signing experience:

```typescript
// Store a brandId per tenant in your DB
// Create brands via: POST /v1/branding/create

const sendRequest = new SendForSign();
sendRequest.onBehalfOf = tenant.email;
sendRequest.brandId = tenant.boldSignBrandId;  // apply tenant's brand
// ... rest of send params
```

---

## Rate Limit Considerations for SaaS

All tenant activity runs through your single BoldSign account. With 2,000 req/hour in production, plan accordingly:

- **At 100 tenants** each sending 20 docs/hour = 2,000 requests/hour — you're at the ceiling
- Use a **request queue** to throttle and prioritize under load
- Monitor `X-RateLimit-Remaining` in responses and back off proactively
- **Webhooks are free** — they don't count against your rate limit
- Contact BoldSign support if you need a higher limit for your scale

---

## Security Checklist for SaaS Embedded Apps

- ✅ Verify requesting user before generating any sign link (prevent link hijacking)
- ✅ Sign links are short-lived — never cache; always generate fresh
- ✅ Verify webhook HMAC on every incoming event
- ✅ Store signed PDFs and audit trails in your own storage (S3, GCS, Azure Blob)
- ✅ Never expose your BoldSign API key to frontend code
- ✅ Check tenant `boldSignStatus === 'approved'` before sending on their behalf
- ✅ Handle `SenderIdentityRevoked` gracefully — pause sending for that tenant
- ✅ Use environment variables for all credentials

---

## Full Architecture Summary

```
Tenant onboarding:
  POST /senderIdentities/create + /request
  → Approval email to tenant
  → Webhook: SenderIdentityApproved → store in DB

Document workflow:
  Your app UI → POST /api/boldsign/embedded-send
    → BoldSign: createEmbeddedRequestUrl (with onBehalfOf)
    → iframe: sender prepares + sends doc

  Recipient notified by email (or redirected to your app sign page)
    → GET /api/boldsign/sign-link
    → BoldSign: getEmbeddedSignLink
    → iframe: recipient signs in your app

  Webhooks:
    Sent → update status
    Completed → download PDF + audit → store → notify
    Declined / Expired → handle gracefully
```
