<?php
declare(strict_types=1);

namespace App;

use BoldSign\Api\BrandingApi;
use BoldSign\Api\DocumentApi;
use BoldSign\Api\TemplateApi;
use BoldSign\ApiException;
use BoldSign\Configuration;
use BoldSign\Model\Role;
use BoldSign\Model\SendForSignFromTemplateForm;

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
     * A new document (or template) is processed asynchronously: for a few seconds the next call on it can return
     * 403 "Invalid Document ID" or 400 "Invalid template id.". Retry those briefly; anything else is a real error.
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

    protected function templates(): TemplateApi
    {
        return new TemplateApi($this->configuration);
    }

    /** One read-only call: 200 means the key and the region's base URL match. */
    public function checkConfig(): mixed
    {
        return (new BrandingApi($this->configuration))->brandList();
    }

    /**
     * The template's roles as [['name' => ..., 'index' => ...]] (read-only), to check the role configuration.
     * @return list<array{name:string,index:int}>
     */
    public function templateRoles(string $templateId): array
    {
        $api = $this->templates();
        $props = self::retrySoon(fn () => $api->getProperties($templateId));
        return array_map(fn ($r) => ['name' => (string) $r->getName(), 'index' => (int) $r->getIndex()], $props->getRoles() ?? []);
    }

    /**
     * Sends from the template, with one signer per role. BoldSign emails each signer. Returns the document ID; the send
     * completes asynchronously, and the webhook reports Sent or SendFailed. A template created seconds ago can return
     * 400 "Invalid template id." for a moment, so the first 400 is retried.
     * @param list<array{role_index:int,signer_role:string,signer_name:string,signer_email:string}> $roles
     */
    public function sendFromTemplate(string $templateId, string $title, array $roles): string
    {
        $request = (new SendForSignFromTemplateForm())->setTitle($title)->setRoles(array_map(
            fn (array $r) => (new Role())->setRoleIndex($r['role_index'])->setSignerRole($r['signer_role'])->setSignerName($r['signer_name'])->setSignerEmail($r['signer_email']),
            $roles,
        ));
        $api = $this->templates();
        return self::retrySoon(fn () => $api->sendUsingTemplate($templateId, $request))->getDocumentId();
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
