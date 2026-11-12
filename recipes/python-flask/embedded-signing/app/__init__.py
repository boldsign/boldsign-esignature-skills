"""Flask app: embedded signing inside the app (iframe), a text-tag sample PDF, and a verified BoldSign webhook."""
import json
import logging
from datetime import datetime, timezone
from html import escape
from urllib.parse import quote

from flask import Flask, make_response, redirect, request

from .boldsign_client import error_summary
from .sample_pdf import sample_pdf
from .webhook import FINAL, apply_event, verify_boldsign_signature

log = logging.getLogger("boldsign-recipe")
STYLE = "<style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}iframe{width:100%;height:80vh;border:1px solid #ccc}</style>"


def page(title, body, csp="default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'"):
    return f"<!doctype html><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"{csp}\"><title>{escape(title)}</title>{STYLE}{body}"


def create_app(cfg, boldsign, store, current_user=None) -> Flask:
    """current_user returns the signed-in user as {"name", "email"}. This recipe uses one demo user from SIGNER_NAME and
    SIGNER_EMAIL: replace it with your application's authentication. Signing links go to whoever it returns."""
    current_user = current_user or (lambda: {"name": cfg.signer_name, "email": cfg.signer_email})
    app = Flask(__name__)

    def owned(document_id):
        d = store.get(document_id)
        return d if d and d["signer"]["email"] == current_user()["email"] else None

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
        return "", 200

    @app.post("/documents")
    def create():
        user = current_user()
        if not user.get("email"):
            return {"error": "No signed-in user (set SIGNER_EMAIL)"}, 400
        body = request.get_json(silent=True) or request.form
        title = body.get("title") or "Policy acknowledgement"
        try:
            document_id = boldsign.create_document(title, sample_pdf(title, user["name"]), user["name"], user["email"])
        except Exception as err:  # noqa: BLE001 - report the BoldSign error, never the request
            e = error_summary(err)
            log.error("create failed %s", e)
            return {"error": "BoldSign create failed", **e}, 502
        store.save({"documentId": document_id, "title": title, "signer": user, "status": "Queued",
                    "createdAt": datetime.now(timezone.utc).isoformat(), "events": []})
        signing_page = f"/documents/{quote(document_id)}/sign"
        if request.accept_mimetypes.best_match(["application/json", "text/html"]) == "text/html":
            return redirect(signing_page, 303)
        return {"documentId": document_id, "status": "Queued", "signingPage": signing_page}, 201

    @app.get("/documents/<document_id>/sign")
    def sign(document_id):
        d = owned(document_id)
        if not d:
            return "", 404
        if d["status"] in FINAL:
            return redirect(f"/signing/complete?documentId={quote(document_id)}")
        try:
            link = boldsign.sign_link(document_id, d["signer"]["email"], f"{cfg.app_base_url}/signing/complete?documentId={quote(document_id)}")
        except Exception as err:  # noqa: BLE001
            return {"error": "Could not get a signing link", **error_summary(err)}, 502
        res = make_response(page(f"Sign: {d['title']}", f"<h1>{escape(d['title'])}</h1><p><a href='/documents'>Back to documents</a></p>"
                                 f"<iframe src=\"{escape(link)}\" title='BoldSign signing' allow='camera'></iframe>",
                                 "default-src 'self'; style-src 'unsafe-inline'; frame-src https://app.boldsign.com https://*.boldsign.com"))
        res.headers["Cache-Control"] = "no-store"
        res.headers["Referrer-Policy"] = "no-referrer"
        return res

    @app.get("/signing/complete")
    def complete():
        """Where BoldSign sends the signer when signing ends (redirect URL). Breaks out of the iframe and shows the status."""
        d = store.get(request.args.get("documentId", ""))
        info = (f"Document <code>{escape(d['documentId'])}</code>: status <b>{escape(d['status'])}</b>. It changes to Completed when BoldSign's webhook arrives."
                if d else "Signing finished.")
        return page("Signing finished", "<script>if (window.top !== window.self) window.top.location.href = window.location.href;</script>"
                    f"<h1>Thank you</h1><p>{info}</p><p><a href='/documents'>Back to documents</a></p>")

    @app.get("/documents")
    def documents():
        me = current_user()
        rows = "".join(
            f"<tr><td>{escape(d['title'])}</td><td><b>{escape(d['status'])}</b>{' (' + escape(d['error']) + ')' if d.get('error') else ''}</td>"
            f"<td><code>{escape(d['documentId'])}</code></td><td>"
            + ("" if d["status"] in FINAL else f"<a href='/documents/{quote(d['documentId'])}/sign'>Open signing page</a>")
            + "</td></tr>" for d in store.all() if d["signer"]["email"] == me["email"])
        return page("My documents", f"<h1>My documents</h1><p>Signed in as {escape(me['name'])} ({escape(me['email'])})</p>"
                    "<form method='post' action='/documents'><button>Create a document and sign it here</button></form>"
                    f"<table><tr><th>Title</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>{rows or '<tr><td colspan=4>No documents yet</td></tr>'}</table>")

    @app.get("/documents.json")
    def documents_json():
        return [d for d in store.all() if d["signer"]["email"] == current_user()["email"]]

    @app.get("/")
    def home():
        return redirect("/documents")

    return app
