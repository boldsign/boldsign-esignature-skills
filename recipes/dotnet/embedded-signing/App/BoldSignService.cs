using BoldSign.Api;
using BoldSign.Model;

namespace Recipe;

/// <summary>The official BoldSign .NET SDK (BoldSign.Api 7.3.4), wrapped for this app.</summary>
public class BoldSignService(Config cfg)
{
    private readonly ApiClient _client = new(cfg.BaseUrl, cfg.ApiKey);

    public static int? StatusOf(Exception e) => e is ApiException a ? a.ErrorCode : null;

    /// <summary>SDK exceptions can carry request details; log only this summary, never the full object.</summary>
    public static Dictionary<string, object?> ErrorSummary(Exception e)
    {
        var text = e is ApiException a ? a.ErrorContent?.ToString() ?? a.Message : e.Message;
        return new() { ["status"] = StatusOf(e), ["error"] = text.Length > 300 ? text[..300] : text };
    }

    /// <summary>
    /// A new document is processed asynchronously: for a few seconds the next call on it can return 403
    /// "Invalid Document ID" (or 400). Retry those briefly; anything else is a real error.
    /// </summary>
    public static T RetrySoon<T>(Func<T> fn, int tries = 5, double waitSeconds = 2.0)
    {
        for (var i = 1; ; i++)
        {
            try { return fn(); }
            catch (ApiException e) when (i < tries && e.ErrorCode is 400 or 403)
            {
                Thread.Sleep(TimeSpan.FromSeconds(waitSeconds * i));
            }
        }
    }

    // The SDK calls, one per method, so tests can replace them.
    protected virtual DocumentCreated SdkSend(SendForSign request) => new DocumentClient(_client).SendDocument(request);
    protected virtual EmbeddedSigningLink SdkSignLink(string documentId, string signerEmail, string redirectUrl) =>
        new DocumentClient(_client).GetEmbeddedSignLink(documentId, signerEmail, null, redirectUrl);
    protected virtual DocumentProperties SdkProperties(string documentId) => new DocumentClient(_client).GetProperties(documentId);

    /// <summary>One read-only call: 200 means the key and the region's base URL match.</summary>
    public virtual void CheckConfig() => new BrandingClient(_client).ListBrand();

    /// <summary>
    /// Creates the document for the signed-in user. Fields come from the text tags in the PDF (UseTextTags), so no
    /// template and no coordinates. DisableEmails: signing happens only inside your app, through the embedded link.
    /// Processing completes asynchronously (webhook: Sent or SendFailed).
    /// </summary>
    public virtual string CreateDocument(string title, byte[] pdf, string signerName, string signerEmail)
    {
        var request = new SendForSign
        {
            Title = title,
            Signers = [new DocumentSigner(signerName: signerName, signerType: SignerType.Signer, signerEmail: signerEmail)],
            Files = [new DocumentFileBytes { ContentType = "application/pdf", FileData = pdf, FileName = "document.pdf" }],
            UseTextTags = true,
            DisableEmails = true,
        };
        return SdkSend(request).DocumentId;
    }

    /// <summary>
    /// A fresh embedded signing link to load in an iframe. Links expire, so get one per visit. Right after the document
    /// is created this can return 403 for a few seconds: RetrySoon handles it.
    /// </summary>
    public virtual string SignLink(string documentId, string signerEmail, string redirectUrl) =>
        RetrySoon(() => SdkSignLink(documentId, signerEmail, redirectUrl)).SignLink;

    /// <summary>Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour).</summary>
    public virtual string Status(string documentId) => RetrySoon(() => SdkProperties(documentId)).Status.ToString();
}
