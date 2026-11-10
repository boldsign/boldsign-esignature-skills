"""Maps each template role to a signer from your application. Edit signer_for_role for your data model: here every role
is filled by the demo user (SIGNER_NAME, SIGNER_EMAIL), so a one-role template works as is."""


def signer_for_role(role: dict, user: dict) -> dict:
    return {"name": user.get("name"), "email": user.get("email")}


def map_roles(template_roles: list, user: dict) -> list:
    """Builds the role list for /v1/template/send. Fails loudly if a role has no signer."""
    out = []
    for role in template_roles:
        signer = signer_for_role(role, user)
        if not signer.get("email"):
            raise ValueError(f'No signer for template role "{role["name"]}"')
        out.append({"role_index": role["index"], "signer_role": role["name"], "signer_name": signer["name"], "signer_email": signer["email"]})
    return out


def compare_roles(configured: list, from_template: list) -> dict:
    """Compares the configured roles with the template's own roles (from GET /v1/template/properties)."""
    def key(r):
        return f'{r["index"]}:{str(r["name"]).lower()}'
    want = {key(r) for r in configured}
    have = {key(r) for r in from_template}
    return {"missing_in_template": [r for r in configured if key(r) not in have],
            "not_configured": [r for r in from_template if key(r) not in want]}
