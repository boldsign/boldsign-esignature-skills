# BoldSign — Python Integration Reference

## SDK Install

```bash
pip install boldsign-python-sdk
```

Docs: https://developers.boldsign.com/sdks/python-sdk/

---

## Setup & Authentication

```python
import boldsign
from boldsign.api import DocumentApi, TemplateApi
import os

# API Key auth
configuration = boldsign.Configuration(
    api_key=os.environ['BOLDSIGN_API_KEY']
)

# EU region
configuration = boldsign.Configuration(
    host='https://eu-api.boldsign.com',
    api_key=os.environ['BOLDSIGN_API_KEY']
)

# OAuth Bearer Token
configuration = boldsign.Configuration(
    access_token=os.environ['BOLDSIGN_ACCESS_TOKEN']
)

api_client = boldsign.ApiClient(configuration)
document_api = DocumentApi(api_client)
```

---

## Send a Document for Signature

```python
from boldsign.models import (
    SendForSign, DocumentSigner, FormField, Rectangle
)
import os

def send_document():
    bounds = Rectangle(x=100, y=200, width=200, height=50)

    signature_field = FormField(
        field_type='Signature',
        page_number=1,
        bounds=bounds,
        is_required=True
    )

    signer = DocumentSigner(
        name='John Doe',
        email_address='john@example.com',
        signer_type='Signer',
        form_fields=[signature_field]
    )

    send_request = SendForSign(
        title='Service Agreement',
        message='Please sign this agreement.',
        signers=[signer],
        files=['contract.pdf']
    )
    result = document_api.send_document(send_request)

    print(f'Document ID: {result.document_id}')
    # NOTE: Async — listen for webhooks to confirm Sent status
    return result.document_id
```

---

## Send from Template

```python
from boldsign.models import SendForSignFromTemplate, Roles

def send_from_template(template_id: str):
    role = Roles(
        role_index=1,
        signer_name='Jane Smith',
        signer_email='jane@example.com'
    )

    send_request = SendForSignFromTemplate(roles=[role])
    result = template_api.send_using_template(template_id=template_id, send_for_sign_from_template_form=send_request)
    return result.document_id
```

---

## Embedded Signing Link

```python
from datetime import datetime, timedelta

def get_embedded_sign_link(document_id: str, signer_email: str) -> str:
    expiry = datetime.utcnow() + timedelta(hours=24)

    result = document_api.get_embedded_sign_link(
        document_id=document_id,
        signer_email=signer_email,
        sign_link_valid_till=expiry,
        redirect_url='https://yourapp.com/signing-complete'
    )
    return result.sign_link  # embed in iframe
```

---

## Webhook Handler (Flask)

```python
from flask import Flask, request, abort
import hmac
import hashlib
import json
import os

app = Flask(__name__)

@app.route('/webhooks/boldsign', methods=['POST'])
def boldsign_webhook():
    # 1. Verify HMAC
    signature = request.headers.get('X-BoldSign-Signature', '')
    secret = os.environ['BOLDSIGN_WEBHOOK_SECRET'].encode()
    expected = hmac.new(secret, request.data, hashlib.sha256).hexdigest()

    if not hmac.compare_digest(signature, expected):
        abort(401)

    # 2. Handle event
    payload = request.json
    event_type = payload['event']['eventType']
    document_id = payload['document']['documentId']

    if event_type == 'Completed':
        handle_completed(document_id)
    elif event_type == 'SendFailed':
        print(f'Send failed: {payload["document"]}')
    elif event_type == 'Declined':
        print(f'Declined: {document_id}')

    return 'OK', 200

def handle_completed(document_id: str):
    # Download signed PDF
    pdf_bytes = document_api.download_document(document_id)
    with open(f'signed/{document_id}.pdf', 'wb') as f:
        f.write(pdf_bytes)

    # Download audit trail
    audit_bytes = document_api.download_audit_log(document_id)
    with open(f'signed/{document_id}-audit.pdf', 'wb') as f:
        f.write(audit_bytes)
```

---

## Webhook Handler (FastAPI)

```python
from fastapi import FastAPI, Request, HTTPException
import hmac, hashlib, os

app = FastAPI()

@app.post('/webhooks/boldsign')
async def boldsign_webhook(request: Request):
    body = await request.body()
    signature = request.headers.get('X-BoldSign-Signature', '')
    secret = os.environ['BOLDSIGN_WEBHOOK_SECRET'].encode()
    expected = hmac.new(secret, body, hashlib.sha256).hexdigest()

    if not hmac.compare_digest(signature, expected):
        raise HTTPException(status_code=401, detail='Invalid signature')

    payload = await request.json()
    event_type = payload['event']['eventType']

    match event_type:
        case 'Completed':
            await handle_completed(payload['document']['documentId'])
        case 'SendFailed':
            print(f"Send failed: {payload['document']}")

    return {'status': 'ok'}
```

---

## Get Document Status

```python
def get_document_status(document_id: str):
    details = document_api.get_properties(document_id=document_id)
    print(f'Status: {details.status}')  # InProgress, Completed, Declined, Expired
    return details
```

---

## Sender Identity — Register and Send on Behalf of Tenant

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
    sender_api.request_for_identity_approval(
        ResendSenderIdentityRequest(email=email)
    )
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

## Rate Limit Handling

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

## SDK Links
- PyPI: `pip install boldsign-python-sdk`
- GitHub: https://github.com/boldsign/boldsign-python-sdk
- Full docs: https://developers.boldsign.com/sdks/python-sdk/
