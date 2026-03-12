# BoldSign Workflow — Multi-Signer & Sequential Signing

## Use Case
Documents that require multiple parties to sign, either in a defined order (sequential)
or all at once in parallel. Common for NDAs, legal agreements, purchase orders,
board resolutions, and partnership contracts.

---

## Parallel Signing (All signers notified at once)

```typescript
// All signers receive the document simultaneously
const signer1 = new DocumentSigner();
signer1.name = 'Party A';
signer1.emailAddress = 'partya@company.com';
signer1.signerType = DocumentSigner.SignerTypeEnum.Signer;
signer1.signerOrder = 1; // order still set, but not enforced
signer1.formFields = [signatureFieldPage1];

const signer2 = new DocumentSigner();
signer2.name = 'Party B';
signer2.emailAddress = 'partyb@company.com';
signer2.signerType = DocumentSigner.SignerTypeEnum.Signer;
signer2.signerOrder = 2;
signer2.formFields = [signatureFieldPage2];

const sendRequest = new SendForSign();
sendRequest.title = 'Partnership NDA';
sendRequest.signers = [signer1, signer2];
sendRequest.enableSigningOrder = false; // PARALLEL — all notified at once
sendRequest.files = [fs.createReadStream('nda.pdf')];
```

---

## Sequential Signing (Each signer notified in order)

```typescript
// Signer 2 is only notified after Signer 1 completes

const signer1 = new DocumentSigner();
signer1.name = 'Employee';
signer1.emailAddress = 'employee@company.com';
signer1.signerOrder = 1; // signs first
signer1.signerType = DocumentSigner.SignerTypeEnum.Signer;
signer1.formFields = [employeeSignatureField];

const signer2 = new DocumentSigner();
signer2.name = 'HR Manager';
signer2.emailAddress = 'hr@company.com';
signer2.signerOrder = 2; // signs after employee
signer2.signerType = DocumentSigner.SignerTypeEnum.Signer;
signer2.formFields = [hrSignatureField];

const sendRequest = new SendForSign();
sendRequest.title = 'Employment Contract';
sendRequest.signers = [signer1, signer2];
sendRequest.enableSigningOrder = true; // SEQUENTIAL
sendRequest.files = [fs.createReadStream('contract.pdf')];
```

---

## Viewer Role (CC recipient — no signing required)

```typescript
const viewer = new DocumentSigner();
viewer.name = 'Legal Team';
viewer.emailAddress = 'legal@company.com';
viewer.signerType = DocumentSigner.SignerTypeEnum.Viewer; // receives copy, no action needed
viewer.signerOrder = 3; // notified after all signers complete
```

---

## In-Person Signing

```typescript
const inPersonSigner = new DocumentSigner();
inPersonSigner.name = 'Client';
inPersonSigner.emailAddress = 'client@example.com';
inPersonSigner.signerType = DocumentSigner.SignerTypeEnum.InPersonSigner;
// You manage the signing session — generate a sign link and hand the device to the client
```

---

## Tracking Individual Signer Status

```typescript
async function getSignerStatuses(documentId: string) {
  const details = await documentApi.getDocumentProperties(documentId);

  for (const signer of details.signerDetails) {
    console.log({
      name: signer.signerName,
      email: signer.signerEmail,
      status: signer.status, // NotCompleted, Completed, Declined
      isViewed: signer.isViewed,
      isAuthFailed: signer.isAuthenticationFailed,
    });
  }
}
```

---

## Webhook Events for Multi-Signer

Multi-signer documents fire one `Signed` event per signer:

```typescript
switch (eventType) {
  case 'Signed':
    // One signer completed — check who
    const signerDetails = payload.document.signerDetails;
    const justSigned = signerDetails.find(s => s.status === 'Completed');
    console.log(`${justSigned.signerName} has signed`);
    // If sequential, next signer will be automatically notified by BoldSign
    break;

  case 'Completed':
    // ALL signers have signed — document is fully executed
    await downloadAndStoreSignedDoc(payload.document.documentId);
    break;

  case 'Declined':
    // One signer declined — whole document halts
    const declined = payload.document.signerDetails.find(s => s.declineMessage);
    console.log(`Declined by ${declined.signerName}: ${declined.declineMessage}`);
    break;
}
```

---

## Sending Reminders to Specific Signers

```typescript
async function remindSigner(documentId: string, signerEmail: string) {
  await documentApi.remindDocument({
    documentId,
    receiverEmails: [signerEmail]
  });
}
```

---

## Bulk Send (Same Document to Many Individuals)

When you need to send the same document to 100s of recipients individually:

```typescript
// Each recipient gets their own unique copy
const bulkRequest = {
  templateId: TEMPLATE_ID,
  title: 'Annual Compliance Agreement',
  files: [fs.createReadStream('agreement.pdf')],
  signerList: [
    { name: 'Alice', email: 'alice@company.com', roleIndex: 1 },
    { name: 'Bob', email: 'bob@company.com', roleIndex: 1 },
    // ...up to your plan limit
  ]
};
// Use: POST /v1/template/bulkSend
// See: https://developers.boldsign.com/template/bulk-send/
```

---

## Key Tips for Multi-Signer Workflows

- **Sequential is more reliable** for legal/regulated docs — ensures proper signing order
- **Always handle Declined events** — one declination halts the entire document
- **Use reminders** — set `reminderDays` and `reminderCount` on the send request
- **Set expiry** — long-running multi-signer docs may need 30–60 day expiry windows
- **Use private messages** — each signer can receive a unique private message explaining their role
