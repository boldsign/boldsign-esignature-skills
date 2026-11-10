using System.Text;
using System.Text.Json;
using static Recipe.Tests.Helpers;

namespace Recipe.Tests;

public class WebhookTests
{
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static readonly string Body = Event("doc-1", "Completed");
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void AcceptsS0AndRotatedS1()
    {
        Assert.True(Webhook.Verify(B(Body), $"t={Now}, s0={Sig(Body, Secret, Now)}", Secret));
        Assert.True(Webhook.Verify(B(Body), $"t={Now}, s0={Sig(Body, "old", Now)}, s1={Sig(Body, Secret, Now)}", Secret));
        Assert.True(Webhook.Verify(B(Body), $"t={Now}, s0={Sig(Body, Secret, Now).ToUpperInvariant()}", Secret));
    }

    [Fact]
    public void RejectsBadSignatures()
    {
        var bodyOnly = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(B(Secret), B(Body))).ToLowerInvariant();
        foreach (var header in new[] { $"t={Now}, s0={Sig(Body, "wrong", Now)}", $"t={Now}, s0={bodyOnly}", $"t={Now - 600}, s0={Sig(Body, Secret, Now - 600)}",
                     $"t={Now}, s0=abc", $"s0={Sig(Body, Secret, Now)}", "", "garbage" })
            Assert.False(Webhook.Verify(B(Body), header, Secret), header);
        Assert.False(Webhook.Verify(B(Body.Replace("doc-1", "doc-2")), $"t={Now}, s0={Sig(Body, Secret, Now)}", Secret));
    }

    [Fact]
    public void StatusRules()
    {
        var store = NewStore();
        store.Save(new DocumentRecord { DocumentId = "doc-1" });
        JsonElement J(string s) => JsonDocument.Parse(s).RootElement;
        var sent = J(Event("doc-1", "Sent", "e1", 100));
        Assert.Equal("applied", Webhook.Apply(store, sent));
        Assert.Equal("Sent", store.Get("doc-1")!.Status);
        Assert.Equal("duplicate", Webhook.Apply(store, sent));
        Assert.Equal("applied", Webhook.Apply(store, J(Event("doc-1", "Completed", "e2", 200))));
        Assert.Equal("stale", Webhook.Apply(store, J(Event("doc-1", "Viewed", "e3", 150))));
        Assert.Equal("applied", Webhook.Apply(store, J(Event("doc-1", "Revoked", "e4", 300))));
        Assert.Equal("Completed", store.Get("doc-1")!.Status);
        Assert.Equal("unknown-document", Webhook.Apply(store, J(Event("other", "Completed"))));
    }

    [Fact]
    public async Task WebhookRoute()
    {
        var store = NewStore();
        store.Save(new DocumentRecord { DocumentId = "doc-1", Title = "T", Signer = new Signer { Name = "S", Email = "signer@boldsign.dev" } });
        var (app, client) = await Start(Cfg(), new BoldSignService(Cfg()), store);
        await using var _ = app;
        var ping = new HttpRequestMessage(HttpMethod.Post, "/webhooks/boldsign") { Content = new StringContent("{}") };
        ping.Headers.Add("X-BoldSign-Event", "Verification");
        Assert.Equal(200, (int)(await client.SendAsync(ping)).StatusCode);
        var raw = Event("doc-1", "Completed");
        Assert.Equal(401, (int)(await client.SendAsync(Signed(raw, "not-the-secret"))).StatusCode);
        Assert.Equal("Queued", store.Get("doc-1")!.Status);
        Assert.Equal(200, (int)(await client.SendAsync(Signed(raw))).StatusCode);
        Assert.Equal("Completed", store.Get("doc-1")!.Status);
        Assert.Contains("<b>Completed</b>", await client.GetStringAsync("/documents"));
    }
}
