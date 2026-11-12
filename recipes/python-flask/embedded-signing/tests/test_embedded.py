import boldsign
import pytest
from boldsign.exceptions import ApiException

from app import create_app
from app.boldsign_client import BoldSign, retry_soon
from app.sample_pdf import DATE_TAG, SIGN_TAG, sample_pdf


def test_sample_pdf_has_the_text_tags():
    pdf = sample_pdf()
    assert pdf.startswith(b"%PDF-") and SIGN_TAG.encode() in pdf and DATE_TAG.encode() in pdf


def test_create_document_uses_text_tags_and_disables_emails(cfg, monkeypatch):
    captured = {}
    monkeypatch.setattr(boldsign.DocumentApi, "send_document", lambda self, send_for_sign: captured.setdefault("req", send_for_sign) and boldsign.DocumentCreated(document_id="doc-42"))
    assert BoldSign(cfg).create_document("T", sample_pdf(), "N", "n@boldsign.dev") == "doc-42"
    req = captured["req"]
    assert req.use_text_tags is True and req.disable_emails is True
    assert not req.signers[0].form_fields


def test_sign_link_redirects_back_and_retries_the_first_403(cfg, monkeypatch):
    calls = []

    def fake(self, document_id, signer_email, redirect_url):
        calls.append((document_id, signer_email, redirect_url))
        if len(calls) < 2:
            raise ApiException(status=403)
        return boldsign.EmbeddedSigningLink(sign_link="https://app.boldsign.com/document/sign/?documentId=x")

    monkeypatch.setattr(boldsign.DocumentApi, "get_embedded_sign_link", fake)
    monkeypatch.setattr("app.boldsign_client.time.sleep", lambda s: None)
    link = BoldSign(cfg).sign_link("doc-1", "n@boldsign.dev", "http://localhost/signing/complete?documentId=doc-1")
    assert link.startswith("https://app.boldsign.com/")
    assert calls[-1] == ("doc-1", "n@boldsign.dev", "http://localhost/signing/complete?documentId=doc-1")


def test_create_signing_iframe_owner_only_and_redirect_page(cfg, store):
    class Fake:
        def create_document(self, *a):
            return "doc-7"

        def sign_link(self, *a):
            return "https://app.boldsign.com/document/sign/?documentId=doc-7&t=1"

    client = create_app(cfg, Fake(), store).test_client()
    res = client.post("/documents", json={}, headers={"Accept": "application/json"})
    assert res.status_code == 201
    signing_page = res.get_json()["signingPage"]
    assert signing_page == "/documents/doc-7/sign"
    sign = client.get(signing_page)
    assert '<iframe src="https://app.boldsign.com/document/sign/?documentId=doc-7&amp;t=1"' in sign.get_data(as_text=True)
    assert "no-store" in sign.headers["Cache-Control"]
    d = store.get("doc-7")
    d["signer"] = {"name": "X", "email": "someone-else@boldsign.dev"}
    store.save(d)
    assert client.get(signing_page).status_code == 404
    assert "window.top.location" in client.get("/signing/complete?documentId=doc-7").get_data(as_text=True)


def test_retry_soon_does_not_retry_401():
    n = {"c": 0}

    def unauthorized():
        n["c"] += 1
        raise ApiException(status=401)

    with pytest.raises(ApiException):
        retry_soon(unauthorized, wait_s=0.001)
    assert n["c"] == 1
