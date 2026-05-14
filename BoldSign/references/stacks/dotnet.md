# BoldSign — .NET / C# Integration Reference

## SDK Install

```bash
dotnet add package BoldSign.Api
# or NuGet Package Manager:
Install-Package BoldSign.Api
```

NuGet: https://www.nuget.org/packages/BoldSign.Api  
Docs: https://developers.boldsign.com/sdks/dotnet-sdk/

---

## Setup & Authentication

```csharp
using BoldSign.Api;
using BoldSign.Model;

// API Key auth (US region - default)
var apiClient = new ApiClient("https://api.boldsign.com", "YOUR_API_KEY");

// EU region
var apiClient = new ApiClient("https://api-eu.boldsign.com", "YOUR_API_KEY");

// OAuth Bearer Token
var configuration = new Configuration();
configuration.SetBearerToken("your-oauth2-access-token");
var apiClient = new ApiClient(configuration);

var documentClient = new DocumentClient(apiClient);
var templateClient = new TemplateClient(apiClient);
```

---

## Send a Document for Signature

```csharp
using BoldSign.Api;
using BoldSign.Model;

public async Task<string> SendDocumentAsync()
{
    var apiClient = new ApiClient("https://api.boldsign.com", Environment.GetEnvironmentVariable("BOLDSIGN_API_KEY"));
    var documentClient = new DocumentClient(apiClient);

    // Load file
    using var fileStream = File.OpenRead("contract.pdf");
    var documentFile = new DocumentFileStream
    {
        ContentType = "application/pdf",
        FileData = fileStream,
        FileName = "contract.pdf"
    };

    // Define signature field
    var bounds = new Rectangle { X = 100, Y = 200, Width = 200, Height = 50 };
    var signatureField = new FormField
    {
        FieldType = FieldType.Signature,
        PageNumber = 1,
        Bounds = bounds,
        IsRequired = true
    };

    // Define signer
    var signer = new DocumentSigner(
        name: "John Doe",
        emailAddress: "john@example.com",
        signerType: SignerType.Signer,
        formFields: new List<FormField> { signatureField }
    );

    // Build send request
    var sendRequest = new SendForSign
    {
        Title = "Service Agreement",
        Message = "Please sign this agreement.",
        Signers = new List<DocumentSigner> { signer },
        Files = new List<IDocumentFile> { documentFile }
        // ExpiryDays = 30,
        // EnableSigningOrder = true
    };

    var result = documentClient.SendDocument(sendRequest);
    Console.WriteLine($"Document ID: {result.DocumentId}");
    // NOTE: Async — listen for webhooks to confirm Sent status
    return result.DocumentId;
}
```

---

## Send from Template

```csharp
public async Task<string> SendFromTemplateAsync(string templateId)
{
    var role = new Roles
    {
        RoleIndex = 1,
        SignerName = "Jane Smith",
        SignerEmail = "jane@example.com"
    };

    var sendRequest = new SendForSignFromTemplate
    {
        TemplateId = templateId,
        Title = "Contract for Jane",
        Roles = new List<Roles> { role }
    };

    var result = templateClient.SendUsingTemplate(sendRequest);
    return result.DocumentId;
}
```

---

## Embedded Signing Link

```csharp
public string GetEmbeddedSignLink(string documentId, string signerEmail)
{
    var signLinkExpiry = DateTime.UtcNow.AddHours(24);

    var result = documentClient.GetEmbeddedSignLink(
        documentId: documentId,
        signerEmail: signerEmail,
        signLinkValidTill: signLinkExpiry,
        redirectUrl: "https://yourapp.com/signing-complete"
    );

    return result.SignLink; // embed in <iframe>
}
```

---

## Webhook Handler (ASP.NET Core)

```csharp
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("webhooks")]
public class BoldSignWebhookController : ControllerBase
{
    [HttpPost("boldsign")]
    public async Task<IActionResult> HandleWebhook()
    {
        // 1. Read raw body
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        // 2. Verify HMAC signature
        var signature = Request.Headers["X-BoldSign-Signature"].ToString();
        var secret = Environment.GetEnvironmentVariable("BOLDSIGN_WEBHOOK_SECRET");
        var expectedSig = ComputeHmac(body, secret);

        if (signature != expectedSig)
            return Unauthorized();

        // 3. Parse and handle
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var eventType = payload.GetProperty("event").GetProperty("eventType").GetString();
        var documentId = payload.GetProperty("document").GetProperty("documentId").GetString();

        switch (eventType)
        {
            case "Completed":
                await HandleCompleted(documentId);
                break;
            case "SendFailed":
                Console.WriteLine($"Send failed for {documentId}");
                break;
            case "Declined":
                Console.WriteLine($"Declined: {documentId}");
                break;
        }

        return Ok();
    }

    private string ComputeHmac(string message, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToHexString(hash).ToLower();
    }

    private async Task HandleCompleted(string documentId)
    {
        var apiClient = new ApiClient("https://api.boldsign.com",
            Environment.GetEnvironmentVariable("BOLDSIGN_API_KEY"));
        var documentClient = new DocumentClient(apiClient);

        // Download signed PDF
        var pdf = documentClient.DownloadDocument(documentId);
        await System.IO.File.WriteAllBytesAsync($"signed/{documentId}.pdf", pdf);

        // Download audit trail
        var audit = documentClient.DownloadAuditLog(documentId);
        await System.IO.File.WriteAllBytesAsync($"signed/{documentId}-audit.pdf", audit);
    }
}
```

---

## Get Document Status

```csharp
public DocumentProperties GetStatus(string documentId)
{
    var details = documentClient.GetDocumentProperties(documentId);
    Console.WriteLine($"Status: {details.Status}"); // InProgress, Completed, Declined
    return details;
}
```

---

## appsettings.json / Environment

```json
{
  "BoldSign": {
    "ApiKey": "your_api_key_here",
    "WebhookSecret": "your_webhook_secret_here",
    "BaseUrl": "https://api.boldsign.com"
  }
}
```

---

## Sender Identity — Register and Send on Behalf of Tenant

```csharp
using BoldSign.Api;
using BoldSign.Model;

public class SenderIdentityService
{
    private readonly SenderIdentityClient _senderClient;

    public SenderIdentityService(ApiClient apiClient)
    {
        _senderClient = new SenderIdentityClient(apiClient);
    }

    public void OnboardTenant(string name, string email)
    {
        // Step 1: Register the identity
        _senderClient.CreateSenderIdentity(new CreateSenderIdentityRequest
        {
            Name = name,
            Email = email
        });

        // Step 2: Send approval email to tenant
        _senderClient.RequestForIdentityApproval(new ResendSenderIdentityRequest
        {
            Email = email
        });
        // Tenant clicks Approve → SenderIdentityApproved webhook fires
    }

    public DocumentCreated SendOnBehalfOf(string tenantEmail, SendForSign baseRequest)
    {
        baseRequest.OnBehalfOf = tenantEmail; // ← key field
        var documentClient = new DocumentClient(/* apiClient */);
        return documentClient.SendDocument(baseRequest);
    }
}
```

---

## Rate Limit Handling

```csharp
public async Task<T> CallWithRetryAsync<T>(Func<Task<T>> fn, int maxRetries = 1)
{
    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await fn();
        }
        catch (ApiException ex) when (ex.ErrorCode == 429)
        {
            if (attempt == maxRetries) throw;
            // Parse Retry-After header if available
            await Task.Delay(TimeSpan.FromSeconds(60));
        }
    }
    throw new Exception("Max retries exceeded");
}
```

**Sandbox: 50 req/hour. Production: 2,000 req/hour (account-level).**  
Use webhooks instead of polling to stay well within limits.

---

## SDK Links
- NuGet: `dotnet add package BoldSign.Api`
- GitHub: https://github.com/boldsign/boldsign-csharp-sdk
- Full docs: https://developers.boldsign.com/sdks/dotnet-sdk/
