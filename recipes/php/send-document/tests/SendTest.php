<?php
declare(strict_types=1);

namespace Tests;

use App\App;
use App\BoldSignClient;
use App\Request;
use App\SamplePdf;
use BoldSign\Api\DocumentApi;
use BoldSign\ApiException;
use BoldSign\Model\DocumentCreated;

final class SendTest extends TestCase
{
    public function testSamplePdfHasTheTextTags(): void
    {
        $pdf = SamplePdf::build();
        $this->assertStringStartsWith('%PDF-', $pdf);
        $this->assertStringContainsString(SamplePdf::SIGN_TAG, $pdf);
        $this->assertStringContainsString(SamplePdf::DATE_TAG, $pdf);
        $this->assertStringStartsWith('{{sign|1|*', SamplePdf::SIGN_TAG);
    }

    public function testSendDocumentBuildsATextTagRequest(): void
    {
        $captured = new \ArrayObject();
        $client = new class ($this->cfg(), $captured) extends BoldSignClient {
            public function __construct(\App\Config $cfg, private \ArrayObject $captured)
            {
                parent::__construct($cfg);
            }

            protected function documents(): DocumentApi
            {
                $captured = $this->captured;
                return new class ($captured) extends DocumentApi {
                    public function __construct(private \ArrayObject $captured)
                    {
                        parent::__construct();
                    }

                    public function sendDocument($send_for_sign = null, string $contentType = self::contentTypes['sendDocument'][0])
                    {
                        $this->captured['req'] = $send_for_sign;
                        $file = $send_for_sign->getFiles()[0];
                        $this->captured['name'] = $file->getFilename();
                        $this->captured['bytes'] = file_get_contents($file->getPathname());
                        return (new DocumentCreated())->setDocumentId('doc-42');
                    }
                };
            }
        };
        $this->assertSame('doc-42', $client->sendDocument('T', SamplePdf::build(), 'N', 'n@boldsign.dev'));
        $req = $captured['req'];
        $this->assertTrue($req->getUseTextTags());
        $this->assertCount(1, $req->getSigners());
        $this->assertSame('n@boldsign.dev', $req->getSigners()[0]->getEmailAddress());
        $this->assertEmpty($req->getSigners()[0]->getFormFields());
        $this->assertSame('document.pdf', $captured['name']);
        $this->assertStringStartsWith('%PDF-', $captured['bytes']);
    }

    public function testPostDocumentsSendsAndStoresQueued(): void
    {
        $store = $this->store();
        $fake = new class ($this->cfg()) extends BoldSignClient {
            public array $calls = [];

            public function sendDocument(string $title, string $pdf, string $signerName, string $signerEmail): string
            {
                $this->calls[] = [$title, $signerName, $signerEmail];
                return 'doc-7';
            }
        };
        $res = (new App($this->cfg(), $fake, $store, fn () => null))->handle(new Request('POST', '/documents', ['accept' => 'application/json', 'content-type' => 'application/json'], '{}'));
        $this->assertSame(201, $res->status);
        $this->assertSame('doc-7', json_decode($res->body, true)['documentId']);
        $this->assertSame('signer@boldsign.dev', $fake->calls[0][2]);
        $this->assertSame('Queued', $store->get('doc-7')['status']);
    }

    public function testRetrySoonRetries400And403Only(): void
    {
        $n = 0;
        $this->assertSame('ok', BoldSignClient::retrySoon(function () use (&$n) {
            if (++$n < 3) {
                throw new ApiException('not ready', 403);
            }
            return 'ok';
        }, 5, 0.001));
        $m = 0;
        try {
            BoldSignClient::retrySoon(function () use (&$m) {
                $m++;
                throw new ApiException('unauthorized', 401);
            }, 5, 0.001);
            $this->fail('expected an exception');
        } catch (ApiException) {
        }
        $this->assertSame(1, $m);
    }

    public function testErrorSummaryKeepsOnlyStatusAndBody(): void
    {
        $s = BoldSignClient::errorSummary(new ApiException('x', 401, ['X-API-KEY' => ['secret']], '{"error":"Unauthorized"}'));
        $this->assertSame(['status' => 401, 'error' => '{"error":"Unauthorized"}'], $s);
    }
}
