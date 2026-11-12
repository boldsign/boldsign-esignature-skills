// Maps each template role to a signer from your application. Edit signerForRole for your data model:
// here every role is filled by the demo user (SIGNER_NAME, SIGNER_EMAIL), so a one-role template works as is.
export function signerForRole(role, { user }) {
  return { name: user.name, email: user.email };
}

// Builds the role list for /v1/template/send. Fails loudly if a role has no signer.
export function mapRoles(templateRoles, context) {
  return templateRoles.map((role) => {
    const signer = signerForRole(role, context);
    if (!signer?.email) throw new Error(`No signer for template role "${role.name}"`);
    return { roleIndex: role.index, signerRole: role.name, signerName: signer.name, signerEmail: signer.email };
  });
}

// Compares the configured roles with the template's own roles (from GET /v1/template/properties).
export function compareRoles(configured, fromTemplate) {
  const key = (r) => `${r.index}:${String(r.name).toLowerCase()}`;
  const want = new Set(configured.map(key));
  const have = new Set(fromTemplate.map((r) => key({ index: r.index, name: r.name })));
  return { missingInTemplate: configured.filter((r) => !have.has(key(r))), notConfigured: fromTemplate.filter((r) => !want.has(key({ index: r.index, name: r.name }))) };
}
