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
    /// A new document (or template) is processed asynchronously: for a few seconds the next call on it can return
    /// 403 "Invalid Document ID" or 400 "Invalid template id.". Retry those briefly; anything else is a real error.
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
    protected virtual DocumentCreated SdkSendFromTemplate(SendForSignFromTemplate request) => new TemplateClient(_client).SendUsingTemplate(request);
    protected virtual TemplateProperties SdkTemplateProperties(string templateId) => new TemplateClient(_client).GetProperties(templateId);
    protected virtual EmbeddedSigningLink SdkSignLink(string documentId, string signerEmail, string redirectUrl) =>
        new DocumentClient(_client).GetEmbeddedSignLink(documentId, signerEmail, null, redirectUrl);
    protected virtual DocumentProperties SdkProperties(string documentId) => new DocumentClient(_client).GetProperties(documentId);

    /// <summary>One read-only call: 200 means the key and the region's base URL match.</summary>
    public virtual void CheckConfig() => new BrandingClient(_client).ListBrand();

    /// <summary>The template's roles (read-only), to check the role configuration.</summary>
    public virtual List<TemplateRole> TemplateRoles(string templateId) =>
        (RetrySoon(() => SdkTemplateProperties(templateId)).Roles ?? []).Select(r => new TemplateRole(r.Name, r.Index)).ToList();

    /// <summary>
    /// Sends from the template, with one signer per role. BoldSign emails each signer. Returns the document ID; the send
    /// completes asynchronously, and the webhook reports Sent or SendFailed. A template created seconds ago can return
    /// 400 "Invalid template id." for a moment, so the first 400 is retried.
    /// </summary>
    public virtual string SendFromTemplate(string templateId, string title, List<RoleSigner> roles)
    {
        var request = new SendForSignFromTemplate
        {
            TemplateId = templateId,
            Title = title,
            Roles = roles.Select(r => new Roles { RoleIndex = r.RoleIndex, SignerRole = r.SignerRole, SignerName = r.SignerName, SignerEmail = r.SignerEmail }).ToList(),
        };
        return RetrySoon(() => SdkSendFromTemplate(request)).DocumentId;
    }

    /// <summary>A signing link for the signer, for an in-app "Sign now" button (the signer can also use the email).</summary>
    public virtual string SignLink(string documentId, string signerEmail, string redirectUrl) =>
        RetrySoon(() => SdkSignLink(documentId, signerEmail, redirectUrl)).SignLink;

    /// <summary>Fallback status read while no webhook can reach the app (do not poll in a loop: sandbox allows 50 requests an hour).</summary>
    public virtual string Status(string documentId) => RetrySoon(() => SdkProperties(documentId)).Status.ToString();
}
