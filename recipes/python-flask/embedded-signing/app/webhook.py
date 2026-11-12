"""BoldSign webhook verification and status rules.

X-BoldSign-Signature: t=<unix seconds>, s0=<hex>[, s1=<hex>]
Signature: hex(HMAC_SHA256(webhook secret, t + "." + raw body)); s1 is present while an old secret is still valid.
https://developers.boldsign.com/webhooks/verify-webhook-events/
"""
import hashlib
import hmac
import re
import time

TOLERANCE_SECONDS = 300
FINAL = {"Completed", "Declined", "Expired", "Revoked", "SendFailed"}
STATUS = {e: e for e in ("Sent", "Viewed", "Signed", "Completed", "Declined", "Expired", "Revoked", "SendFailed", "DeliveryFailed")}


def verify_boldsign_signature(raw_body: bytes, header: str, secret: str, now=None) -> bool:
    if not header or not secret or not isinstance(raw_body, bytes):
        return False
    timestamp, signatures = "", []
    for part in header.split(","):
        key, sep, value = part.strip().partition("=")
        if not sep:
            return False
        if key == "t":
            timestamp = value.strip()
        elif re.fullmatch(r"s\d+", key):
            signatures.append(value.strip().lower())
    if not timestamp.isdigit() or not signatures:
        return False
    now = int(time.time()) if now is None else now
    if abs(now - int(timestamp)) > TOLERANCE_SECONDS:
        return False
    expected = hmac.new(secret.encode(), timestamp.encode() + b"." + raw_body, hashlib.sha256).hexdigest()
    return any(hmac.compare_digest(sig, expected) for sig in signatures)


def apply_event(store, payload: dict) -> str:
    """Applies a verified event to the stored document. Returns what happened, for logging and tests."""
    data = (payload or {}).get("data") or {}
    event = (payload or {}).get("event") or {}
    if data.get("object") != "document":
        return "ignored"
    record = store.get(data.get("documentId"))
    if not record:
        return "unknown-document"
    events = record.setdefault("events", [])
    if event.get("id") and any(e.get("id") == event.get("id") for e in events):
        return "duplicate"
    created = event.get("created")
    if record.get("lastEventCreated") and created and created < record["lastEventCreated"]:
        return "stale"
    events.append({"id": event.get("id"), "type": event.get("eventType"), "created": created})
    record["lastEventCreated"] = created or record.get("lastEventCreated")
    nxt = STATUS.get(event.get("eventType"))
    if nxt and record.get("status") not in FINAL:
        record["status"] = nxt
    if event.get("eventType") == "SendFailed":
        record["error"] = data.get("errorMessage") or "Send failed"
    store.save(record)
    return "applied"
