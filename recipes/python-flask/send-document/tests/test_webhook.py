import hashlib
import hmac
import json
import time

from app import create_app
from app.webhook import apply_event, verify_boldsign_signature
from conftest import SECRET, event, signed_headers

BODY = event("doc-1", "Completed").encode()
NOW = int(time.time())


def sig(key, t=NOW, body=BODY):
    return hmac.new(key.encode(), f"{t}.".encode() + body, hashlib.sha256).hexdigest()


def test_verifier_accepts_s0_and_rotated_s1():
    assert verify_boldsign_signature(BODY, f"t={NOW}, s0={sig(SECRET)}", SECRET)
    assert verify_boldsign_signature(BODY, f"t={NOW}, s0={sig('old')}, s1={sig(SECRET)}", SECRET)


def test_verifier_rejects_bad_signatures():
    body_only = hmac.new(SECRET.encode(), BODY, hashlib.sha256).hexdigest()
    for header in [f"t={NOW}, s0={sig('wrong')}", f"t={NOW}, s0={body_only}", f"t={NOW - 600}, s0={sig(SECRET, NOW - 600)}",
                   f"t={NOW}, s0=abc", f"s0={sig(SECRET)}", "", "garbage"]:
        assert not verify_boldsign_signature(BODY, header, SECRET), header
    assert not verify_boldsign_signature(BODY.replace(b"doc-1", b"doc-2"), f"t={NOW}, s0={sig(SECRET)}", SECRET)


def test_apply_event_rules(store):
    store.save({"documentId": "doc-1", "status": "Queued", "events": []})
    sent = json.loads(event("doc-1", "Sent", "e1", 100))
    assert apply_event(store, sent) == "applied" and store.get("doc-1")["status"] == "Sent"
    assert apply_event(store, sent) == "duplicate"
    assert apply_event(store, json.loads(event("doc-1", "Completed", "e2", 200))) == "applied"
    assert apply_event(store, json.loads(event("doc-1", "Viewed", "e3", 150))) == "stale"
    assert apply_event(store, json.loads(event("doc-1", "Revoked", "e4", 300))) == "applied"
    assert store.get("doc-1")["status"] == "Completed"
    assert apply_event(store, json.loads(event("other", "Completed"))) == "unknown-document"


def test_webhook_route(cfg, store):
    store.save({"documentId": "doc-1", "title": "T", "signer": {"name": "S", "email": "signer@boldsign.dev"}, "status": "Queued", "events": [], "createdAt": "2026-01-01"})
    client = create_app(cfg, None, store).test_client()
    assert client.post("/webhooks/boldsign", headers={"X-BoldSign-Event": "Verification"}, data="{}").status_code == 200
    raw = event("doc-1", "Completed")
    assert client.post("/webhooks/boldsign", headers=signed_headers(raw, "not-the-secret"), data=raw).status_code == 401
    assert store.get("doc-1")["status"] == "Queued"
    assert client.post("/webhooks/boldsign", headers=signed_headers(raw), data=raw).status_code == 200
    assert store.get("doc-1")["status"] == "Completed"
    assert "<b>Completed</b>" in client.get("/documents").get_data(as_text=True)
