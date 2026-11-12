<?php
declare(strict_types=1);

// Read-only checks before sending: the API key and region, then the template and its roles.
require __DIR__ . '/../src/bootstrap.php';

use App\{BoldSignClient, Config, Roles};

$cfg = Config::fromEnv();
if ($cfg->apiKey === '') {
    fwrite(STDERR, "BOLDSIGN_API_KEY is not set (see .env.example).\n");
    exit(1);
}
$bs = new BoldSignClient($cfg);
try {
    $bs->checkConfig();
    echo "OK: GET /v1/brand/list returned 200 on {$cfg->baseUrl} (region {$cfg->region}).\n";
} catch (\Throwable $e) {
    $status = BoldSignClient::statusOf($e);
    fwrite(STDERR, "GET /v1/brand/list on {$cfg->baseUrl} failed with HTTP " . ($status ?? 'error') . ".\n");
    if ($status === 401) {
        fwrite(STDERR, '401 usually means a wrong key or the wrong region. Regions: '
            . implode(', ', array_map(fn ($k, $v) => "$k $v", array_keys(Config::REGIONS), Config::REGIONS)) . "\n");
    }
    exit(1);
}
if ($cfg->templateId === '') {
    fwrite(STDERR, "BOLDSIGN_TEMPLATE_ID is not set.\n");
    exit(1);
}
$fmt = fn (array $rs) => implode(', ', array_map(fn ($r) => "{$r['name']}:{$r['index']}", $rs)) ?: 'none';
try {
    $roles = $bs->templateRoles($cfg->templateId);
} catch (\Throwable $e) {
    fwrite(STDERR, 'GET /v1/template/properties failed with HTTP ' . (BoldSignClient::statusOf($e) ?? 'error')
        . ": check BOLDSIGN_TEMPLATE_ID (a template created seconds ago can need a moment).\n");
    exit(1);
}
echo "OK: template {$cfg->templateId} has roles {$fmt($roles)}\n";
$diff = Roles::compare($cfg->templateRoles, $roles);
if ($diff['missing_in_template'] || $diff['not_configured']) {
    fwrite(STDERR, "Role mismatch. Configured but not in the template: {$fmt($diff['missing_in_template'])}. "
        . "In the template but not configured: {$fmt($diff['not_configured'])}. Fix BOLDSIGN_TEMPLATE_ROLES.\n");
    exit(1);
}
echo "OK: BOLDSIGN_TEMPLATE_ROLES matches the template.\n";
