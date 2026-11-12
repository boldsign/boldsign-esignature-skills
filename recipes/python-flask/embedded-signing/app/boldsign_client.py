"""The official BoldSign Python SDK (boldsign 3.3.4), wrapped for this app."""
import os
import tempfile
import time

import boldsign
from boldsign.exceptions import ApiException


def status_of(err) -> int | None:
    return getattr(err, "status", None)


def error_summary(err) -> dict:
    """SDK exceptions can carry request details; log only this summary, never the full object."""
    body = getattr(err, "body", None)
    return {"status": status_of(err), "error": (body or getattr(err, "reason", None) or str(err))[:300]}


def retry_soon(fn, tries: int = 5, wait_s: float = 2.0):
    """A new document is processed asynchronously: for a few seconds the next call on it can return 403
    "Invalid Document ID" (or 400). Retry those briefly; anything else is a real error."""
    for i in range(1, tries + 1):
        try:
            return fn()
        except ApiException as err:
            if i >= tries or status_of(err) not in (400, 403):
                raise
            time.sleep(wait_s * i)


class BoldSign:
    def __init__(self, cfg):
        self.cfg = cfg
        self._configuration = boldsign.Configuration(host=cfg.base_url, api_key=cfg.api_key)

    def _client(self):
        return boldsign.ApiClient(self._configuration)

    def check_config(self):
        """One read-only call: 200 means the key and the region's base URL match."""
        with self._client() as client:
            return boldsign.BrandingApi(client).brand_list()

    def create_document(self, title: str, pdf: bytes, signer_name: str, signer_email: str) -> str:
        """Creates the document for the signed-in user. Fields come from the text tags in the PDF (use_text_tags), so no
        template and no coordinates. disable_emails: signing happens only inside your app, through the embedded link.
        Processing completes asynchronously (webhook: Sent or SendFailed)."""
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "document.pdf")  # the SDK uploads a file path with its file name
            with open(path, "wb") as f:
                f.write(pdf)
            request = boldsign.SendForSign(
                title=title,
                signers=[boldsign.DocumentSigner(name=signer_name, email_address=signer_email, signer_type="Signer")],
                files=[path],
                use_text_tags=True,
                disable_emails=True,
            )
            with self._client() as client:
                return boldsign.DocumentApi(client).send_document(send_for_sign=request).document_id

    def sign_link(self, document_id: str, signer_email: str, redirect_url: str) -> str:
        """A fresh embedded signing link to load in an iframe. Links expire, so get one per visit. Right after the document
        is created this can return 403 for a few seconds: retry_soon handles it."""
        with self._client() as client:
            api = boldsign.DocumentApi(client)
            return retry_soon(lambda: api.get_embedded_sign_link(document_id=document_id, signer_email=signer_email, redirect_url=redirect_url)).sign_link

    def properties(self, document_id: str):
        """Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour)."""
        with self._client() as client:
            api = boldsign.DocumentApi(client)
            return retry_soon(lambda: api.get_properties(document_id=document_id))
