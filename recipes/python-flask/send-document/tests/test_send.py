import boldsign
import pytest
from boldsign.exceptions import ApiException

from app import create_app
from app.boldsign_client import BoldSign, retry_soon
from app.sample_pdf import DATE_TAG, SIGN_TAG, sample_pdf


def test_sample_pdf_has_the_text_tags():
    pdf = sample_pdf()
    assert pdf.startswith(b"%PDF-")
    assert SIGN_TAG.encode() in pdf and DATE_TAG.encode() in pdf
    assert SIGN_TAG.startswith("{{sign|1|*")


def test_send_document_builds_a_text_tag_request(cfg, monkeypatch):
    captured = {}

    def fake_send(self, send_for_sign):
        captured["req"] = send_for_sign
        captured["file"] = open(send_for_sign.files[0], "rb").read()
        return boldsign.DocumentCreated(document_id="doc-42")

    monkeypatch.setattr(boldsign.DocumentApi, "send_document", fake_send)
    assert BoldSign(cfg).send_document("T", sample_pdf(), "N", "n@boldsign.dev") == "doc-42"
    req = captured["req"]
    assert req.use_text_tags is True
    assert len(req.signers) == 1 and req.signers[0].email_address == "n@boldsign.dev"
    assert not req.signers[0].form_fields
    assert req.files[0].endswith(".pdf") and captured["file"].startswith(b"%PDF-")


def test_post_documents_sends_and_stores_queued(cfg, store):
    calls = []

    class Fake:
        def send_document(self, *a):
            calls.append(a)
            return "doc-7"

    client = create_app(cfg, Fake(), store).test_client()
    res = client.post("/documents", json={}, headers={"Accept": "application/json"})
    assert res.status_code == 201 and res.get_json()["documentId"] == "doc-7"
    assert calls[0][3] == "signer@boldsign.dev"
    assert store.get("doc-7")["status"] == "Queued"


def test_retry_soon_retries_400_and_403_only():
    n = {"c": 0}

    def flaky():
        n["c"] += 1
        if n["c"] < 3:
            raise ApiException(status=403)
        return "ok"

    assert retry_soon(flaky, wait_s=0.001) == "ok"
    m = {"c": 0}

    def unauthorized():
        m["c"] += 1
        raise ApiException(status=401)

    with pytest.raises(ApiException):
        retry_soon(unauthorized, wait_s=0.001)
    assert m["c"] == 1
