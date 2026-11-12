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
    ->setApiKey(getenv('BOLDSIGN_API_KEY'));

// EU region
$config = Configuration::getDefaultConfiguration()
    ->setHost('https://api-eu.boldsign.com')
    ->setApiKey(getenv('BOLDSIGN_API_KEY'));

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
use BoldSign\Model\Role;

function sendFromTemplate(TemplateApi $templateApi, string $templateId): string
{
    $role = new Role([
        'role_index' => 1,
        'signer_name' => 'Jane Smith',
        'signer_email' => 'jane@example.com',
    ]);

    $sendRequest = new SendForSignFromTemplateForm([
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
        null,   // phoneNumber
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

// Header: "t=<unix seconds>, s0=<hex>[, s1=<hex>]". Signature: HMAC SHA256 over "<t>." + raw body.
function verifyBoldSignSignature(string $rawBody, string $header, string $secret, int $tolerance = 300): bool
{
    if ($header === '' || $secret === '') {
        return false;
    }
    $timestamp = '';
    $signatures = [];
    foreach (explode(',', $header) as $part) {
        $pair = explode('=', trim($part), 2);
        if (count($pair) !== 2) {
            return false;
        }
        if ($pair[0] === 't') {
            $timestamp = $pair[1];
        } elseif (preg_match('/^s\d+$/', $pair[0])) {
            $signatures[] = strtolower($pair[1]);
        }
    }
    if (!ctype_digit($timestamp) || $signatures === []) {
        return false;
    }
    if (abs(time() - (int) $timestamp) > $tolerance) {
        return false;
    }
    $expected = hash_hmac('sha256', $timestamp . '.' . $rawBody, $secret);
    foreach ($signatures as $signature) {
        if (hash_equals($expected, $signature)) {
            return true;
        }
    }
    return false;
}

// BoldSign's URL check when the webhook is added: identified by the header, answered with 200
if (($_SERVER['HTTP_X_BOLDSIGN_EVENT'] ?? '') === 'Verification') {
    http_response_code(200);
    exit;
}

$rawBody = file_get_contents('php://input'); // raw bytes, exactly as sent
$signature = $_SERVER['HTTP_X_BOLDSIGN_SIGNATURE'] ?? '';

if (!verifyBoldSignSignature($rawBody, $signature, getenv('BOLDSIGN_WEBHOOK_SECRET') ?: '')) {
    http_response_code(401);
    exit('Invalid signature');
}

$payload = json_decode($rawBody, true); // parse only after verifying
$eventType = $payload['event']['eventType'];
$data = $payload['data'] ?? [];          // the document is in data, not document
if (($data['object'] ?? '') !== 'document') {
    http_response_code(200);
    exit;
}
$documentId = $data['documentId'];

switch ($eventType) {
    case 'Completed':
        handleCompleted($documentId);
        break;
    case 'SendFailed':
        error_log("Send failed for $documentId: " . ($data['errorMessage'] ?? ''));
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
        // BoldSign's URL check when the webhook is added
        if ($request->header('X-BoldSign-Event') === 'Verification') {
            return response('', 200);
        }

        // verifyBoldSignSignature() as in the plain PHP handler above; getContent() is the raw body
        $valid = verifyBoldSignSignature(
            $request->getContent(),
            (string) $request->header('X-BoldSign-Signature', ''),
            (string) config('services.boldsign.webhook_secret')
        );
        if (!$valid) {
            abort(401);
        }

        $payload = json_decode($request->getContent(), true); // parse only after verifying
        $eventType = $payload['event']['eventType'];
        $data = $payload['data'] ?? [];                        // the document is in data

        match($eventType) {
            'Completed' => $this->handleCompleted($data['documentId']),
            'SendFailed' => Log::error('BoldSign send failed', ['documentId' => $data['documentId'] ?? null, 'error' => $data['errorMessage'] ?? null]),
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
    $api->resendInvitationSenderIdentities($email);
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
