"""Flask app: send from a BoldSign template (each role mapped to a signer from the app), a status page, and a verified
BoldSign webhook."""
import json
import logging
from datetime import datetime, timezone
from html import escape
from urllib.parse import quote

from flask import Flask, redirect, request

from .boldsign_client import error_summary
from .roles import map_roles
from .webhook import FINAL, apply_event, verify_boldsign_signature

log = logging.getLogger("boldsign-recipe")
PAGE = "<!doctype html><meta charset='utf-8'><title>Documents</title><style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}</style>"


def create_app(cfg, boldsign, store) -> Flask:
    app = Flask(__name__)

    @app.post("/webhooks/boldsign")
    def webhook():
        if request.headers.get("X-BoldSign-Event") == "Verification":  # BoldSign's URL check
            return "", 200
        if not cfg.webhook_secret:
            return "Webhook secret not configured", 500
        raw = request.get_data()  # raw bytes, exactly as sent
        if not verify_boldsign_signature(raw, request.headers.get("X-BoldSign-Signature", ""), cfg.webhook_secret):
            log.warning("webhook: signature check failed")
            return "Invalid signature", 401
        try:
            payload = json.loads(raw)
        except ValueError:
            return "Invalid JSON", 400
        result = apply_event(store, payload)
        log.info("webhook: %s %s %s", payload.get("event", {}).get("eventType"), (payload.get("data") or {}).get("documentId"), result)
        return "", 200  # also for unknown documents, so BoldSign does not retry them

    @app.post("/documents")
    def send():
        if not cfg.template_id:
            return {"error": "Set BOLDSIGN_TEMPLATE_ID"}, 400
        body = request.get_json(silent=True) or request.form
        user = {"name": body.get("signerName") or cfg.signer_name, "email": body.get("signerEmail") or cfg.signer_email}
        try:
            roles = map_roles(cfg.template_roles, user)
        except ValueError as err:
            return {"error": str(err)}, 400
        title = body.get("title") or "Offer letter"
        try:
            document_id = boldsign.send_from_template(cfg.template_id, title, roles)
        except Exception as err:  # noqa: BLE001 - report the BoldSign error, never the request
            e = error_summary(err)
            log.error("send from template failed %s", e)
            return {"error": "BoldSign send from template failed", **e}, 502
        signer = {"name": roles[0]["signer_name"], "email": roles[0]["signer_email"]}
        record = store.save({"documentId": document_id, "title": title, "signer": signer, "roles": roles,
                             "status": "Queued", "createdAt": datetime.now(timezone.utc).isoformat(), "events": []})
        if request.accept_mimetypes.best_match(["application/json", "text/html"]) == "text/html":
            return redirect("/documents", 303)
        return record, 201

    @app.get("/documents")
    def documents():
        rows = "".join(
            f"<tr><td>{escape(d['title'])}</td><td>{escape(d['signer']['email'])}</td><td><b>{escape(d['status'])}</b>"
            f"{' (' + escape(d['error']) + ')' if d.get('error') else ''}</td><td><code>{escape(d['documentId'])}</code></td><td>"
            + ("" if d["status"] in FINAL else f"<a href='/documents/{quote(d['documentId'])}/sign'>Sign now</a> "
               f"<form method='post' action='/documents/{quote(d['documentId'])}/refresh' style='display:inline'><button>Refresh</button></form>")
            + "</td></tr>" for d in store.all())
        return (PAGE + f"<h1>Documents</h1><form method='post' action='/documents'><button>Send the template to {escape(cfg.signer_email or 'SIGNER_EMAIL')}</button></form>"
                f"<table><tr><th>Title</th><th>Signer</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>{rows or '<tr><td colspan=5>No documents yet</td></tr>'}</table>")

    @app.get("/documents.json")
    def documents_json():
        return store.all()

    @app.get("/documents/<document_id>/sign")
    def sign(document_id):
        d = store.get(document_id)
        if not d:
            return "", 404
        try:
            return redirect(boldsign.sign_link(document_id, d["signer"]["email"], f"{cfg.app_base_url}/documents"))
        except Exception as err:  # noqa: BLE001
            return {"error": "Could not get a signing link", **error_summary(err)}, 502

    @app.post("/documents/<document_id>/refresh")
    def refresh(document_id):
        d = store.get(document_id)
        if not d:
            return "", 404
        try:
            p = boldsign.properties(document_id)
        except Exception as err:  # noqa: BLE001
            return {"error": "Could not read the status", **error_summary(err)}, 502
        status = getattr(p, "status", None)
        if status and not (status == "InProgress" and d["status"] != "Queued") and d["status"] not in FINAL:
            d["status"] = status
            store.save(d)
        return redirect("/documents", 303)

    @app.get("/")
    def home():
        return redirect("/documents")

    return app
