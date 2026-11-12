import hashlib
import hmac
import json
import os
import sys
import time
import uuid

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from app.config import Config  # noqa: E402
from app.store import Store  # noqa: E402

SECRET = "test-webhook-secret"


@pytest.fixture
def cfg():
    return Config(api_key="test", webhook_secret=SECRET, signer_name="Test Signer", signer_email="signer@boldsign.dev")


@pytest.fixture
def store(tmp_path):
    return Store(str(tmp_path / "documents.json"))


def event(document_id, event_type, event_id=None, created=None):
    return json.dumps({"event": {"id": event_id or str(uuid.uuid4()), "created": created or int(time.time()), "eventType": event_type, "environment": "Test"},
                       "data": {"object": "document", "documentId": document_id, "status": event_type}})


def signed_headers(body: str, secret: str = SECRET, t: int | None = None):
    t = t or int(time.time())
    s0 = hmac.new(secret.encode(), f"{t}.{body}".encode(), hashlib.sha256).hexdigest()
    return {"Content-Type": "application/json", "X-BoldSign-Event": "Completed", "X-BoldSign-Signature": f"t={t}, s0={s0}"}
