"""Read-only checks before sending: the API key and region, then the template and its roles."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from app.boldsign_client import BoldSign, status_of  # noqa: E402
from app.config import REGIONS, config, load_env  # noqa: E402
from app.roles import compare_roles  # noqa: E402

load_env()
cfg = config()
if not cfg.api_key:
    sys.exit("BOLDSIGN_API_KEY is not set (see .env.example).")
bs = BoldSign(cfg)
try:
    bs.check_config()
    print(f"OK: GET /v1/brand/list returned 200 on {cfg.base_url} (region {cfg.region}).")
except Exception as err:  # noqa: BLE001
    status = status_of(err)
    print(f"GET /v1/brand/list on {cfg.base_url} failed with HTTP {status}.")
    if status == 401:
        print("401 usually means a wrong key or the wrong region. Regions: " + ", ".join(f"{k} {v}" for k, v in REGIONS.items()))
    sys.exit(1)
if not cfg.template_id:
    sys.exit("BOLDSIGN_TEMPLATE_ID is not set.")
try:
    roles = bs.template_roles(cfg.template_id)
except Exception as err:  # noqa: BLE001
    sys.exit(f"GET /v1/template/properties failed with HTTP {status_of(err)}: check BOLDSIGN_TEMPLATE_ID (a template created seconds ago can need a moment).")
fmt = lambda rs: ", ".join(f'{r["name"]}:{r["index"]}' for r in rs) or "none"  # noqa: E731
print(f"OK: template {cfg.template_id} has roles {fmt(roles)}")
diff = compare_roles(cfg.template_roles, roles)
if diff["missing_in_template"] or diff["not_configured"]:
    sys.exit(f"Role mismatch. Configured but not in the template: {fmt(diff['missing_in_template'])}. "
             f"In the template but not configured: {fmt(diff['not_configured'])}. Fix BOLDSIGN_TEMPLATE_ROLES.")
print("OK: BOLDSIGN_TEMPLATE_ROLES matches the template.")
