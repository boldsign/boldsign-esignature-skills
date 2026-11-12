import fs from 'node:fs';
import path from 'node:path';

// Loads a git-ignored .env file without overriding variables that are already set
// (hosted agents and servers usually provide them as secrets).
export function loadEnv(file = path.resolve('.env')) {
  if (!fs.existsSync(file)) return;
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$/);
    if (!m || process.env[m[1]] !== undefined) continue;
    process.env[m[1]] = m[2].replace(/^(['"])(.*)\1$/, '$2');
  }
}

export const REGIONS = {
  US: 'https://api.boldsign.com',
  EU: 'https://api-eu.boldsign.com',
  CA: 'https://api-ca.boldsign.com',
  AU: 'https://api-au.boldsign.com',
};

export function config(env = process.env) {
  const region = (env.BOLDSIGN_REGION || 'US').toUpperCase();
  if (!REGIONS[region]) throw new Error(`BOLDSIGN_REGION must be one of ${Object.keys(REGIONS).join(', ')}`);
  const port = Number(env.PORT || 3000);
  return {
    apiKey: env.BOLDSIGN_API_KEY || '',
    webhookSecret: env.BOLDSIGN_WEBHOOK_SECRET || '',
    region,
    baseUrl: REGIONS[region],
    port,
    appBaseUrl: env.APP_BASE_URL || `http://localhost:${port}`,
    signer: { name: env.SIGNER_NAME || 'Test Signer', email: env.SIGNER_EMAIL || '' },
    storeFile: env.STORE_FILE || 'data/documents.json',
    templateId: env.BOLDSIGN_TEMPLATE_ID || '',
    templateRoles: parseRoles(env.BOLDSIGN_TEMPLATE_ROLES || 'Employee:1'),
  };
}

// "Employee:1, Manager:2" -> [{ name: 'Employee', index: 1 }, { name: 'Manager', index: 2 }]
export function parseRoles(text) {
  return text.split(',').map((s) => s.trim()).filter(Boolean).map((s) => {
    const m = s.match(/^(.+):(\d+)$/);
    if (!m) throw new Error(`BOLDSIGN_TEMPLATE_ROLES: "${s}" is not Name:index`);
    return { name: m[1].trim(), index: Number(m[2]) };
  });
}
