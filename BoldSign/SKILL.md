---
name: BoldSign
description:  Use this skill whenever the user wants to integrate BoldSign eSignature into their application. This includes sending documents for signature, setting up embedded signing or sending, configuring webhooks, working with templates,implementing Sender Identities for multi-tenant SaaS, choosing between OAuth and Sender Identities, handling rate limits, or generating stack-specific code in Node.js, TypeScript, Python, .NET/C#, or PHP. Also use this skill when the user asks about BoldSign best practices, common integration mistakes, sandbox vs live environment differences, or how to avoid pitfalls like polling instead of webhooks, skipping HMAC verification, or hitting rate limits. If the user mentions BoldSign, eSignature, document signing, or wants to automate a signing workflow, use this skill.
---

# BoldSign eSignature Integration Skill

This skill covers the BoldSign eSignature API — authentication, document sending,
embedded signing, templates, webhooks, Sender Identities, rate limits, best
practices, and common mistakes to avoid — with working code examples for
Node.js, TypeScript, Python, .NET/C#, and PHP.

**Live Docs:** https://developers.boldsign.com  
**API Base URL (US):** `https://api.boldsign.com`  
**API Base URL (EU):** `https://api-eu.boldsign.com`   
**API Base URL (CA):** `https://api-ca.boldsign.com`   
**API Base URL (AU):** `https://api-au.boldsign.com`    

---

## Understand What the User Needs

Before writing code, always clarify:

1. **Stack** — Node.js/TypeScript, Python, .NET/C#, or PHP?
2. **Auth model** — API Key, OAuth 2.0, or Sender Identities (on-behalf-of)?
3. **Workflow type:**
   - Send document → signer gets email → signs externally
   - Embedded sending (sender never leaves your app)
   - Embedded signing (signer signs inside an iframe in your app)
   - Template-based sending
   - Multi-signer / sequential signing order
4. **Is this a multi-tenant SaaS app?** → Read the SaaS multitenancy section below and `references/workflows/embedded-saas.md`

Then read the relevant reference file **before writing any code**:

| Stack | Reference File |
|---|---|
| Node.js / TypeScript | `references/stacks/nodejs.md` |
| Python | `references/stacks/python.md` |
| .NET / C# | `references/stacks/dotnet.md` |
| PHP | `references/stacks/php.md` |

| Workflow | Reference File |
|---|---|
| Multi-signer / sequential / bulk | `references/workflows/multi-signer.md` |
| Embedded send + sign (SaaS) | `references/workflows/embedded-saas.md` |

---

## Sandbox vs Live Environments

BoldSign has two environments. Always start in Sandbox.

| | Sandbox | Live (Production) |
|---|---|---|
| **Purpose** | Development & testing | Real-world transactions |
| **Rate limit** | **50 requests / hour / account** | **2,000 requests / hour / account** |
| **Documents** | Watermarked, auto-deleted after 14 days | Permanent |
| **API Key** | BoldSign App → API → API Key (select Sandbox) | Same UI, select Live |
| **OAuth Apps** | Toggle Sandbox/Live when creating the app | Toggle in app settings |
| **Cost** | Free | Paid plan |

⚠️ **Sandbox rate limit is strict at 50 req/hour.** Avoid running tests in tight loops.  
Test webhooks thoroughly in Sandbox before switching to Live.

**Switch to Live:** BoldSign App → API → OAuth Apps → Edit app → toggle environment to Live.

---

## Rate Limits

| Environment | Limit | Scope |
|---|---|---|
| **Sandbox** | 50 requests / hour | Per account |
| **Live** | 2,000 requests / hour | Per account |

**Key facts:**
- Rate limits are **account-level** — not per API key, OAuth app, or user
- Both API Key and OAuth calls count toward the **same account quota**
- If your use case requires more than 2,000 req/hour, contact BoldSign support

**Response headers to monitor:**
```
X-RateLimit-Limit      → Max requests allowed in current window
X-RateLimit-Remaining  → Requests remaining
X-RateLimit-Reset      → Unix timestamp when the window resets
```

**When the limit is exceeded:** HTTP `429 Too Many Requests` + `Retry-After` header.

**Best practices:**
- Use **webhooks** instead of polling for document status — biggest source of avoidable calls
- Implement **exponential backoff** on 429 responses
- Use **background queues** for bulk operations — spread requests evenly over time
- Prioritize send/sign calls over informational calls (list, status) 
- Use the email `<anyname>@boldsign.dev`, and the email will land in the link below: https://emaildemo.boldsign.com/
. This will be helpful for multi-user simulation testing, to avoid account blocking for bounce. 



---

## Authentication

### API Key (simplest — server-to-server)
```
Header: X-API-KEY: {your_api_key}
```
Retrieve: BoldSign Web App → API → API Key  
Best for: Backend automation, internal tools, scheduled workflows

### OAuth 2.0 (user-delegated)
- Create OAuth App: BoldSign App → API → OAuth Apps → Create Application
- Use **Authorization Code** flow for user-facing apps
- Enable **sliding refresh token expiry** — extends by 30 days on each use
- Header: `Authorization: Bearer {access_token}`  
- Best for: Apps where each end user has their own BoldSign account

### Sender Identities / `onBehalfOf` (multi-tenant SaaS)
Covered in full in the section below. Best for: SaaS apps where customers don't need their own BoldSign account but documents should appear sent from their identity.

**Region config:** US = default `https://api.boldsign.com`, EU = `https://api-eu.boldsign.com`, CA = `https://api-ca.boldsign.com`, AU = `https://api-au.boldsign.com`. A key used against the wrong region's host gets `401` on every call.

**Confirm the key and region first:** make one read-only call, for example `GET /v1/brand/list`, before building. `200` means the key and base URL match.


## Document Send — Critical Facts

⚠️ **Document sending is ASYNCHRONOUS.** The API returns a `documentId` immediately, but the document is still processing. Do NOT assume it's ready.

To confirm success: **listen for webhooks** — `Sent` (success) or `SendFailed` (failure with error).

**Minimum required fields:**
- `Files` (multipart) or `FileUrls` (JSON)
- At least one signer with `name`, `emailAddress`, `signerType`
- Fields for each signer: either `formFields` with `fieldType`, `pageNumber`, `bounds`, **or** text tags in the document with `useTextTags: true` (no `formFields` needed; see Text Tags)

⚠️ **The next call can fail for a few seconds after a send.** Right after `/v1/document/send`, `getEmbeddedSignLink` and `properties` can return `403 {"error":"Invalid Document ID"}` while the document is processing; the same call succeeds seconds later. Retry a `400` or `403` briefly (for example 3 to 5 tries, 2 to 3 seconds apart) before treating it as an error.

**Signer types:** `Signer`, `Viewer`, `InPersonSigner`

**Form field types:** `Signature`, `Initial`, `DateSigned`, `TextBox`, `CheckBox`, `RadioButton`, `Dropdown`, `Image`, `EditableDate`, `Label`, `Attachment`

For embedded signing workflows in BoldSign:
 
- Always include DisableEmails = true in requests for all supported DisableEmails workflows, including document send, template send, merge/send, embedded request URL, and document edit operations.
- This applies to all SDKs and API formats (C#, Node.js, Python, Java, etc.).
- The property name should follow the SDK/API conventions but must always be included in the request payload.
- This is required to disable email notifications and ensure signing happens only via the embedded signing link.

---

## Webhooks — Essential for Production

All async operations require webhooks to confirm status.  
BoldSign retries webhook delivery for **up to 8 hours** if your server is down.

**Setup:** BoldSign App → API → Webhooks → Create webhook (account-level), or API → OAuth Apps → your app (app-level). Copy the webhook's signing secret with **Reveal**. To receive webhooks on a local machine, expose it with a tunnel, for example `cloudflared tunnel --url http://localhost:3000`, and register that HTTPS URL.

**Two types:**
- **Account-level** — fires for all docs sent via web app or API
- **App-level** — fires only for docs sent via a specific OAuth app

**Key events:**
```
Sent / SendFailed                          → Document send success/failure
Viewed                                     → Signer opened document
Signed                                     → One signer completed
Completed                                  → All signers done → download PDF
Declined                                   → Signer declined
Expired                                    → Document expired
TemplateCreated / TemplateCreateFailed     → Template async result
IdentityVerificationSucceeded/Failed       → ID verification outcome
SenderIdentityApproved/Denied/Revoked      → Sender identity lifecycle
```

**Security: verify every event before trusting it** (docs: https://developers.boldsign.com/webhooks/verify-webhook-events/)

1. **Verification ping:** when a webhook is added, BoldSign sends a request with header `X-BoldSign-Event: Verification`. Return `200` and do nothing else. It is identified by the **header**, not the body.
2. **Raw body:** read the request body as raw bytes, exactly as sent. Do not parse and re-serialise the JSON first, or the signature will not match.
3. **Header format:** `X-BoldSign-Signature: t=<unix seconds>, s0=<hex>[, s1=<hex>]`. `s1` appears only while an old secret is still valid after the secret is rolled.
4. **Expected signature:** `hex(HMAC_SHA256(key = webhook secret, message = t + "." + rawBody))`. The timestamp is part of the signed message; an HMAC of the body alone never matches.
5. **Compare** the expected value with `s0` and `s1` using a constant-time comparison (`crypto.timingSafeEqual`, `hmac.compare_digest`, `hash_equals`), never `==`/`!==` and never against the whole header string.
6. **Reject old events:** reject if `|now - t|` is more than 300 seconds (replay protection).
7. Only then parse the JSON. **Event metadata is in `event`** (`event.id`, `event.eventType`, `event.created`, `event.environment`: `Test` for sandbox or `Live`) and **the subject of the event is in `data`**: `data.object` says what it is (`"document"`, `"template"`, `"senderIdentity"`, `"identityVerification"`), then for a document `data.documentId`, `data.status`, `data.signerDetails`, and for a sender identity `data.email`, `data.status`. Read `data`, as the documented sample payloads do; do not rely on a top-level `document` property.
8. Return `401` (or `400`) for any failed check without changing anything. De-duplicate by `event.id`, and ignore an event older than the last one applied for that document.

The .NET SDK's `WebhookUtility.ValidateSignature` implements steps 3 to 6. The Node SDK has the same helper in `boldsign/dist/WebhookUtility` (not exported from the package root); wrap it in `try/catch`, because a signature of the wrong length makes it throw a non-signature error.

---

## Signer Authentication Options

| Type | `authenticationType` | Notes |
|---|---|---|
| None | `None` | Default |
| Access Code | `AccessCode` | Sender sets code, shares out-of-band |
| Email OTP | `EmailOTP` | System sends OTP to signer's email |
| SMS OTP | `SMSOTP` | Higher-tier plans only |
| ID Verification | `IdVerification` | Passport/Gov ID — configure `identityVerificationSettings` |

---

## Templates

Templates decouple document setup from sending. Use for any document sent repeatedly.

**Template creation is also async** — listen for `TemplateCreated` webhook.

```
POST /v1/template/send
Body: { templateId, title, roles: [{ roleIndex, signerName, signerEmail }] }
```

---

## Text Tags

Put text tags in your Word/PDF to place fields, with no x/y coordinates, and send with **`useTextTags: true`** (docs: https://developers.boldsign.com/text-tags/introduction/).

Syntax: `{{fieldType|signerIndex|required|label|fieldId}}`. Only the first two parts are required; the signer index starts at 1; `*` marks the field required.

```
{{sign|1|*}}                       → Signature for signer 1, required
{{init|1|*}}                       → Initials
{{date|1|*}}                       → Date signed (filled automatically)
{{text|1|*|Enter name|full_name}}  → Text box with a placeholder and a field ID
{{editdate|1|*|dd/MM/yyyy}}        → Editable date with a format
```

Field types: `text` (Textbox), `sign` (Signature), `init` (Initial), `date` (Date signed), `editdate` (Editable date), `title`, `company`. Checkboxes and radio buttons need definition tags (`textTagDefinitions`).

- **Size:** add spaces inside the braces to widen a field (`{{sign|1|*          }}`); a larger font makes it taller.
- **Hidden tags:** draw the tag text in white so it does not show on the signed PDF.
- BoldSign converts the tags to real fields when it processes the document.

**If you use `bounds` instead:** coordinates are measured from the top-left of the page in pixels at 96 per inch, not PDF points (1 point = 1.333 px). Fields placed with point values land too high and too far left. Text tags avoid this.

---

## Implementation Order

1. **Install SDK** for target stack (see stack reference)
2. **Start in Sandbox** — 50 req/hour, watermarked docs, free
3. **Choose auth model** — API Key / OAuth / Sender Identities
4. **Configure region** if non-US
5. **Implement document send or template send**
6. **Set up webhook endpoint**, verify HMAC
7. **Handle `Completed`** → download signed PDF + audit trail
8. **Handle error events** → `SendFailed`, `Declined`, `Expired`
9. **Switch to Live** when ready for production

---

## Common Mistakes to Avoid

- ❌ Polling for document status instead of using webhooks (wastes your rate limit)
- ❌ Not verifying webhook HMAC signatures
- ❌ Computing the HMAC over the body alone, or comparing it with the whole `X-BoldSign-Signature` header: the signed message is `t + "." + rawBody` and the header carries `t`, `s0` and `s1`
- ❌ Parsing the JSON (or letting a framework parse it) before verifying: verification needs the raw bytes
- ❌ Reading `payload.document`: the documented payload puts the document in `payload.data` (check `data.object`)
- ❌ Detecting the verification ping from the body: use the `X-BoldSign-Event: Verification` header
- ❌ Treating a `403 Invalid Document ID` straight after a send as final: retry briefly
- ❌ Not handling `SendFailed` events — documents may silently fail
- ❌ Using `application/json` for file uploads — must use `multipart/form-data`
- ❌ Hardcoding API keys — use environment variables
- ❌ Not setting region base URL for EU/AU/CA accounts
- ❌ Assuming template or document creation is synchronous
- ❌ Tight test loops in Sandbox — 50 req/hour is very easy to hit
- ❌ Using OAuth for SaaS tenants who don't have BoldSign accounts — use Sender Identities
- ❌ Avoid using wrong email to avoid account locking for bounce. 

---



## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `401` on every call | Wrong API key, or the base URL is for another region | Check the key; call `GET /v1/brand/list` on each regional host; use the one that returns `200` |
| `403 {"error":"Invalid Document ID"}` right after a send (from `getEmbeddedSignLink` or `properties`) | The document is still processing | Retry briefly (a few seconds); in testing it cleared within about 3 seconds |
| `400 {"error":"Invalid template id."}` right after creating a template | The template is still processing | Retry briefly, or wait for the `TemplateCreated` webhook |
| `403` still returned minutes later for a document your key sent | The send failed, or the key cannot read that document (another account, environment or team) | Check for a `SendFailed` event and the document in the BoldSign web app; make sure the key comes from the account and environment (Sandbox or Live) you are looking at |
| Your own webhook endpoint returns `401` for real BoldSign events | The body was parsed and re-serialised before the HMAC check, the wrong secret is configured (each webhook has its own, see **Reveal**), or the server clock is off by more than 5 minutes | Verify the raw body; copy the secret from that webhook; check the clock |
| A webhook never arrives for a document | The webhook was added after the event, or the URL is not public HTTPS | BoldSign does not replay events from before the webhook existed; use a tunnel for local testing; check delivery status in API → Webhooks |
| Fields appear in the wrong place | `bounds` given in PDF points | Use pixels (96 per inch), or text tags |
| `429 Too Many Requests` | Sandbox allows 50 requests per hour per account | Back off using `Retry-After`; use webhooks instead of polling |
| A signer gets no email | `disableEmails: true` (embedded signing), or a bounced address | For embedded signing this is expected; use `<anyname>@boldsign.dev` addresses for test signers |

For agent and integration testing, use a dedicated developer sandbox account (https://account.boldsign.com/signup?planId=1076), so test documents, rate limits and list results are not shared with other users.

---

## Key API Endpoints

| Action | Method | Endpoint |
|---|---|---|
| Send document | POST | `/v1/document/send` |
| Get document status | GET | `/v1/document/properties?documentId={id}` |
| List documents | GET | `/v1/document/list` |
| Download document | GET | `/v1/document/download?documentId={id}` |
| Download audit trail | GET | `/v1/document/downloadauditlog?documentId={id}` |
| Remind signer | POST | `/v1/document/remind` |
| Revoke document | POST | `/v1/document/revoke` |
| Delete document | DELETE | `/v1/document/delete?documentId={id}` |
| Prefill form fields | POST | `/v1/document/prefillfields` |
| Embedded sign link | GET | `/v1/document/getembeddedsignlink` |
| Embedded send link | POST | `/v1/document/createembeddedrequest` |
| Send from template | POST | `/v1/template/send` |
| Create template | POST | `/v1/template/create` |
| List templates | GET | `/v1/template/list` |
| Create sender identity | POST | `/v1/senderIdentities/create` |

---

## Reference Files

**Stacks** (read before writing code):
- `references/stacks/nodejs.md` — Node.js & TypeScript (SDK, auth, send, embedded, webhooks, Sender Identity)
- `references/stacks/python.md` — Python SDK
- `references/stacks/dotnet.md` — .NET / C# SDK (NuGet)
- `references/stacks/php.md` — PHP SDK (Composer)

**Workflows** (read for complex patterns):
- `references/workflows/multi-signer.md` — Sequential/parallel signing, bulk send, signer tracking
- `references/workflows/embedded-saas.md` — Full embedded UI, Sender Identity lifecycle, multi-tenant architecture
