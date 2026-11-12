// Read-only checks before sending: the API key and region, then the template and its roles.
import { config, loadEnv, REGIONS } from '../src/config.js';
import { createBoldSign, statusOf } from '../src/boldsign.js';
import { compareRoles } from '../src/roles.js';

loadEnv();
const cfg = config();
if (!cfg.apiKey) { console.error('BOLDSIGN_API_KEY is not set (see .env.example).'); process.exit(1); }
const boldsign = createBoldSign(cfg);
try {
  await boldsign.checkConfig();
  console.log(`OK: GET /v1/brand/list returned 200 on ${cfg.baseUrl} (region ${cfg.region}).`);
} catch (err) {
  const status = statusOf(err);
  console.error(`GET /v1/brand/list on ${cfg.baseUrl} failed with HTTP ${status ?? 'error'}.`);
  if (status === 401) console.error(`401 usually means a wrong key or the wrong region. Regions: ${Object.entries(REGIONS).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  process.exit(1);
}
if (!cfg.templateId) { console.error('BOLDSIGN_TEMPLATE_ID is not set.'); process.exit(1); }
try {
  const roles = await boldsign.templateRoles(cfg.templateId);
  console.log(`OK: template ${cfg.templateId} has roles ${roles.map((r) => `${r.name}:${r.index}`).join(', ') || '(none)'}`);
  const diff = compareRoles(cfg.templateRoles, roles);
  if (diff.missingInTemplate.length || diff.notConfigured.length) {
    console.error(`Role mismatch. Configured but not in the template: ${diff.missingInTemplate.map((r) => `${r.name}:${r.index}`).join(', ') || 'none'}. In the template but not configured: ${diff.notConfigured.map((r) => `${r.name}:${r.index}`).join(', ') || 'none'}. Fix BOLDSIGN_TEMPLATE_ROLES.`);
    process.exit(1);
  }
  console.log(`OK: BOLDSIGN_TEMPLATE_ROLES matches the template.`);
} catch (err) {
  console.error(`GET /v1/template/properties failed with HTTP ${statusOf(err) ?? 'error'}: check BOLDSIGN_TEMPLATE_ID (a template created seconds ago can need a moment).`);
  process.exit(1);
}
