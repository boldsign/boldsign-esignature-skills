# BoldSign eSignature Skills for Claude

A Claude skill for generating BoldSign eSignature integration guidance and code scaffolding for .NET, Node.js, Python, and PHP.

This repository packages the `BoldSign/` skill folder with the root `SKILL.md` entrypoint and stack- and workflow-specific reference files.

## What this repository includes

```text
BoldSign/
├── SKILL.md
└── references/
    ├── stacks/
    │   ├── dotnet.md
    │   ├── nodejs.md
    │   ├── php.md
    │   └── python.md
    └── workflows/
        ├── embedded-saas.md
        └── multi-signer.md
```

## What the skill helps with

- Sending documents for signature
- Embedded signing and embedded sending flows
- Webhook handling and HMAC verification
- Template-based workflows
- Multi-signer and sequential signing flows
- Multi-tenant SaaS patterns using Sender Identities
- Stack-specific guidance for .NET, Node.js, Python, and PHP

## Installation

1. Download or clone this repository.
2. Copy the `BoldSign/` folder into your Claude skills directory.
3. Start prompting Claude with BoldSign eSignature tasks.

Example install path used in our documentation:

```bash
cp -R BoldSign /mnt/skills/user/boldsign-esignature/
```

Adjust the target path if your Claude environment uses a different skills directory.

## Example prompts

- `Send a PDF for signature using BoldSign in .NET C#. Use API Key auth.`
- `Add a BoldSign webhook handler for ASP.NET Core.`
- `Generate an embedded signing link so signers can sign inside my app.`
- `Set up a 3-party NDA with sequential signing in C#.`
- `Show me the Node.js flow for sending a document from a template.`

## Repository structure

- `BoldSign/SKILL.md` contains the universal guidance shared across all integrations.
- `BoldSign/references/stacks/*.md` contains stack-specific instructions.
- `BoldSign/references/workflows/*.md` contains workflow-specific instructions.

## Notes

- Start in the BoldSign Sandbox environment before switching to Live.
- The generated output is intended as strong implementation scaffolding and should still be reviewed, wired into your application, and tested end to end.
- Webhook-based status handling is preferred over polling.

## Related resources

- BoldSign API docs: https://developers.boldsign.com
- BoldSign website: https://boldsign.com

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
