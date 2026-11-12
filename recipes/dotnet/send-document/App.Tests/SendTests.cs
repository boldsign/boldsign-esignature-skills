using System.Net.Http.Json;
using System.Text.Json;
using BoldSign.Api;
using BoldSign.Model;
using static Recipe.Tests.Helpers;

namespace Recipe.Tests;

public class SendTests
{
    /// <summary>Replaces the SDK send call and records the request.</summary>
    private sealed class CapturingService(Config cfg) : BoldSignService(cfg)
    {
        public SendForSign? Request;
        protected override DocumentCreated SdkSend(SendForSign request) { Request = request; return new DocumentCreated { DocumentId = "doc-42" }; }
    }

    private sealed class FakeService(Config cfg) : BoldSignService(cfg)
    {
        public readonly List<(string Title, string Name, string Email)> Calls = new();
        public override string SendDocument(string title, byte[] pdf, string signerName, string signerEmail) { Calls.Add((title, signerName, signerEmail)); return "doc-7"; }
    }

    [Fact]
    public void SamplePdfHasTheTextTags()
    {
        var pdf = System.Text.Encoding.Latin1.GetString(SamplePdf.Build());
        Assert.StartsWith("%PDF-", pdf);
        Assert.Contains(SamplePdf.SignTag, pdf);
        Assert.Contains(SamplePdf.DateTag, pdf);
        Assert.StartsWith("{{sign|1|*", SamplePdf.SignTag);
    }

    [Fact]
    public void SendDocumentBuildsATextTagRequest()
    {
        var svc = new CapturingService(Cfg());
        Assert.Equal("doc-42", svc.SendDocument("T", SamplePdf.Build(), "N", "n@boldsign.dev"));
        var req = svc.Request!;
        Assert.True(req.UseTextTags);
        var signer = Assert.Single(req.Signers);
        Assert.Equal("n@boldsign.dev", signer.EmailAddress);
        Assert.True(signer.FormFields is null || signer.FormFields.Count == 0);
        var file = Assert.IsType<DocumentFileBytes>(Assert.Single(req.Files));
        Assert.Equal("document.pdf", file.FileName);
        Assert.StartsWith("%PDF-", System.Text.Encoding.Latin1.GetString(file.FileData));
    }

    [Fact]
    public async Task PostDocumentsSendsAndStoresQueued()
    {
        var store = NewStore();
        var fake = new FakeService(Cfg());
        var (app, client) = await Start(Cfg(), fake, store);
        await using var _ = app;
        var res = await client.PostAsJsonAsync("/documents", new { });
        Assert.Equal(201, (int)res.StatusCode);
        Assert.Equal("doc-7", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("documentId").GetString());
        Assert.Equal("signer@boldsign.dev", fake.Calls[0].Email);
        Assert.Equal("Queued", store.Get("doc-7")!.Status);
    }

    [Fact]
    public void RetrySoonRetries400And403Only()
    {
        var n = 0;
        Assert.Equal("ok", BoldSignService.RetrySoon(() => ++n < 3 ? throw new ApiException(403, "not ready") : "ok", 5, 0.001));
        var m = 0;
        Assert.Throws<ApiException>(() => BoldSignService.RetrySoon<string>(() => { m++; throw new ApiException(401, "unauthorized"); }, 5, 0.001));
        Assert.Equal(1, m);
    }

    [Fact]
    public void ErrorSummaryKeepsOnlyStatusAndBody()
    {
        var s = BoldSignService.ErrorSummary(new ApiException(401, "Error calling SendDocument", "{\"error\":\"Unauthorized\"}"));
        Assert.Equal(401, s["status"]);
        Assert.Equal("{\"error\":\"Unauthorized\"}", s["error"]);
        Assert.Equal(2, s.Count);
    }
}
