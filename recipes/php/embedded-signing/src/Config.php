<?php
declare(strict_types=1);

namespace App;

/** Settings from the environment, with a git-ignored .env file as a fallback. */
final class Config
{
    public const REGIONS = [
        'US' => 'https://api.boldsign.com',
        'EU' => 'https://api-eu.boldsign.com',
        'CA' => 'https://api-ca.boldsign.com',
        'AU' => 'https://api-au.boldsign.com',
    ];

    public function __construct(
        public string $apiKey = '',
        public string $webhookSecret = '',
        public string $region = 'US',
        public string $baseUrl = self::REGIONS['US'],
        public int $port = 3000,
        public string $appBaseUrl = 'http://localhost:3000',
        public string $signerName = 'Test Signer',
        public string $signerEmail = '',
        public string $storeFile = 'data/documents.json',
    ) {
    }

    /** Loads .env without overriding variables that are already set (hosted agents and servers set them as secrets). */
    public static function loadEnv(string $path): void
    {
        if (!is_file($path)) {
            return;
        }
        foreach (file($path, FILE_IGNORE_NEW_LINES) as $line) {
            if (preg_match('/^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$/', $line, $m) && getenv($m[1]) === false) {
                putenv($m[1] . '=' . preg_replace('/^([\'"])(.*)\1$/', '$2', $m[2]));
            }
        }
    }

    /** @param array<string,string>|null $env */
    public static function fromEnv(?array $env = null): self
    {
        $get = static fn (string $k): string => (string) ($env !== null ? ($env[$k] ?? '') : (getenv($k) ?: ''));
        $region = strtoupper($get('BOLDSIGN_REGION') ?: 'US');
        if (!isset(self::REGIONS[$region])) {
            throw new \InvalidArgumentException('BOLDSIGN_REGION must be one of ' . implode(', ', array_keys(self::REGIONS)));
        }
        $port = (int) ($get('PORT') ?: 3000);
        return new self(
            apiKey: $get('BOLDSIGN_API_KEY'),
            webhookSecret: $get('BOLDSIGN_WEBHOOK_SECRET'),
            region: $region,
            baseUrl: self::REGIONS[$region],
            port: $port,
            appBaseUrl: $get('APP_BASE_URL') ?: "http://localhost:$port",
            signerName: $get('SIGNER_NAME') ?: 'Test Signer',
            signerEmail: $get('SIGNER_EMAIL'),
            storeFile: $get('STORE_FILE') ?: 'data/documents.json',
        );
    }
}
