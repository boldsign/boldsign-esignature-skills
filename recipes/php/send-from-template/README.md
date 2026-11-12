# Recipe: send from a template (PHP)

Sends a document from a BoldSign **template**, mapping each template role to a signer from your app, and tracks its status through a **verified webhook**. A read-only check confirms the key, the region and the template's roles before anything is sent. Tested end to end in a BoldSign sandbox: role check, send, sign, Completed, webhook, status page.

| | |
| --- | --- |
| Stack | PHP 8.2 or newer (ext-curl, ext-json, ext-mbstring), no framework: the built-in server for local use |
| SDK | `boldsign/boldsign-php` **3.3.4**, pinned exactly with `composer.lock` (tests: PHPUnit 11.5.57) |
| BoldSign calls | `GET /v1/brand/list` and `GET /v1/template/properties` (checks), `POST /v1/template/send`, `GET /v1/document/getEmbeddedSignLink`, `GET /v1/document/properties` (fallback only) |
| Webhook | `POST /webhooks/boldsign`, verifies `X-BoldSign-Signature` |

## 1. Set up

1. Create a free developer sandbox: https://account.boldsign.com/signup?planId=1076. Use a dedicated sandbox account for testing, not a company or team account.
2. In BoldSign, open **API > API Key**, generate a key (Sandbox environment) and note your account's region (US, EU, CA or AU).
3. In BoldSign, create the template under **Templates > Create template**:
   - upload a PDF;
   - add a role, for example **Employee**;
   - place that role's Signature and Date signed fields;
   - save, and copy the **template ID**.
4. Copy the folder, then:
   ```bash
   composer install
   cp .env.example .env           # fill in BOLDSIGN_API_KEY, BOLDSIGN_REGION, BOLDSIGN_TEMPLATE_ID, BOLDSIGN_TEMPLATE_ROLES, SIGNER_EMAIL
   composer check:boldsign        # read-only: key and region, then the template and its roles
   ```
   `BOLDSIGN_TEMPLATE_ROLES` lists the template's roles as `Name:index`, for example `Employee:1` or `Employee:1, Manager:2`. The check fails if they do not match the template.

## 2. Map roles to your users

`src/Roles.php` decides who fills each role. `Roles::signerForRole($role, $user)` returns `['name' => ..., 'email' => ...]`, and the demo fills every role with `SIGNER_NAME` and `SIGNER_EMAIL`. Change it to your data model, for example the employee for **Employee** and their manager for **Manager**. `Roles::map` refuses to send if any role has no signer.

## 3. Run

```bash
composer start   # php -S 127.0.0.1:3000 public/index.php
```

1. Open http://localhost:3000/documents and press **Send the template**. The document appears with status `Queued`.
2. Each signer receives BoldSign's email, or press **Sign now** to open the signing page.
3. Sign: accept the disclosure, click the signature field, **Save & use**, then **Finish**. In BoldSign, the document is **Completed**.

A template created seconds ago can answer `400 Invalid template id.` for a moment, and a new document `403 Invalid Document ID`. `retrySoon` in `src/BoldSignClient.php` retries both briefly.

## 4. Receive webhooks

BoldSign must reach the app over public HTTPS. For local testing:

1. Run `cloudflared tunnel --url http://localhost:3000` and copy the `https://...` address.
2. In BoldSign, open **API > Webhooks > Create webhook**, enter `https://<that address>/webhooks/boldsign`, and select at least Sent, SendFailed, Viewed, Signed, Completed, Declined, Expired and Revoked. Saving sends a `Verification` request, and the app answers 200.
3. Click **Reveal**, put that secret in `BOLDSIGN_WEBHOOK_SECRET`, and restart. Do this before signing: BoldSign does not replay earlier events.

The verifier (`src/Webhook.php`) works as follows:
- it reads the raw body (`php://input`);
- it answers `X-BoldSign-Event: Verification` with 200;
- it checks `t=<unix>, s0=<hex>[, s1=<hex>]` against `hex(HMAC_SHA256(secret, t + "." + rawBody))` with `hash_equals`;
- it rejects events more than 300 seconds old;
- it reads the document from `payload.data`;
- it ignores duplicates, older events and changes after a final status.

## 5. Test

```bash
composer test
```

The tests make no network calls. They check:
- the role parsing, the mapping and the comparison with the template;
- that the send uses the template ID and SDK `Role` objects, and retries the first 400;
- `POST /documents`, including the missing template ID case;
- the verifier, the status rules and the webhook route: ping 200, forged 401, signed Completed updates the page.

## Files

| File | What it does |
| --- | --- |
| `src/Roles.php` | Role to signer mapping (edit `signerForRole`), and the comparison with the template's roles |
| `src/BoldSignClient.php` | SDK client (regional base URL), send from template, template roles, signing link, retry, safe error summary |
| `src/Webhook.php` | Signature verifier and status rules |
| `src/App.php` | Routes: `POST /documents`, `GET /documents`, `GET /documents/{id}/sign`, `POST /documents/{id}/refresh`, `POST /webhooks/boldsign` |
| `src/Config.php`, `src/Store.php`, `src/Request.php`, `src/Response.php`, `public/index.php` | Settings and `.env` loading (including `BOLDSIGN_TEMPLATE_ROLES`), a JSON file store (replace it with your database), the request and response, the front controller |
| `scripts/check-boldsign.php` | Read-only checks: key and region, template and roles |

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `check:boldsign` prints 401 | Wrong key, or `BOLDSIGN_REGION` does not match the account |
| `Role mismatch` | Make `BOLDSIGN_TEMPLATE_ROLES` match the template's role names and order |
| `template/properties failed` | Check `BOLDSIGN_TEMPLATE_ID`, and that the template is in the same account and environment as the key |
| Status stays `Queued` | No webhook reaches the app: see section 4, or press **Refresh** |
| `429 Too Many Requests` | The sandbox allows 50 requests an hour: wait, and avoid polling |
