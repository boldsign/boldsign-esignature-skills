# Recipe: send a document for signature (Node.js, Express)

Sends a PDF to one signer with **BoldSign text tags**, so there is no template and no field coordinates, and tracks the document's status through a **verified webhook**. Tested end to end in a BoldSign sandbox: send, sign, Completed, webhook, status page.

| | |
| --- | --- |
| Stack | Node.js 18.18 or newer, Express 4.22.3 |
| SDK | `boldsign` **3.3.4**, pinned exactly |
| BoldSign calls | `GET /v1/brand/list` (config check), `POST /v1/document/send` (useTextTags), `GET /v1/document/getEmbeddedSignLink`, `GET /v1/document/properties` (fallback only) |
| Webhook | `POST /webhooks/boldsign`, verifies `X-BoldSign-Signature` |

## 1. Set up

1. Create a free developer sandbox: https://account.boldsign.com/signup?planId=1076. Use a dedicated sandbox account for testing, not a company or team account.
2. In BoldSign, open **API > API Key**, generate a key (Sandbox environment) and note your account's region (US, EU, CA or AU).
3. Copy the folder, then:
   ```bash
   npm install
   cp .env.example .env   # then fill in BOLDSIGN_API_KEY, BOLDSIGN_REGION and SIGNER_EMAIL
   npm run check:boldsign # one read-only call: must print "OK ... returned 200"
   ```
   `.env` is git-ignored. In a hosted environment (a CI system, Bold Create, Codespaces), set the same names as secrets instead; variables already set are not overridden.

## 2. Run

```bash
npm start
```

1. Open http://localhost:3000/documents and press **Send the sample PDF**. The document appears with status `Queued`.
2. The signer receives BoldSign's email. Or press **Sign now** to open the signing page directly.
3. Sign: accept the disclosure, click the signature field, **Save & use**, then **Finish**. In BoldSign, the document is now **Completed**.

The send is asynchronous: BoldSign returns the document ID at once and confirms with a `Sent` or `SendFailed` webhook. For a few seconds after the send, the next call on the document can return `403 Invalid Document ID`; `retrySoon` in `src/boldsign.js` retries that briefly.

## 3. Receive webhooks

BoldSign must reach the app over public HTTPS. For local testing:

1. `cloudflared tunnel --url http://localhost:3000` and copy the `https://...trycloudflare.com` address.
2. In BoldSign, open **API > Webhooks > Create webhook**, enter `https://<that address>/webhooks/boldsign`, and select at least Sent, SendFailed, Viewed, Signed, Completed, Declined, Expired and Revoked. Saving sends a `Verification` request, and the app answers 200.
3. Click **Reveal**, put that secret in `BOLDSIGN_WEBHOOK_SECRET`, and restart the app.
4. Send a new document and sign it: the status page moves from Queued to Sent, Viewed and Completed.

Do this before signing: BoldSign does not replay events from before the webhook existed. Without a webhook, **Refresh** makes one `properties` call.

### How the webhook is verified (`src/webhook.js`)

1. The route reads the **raw body**; it is mounted before `express.json()`.
2. `X-BoldSign-Event: Verification` gets 200 and nothing else.
3. `X-BoldSign-Signature` is `t=<unix seconds>, s0=<hex>[, s1=<hex>]`. The expected value is `hex(HMAC_SHA256(secret, t + "." + rawBody))`, compared with `s0` and `s1` in constant time.
4. Events more than 300 seconds old are rejected. Any failure returns 401 and changes nothing.
5. The JSON is parsed only after that. The document is in `payload.data`. Duplicate event IDs, older events and changes after a final status (Completed, Declined, Expired, Revoked, SendFailed) are ignored.

## 4. Test

```bash
npm test
```

The tests make no network calls. They check the verifier (valid `s0`, rotated `s1`, wrong secret, a body-only HMAC, a stale timestamp, a tampered body, malformed headers), the status rules, the webhook route (ping 200, forged 401, signed Completed updates the status page), that the send request uses text tags for one signer with no form fields, and the retry.

## Files

| File | What it does |
| --- | --- |
| `src/boldsign.js` | SDK clients (regional base URL), send with text tags, signing link, retry, safe error summary |
| `src/webhook.js` | Signature verifier and status rules |
| `src/sample-pdf.js` | One-page PDF with white `{{sign|1|*}}` and `{{date|1|*}}` tags |
| `src/app.js` | Routes: `POST /documents`, `GET /documents`, `GET /documents/:id/sign`, `POST /documents/:id/refresh`, `POST /webhooks/boldsign` |
| `src/config.js`, `src/store.js`, `src/server.js` | Settings and `.env` loading, a JSON file store (replace it with your database), start-up |
| `scripts/check-boldsign.js` | The read-only key and region check |

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `check:boldsign` prints 401 | Wrong key, or `BOLDSIGN_REGION` does not match the account |
| Status stays `Queued` | No webhook reaches the app: see section 3, or press **Refresh** |
| Webhook returns 401 for real events | `BOLDSIGN_WEBHOOK_SECRET` is not that webhook's secret, or the server clock is more than 5 minutes off |
| `429 Too Many Requests` | The sandbox allows 50 requests an hour: wait, and avoid polling |

Never log or commit the API key. SDK error objects include the request headers, so this recipe logs only `errorSummary(err)`.
