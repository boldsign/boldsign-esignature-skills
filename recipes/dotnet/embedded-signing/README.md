# Recipe: embedded signing (.NET, ASP.NET Core)

Creates a document for the **signed-in user** and opens BoldSign's signing page **inside your app** in an iframe. The document is a sample PDF with **text tags**, so no template and no field coordinates are needed. BoldSign emails are off, so signing happens only in the app. The status comes from a **verified webhook**. Tested end to end in a BoldSign sandbox: create, sign in the iframe, Completed, webhook, status page.

| | |
| --- | --- |
| Stack | .NET 8 SDK, ASP.NET Core minimal API |
| SDK | `BoldSign.Api` **7.3.4**, pinned exactly with `packages.lock.json` (tests: xUnit 2.9.3) |
| BoldSign calls | `GET /v1/brand/list` (config check), `POST /v1/document/send` (useTextTags, disableEmails), `GET /v1/document/getEmbeddedSignLink` |
| Pages | `/documents` (my documents), `/documents/{id}/sign` (signing in an iframe), `/signing/complete` (redirect when signing ends) |
| Webhook | `POST /webhooks/boldsign`, verifies `X-BoldSign-Signature` |

## 1. Set up

1. Create a free developer sandbox: https://account.boldsign.com/signup?planId=1076. Use a dedicated sandbox account for testing, not a company or team account.
2. In BoldSign, open **API > API Key**, generate a key (Sandbox environment) and note your account's region (US, EU, CA or AU).
3. Copy the folder, then:
   ```bash
   dotnet restore App.Tests --locked-mode
   cp .env.example .env                   # then fill in BOLDSIGN_API_KEY, BOLDSIGN_REGION, SIGNER_NAME, SIGNER_EMAIL
   dotnet run --project App -- check      # one read-only call: must print "OK ... returned 200"
   ```
   `SIGNER_NAME` and `SIGNER_EMAIL` stand in for your app's signed-in user. Pass your own `currentUser` to `Routes.Build` (`App/Routes.cs`, called from `App/Program.cs`): the signing link goes to whoever it returns.

## 2. Run

```bash
dotnet run --project App
```

1. Open http://localhost:3000/documents and press **Create a document and sign it here**.
2. The signing page opens with BoldSign inside the iframe. Accept the disclosure, click the signature field, **Save & use**, then **Finish**.
3. BoldSign redirects the iframe to `/signing/complete`, which breaks out of the iframe and shows the status. In BoldSign, the document is **Completed**.

- **Fresh links:** each visit to `/documents/{id}/sign` gets a fresh embedded link, so an expired link is never a problem.
- **The first link can be refused:** right after the document is created, `GetEmbeddedSignLink` can return `403 Invalid Document ID` for a few seconds. `RetrySoon` in `App/BoldSignService.cs` retries it.
- **Owner only:** only the document's signer can open its signing page; anyone else gets 404.

## 3. Receive webhooks

BoldSign must reach the app over public HTTPS. For local testing:

1. Run `cloudflared tunnel --url http://localhost:3000` and set `APP_BASE_URL` to the `https://...` address it prints, so the redirect after signing comes back through it.
2. In BoldSign, open **API > Webhooks > Create webhook**, enter `https://<that address>/webhooks/boldsign`, and select at least Sent, SendFailed, Viewed, Signed, Completed, Declined, Expired and Revoked. Saving sends a `Verification` request, and the app answers 200.
3. Click **Reveal**, put that secret in `BOLDSIGN_WEBHOOK_SECRET`, and restart.
4. Create and sign a new document: the status moves from Queued to Sent, Viewed and Completed.

The verifier (`App/Webhook.cs`) works as follows:
- it reads the raw body bytes from `Request.Body`;
- it answers `X-BoldSign-Event: Verification` with 200;
- it checks `t=<unix>, s0=<hex>[, s1=<hex>]` against `hex(HMAC_SHA256(secret, t + "." + rawBody))` with `CryptographicOperations.FixedTimeEquals`;
- it rejects events more than 300 seconds old;
- it reads the document from `payload.data`;
- it ignores duplicates, older events and changes after a final status.

## 4. Test

```bash
dotnet test App.Tests
```

The tests make no network calls. They check:
- the verifier, the status rules and the webhook route: ping 200, forged 401, signed Completed updates the page;
- that the request uses text tags with BoldSign emails off;
- that the signing link redirects back to the app and is retried after a 403;
- the iframe page, the owner-only check and the redirect page.

## Files

| File | What it does |
| --- | --- |
| `App/BoldSignService.cs` | SDK client (regional base URL), create with text tags and `DisableEmails`, embedded signing link, retry, safe error summary |
| `App/Routes.cs` | Routes, the iframe page (CSP `frame-src https://app.boldsign.com`, `no-store`), the redirect page, `currentUser` |
| `App/Webhook.cs` | Signature verifier and status rules |
| `App/SamplePdf.cs` | One-page PDF with white `{{sign|1|*}}` and `{{date|1|*}}` tags |
| `App/Config.cs`, `App/Store.cs`, `App/Program.cs` | Settings and `.env` loading, a JSON file store (replace it with your database), start-up and the `check` command |
| `App.Tests/` | xUnit tests on an in-memory test server |

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `dotnet run --project App -- check` prints 401 | Wrong key, or `BOLDSIGN_REGION` does not match the account |
| The iframe stays blank | Check the browser console. Your own CSP must allow `frame-src https://app.boldsign.com`, and an ad blocker may block it |
| After Finish the app page does not appear | `APP_BASE_URL` must be the address the browser uses (the tunnel address when testing through it) |
| Status stays `Queued` | No webhook reaches the app: see section 3 |
| `429 Too Many Requests` | The sandbox allows 50 requests an hour, and every visit to the signing page uses one |
