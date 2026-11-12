<?php
declare(strict_types=1);

// One read-only call to confirm the API key and region before building or sending anything.
require __DIR__ . '/../src/bootstrap.php';

use App\{BoldSignClient, Config};

$cfg = Config::fromEnv();
if ($cfg->apiKey === '') {
    fwrite(STDERR, "BOLDSIGN_API_KEY is not set (see .env.example).\n");
    exit(1);
}
try {
    (new BoldSignClient($cfg))->checkConfig();
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
