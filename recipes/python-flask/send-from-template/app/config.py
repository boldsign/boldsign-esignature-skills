"""Settings from the environment, with a git-ignored .env file as a fallback."""
import os
import re
from dataclasses import dataclass, field

REGIONS = {
    "US": "https://api.boldsign.com",
    "EU": "https://api-eu.boldsign.com",
    "CA": "https://api-ca.boldsign.com",
    "AU": "https://api-au.boldsign.com",
}


def load_env(path: str = ".env") -> None:
    """Loads .env without overriding variables that are already set (hosted agents and servers set them as secrets)."""
    if not os.path.exists(path):
        return
    with open(path, encoding="utf-8") as f:
        for line in f:
            m = re.match(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$", line)
            if m and m.group(1) not in os.environ:
                os.environ[m.group(1)] = re.sub(r"^(['\"])(.*)\1$", r"\2", m.group(2))


@dataclass
class Config:
    api_key: str = ""
    webhook_secret: str = ""
    region: str = "US"
    base_url: str = REGIONS["US"]
    port: int = 3000
    app_base_url: str = "http://localhost:3000"
    signer_name: str = "Test Signer"
    signer_email: str = ""
    store_file: str = "data/documents.json"
    template_id: str = ""
    template_roles: list = field(default_factory=list)


def parse_roles(value: str) -> list:
    """Reads BOLDSIGN_TEMPLATE_ROLES, "Employee:1, Manager:2", into [{"name": "Employee", "index": 1}, ...]."""
    roles = []
    for part in (value or "").split(","):
        if not part.strip():
            continue
        m = re.fullmatch(r"\s*(.+?)\s*:\s*(\d+)\s*", part)
        if not m:
            raise ValueError(f'BOLDSIGN_TEMPLATE_ROLES: "{part.strip()}" is not Name:index')
        roles.append({"name": m.group(1), "index": int(m.group(2))})
    return roles


def config(env=None) -> Config:
    env = os.environ if env is None else env
    region = (env.get("BOLDSIGN_REGION") or "US").upper()
    if region not in REGIONS:
        raise ValueError(f"BOLDSIGN_REGION must be one of {', '.join(REGIONS)}")
    port = int(env.get("PORT") or 3000)
    return Config(
        api_key=env.get("BOLDSIGN_API_KEY", ""),
        webhook_secret=env.get("BOLDSIGN_WEBHOOK_SECRET", ""),
        region=region,
        base_url=REGIONS[region],
        port=port,
        app_base_url=env.get("APP_BASE_URL") or f"http://localhost:{port}",
        signer_name=env.get("SIGNER_NAME") or "Test Signer",
        signer_email=env.get("SIGNER_EMAIL", ""),
        store_file=env.get("STORE_FILE") or "data/documents.json",
        template_id=env.get("BOLDSIGN_TEMPLATE_ID", ""),
        template_roles=parse_roles(env.get("BOLDSIGN_TEMPLATE_ROLES") or "Employee:1"),
    )
