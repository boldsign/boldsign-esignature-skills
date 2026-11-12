# BoldSign starter recipes: Python and Flask

Three complete, tested starter apps, one per common workflow. Each is a small Flask app you can copy into a project or hand to a coding agent as a reference. Each one:
- pins the official SDK (`boldsign` 3.3.4) and Flask 3.1.3;
- confirms the API key and region with a read-only call;
- retries the brief "not ready yet" errors after a create;
- tracks status through a webhook verified as the developer docs describe;
- logs only an error summary, never the SDK's full error object;
- comes with offline tests (pytest).

| Recipe | Workflow | Starts from |
| --- | --- | --- |
| [send-document](send-document/) | Send a document for signature; the signer gets BoldSign's email | A sample PDF with text tags, no template |
| [embedded-signing](embedded-signing/) | Signing inside your app in an iframe, with a redirect page; BoldSign emails off | A sample PDF with text tags, no template |
| [send-from-template](send-from-template/) | Send from a template, mapping each role to a signer from your app; checks the template's roles | A template you create in BoldSign |

## Tested

All three were run end to end in a BoldSign developer sandbox (US region) on 11 November 2026, using SDK 3.3.4, Flask 3.1.3 and Python 3.13. Each run:
1. installs the recipe from the committed files and passes its tests (8, 9 and 11);
2. sends the document through the app's own `POST /documents` (send from a template also passes `scripts/check_boldsign.py`, including the role check);
3. signs it in Chromium (for embedded signing, inside the app's own iframe page);
4. sees Completed in BoldSign;
5. rejects a forged webhook with 401, accepts a correctly signed Completed webhook with 200, and shows the document as Completed on the app's status page.

| Recipe | Document | Tests | Signed | Completed | Forged webhook | Signed webhook | Status page |
| --- | --- | --- | --- | --- | --- | --- | --- |
| send-document | 5aa4fafd-5d74-4834-9e7f-3a9b6065b40f | 8/8 | Yes | Yes | 401 | 200 | Completed |
| embedded-signing | cc305193-a9a3-4463-bc9d-ef1daca677c3 | 9/9 | Yes, inside the app's iframe | Yes | 401 | 200 | Completed |
| send-from-template | a274158f-85a4-4b0b-ae52-72c6f03289c1 | 11/11 | Yes | Yes | 401 | 200 | Completed |

## Use with a coding agent

Point your agent at the recipe for your workflow, for example: "Follow `recipes/python-flask/send-document` and the BoldSign skill to add BoldSign to this app." Keep the API key and webhook secret in the app's secret configuration, never in code or commits.
