import boldsign
import pytest
from boldsign.exceptions import ApiException

from app import create_app
from app.boldsign_client import BoldSign, retry_soon
from app.config import parse_roles
from app.roles import compare_roles, map_roles


def test_parse_roles_reads_name_index_pairs():
    assert parse_roles("Employee:1, Manager:2") == [{"name": "Employee", "index": 1}, {"name": "Manager", "index": 2}]
    with pytest.raises(ValueError):
        parse_roles("Employee")


def test_map_roles_maps_each_template_role_to_a_signer():
    assert map_roles([{"name": "Employee", "index": 1}], {"name": "Ann", "email": "ann@boldsign.dev"}) == [
        {"role_index": 1, "signer_role": "Employee", "signer_name": "Ann", "signer_email": "ann@boldsign.dev"}]
    with pytest.raises(ValueError, match="Employee"):
        map_roles([{"name": "Employee", "index": 1}], {"name": "No email"})


def test_compare_roles_finds_mismatches():
    assert compare_roles([{"name": "Employee", "index": 1}], [{"name": "employee", "index": 1}]) == {"missing_in_template": [], "not_configured": []}
    d = compare_roles([{"name": "Employee", "index": 1}], [{"name": "Manager", "index": 1}])
    assert len(d["missing_in_template"]) == 1 and len(d["not_configured"]) == 1


def test_send_from_template_sends_role_objects_and_retries_the_first_400(cfg, monkeypatch):
    calls = []

    def fake(self, template_id, send_for_sign_from_template_form):
        calls.append((template_id, send_for_sign_from_template_form))
        if len(calls) < 2:
            raise ApiException(status=400)
        return boldsign.DocumentCreated(document_id="doc-9")

    monkeypatch.setattr(boldsign.TemplateApi, "send_using_template", fake)
    monkeypatch.setattr("app.boldsign_client.time.sleep", lambda s: None)
    roles = map_roles([{"name": "Employee", "index": 1}], {"name": "Ann", "email": "ann@boldsign.dev"})
    assert BoldSign(cfg).send_from_template("tpl-1", "Offer", roles) == "doc-9"
    assert len(calls) == 2 and calls[1][0] == "tpl-1"
    role = calls[1][1].roles[0]
    assert isinstance(role, boldsign.Role)
    assert (role.role_index, role.signer_role, role.signer_email) == (1, "Employee", "ann@boldsign.dev")


def test_post_documents_sends_from_the_template_and_stores_queued(cfg, store):
    calls = []

    class Fake:
        def send_from_template(self, *a):
            calls.append(a)
            return "doc-5"

    res = create_app(cfg, Fake(), store).test_client().post("/documents", json={}, headers={"Accept": "application/json"})
    assert res.status_code == 201
    assert calls[0][0] == "tpl-1" and calls[0][2][0]["signer_email"] == "signer@boldsign.dev"
    assert store.get("doc-5")["status"] == "Queued"


def test_post_documents_needs_a_template_id(cfg, store):
    cfg.template_id = ""
    res = create_app(cfg, object(), store).test_client().post("/documents", json={})
    assert res.status_code == 400


def test_retry_soon_does_not_retry_a_401():
    n = {"c": 0}

    def unauthorized():
        n["c"] += 1
        raise ApiException(status=401)

    with pytest.raises(ApiException):
        retry_soon(unauthorized, wait_s=0.001)
    assert n["c"] == 1
