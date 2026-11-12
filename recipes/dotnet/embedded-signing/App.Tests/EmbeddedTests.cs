using System.Net.Http.Json;
using System.Text.Json;
using BoldSign.Api;
using BoldSign.Model;
using static Recipe.Tests.Helpers;

namespace Recipe.Tests;

public class EmbeddedTests
{
    /// <summary>Replaces the SDK calls and records them; the first <c>failLinks</c> signing links answer 403.</summary>
    private sealed class StubService(Config cfg, int failLinks = 0) : BoldSignService(cfg)
    {
        public SendForSign? Sent;
        public int Links;
        public string? Redirect;
        protected override DocumentCreated SdkSend(SendForSign request) { Sent = request; return new DocumentCreated { DocumentId = "doc-1" }; }
        protected override EmbeddedSigningLink SdkSignLink(string documentId, string signerEmail, string redirectUrl)
        {
            Links++;
            Redirect = redirectUrl;
            if (failLinks-- > 0) throw new ApiException(403, "Invalid Document ID");
            return new EmbeddedSigningLink { SignLink = "https://app.boldsign.com/document/sign/?documentId=x" };
        }
    }

    [Fact]
    public void SamplePdfHasTheTextTags() => Assert.Contains(SamplePdf.SignTag, System.Text.Encoding.Latin1.GetString(SamplePdf.Build()));

    [Fact]
    public void CreateDocumentUsesTextTagsAndDisablesEmails()
    {
        var svc = new StubService(Cfg());
        Assert.Equal("doc-1", svc.CreateDocument("T", SamplePdf.Build(), "N", "n@boldsign.dev"));
        Assert.True(svc.Sent!.UseTextTags);
        Assert.True(svc.Sent.DisableEmails);
        var signer = Assert.Single(svc.Sent.Signers);
        Assert.True(signer.FormFields is null || signer.FormFields.Count == 0);
    }

    [Fact]
    public void SignLinkRedirectsBackAndRetriesTheFirst403()
    {
        var svc = new StubService(Cfg(), failLinks: 1);
        var link = svc.SignLink("doc-1", "n@boldsign.dev", "http://localhost:3000/signing/complete?documentId=doc-1");
        Assert.StartsWith("https://app.boldsign.com/", link);
        Assert.Equal(2, svc.Links);
        Assert.Equal("http://localhost:3000/signing/complete?documentId=doc-1", svc.Redirect);
    }

    [Fact]
    public async Task CreateSigningIframeOwnerOnlyAndRedirectPage()
    {
        var store = NewStore();
        var (app, client) = await Start(Cfg(), new StubService(Cfg()), store);
        await using var _ = app;
        var res = await client.PostAsJsonAsync("/documents", new { });
        Assert.Equal(201, (int)res.StatusCode);
        Assert.Equal("/documents/doc-1/sign", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("signingPage").GetString());

        var page = await client.GetAsync("/documents/doc-1/sign");
        Assert.Equal(200, (int)page.StatusCode);
        Assert.Contains("<iframe src=\"https://app.boldsign.com/", await page.Content.ReadAsStringAsync());
        Assert.Contains("frame-src https://app.boldsign.com", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", page.Headers.CacheControl!.ToString());

        var (other, otherClient) = await Start(Cfg(), new StubService(Cfg()), store, _ => new Signer { Name = "Other", Email = "other@boldsign.dev" });
        await using var __ = other;
        Assert.Equal(404, (int)(await otherClient.GetAsync("/documents/doc-1/sign")).StatusCode);

        var done = await client.GetStringAsync("/signing/complete?documentId=doc-1");
        Assert.Contains("window.top.location.href", done);
        Assert.Contains("<b>Queued</b>", done);
    }

    [Fact]
    public void RetrySoonDoesNotRetry401()
    {
        var n = 0;
        Assert.Throws<ApiException>(() => BoldSignService.RetrySoon<string>(() => { n++; throw new ApiException(401, "unauthorized"); }, 5, 0.001));
        Assert.Equal(1, n);
    }
}
