<?php
declare(strict_types=1);

namespace App;

/**
 * Routes: create a document for the signed-in user, sign it inside the app in an iframe, a redirect page for when
 * signing ends, and a verified BoldSign webhook.
 */
final class App
{
    private const STYLE = '<style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}iframe{width:100%;height:80vh;border:1px solid #ccc}</style>';

    /** @var callable(string):void */
    private $log;
    /** @var callable():array{name:string,email:string} */
    private $currentUser;

    /**
     * $currentUser returns the signed-in user as ['name' => ..., 'email' => ...]. This recipe uses one demo user from
     * SIGNER_NAME and SIGNER_EMAIL: replace it with your application's authentication. Signing links go to whoever it returns.
     */
    public function __construct(private Config $cfg, private ?BoldSignClient $boldsign, private Store $store, ?callable $log = null, ?callable $currentUser = null)
    {
        $this->log = $log ?? static fn (string $m) => error_log($m);
        $this->currentUser = $currentUser ?? fn () => ['name' => $this->cfg->signerName, 'email' => $this->cfg->signerEmail];
    }

    private static function e(mixed $s): string
    {
        return htmlspecialchars((string) $s, ENT_QUOTES, 'UTF-8');
    }

    private static function page(string $title, string $body, string $csp = "default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'", array $headers = []): Response
    {
        return Response::html("<!doctype html><meta charset='utf-8'><title>" . self::e($title) . '</title>' . self::STYLE . $body, 200,
            ['Content-Security-Policy' => $csp] + $headers);
    }

    private function me(): array
    {
        return ($this->currentUser)();
    }

    /** Only the document's signer may open it. */
    private function owned(string $id): ?array
    {
        $d = $this->store->get($id);
        return $d && $d['signer']['email'] === $this->me()['email'] ? $d : null;
    }

    public function handle(Request $r): Response
    {
        $m = $r->method;
        $p = rtrim($r->path, '/') ?: '/';
        if ($m === 'POST' && $p === '/webhooks/boldsign') {
            return $this->webhook($r);
        }
        if ($m === 'POST' && $p === '/documents') {
            return $this->create($r);
        }
        if ($m === 'GET' && $p === '/documents') {
            return $this->documents();
        }
        if ($m === 'GET' && $p === '/documents.json') {
            return Response::json(array_values(array_filter($this->store->all(), fn ($d) => $d['signer']['email'] === $this->me()['email'])));
        }
        if ($m === 'GET' && preg_match('#^/documents/([^/]+)/sign$#', $p, $x)) {
            return $this->sign(rawurldecode($x[1]));
        }
        if ($m === 'GET' && $p === '/signing/complete') {
            return $this->complete((string) ($r->query['documentId'] ?? ''));
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

    private function create(Request $r): Response
    {
        $user = $this->me();
        if (($user['email'] ?? '') === '') {
            return Response::json(['error' => 'No signed-in user (set SIGNER_EMAIL)'], 400);
        }
        $title = (string) ($r->input()['title'] ?? '') ?: 'Policy acknowledgement';
        try {
            $documentId = $this->boldsign->createDocument($title, SamplePdf::build($title, $user['name']), $user['name'], $user['email']);
        } catch (\Throwable $e) { // report the BoldSign error, never the request
            $s = BoldSignClient::errorSummary($e);
            ($this->log)('create failed ' . json_encode($s));
            return Response::json(['error' => 'BoldSign create failed'] + $s, 502);
        }
        $this->store->save(['documentId' => $documentId, 'title' => $title, 'signer' => $user, 'status' => 'Queued', 'createdAt' => gmdate('c'), 'events' => []]);
        $signingPage = '/documents/' . rawurlencode($documentId) . '/sign';
        return $r->wantsHtml() ? Response::redirect($signingPage, 303)
            : Response::json(['documentId' => $documentId, 'status' => 'Queued', 'signingPage' => $signingPage], 201);
    }

    private function sign(string $id): Response
    {
        $d = $this->owned($id);
        if (!$d) {
            return new Response(404);
        }
        $complete = '/signing/complete?documentId=' . rawurlencode($id);
        if (in_array($d['status'], Webhook::FINAL, true)) {
            return Response::redirect($complete);
        }
        try {
            $link = $this->boldsign->signLink($id, $d['signer']['email'], $this->cfg->appBaseUrl . $complete);
        } catch (\Throwable $e) {
            return Response::json(['error' => 'Could not get a signing link'] + BoldSignClient::errorSummary($e), 502);
        }
        return self::page('Sign: ' . $d['title'], '<h1>' . self::e($d['title']) . "</h1><p><a href='/documents'>Back to documents</a></p>"
            . '<iframe src="' . self::e($link) . "\" title='BoldSign signing' allow='camera'></iframe>",
            "default-src 'self'; style-src 'unsafe-inline'; frame-src https://app.boldsign.com https://*.boldsign.com",
            ['Cache-Control' => 'no-store', 'Referrer-Policy' => 'no-referrer']);
    }

    /** Where BoldSign sends the signer when signing ends (redirect URL). Breaks out of the iframe and shows the status. */
    private function complete(string $id): Response
    {
        $d = $this->store->get($id);
        $info = $d ? 'Document <code>' . self::e($d['documentId']) . '</code>: status <b>' . self::e($d['status']) . '</b>. It changes to Completed when BoldSign\'s webhook arrives.'
            : 'Signing finished.';
        return self::page('Signing finished', '<script>if (window.top !== window.self) window.top.location.href = window.location.href;</script>'
            . "<h1>Thank you</h1><p>$info</p><p><a href='/documents'>Back to documents</a></p>");
    }

    private function documents(): Response
    {
        $me = $this->me();
        $rows = '';
        foreach ($this->store->all() as $d) {
            if ($d['signer']['email'] !== $me['email']) {
                continue;
            }
            $action = in_array($d['status'], Webhook::FINAL, true) ? '' : "<a href='/documents/" . rawurlencode($d['documentId']) . "/sign'>Open signing page</a>";
            $rows .= '<tr><td>' . self::e($d['title']) . '</td><td><b>' . self::e($d['status']) . '</b>' . (!empty($d['error']) ? ' (' . self::e($d['error']) . ')' : '')
                . '</td><td><code>' . self::e($d['documentId']) . "</code></td><td>$action</td></tr>";
        }
        return self::page('My documents', '<h1>My documents</h1><p>Signed in as ' . self::e($me['name']) . ' (' . self::e($me['email']) . ')</p>'
            . "<form method='post' action='/documents'><button>Create a document and sign it here</button></form>"
            . '<table><tr><th>Title</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>' . ($rows ?: '<tr><td colspan=4>No documents yet</td></tr>') . '</table>');
    }
}
