<?php
declare(strict_types=1);

namespace App;

use BoldSign\Api\BrandingApi;
use BoldSign\Api\DocumentApi;
use BoldSign\ApiException;
use BoldSign\Configuration;
use BoldSign\Model\DocumentSigner;
use BoldSign\Model\SendForSign;

/** The official BoldSign PHP SDK (boldsign/boldsign-php 3.3.4), wrapped for this app. */
class BoldSignClient
{
    private Configuration $configuration;

    public function __construct(Config $cfg)
    {
        $this->configuration = (new Configuration())->setApiKey($cfg->apiKey)->setHost($cfg->baseUrl);
    }

    public static function statusOf(\Throwable $e): ?int
    {
        return $e instanceof ApiException ? $e->getCode() : null;
    }

    /** SDK exceptions can carry request details; log only this summary, never the full object. */
    public static function errorSummary(\Throwable $e): array
    {
        $body = $e instanceof ApiException ? $e->getResponseBody() : null;
        $text = is_string($body) ? $body : ($body !== null ? json_encode($body) : $e->getMessage());
        return ['status' => self::statusOf($e), 'error' => mb_substr((string) $text, 0, 300)];
    }

    /**
     * A new document is processed asynchronously: for a few seconds the next call on it can return 403
     * "Invalid Document ID" (or 400). Retry those briefly; anything else is a real error.
     */
    public static function retrySoon(callable $fn, int $tries = 5, float $waitS = 2.0): mixed
    {
        for ($i = 1; ; $i++) {
            try {
                return $fn();
            } catch (ApiException $e) {
                if ($i >= $tries || !in_array($e->getCode(), [400, 403], true)) {
                    throw $e;
                }
                usleep((int) ($waitS * $i * 1_000_000));
            }
        }
    }

    protected function documents(): DocumentApi
    {
        return new DocumentApi($this->configuration);
    }

    /** One read-only call: 200 means the key and the region's base URL match. */
    public function checkConfig(): mixed
    {
        return (new BrandingApi($this->configuration))->brandList();
    }

    /**
     * Sends the PDF to one signer. Fields come from the text tags in the PDF (setUseTextTags), so no template and no
     * coordinates. BoldSign emails the signer. The send completes asynchronously (webhook: Sent or SendFailed).
     */
    public function sendDocument(string $title, string $pdf, string $signerName, string $signerEmail): string
    {
        $dir = sys_get_temp_dir() . '/boldsign-' . bin2hex(random_bytes(6));
        mkdir($dir, 0700);
        $path = $dir . '/document.pdf'; // the SDK uploads a file with its file name
        file_put_contents($path, $pdf);
        try {
            $signer = (new DocumentSigner())->setName($signerName)->setEmailAddress($signerEmail)->setSignerType('Signer');
            $request = (new SendForSign())
                ->setTitle($title)
                ->setSigners([$signer])
                ->setFiles([new \SplFileObject($path)])
                ->setUseTextTags(true);
            return $this->documents()->sendDocument($request)->getDocumentId();
        } finally {
            @unlink($path);
            @rmdir($dir);
        }
    }

    /** A signing link for the signer, for an in-app "Sign now" button (the signer can also use the email). */
    public function signLink(string $documentId, string $signerEmail, string $redirectUrl): string
    {
        $api = $this->documents();
        return self::retrySoon(fn () => $api->getEmbeddedSignLink($documentId, $signerEmail, null, null, null, $redirectUrl))->getSignLink();
    }

    /** Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour). */
    public function status(string $documentId): ?string
    {
        $api = $this->documents();
        return self::retrySoon(fn () => $api->getProperties($documentId))->getStatus();
    }
}
