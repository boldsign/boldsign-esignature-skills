<?php
declare(strict_types=1);

namespace App;

/** Routes: send a PDF with text tags, a status page, and a verified BoldSign webhook. */
final class App
{
    private const STYLE = '<style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}</style>';

    /** @var callable(string):void */
    private $log;

    public function __construct(private Config $cfg, private ?BoldSignClient $boldsign, private Store $store, ?callable $log = null)
    {
        $this->log = $log ?? static fn (string $m) => error_log($m);
    }

    private static function e(mixed $s): string
    {
        return htmlspecialchars((string) $s, ENT_QUOTES, 'UTF-8');
    }

    public function handle(Request $r): Response
    {
        $m = $r->method;
        $p = rtrim($r->path, '/') ?: '/';
        if ($m === 'POST' && $p === '/webhooks/boldsign') {
            return $this->webhook($r);
        }
        if ($m === 'POST' && $p === '/documents') {
            return $this->send($r);
        }
        if ($m === 'GET' && $p === '/documents') {
            return $this->documents();
        }
        if ($m === 'GET' && $p === '/documents.json') {
            return Response::json($this->store->all());
        }
        if ($m === 'GET' && preg_match('#^/documents/([^/]+)/sign$#', $p, $x)) {
            return $this->sign(rawurldecode($x[1]));
        }
        if ($m === 'POST' && preg_match('#^/documents/([^/]+)/refresh$#', $p, $x)) {
            return $this->refresh(rawurldecode($x[1]));
        }
        if ($m === 'GET' && $p === '/') {
            return Response::redirect('/documents');
        }
        return new Response(404, 'Not found');
    }

    private function webhook(Request $r): Response
    {
        if ($r->header('X-BoldSign-Event') === 'Verification') { // BoldSign's URL check
            return new Response(200);
        }
        if ($this->cfg->webhookSecret === '') {
            return new Response(500, 'Webhook secret not configured');
        }
        if (!Webhook::verify($r->body, $r->header('X-BoldSign-Signature'), $this->cfg->webhookSecret)) {
            ($this->log)('webhook: signature check failed');
            return new Response(401, 'Invalid signature');
        }
        $payload = json_decode($r->body, true);
        if (!is_array($payload)) {
            return new Response(400, 'Invalid JSON');
        }
        $result = Webhook::apply($this->store, $payload);
        ($this->log)(sprintf('webhook: %s %s %s', $payload['event']['eventType'] ?? '', $payload['data']['documentId'] ?? '', $result));
        return new Response(200); // also for unknown documents, so BoldSign does not retry them
    }

    private function send(Request $r): Response
    {
        $in = $r->input();
        $name = (string) ($in['signerName'] ?? '') ?: $this->cfg->signerName;
        $email = (string) ($in['signerEmail'] ?? '') ?: $this->cfg->signerEmail;
        if ($email === '') {
            return Response::json(['error' => 'Set SIGNER_EMAIL or post signerEmail'], 400);
        }
        $title = (string) ($in['title'] ?? '') ?: 'Policy acknowledgement';
        try {
            $documentId = $this->boldsign->sendDocument($title, SamplePdf::build($title, $name), $name, $email);
        } catch (\Throwable $e) { // report the BoldSign error, never the request
            $s = BoldSignClient::errorSummary($e);
            ($this->log)('send failed ' . json_encode($s));
            return Response::json(['error' => 'BoldSign send failed'] + $s, 502);
        }
        $record = $this->store->save(['documentId' => $documentId, 'title' => $title, 'signer' => ['name' => $name, 'email' => $email],
            'status' => 'Queued', 'createdAt' => gmdate('c'), 'events' => []]);
        return $r->wantsHtml() ? Response::redirect('/documents', 303) : Response::json($record, 201);
    }

    private function documents(): Response
    {
        $rows = '';
        foreach ($this->store->all() as $d) {
            $id = rawurlencode($d['documentId']);
            $actions = in_array($d['status'], Webhook::FINAL, true) ? '' :
                "<a href='/documents/$id/sign'>Sign now</a> <form method='post' action='/documents/$id/refresh' style='display:inline'><button>Refresh</button></form>";
            $rows .= '<tr><td>' . self::e($d['title']) . '</td><td>' . self::e($d['signer']['email']) . '</td><td><b>' . self::e($d['status']) . '</b>'
                . (!empty($d['error']) ? ' (' . self::e($d['error']) . ')' : '') . '</td><td><code>' . self::e($d['documentId']) . "</code></td><td>$actions</td></tr>";
        }
        $to = self::e($this->cfg->signerEmail ?: 'SIGNER_EMAIL');
        return Response::html("<!doctype html><meta charset='utf-8'><title>Documents</title>" . self::STYLE
            . "<h1>Documents</h1><form method='post' action='/documents'><button>Send the sample PDF to $to</button></form>"
            . '<table><tr><th>Title</th><th>Signer</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>'
            . ($rows ?: '<tr><td colspan=5>No documents yet</td></tr>') . '</table>');
    }

    private function sign(string $id): Response
    {
        $d = $this->store->get($id);
        if (!$d) {
            return new Response(404);
        }
        try {
            return Response::redirect($this->boldsign->signLink($id, $d['signer']['email'], $this->cfg->appBaseUrl . '/documents'));
        } catch (\Throwable $e) {
            return Response::json(['error' => 'Could not get a signing link'] + BoldSignClient::errorSummary($e), 502);
        }
    }

    private function refresh(string $id): Response
    {
        $d = $this->store->get($id);
        if (!$d) {
            return new Response(404);
        }
        try {
            $status = $this->boldsign->status($id);
        } catch (\Throwable $e) {
            return Response::json(['error' => 'Could not read the status'] + BoldSignClient::errorSummary($e), 502);
        }
        // InProgress only replaces Queued: later webhook statuses (Sent, Viewed, Signed) are more precise
        if ($status && !($status === 'InProgress' && $d['status'] !== 'Queued') && !in_array($d['status'], Webhook::FINAL, true)) {
            $d['status'] = $status;
            $this->store->save($d);
        }
        return Response::redirect('/documents', 303);
    }
}
