# BoldSign starter recipes: PHP

Three complete, tested starter apps, one per common workflow. Each is a small plain-PHP app (no framework) you can copy into a project or hand to a coding agent as a reference; the routes are in one `App` class, so they move easily into Laravel, Symfony or Slim controllers. Each one:
- pins the official SDK (`boldsign/boldsign-php` 3.3.4) with a `composer.lock` (`composer audit`: no advisories);
- confirms the API key and region with a read-only call;
- retries the brief "not ready yet" errors after a create;
- tracks status through a webhook verified as the developer docs describe;
- logs only an error summary, never the SDK's full exception;
- comes with offline tests (PHPUnit 11.5.57).

| Recipe | Workflow | Starts from |
| --- | --- | --- |
| [send-document](send-document/) | Send a document for signature; the signer gets BoldSign's email | A sample PDF with text tags, no template |
| [embedded-signing](embedded-signing/) | Signing inside your app in an iframe, with a redirect page; BoldSign emails off | A sample PDF with text tags, no template |
| [send-from-template](send-from-template/) | Send from a template, mapping each role to a signer from your app; checks the template's roles | A template you create in BoldSign |

## Tested

All three were run end to end in a BoldSign developer sandbox (US region) on 11 November 2026, using SDK 3.3.4 and PHP 8.3. Each run:
1. takes the recipe from the committed files, installs it from `composer.lock` and passes its tests (9, 9 and 11);
2. sends the document through the app's own `POST /documents` (send from a template also passes `composer check:boldsign`, including the role check);
3. signs it in Chromium (for embedded signing, inside the app's own iframe page);
4. sees Completed in BoldSign;
5. rejects a forged webhook with 401, accepts a correctly signed Completed webhook with 200, and shows the document as Completed on the app's status page.

| Recipe | Document | Tests | Signed | Completed | Forged webhook | Signed webhook | Status page |
| --- | --- | --- | --- | --- | --- | --- | --- |
| send-document | 49082d5d-0b79-4b1a-9011-5994f84ddf47 | 9/9 | Yes | Yes | 401 | 200 | Completed |
| embedded-signing | 2a08b601-c256-48fb-855d-7d57d78633c9 | 9/9 | Yes, inside the app's iframe | Yes | 401 | 200 | Completed |
| send-from-template | 4b3e9c2c-e486-4498-9f16-ad1209575e7d | 11/11 | Yes | Yes | 401 | 200 | Completed |

## Use with a coding agent

Point your agent at the recipe for your workflow, for example: "Follow `recipes/php/send-document` and the BoldSign skill to add BoldSign to this app." Keep the API key and webhook secret in the app's secret configuration, never in code or commits.
