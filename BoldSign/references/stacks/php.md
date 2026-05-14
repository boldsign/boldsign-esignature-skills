# BoldSign — PHP Integration Reference

## SDK Install

```bash
composer require boldsign/boldsign-php
```

Docs: https://developers.boldsign.com/sdks/php-sdk/

---

## Setup & Authentication

```php
<?php
require_once 'vendor/autoload.php';

use BoldSign\Api\DocumentApi;
use BoldSign\Api\TemplateApi;
use BoldSign\Configuration;

// API Key auth
$config = Configuration::getDefaultConfiguration()
    ->setApiKey('X-API-KEY', getenv('BOLDSIGN_API_KEY'));

// EU region
$config = Configuration::getDefaultConfiguration()
    ->setHost('https://api-eu.boldsign.com')
    ->setApiKey('X-API-KEY', getenv('BOLDSIGN_API_KEY'));

// OAuth Bearer Token
$config = Configuration::getDefaultConfiguration()
    ->setAccessToken(getenv('BOLDSIGN_ACCESS_TOKEN'));

$documentApi = new DocumentApi(null, $config);
$templateApi = new TemplateApi(null, $config);
```

---

## Send a Document for Signature

```php
<?php
use BoldSign\Model\SendForSign;
use BoldSign\Model\DocumentSigner;
use BoldSign\Model\FormField;
use BoldSign\Model\Rectangle;

function sendDocument(DocumentApi $documentApi): string
{
    $bounds = new Rectangle([
        'x' => 100,
        'y' => 200,
        'width' => 200,
        'height' => 50,
    ]);

    $signatureField = new FormField([
        'field_type' => 'Signature',
        'page_number' => 1,
        'bounds' => $bounds,
        'is_required' => true,
    ]);

    $signer = new DocumentSigner([
        'name' => 'John Doe',
        'email_address' => 'john@example.com',
        'signer_type' => 'Signer',
        'form_fields' => [$signatureField],
    ]);

    $sendRequest = new SendForSign([
        'title' => 'Service Agreement',
        'message' => 'Please sign this agreement.',
        'signers' => [$signer],
        'files' => [new \SplFileObject('contract.pdf')],
    ]);

    $result = $documentApi->sendDocument($sendRequest);
    echo 'Document ID: ' . $result->getDocumentId() . PHP_EOL;
    // NOTE: Async — use webhooks to confirm Sent status
    return $result->getDocumentId();
}
```

---

## Send from Template

```php
use BoldSign\Model\SendForSignFromTemplate;
use BoldSign\Model\Roles;

function sendFromTemplate(TemplateApi $templateApi, string $templateId): string
{
    $role = new Roles([
        'role_index' => 1,
        'signer_name' => 'Jane Smith',
        'signer_email' => 'jane@example.com',
    ]);

    $sendRequest = new SendForSignFromTemplate([
        'template_id' => $templateId,
        'title' => 'Contract for Jane',
        'roles' => [$role],
    ]);

    $result = $templateApi->sendUsingTemplate($sendRequest);
    return $result->getDocumentId();
}
```

---

## Embedded Signing Link

```php
function getEmbeddedSignLink(DocumentApi $documentApi, string $documentId, string $signerEmail): string
{
    $expiry = new \DateTime('+24 hours');

    $result = $documentApi->getEmbeddedSignLink(
        $documentId,
        $signerEmail,
        null,   // countryCode
        null,   // sendSMS
        $expiry,
        'https://yourapp.com/signing-complete'
    );

    return $result->getSignLink(); // embed in <iframe>
}
```

---

## Webhook Handler

```php
<?php
// webhook.php

function verifyHmac(string $payload, string $signature): bool
{
    $secret = getenv('BOLDSIGN_WEBHOOK_SECRET');
    $expected = hash_hmac('sha256', $payload, $secret);
    return hash_equals($expected, $signature);
}

$rawBody = file_get_contents('php://input');
$signature = $_SERVER['HTTP_X_BOLDSIGN_SIGNATURE'] ?? '';

if (!verifyHmac($rawBody, $signature)) {
    http_response_code(401);
    exit('Invalid signature');
}

$payload = json_decode($rawBody, true);
$eventType = $payload['event']['eventType'];
$documentId = $payload['document']['documentId'];

switch ($eventType) {
    case 'Completed':
        handleCompleted($documentId);
        break;
    case 'SendFailed':
        error_log("Send failed for $documentId: " . json_encode($payload['document']));
        break;
    case 'Declined':
        error_log("Declined: $documentId");
        break;
}

http_response_code(200);
echo 'OK';

function handleCompleted(string $documentId): void
{
    global $documentApi;

    // Download signed PDF
    $pdf = $documentApi->downloadDocument($documentId);
    file_put_contents("signed/{$documentId}.pdf", $pdf);

    // Download audit trail
    $audit = $documentApi->downloadAuditLog($documentId);
    file_put_contents("signed/{$documentId}-audit.pdf", $audit);
}
```

---

## Laravel Integration

```php
// routes/api.php
Route::post('/webhooks/boldsign', [BoldSignWebhookController::class, 'handle']);

// app/Http/Controllers/BoldSignWebhookController.php
class BoldSignWebhookController extends Controller
{
    public function handle(Request $request): Response
    {
        $signature = $request->header('X-BoldSign-Signature');
        $secret = config('services.boldsign.webhook_secret');
        $expected = hash_hmac('sha256', $request->getContent(), $secret);

        if (!hash_equals($expected, $signature)) {
            abort(401);
        }

        $payload = $request->json()->all();
        $eventType = $payload['event']['eventType'];

        match($eventType) {
            'Completed' => $this->handleCompleted($payload),
            'SendFailed' => Log::error('BoldSign send failed', $payload),
            default => null,
        };

        return response('OK', 200);
    }
}
```

---

## .env

```env
BOLDSIGN_API_KEY=your_api_key_here
BOLDSIGN_WEBHOOK_SECRET=your_webhook_secret_here
```

---

## Sender Identity — Register and Send on Behalf of Tenant

```php
use BoldSign\Api\SenderIdentitiesApi;
use BoldSign\Model\CreateSenderIdentityRequest;
use BoldSign\Model\ResendSenderIdentityRequest;

$senderApi = new SenderIdentitiesApi(null, $config);

function onboardTenant(SenderIdentitiesApi $api, string $name, string $email): void
{
    // Step 1: Register identity
    $createRequest = new CreateSenderIdentityRequest();
    $createRequest->setName($name);
    $createRequest->setEmail($email);
    $api->createSenderIdentities($createRequest);

    // Step 2: Send approval email to tenant
    $approvalRequest = new ResendSenderIdentityRequest();
    $approvalRequest->setEmail($email);
    $api->requestForIdentityApproval($approvalRequest);
    // Tenant clicks Approve in email → SenderIdentityApproved webhook fires
}

function sendOnBehalfOf(DocumentApi $documentApi, string $tenantEmail, SendForSign $request): object
{
    $request->setOnBehalfOf($tenantEmail); // ← key field
    return $documentApi->sendDocument($request);
}
```

---

## Rate Limit Handling

```php
function callWithRetry(callable $fn, int $maxRetries = 1): mixed
{
    for ($attempt = 0; $attempt <= $maxRetries; $attempt++) {
        try {
            return $fn();
        } catch (\Exception $e) {
            if ($attempt === $maxRetries || $e->getCode() !== 429) {
                throw $e;
            }
            sleep(60); // Wait before retry
        }
    }
}
```

**Sandbox: 50 req/hour. Production: 2,000 req/hour (account-level).**  
Use webhooks instead of polling to avoid burning quota.

---

## SDK Links
- Composer: `composer require boldsign/boldsign-php`
- GitHub: https://github.com/boldsign/boldsign-php-sdk
- Full docs: https://developers.boldsign.com/sdks/php-sdk/
