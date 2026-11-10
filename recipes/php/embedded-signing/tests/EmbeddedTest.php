<?php
declare(strict_types=1);

namespace Tests;

use App\App;
use App\BoldSignClient;
use App\Config;
use App\Request;
use App\SamplePdf;
use BoldSign\Api\DocumentApi;
use BoldSign\ApiException;
use BoldSign\Model\DocumentCreated;
use BoldSign\Model\EmbeddedSigningLink;

final class EmbeddedTest extends TestCase
{
    /** A BoldSignClient whose SDK document API is a stub that records calls. */
    private function stubbed(\ArrayObject $calls, int $failFirstLinks = 0): BoldSignClient
    {
        return new class ($this->cfg(), $calls, $failFirstLinks) extends BoldSignClient {
            public function __construct(Config $cfg, private \ArrayObject $calls, private int $failFirstLinks)
            {
                parent::__construct($cfg);
            }

            protected function documents(): DocumentApi
            {
                $calls = $this->calls;
                $fail = &$this->failFirstLinks;
                return new class ($calls, $fail) extends DocumentApi {
                    public function __construct(private \ArrayObject $calls, private int &$fail)
                    {
                        parent::__construct();
                    }

                    public function sendDocument($send_for_sign = null, string $contentType = self::contentTypes['sendDocument'][0])
                    {
                        $this->calls['send'] = $send_for_sign;
                        return (new DocumentCreated())->setDocumentId('doc-1');
                    }

                    public function getEmbeddedSignLink($document_id, $signer_email = null, $country_code = null, $phone_number = null, $sign_link_valid_till = null, $redirect_url = null, string $contentType = self::contentTypes['getEmbeddedSignLink'][0])
                    {
                        $this->calls['links'] = ($this->calls['links'] ?? 0) + 1;
                        $this->calls['redirect'] = $redirect_url;
                        if ($this->fail-- > 0) {
                            throw new ApiException('Invalid Document ID', 403);
                        }
                        return (new EmbeddedSigningLink())->setSignLink('https://app.boldsign.com/document/sign/?documentId=x');
                    }
                };
            }
        };
    }

    public function testSamplePdfHasTheTextTags(): void
    {
        $this->assertStringContainsString(SamplePdf::SIGN_TAG, SamplePdf::build());
    }

    public function testCreateDocumentUsesTextTagsAndDisablesEmails(): void
    {
        $calls = new \ArrayObject();
        $this->assertSame('doc-1', $this->stubbed($calls)->createDocument('T', SamplePdf::build(), 'N', 'n@boldsign.dev'));
        $req = $calls['send'];
        $this->assertTrue($req->getUseTextTags());
        $this->assertTrue($req->getDisableEmails());
        $this->assertEmpty($req->getSigners()[0]->getFormFields());
    }

    public function testSignLinkRedirectsBackAndRetriesTheFirst403(): void
    {
        $calls = new \ArrayObject();
        $link = $this->stubbed($calls, 1)->signLink('doc-1', 'n@boldsign.dev', 'http://localhost:3000/signing/complete?documentId=doc-1');
        $this->assertStringStartsWith('https://app.boldsign.com/', $link);
        $this->assertSame(2, $calls['links']);
        $this->assertSame('http://localhost:3000/signing/complete?documentId=doc-1', $calls['redirect']);
    }

    public function testCreateSigningIframeOwnerOnlyAndRedirectPage(): void
    {
        $store = $this->store();
        $calls = new \ArrayObject();
        $app = new App($this->cfg(), $this->stubbed($calls), $store, fn () => null);
        $res = $app->handle(new Request('POST', '/documents', ['accept' => 'application/json']));
        $this->assertSame(201, $res->status);
        $this->assertSame('/documents/doc-1/sign', json_decode($res->body, true)['signingPage']);

        $page = $app->handle(new Request('GET', '/documents/doc-1/sign'));
        $this->assertSame(200, $page->status);
        $this->assertStringContainsString('<iframe src="https://app.boldsign.com/', $page->body);
        $this->assertStringContainsString('frame-src https://app.boldsign.com', $page->headers['Content-Security-Policy']);
        $this->assertSame('no-store', $page->headers['Cache-Control']);

        $other = new App($this->cfg(), $this->stubbed($calls), $store, fn () => null, fn () => ['name' => 'Other', 'email' => 'other@boldsign.dev']);
        $this->assertSame(404, $other->handle(new Request('GET', '/documents/doc-1/sign'))->status);

        $done = $app->handle(new Request('GET', '/signing/complete', [], '', ['documentId' => 'doc-1']));
        $this->assertStringContainsString('window.top.location.href', $done->body);
        $this->assertStringContainsString('<b>Queued</b>', $done->body);
    }

    public function testRetrySoonDoesNotRetry401(): void
    {
        $n = 0;
        $this->expectException(ApiException::class);
        try {
            BoldSignClient::retrySoon(function () use (&$n) {
                $n++;
                throw new ApiException('unauthorized', 401);
            }, 5, 0.001);
        } finally {
            $this->assertSame(1, $n);
        }
    }
}
