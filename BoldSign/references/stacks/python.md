# BoldSign — Python Integration Reference

**Docs:** https://developers.boldsign.com/sdks/python-sdk/  
**PyPI:** https://pypi.org/project/boldsign/

---

## 1. Installation
```bash
pip install boldsign
```

---

## 2. Authentication & API Client Setup
```python
import boldsign
from boldsign import DocumentApi, TemplateApi
```

### API Key Authentication
```python
configuration = boldsign.Configuration(api_key="Your-API-Key-Here") 
```

### OAuth Bearer Token
```python
configuration = boldsign.Configuration(access_token="Your-Bearer-Token-Here")
```

### Region‑specific Base URL (EU Example)
```python
configuration = boldsign.Configuration(api_key = "Your-API-Key-Here", host = "https://api-eu.boldsign.com")
```

### Create API Clients
```python
with boldsign.ApiClient(configuration) as api_client:
    document_api = DocumentApi(api_client)
    template_api = TemplateApi(api_client)
```

---

## 3. Send a Document for Signature
```python
from boldsign import SendForSign, DocumentSigner, FormField, Rectangle

def send_document():
    signature_field = FormField(
        fieldType="Signature",
        pageNumber=1,
        bounds=Rectangle(x=100, y=200, width=200, height=50),
        isRequired=True
    )

    signer = DocumentSigner(
        name="John Doe",
        emailAddress="john@example.com",
        signerType="Signer",
        formFields=[signature_field]
    )

    send_for_sign = SendForSign(
        title="Service Agreement",
        message="Please sign this agreement.",
        signers=[signer],
        files=["contract.pdf"]
    )

    response = document_api.send_document(
        send_for_sign=send_for_sign
    )

    return response.document_id
```

---

## 4. Send Document Using a Template
```python
from boldsign import SendForSignFromTemplateForm, Role

def send_from_template(template_id: str):
    role = Role(
        roleIndex=1,
        signerName="Jane Smith",
        signerEmail="jane@example.com"
    )

    request = SendForSignFromTemplateForm(
        roles=[role]
    )

    response = template_api.send_using_template(
        template_id=template_id,
        send_for_sign_from_template_form=request
    )

    return response.document_id
```

---

## 5. Generate Embedded Signing Link
```python
from datetime import datetime, timedelta

def get_embedded_signing_link(document_id: str, signer_email: str) -> str:
    response = document_api.get_embedded_sign_link(
        document_id=document_id,
        signer_email=signer_email,
        redirect_url="https://yourapp.com/signing-complete",
        sign_link_valid_till=datetime.utcnow() + timedelta(hours=24)
    )
    return response
```

---

## 6. Secure Webhook Handler (Flask)
```python
from flask import Flask, request, abort
import hmac
import hashlib
import json
import os

app = Flask(__name__)

@app.route("/webhooks/boldsign", methods=["POST"])
def boldsign_webhook():
    # BoldSign's URL check when the webhook is added: identified by the header, answered with 200
    if request.headers.get("X-BoldSign-Event") == "Verification":
        return "", 200

    raw_body = request.get_data()  # raw bytes, exactly as sent
    if not verify_signature(request.headers.get("X-BoldSign-Signature", ""), raw_body):
        abort(401)

    payload = json.loads(raw_body)          # parse only after verifying
    event_type = payload["event"]["eventType"]
    data = payload.get("data") or {}        # the document is in data, not document
    if data.get("object") != "document":
        return "", 200
    document_id = data["documentId"]

    if event_type == "Completed":
        handle_completed(document_id)
    elif event_type == "SendFailed":
        print(f'Send failed: {document_id} {data.get("errorMessage")}')
    elif event_type == "Declined":
        print(f'Declined: {document_id}')

    return "OK", 200
```

### Signature Verification Helper

The header is `t=<unix seconds>, s0=<hex>[, s1=<hex>]` and the signature is HMAC SHA256 over `t + "." + raw body`. Check both `s0` and `s1` (`s1` is present while an old secret is still valid) and reject events older than 5 minutes.

```python
import json
import time

TOLERANCE_SECONDS = 300

def verify_signature(header: str, raw_body: bytes) -> bool:
    secret = os.environ.get("BOLDSIGN_WEBHOOK_SECRET", "")
    if not header or not secret:
        return False
    timestamp, signatures = "", []
    for part in header.split(","):
        key, sep, value = part.strip().partition("=")
        if not sep:
            return False
        if key == "t":
            timestamp = value
        elif key.startswith("s"):
            signatures.append(value.lower())
    if not timestamp.isdigit() or not signatures:
        return False
    if abs(int(time.time()) - int(timestamp)) > TOLERANCE_SECONDS:
        return False

    expected = hmac.new(secret.encode(), timestamp.encode() + b"." + raw_body, hashlib.sha256).hexdigest()
    return any(hmac.compare_digest(sig, expected) for sig in signatures)
```

---

## 7. Download Signed Document & Audit Log
```python
def handle_completed(document_id: str):
    pdf_stream = document_api.download_document(document_id)
    with open(f"signed/{document_id}.pdf", "wb") as f:
        f.write(pdf_stream.read())

    audit_stream = document_api.download_audit_log(document_id)
    with open(f"signed/{document_id}_audit.pdf", "wb") as f:
        f.write(audit_stream.read())
```

---

## 8. Get Document Status
```python
def get_document_status(document_id: str):
    details = document_api.get_properties(document_id=document_id)
    return details.status
```

---

## 9. Sender Identity — Register and Send on Behalf of Tenant

```python
from boldsign.api import SenderIdentitiesApi
from boldsign.models import CreateSenderIdentityRequest, ResendSenderIdentityRequest

sender_api = SenderIdentitiesApi(api_client)

def onboard_tenant(name: str, email: str):
    # Step 1: Register identity
    sender_api.create_sender_identities(
        CreateSenderIdentityRequest(name=name, email=email)
    )
    # Step 2: Send approval email to tenant
    sender_api.resend_invitation_sender_identities(email=email)
    # Tenant clicks Approve in email → SenderIdentityApproved webhook fires

def send_on_behalf_of(tenant_email: str, title: str, signer, file_path: str):
        request = SendForSign(
            on_behalf_of=tenant_email,  # ← key field
            title=title,
            signers=[signer],
            files=["YOUR_FILE_PATH"]
        )
        return document_api.send_document(request)
```

---

## 10. Rate Limit Handling

```python
import time

def call_with_retry(fn, max_retries=1):
    for attempt in range(max_retries + 1):
        try:
            return fn()
        except Exception as e:
            if hasattr(e, 'status') and e.status == 429:
                retry_after = int(getattr(e, 'headers', {}).get('retry-after', 60))
                print(f"Rate limit hit. Waiting {retry_after}s")
                time.sleep(retry_after)
            else:
                raise
    raise Exception("Max retries exceeded")
```

**Sandbox: 50 req/hour. Production: 2,000 req/hour (account-level).**  
Use webhooks — not polling — to avoid burning your quota.

---