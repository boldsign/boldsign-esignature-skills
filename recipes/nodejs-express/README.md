# BoldSign starter recipes: Node.js and Express

Three complete, tested starter apps, one per common workflow. Each is a small Express app you can copy into a project or hand to a coding agent as a reference. Each one:
- pins the official SDK (`boldsign` 3.3.4);
- confirms the API key and region with a read-only call;
- retries the brief "not ready yet" errors after a create;
- tracks status through a webhook verified as the developer docs describe;
- comes with offline tests.

| Recipe | Workflow | Starts from |
| --- | --- | --- |
| [send-document](send-document/) | Send a document for signature; the signer gets BoldSign's email | A sample PDF with text tags, no template |
| [embedded-signing](embedded-signing/) | Signing inside your app in an iframe, with a redirect page; BoldSign emails off | A sample PDF with text tags, no template |
| [send-from-template](send-from-template/) | Send from a template, mapping each role to a signer from your app; checks the template's roles | A template you create in BoldSign |

## Tested

All three were run end to end in a BoldSign developer sandbox (US region) on 10 November 2026, using SDK 3.3.4, Express 4.22.3 and Node.js 22. Each run:
1. installs the recipe (`npm audit`: 0 vulnerabilities) and passes its tests (9, 10 and 11);
2. passes `npm run check:boldsign`;
3. sends the document and signs it in Chromium (for embedded signing, inside the app's own iframe page);
4. sees Completed in BoldSign;
5. rejects a forged webhook with 401, accepts a correctly signed Completed webhook with 200, and shows the document as Completed on the app's status page.

## Use with a coding agent

Point your agent at the recipe for your workflow, for example: "Follow `recipes/nodejs-express/send-document` and the BoldSign skill to add BoldSign to this app." Keep the API key and webhook secret in the app's secret configuration, never in code or commits.
