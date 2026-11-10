<?php
declare(strict_types=1);

namespace App;

/**
 * BoldSign webhook verification and status rules.
 *
 * X-BoldSign-Signature: t=<unix seconds>, s0=<hex>[, s1=<hex>]
 * Signature: hex(HMAC_SHA256(webhook secret, t + "." + raw body)); s1 is present while an old secret is still valid.
 * https://developers.boldsign.com/webhooks/verify-webhook-events/
 */
final class Webhook
{
    public const TOLERANCE_SECONDS = 300;
    public const FINAL = ['Completed', 'Declined', 'Expired', 'Revoked', 'SendFailed'];
    public const STATUS = ['Sent', 'Viewed', 'Signed', 'Completed', 'Declined', 'Expired', 'Revoked', 'SendFailed', 'DeliveryFailed'];

    public static function verify(string $rawBody, string $header, string $secret, ?int $now = null): bool
    {
        if ($header === '' || $secret === '') {
            return false;
        }
        $timestamp = '';
        $signatures = [];
        foreach (explode(',', $header) as $part) {
            $kv = explode('=', trim($part), 2);
            if (count($kv) !== 2) {
                return false;
            }
            [$key, $value] = [trim($kv[0]), strtolower(trim($kv[1]))];
            if ($key === 't') {
                $timestamp = $value;
            } elseif (preg_match('/^s\d+$/', $key)) {
                $signatures[] = $value;
            }
        }
        if (!ctype_digit($timestamp) || !$signatures) {
            return false;
        }
        if (abs(($now ?? time()) - (int) $timestamp) > self::TOLERANCE_SECONDS) {
            return false;
        }
        $expected = hash_hmac('sha256', $timestamp . '.' . $rawBody, $secret);
        foreach ($signatures as $sig) {
            if (hash_equals($expected, $sig)) {
                return true;
            }
        }
        return false;
    }

    /** Applies a verified event to the stored document. Returns what happened, for logging and tests. */
    public static function apply(Store $store, array $payload): string
    {
        $data = is_array($payload['data'] ?? null) ? $payload['data'] : [];
        $event = is_array($payload['event'] ?? null) ? $payload['event'] : [];
        if (($data['object'] ?? null) !== 'document') {
            return 'ignored';
        }
        $record = $store->get((string) ($data['documentId'] ?? ''));
        if (!$record) {
            return 'unknown-document';
        }
        $record['events'] ??= [];
        $id = $event['id'] ?? null;
        foreach ($record['events'] as $e) {
            if ($id && ($e['id'] ?? null) === $id) {
                return 'duplicate';
            }
        }
        $created = $event['created'] ?? null;
        if (!empty($record['lastEventCreated']) && $created && $created < $record['lastEventCreated']) {
            return 'stale';
        }
        $type = (string) ($event['eventType'] ?? '');
        $record['events'][] = ['id' => $id, 'type' => $type, 'created' => $created];
        $record['lastEventCreated'] = $created ?: ($record['lastEventCreated'] ?? null);
        if (in_array($type, self::STATUS, true) && !in_array($record['status'] ?? '', self::FINAL, true)) {
            $record['status'] = $type;
        }
        if ($type === 'SendFailed') {
            $record['error'] = $data['errorMessage'] ?? 'Send failed';
        }
        $store->save($record);
        return 'applied';
    }
}
