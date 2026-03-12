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
**API Base URL (EU):** `https://eu-api.boldsign.com`   
**API Base URL (CA):** `https://ca-api.boldsign.com`   
**API Base URL (AU):** `https://au-api.boldsign.com`    

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

**Region config:** US = default `https://api.boldsign.com`, EU = `https://eu-api.boldsign.com`


## Document Send — Critical Facts

⚠️ **Document sending is ASYNCHRONOUS.** The API returns a `documentId` immediately, but the document is still processing. Do NOT assume it's ready.

To confirm success: **listen for webhooks** — `Sent` (success) or `SendFailed` (failure with error).

**Minimum required fields:**
- `Files` (multipart) or `FileUrls` (JSON)
- At least one signer with `name`, `emailAddress`, `signerType`
- At least one `formField` with `fieldType`, `pageNumber`, `bounds`

**Signer types:** `Signer`, `Viewer`, `InPersonSigner`

**Form field types:** `Signature`, `Initial`, `DateSigned`, `Textbox`, `Checkbox`, `RadioButton`, `Dropdown`, `Image`, `EditableDate`, `Label`, `Attachment`

---

## Webhooks — Essential for Production

All async operations require webhooks to confirm status.  
BoldSign retries webhook delivery for **up to 8 hours** if your server is down.

**Setup:** BoldSign App → Settings → Webhooks → Add webhook URL

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

**Security:** Always verify `X-BoldSign-Signature` HMAC SHA-256 before processing.

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

Embed invisible text tags in your Word/PDF to auto-place fields — no x/y coordinates:

```
{{:sign:signer1}}       → Signature field for signer1
{{:initial:signer1}}    → Initials field
{{:date:signer1}}       → Date signed
{{:textbox:signer1}}    → Text input
```

BoldSign auto-converts these to real fields on upload.

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
- ❌ Not handling `SendFailed` events — documents may silently fail
- ❌ Using `application/json` for file uploads — must use `multipart/form-data`
- ❌ Hardcoding API keys — use environment variables
- ❌ Not setting region base URL for EU/AU/CA accounts
- ❌ Assuming template or document creation is synchronous
- ❌ Tight test loops in Sandbox — 50 req/hour is very easy to hit
- ❌ Using OAuth for SaaS tenants who don't have BoldSign accounts — use Sender Identities
- ❌ Avoid using wrong email to avoid account locking for bounce. 

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
