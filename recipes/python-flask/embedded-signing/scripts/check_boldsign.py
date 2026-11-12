"""One read-only call to confirm the API key and region before building or sending anything."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from app.boldsign_client import BoldSign, status_of  # noqa: E402
from app.config import REGIONS, config, load_env  # noqa: E402

load_env()
cfg = config()
if not cfg.api_key:
    sys.exit("BOLDSIGN_API_KEY is not set (see .env.example).")
try:
    brands = BoldSign(cfg).check_config()
    names = ", ".join(b.brand_name or "" for b in (brands.result or [])) or "none"
    print(f"OK: GET /v1/brand/list returned 200 on {cfg.base_url} (region {cfg.region}). Brands: {names}")
except Exception as err:  # noqa: BLE001
    status = status_of(err)
    print(f"GET /v1/brand/list on {cfg.base_url} failed with HTTP {status}.")
    if status == 401:
        print("401 usually means a wrong key or the wrong region. Regions: " + ", ".join(f"{k} {v}" for k, v in REGIONS.items()))
    sys.exit(1)
