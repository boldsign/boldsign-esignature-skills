// One read-only call to confirm the API key and region before building or sending anything.
import { config, loadEnv, REGIONS } from '../src/config.js';
import { createBoldSign, statusOf } from '../src/boldsign.js';

loadEnv();
const cfg = config();
if (!cfg.apiKey) { console.error('BOLDSIGN_API_KEY is not set (see .env.example).'); process.exit(1); }
try {
  const brands = await createBoldSign(cfg).checkConfig();
  console.log(`OK: GET /v1/brand/list returned 200 on ${cfg.baseUrl} (region ${cfg.region}). Brands: ${(brands?.result || []).map((b) => b.brandName).join(', ') || 'none'}`);
} catch (err) {
  const status = statusOf(err);
  console.error(`GET /v1/brand/list on ${cfg.baseUrl} failed with HTTP ${status ?? 'error'}.`);
  if (status === 401) console.error(`401 usually means a wrong key or the wrong region. Regions: ${Object.entries(REGIONS).map(([k, v]) => `${k} ${v}`).join(', ')}`);
  process.exit(1);
}
