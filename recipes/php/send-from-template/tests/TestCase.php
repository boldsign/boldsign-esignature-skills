<?php
declare(strict_types=1);

namespace Tests;

use App\Config;
use App\Request;
use App\Store;

abstract class TestCase extends \PHPUnit\Framework\TestCase
{
    public const SECRET = 'test-webhook-secret';

    protected function cfg(): Config
    {
        return new Config(apiKey: 'test', webhookSecret: self::SECRET, signerName: 'Test Signer', signerEmail: 'signer@boldsign.dev',
            templateId: 'tpl-1', templateRoles: [['name' => 'Employee', 'index' => 1]]);
    }

    protected function store(): Store
    {
        return new Store(sys_get_temp_dir() . '/boldsign-recipe-test-' . bin2hex(random_bytes(6)) . '/documents.json');
    }

    protected static function event(string $documentId, string $type, ?string $id = null, ?int $created = null): string
    {
        return json_encode(['event' => ['id' => $id ?? bin2hex(random_bytes(8)), 'created' => $created ?? time(), 'eventType' => $type, 'environment' => 'Test'],
            'data' => ['object' => 'document', 'documentId' => $documentId, 'status' => $type]]);
    }

    protected static function signed(string $body, string $secret = self::SECRET, ?int $t = null): Request
    {
        $t ??= time();
        return new Request('POST', '/webhooks/boldsign', [
            'content-type' => 'application/json',
            'x-boldsign-event' => 'Completed',
            'x-boldsign-signature' => "t=$t, s0=" . hash_hmac('sha256', "$t.$body", $secret),
        ], $body);
    }
}
