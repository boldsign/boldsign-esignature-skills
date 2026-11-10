<?php
declare(strict_types=1);

namespace Tests;

use App\App;
use App\Request;
use App\Webhook;

final class WebhookTest extends TestCase
{
    public function testAcceptsS0AndRotatedS1(): void
    {
        $body = self::event('doc-1', 'Completed');
        $now = time();
        $sig = fn (string $k, int $t = 0) => hash_hmac('sha256', ($t ?: $now) . '.' . $body, $k);
        $this->assertTrue(Webhook::verify($body, "t=$now, s0={$sig(self::SECRET)}", self::SECRET));
        $this->assertTrue(Webhook::verify($body, "t=$now, s0={$sig('old')}, s1={$sig(self::SECRET)}", self::SECRET));
        $this->assertTrue(Webhook::verify($body, "t=$now, s0=" . strtoupper($sig(self::SECRET)), self::SECRET));
    }

    public function testRejectsBadSignatures(): void
    {
        $body = self::event('doc-1', 'Completed');
        $now = time();
        $sig = fn (string $k, int $t = 0) => hash_hmac('sha256', ($t ?: $now) . '.' . $body, $k);
        $bodyOnly = hash_hmac('sha256', $body, self::SECRET);
        foreach (["t=$now, s0={$sig('wrong')}", "t=$now, s0=$bodyOnly", 't=' . ($now - 600) . ', s0=' . $sig(self::SECRET, $now - 600),
            "t=$now, s0=abc", "s0={$sig(self::SECRET)}", '', 'garbage'] as $header) {
            $this->assertFalse(Webhook::verify($body, $header, self::SECRET), $header);
        }
        $this->assertFalse(Webhook::verify(str_replace('doc-1', 'doc-2', $body), "t=$now, s0={$sig(self::SECRET)}", self::SECRET));
    }

    public function testStatusRules(): void
    {
        $store = $this->store();
        $store->save(['documentId' => 'doc-1', 'status' => 'Queued', 'events' => []]);
        $sent = json_decode(self::event('doc-1', 'Sent', 'e1', 100), true);
        $this->assertSame('applied', Webhook::apply($store, $sent));
        $this->assertSame('Sent', $store->get('doc-1')['status']);
        $this->assertSame('duplicate', Webhook::apply($store, $sent));
        $this->assertSame('applied', Webhook::apply($store, json_decode(self::event('doc-1', 'Completed', 'e2', 200), true)));
        $this->assertSame('stale', Webhook::apply($store, json_decode(self::event('doc-1', 'Viewed', 'e3', 150), true)));
        $this->assertSame('applied', Webhook::apply($store, json_decode(self::event('doc-1', 'Revoked', 'e4', 300), true)));
        $this->assertSame('Completed', $store->get('doc-1')['status']);
        $this->assertSame('unknown-document', Webhook::apply($store, json_decode(self::event('other', 'Completed'), true)));
    }

    public function testWebhookRoute(): void
    {
        $store = $this->store();
        $store->save(['documentId' => 'doc-1', 'title' => 'T', 'signer' => ['name' => 'S', 'email' => 'signer@boldsign.dev'], 'status' => 'Queued', 'events' => [], 'createdAt' => '2026-01-01']);
        $app = new App($this->cfg(), null, $store, fn () => null);
        $this->assertSame(200, $app->handle(new Request('POST', '/webhooks/boldsign', ['x-boldsign-event' => 'Verification'], '{}'))->status);
        $raw = self::event('doc-1', 'Completed');
        $this->assertSame(401, $app->handle(self::signed($raw, 'not-the-secret'))->status);
        $this->assertSame('Queued', $store->get('doc-1')['status']);
        $this->assertSame(200, $app->handle(self::signed($raw))->status);
        $this->assertSame('Completed', $store->get('doc-1')['status']);
        $this->assertStringContainsString('<b>Completed</b>', $app->handle(new Request('GET', '/documents'))->body);
    }
}
