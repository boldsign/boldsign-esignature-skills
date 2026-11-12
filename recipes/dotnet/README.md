# BoldSign starter recipes: .NET

Three complete, tested starter apps, one per common workflow. Each is a small ASP.NET Core minimal API on .NET 8 you can copy into a project or hand to a coding agent as a reference. Each one:
- pins the official SDK (`BoldSign.Api` 7.3.4) with a `packages.lock.json` (`dotnet list package --vulnerable --include-transitive`: none);
- confirms the API key and region with a read-only call;
- retries the brief "not ready yet" errors after a create;
- tracks status through a webhook verified as the developer docs describe;
- logs only an error summary, never the SDK's full exception;
- comes with offline tests (xUnit, on an in-memory test server).

`BoldSign.Api` 7.3.4 brings in `System.Net.Http` 4.3.0 and `System.Text.RegularExpressions` 4.3.0, which have high-severity advisories. Each `App.csproj` references the patched versions (4.3.4 and 4.3.1) directly.

| Recipe | Workflow | Starts from |
| --- | --- | --- |
| [send-document](send-document/) | Send a document for signature; the signer gets BoldSign's email | A sample PDF with text tags, no template |
| [embedded-signing](embedded-signing/) | Signing inside your app in an iframe, with a redirect page; BoldSign emails off | A sample PDF with text tags, no template |
| [send-from-template](send-from-template/) | Send from a template, mapping each role to a signer from your app; checks the template's roles | A template you create in BoldSign |

## Tested

All three were run end to end in a BoldSign developer sandbox (US region) on 11 November 2026, using SDK 7.3.4 and the .NET 8 SDK (8.0.133). Each run:
1. takes the recipe from the committed files, restores it with `--locked-mode`, builds it with no warnings and passes its tests (9, 9 and 11);
2. sends the document through the app's own `POST /documents` (send from a template also passes `dotnet run --project App -- check`, including the role check);
3. signs it in Chromium (for embedded signing, inside the app's own iframe page);
4. sees Completed in BoldSign;
5. rejects a forged webhook with 401, accepts a correctly signed Completed webhook with 200, and shows the document as Completed on the app's status page.

| Recipe | Document | Tests | Signed | Completed | Forged webhook | Signed webhook | Status page |
| --- | --- | --- | --- | --- | --- | --- | --- |
| send-document | 43c0b8d5-0fb9-4c14-b3d7-8a512ed8677d | 9/9 | Yes | Yes | 401 | 200 | Completed |
| embedded-signing | 76f79e43-2656-4353-8ce0-898e7ab90233 | 9/9 | Yes, inside the app's iframe | Yes | 401 | 200 | Completed |
| send-from-template | 73a6e80f-723e-4ee5-98c1-851fc0d3608d | 11/11 | Yes | Yes | 401 | 200 | Completed |

## Use with a coding agent

Point your agent at the recipe for your workflow, for example: "Follow `recipes/dotnet/send-document` and the BoldSign skill to add BoldSign to this app." Keep the API key and webhook secret in the app's secret configuration (for example `dotnet user-secrets` or environment variables), never in code or commits.
