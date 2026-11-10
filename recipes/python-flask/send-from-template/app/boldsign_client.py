"""The official BoldSign Python SDK (boldsign 3.3.4), wrapped for this app."""
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
    """A new document (or template) is processed asynchronously: for a few seconds the next call on it can return 403
    "Invalid Document ID" or 400 "Invalid template id.". Retry those briefly; anything else is a real error."""
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

    def template_roles(self, template_id: str) -> list:
        """The template's roles as [{"name", "index"}] (read-only), to check the role configuration."""
        with self._client() as client:
            api = boldsign.TemplateApi(client)
            props = retry_soon(lambda: api.get_properties(template_id=template_id))
            return [{"name": r.name, "index": r.index} for r in (props.roles or [])]

    def send_from_template(self, template_id: str, title: str, roles: list) -> str:
        """Sends from the template, with one signer per role. BoldSign emails each signer. Returns the document ID; the
        send completes asynchronously, and the webhook reports Sent or SendFailed. A template created seconds ago can
        return 400 "Invalid template id." for a moment, so the first 400 is retried."""
        request = boldsign.SendForSignFromTemplateForm(title=title, roles=[boldsign.Role(**r) for r in roles])
        with self._client() as client:
            api = boldsign.TemplateApi(client)
            return retry_soon(lambda: api.send_using_template(template_id=template_id, send_for_sign_from_template_form=request)).document_id

    def sign_link(self, document_id: str, signer_email: str, redirect_url: str) -> str:
        """A signing link for the signer, for an in-app "Sign now" button (the signer can also use the email)."""
        with self._client() as client:
            api = boldsign.DocumentApi(client)
            return retry_soon(lambda: api.get_embedded_sign_link(document_id=document_id, signer_email=signer_email, redirect_url=redirect_url)).sign_link

    def properties(self, document_id: str):
        """Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour)."""
        with self._client() as client:
            api = boldsign.DocumentApi(client)
            return retry_soon(lambda: api.get_properties(document_id=document_id))
