<?php
declare(strict_types=1);

namespace Tests;

use App\App;
use App\BoldSignClient;
use App\Config;
use App\Request;
use App\Roles;
use BoldSign\Api\TemplateApi;
use BoldSign\ApiException;
use BoldSign\Model\DocumentCreated;
use BoldSign\Model\Role;

final class TemplateTest extends TestCase
{
    public function testParseRolesReadsNameIndexPairs(): void
    {
        $this->assertSame([['name' => 'Employee', 'index' => 1], ['name' => 'Manager', 'index' => 2]], Config::parseRoles('Employee:1, Manager:2'));
        $this->expectException(\InvalidArgumentException::class);
        Config::parseRoles('Employee');
    }

    public function testMapRolesMapsEachTemplateRoleToASigner(): void
    {
        $this->assertSame([['role_index' => 1, 'signer_role' => 'Employee', 'signer_name' => 'Ann', 'signer_email' => 'ann@boldsign.dev']],
            Roles::map([['name' => 'Employee', 'index' => 1]], ['name' => 'Ann', 'email' => 'ann@boldsign.dev']));
        $this->expectExceptionMessageMatches('/Employee/');
        Roles::map([['name' => 'Employee', 'index' => 1]], ['name' => 'No email']);
    }

    public function testCompareRolesFindsMismatches(): void
    {
        $this->assertSame(['missing_in_template' => [], 'not_configured' => []], Roles::compare([['name' => 'Employee', 'index' => 1]], [['name' => 'employee', 'index' => 1]]));
        $d = Roles::compare([['name' => 'Employee', 'index' => 1]], [['name' => 'Manager', 'index' => 1]]);
        $this->assertCount(1, $d['missing_in_template']);
        $this->assertCount(1, $d['not_configured']);
    }

    public function testSendFromTemplateSendsRoleObjectsAndRetriesTheFirst400(): void
    {
        $calls = new \ArrayObject();
        $client = new class ($this->cfg(), $calls) extends BoldSignClient {
            public function __construct(Config $cfg, private \ArrayObject $calls)
            {
                parent::__construct($cfg);
            }

            protected function templates(): TemplateApi
            {
                $calls = $this->calls;
                return new class ($calls) extends TemplateApi {
                    public function __construct(private \ArrayObject $calls)
                    {
                        parent::__construct();
                    }

                    public function sendUsingTemplate($template_id, $send_for_sign_from_template_form = null, string $contentType = self::contentTypes['sendUsingTemplate'][0])
                    {
                        $this->calls[] = [$template_id, $send_for_sign_from_template_form];
                        if (count($this->calls) < 2) {
                            throw new ApiException('Invalid template id.', 400);
                        }
                        return (new DocumentCreated())->setDocumentId('doc-9');
                    }
                };
            }
        };
        $roles = Roles::map([['name' => 'Employee', 'index' => 1]], ['name' => 'Ann', 'email' => 'ann@boldsign.dev']);
        $this->assertSame('doc-9', $client->sendFromTemplate('tpl-1', 'Offer', $roles));
        $this->assertCount(2, $calls);
        $this->assertSame('tpl-1', $calls[1][0]);
        $role = $calls[1][1]->getRoles()[0];
        $this->assertInstanceOf(Role::class, $role);
        $this->assertSame([1, 'Employee', 'ann@boldsign.dev'], [$role->getRoleIndex(), $role->getSignerRole(), $role->getSignerEmail()]);
    }

    public function testPostDocumentsSendsFromTheTemplateAndStoresQueued(): void
    {
        $store = $this->store();
        $fake = new class ($this->cfg()) extends BoldSignClient {
            public array $calls = [];

            public function sendFromTemplate(string $templateId, string $title, array $roles): string
            {
                $this->calls[] = [$templateId, $title, $roles];
                return 'doc-5';
            }
        };
        $res = (new App($this->cfg(), $fake, $store, fn () => null))->handle(new Request('POST', '/documents', ['accept' => 'application/json', 'content-type' => 'application/json'], '{}'));
        $this->assertSame(201, $res->status);
        $this->assertSame('tpl-1', $fake->calls[0][0]);
        $this->assertSame('signer@boldsign.dev', $fake->calls[0][2][0]['signer_email']);
        $this->assertSame('Queued', $store->get('doc-5')['status']);
    }

    public function testPostDocumentsNeedsATemplateId(): void
    {
        $cfg = $this->cfg();
        $cfg->templateId = '';
        $this->assertSame(400, (new App($cfg, null, $this->store(), fn () => null))->handle(new Request('POST', '/documents'))->status);
    }

    public function testRetrySoonDoesNotRetryA401(): void
    {
        $n = 0;
        try {
            BoldSignClient::retrySoon(function () use (&$n) {
                $n++;
                throw new ApiException('unauthorized', 401);
            }, 5, 0.001);
            $this->fail('expected an exception');
        } catch (ApiException) {
        }
        $this->assertSame(1, $n);
    }
}
