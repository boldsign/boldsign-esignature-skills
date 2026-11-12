# Recipe: send a document for signature (Python, Flask)

Sends a sample PDF with **text tags** to one signer, so no template and no field coordinates are needed. BoldSign emails the signer, and the app tracks the status through a **verified webhook**. Tested end to end in a BoldSign sandbox: send, sign, Completed, webhook, status page.

| | |
| --- | --- |
| Stack | Python 3.10 or newer, Flask 3.1.3 |
| SDK | `boldsign` **3.3.4**, pinned exactly |
| BoldSign calls | `GET /v1/brand/list` (config check), `POST /v1/document/send` (useTextTags), `GET /v1/document/getEmbeddedSignLink` ("Sign now"), `GET /v1/document/properties` (fallback only) |
| Webhook | `POST /webhooks/boldsign`, verifies `X-BoldSign-Signature` |

## 1. Set up

1. Create a free developer sandbox: https://account.boldsign.com/signup?planId=1076. Use a dedicated sandbox account for testing, not a company or team account.
2. In BoldSign, open **API > API Key**, generate a key (Sandbox environment) and note your account's region (US, EU, CA or AU).
3. Copy the folder, then:
   ```bash
   python3 -m venv .venv && . .venv/bin/activate
   pip install -r requirements-dev.txt
   cp .env.example .env              # then fill in BOLDSIGN_API_KEY, BOLDSIGN_REGION, SIGNER_EMAIL
   python scripts/check_boldsign.py  # one read-only call: must print "OK ... returned 200"
   ```

## 2. Run

```bash
python run.py
```

1. Open http://localhost:3000/documents and press **Send the sample PDF**. The document appears with status `Queued`.
2. The signer receives BoldSign's email, or press **Sign now** to open the signing page.
3. Sign: accept the disclosure, click the signature field, **Save & use**, then **Finish**. In BoldSign, the document is **Completed**.

- **Where the PDF comes from:** `app/sample_pdf.py` builds a one-page PDF with white `{{sign|1|*}}` and `{{date|1|*}}` tags beside the labels. `python scripts/make_sample_pdf.py` writes it to `sample.pdf` so you can look at it. Use your own PDF with the same tags in production.
- **The first call can be refused:** right after a send, the next call on the document can return `403 Invalid Document ID` for a few seconds. `retry_soon` in `app/boldsign_client.py` retries it.
- **Signing straight after a send:** if you open the signing page within seconds of sending, BoldSign can ask you to reload the document. Reload and sign again.

## 3. Receive webhooks

BoldSign must reach the app over public HTTPS. For local testing:

1. Run `cloudflared tunnel --url http://localhost:3000` and copy the `https://...` address.
2. In BoldSign, open **API > Webhooks > Create webhook**, enter `https://<that address>/webhooks/boldsign`, and select at least Sent, SendFailed, Viewed, Signed, Completed, Declined, Expired and Revoked. Saving sends a `Verification` request, and the app answers 200.
3. Click **Reveal**, put that secret in `BOLDSIGN_WEBHOOK_SECRET`, and restart. Do this before signing: BoldSign does not replay earlier events.

The verifier (`app/webhook.py`) works as follows:
- it reads the raw body (`request.get_data()`);
- it answers `X-BoldSign-Event: Verification` with 200;
- it checks `t=<unix>, s0=<hex>[, s1=<hex>]` against `hex(HMAC_SHA256(secret, t + "." + rawBody))` with `hmac.compare_digest`;
- it rejects events more than 300 seconds old;
- it reads the document from `payload.data`;
- it ignores duplicates, older events and changes after a final status.

## 4. Test

```bash
python -m pytest -q
```

The tests make no network calls. They check:
- the sample PDF and its text tags, and that the send uses text tags with no form fields;
- `POST /documents` and the retry (400 and 403 only);
- the verifier, the status rules and the webhook route: ping 200, forged 401, signed Completed updates the page.

## Files

| File | What it does |
| --- | --- |
| `app/boldsign_client.py` | SDK client (regional base URL), send with text tags, signing link, status read, retry, safe error summary |
| `app/webhook.py` | Signature verifier and status rules |
| `app/__init__.py` | Routes: `POST /documents`, `GET /documents`, `GET /documents/<id>/sign`, `POST /documents/<id>/refresh`, `POST /webhooks/boldsign` |
| `app/sample_pdf.py` | One-page PDF with white text tags |
| `app/config.py`, `app/store.py`, `run.py` | Settings and `.env` loading, a JSON file store (replace it with your database), start-up |
| `scripts/check_boldsign.py` | The read-only key and region check |

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `check_boldsign.py` prints 401 | Wrong key, or `BOLDSIGN_REGION` does not match the account |
| Status stays `Queued` | No webhook reaches the app: see section 3, or press **Refresh** |
| "This document's state has been modified" on the signing page | The document was opened seconds after sending: reload it |
| `429 Too Many Requests` | The sandbox allows 50 requests an hour: wait, and avoid polling |
