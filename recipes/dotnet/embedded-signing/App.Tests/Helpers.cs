using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Recipe.Tests;

public static class Helpers
{
    public const string Secret = "test-webhook-secret";

    public static Config Cfg() => new() { ApiKey = "test", WebhookSecret = Secret, SignerName = "Test Signer", SignerEmail = "signer@boldsign.dev" };

    public static Store NewStore() => new(Path.Combine(Path.GetTempPath(), "boldsign-recipe-test-" + Guid.NewGuid().ToString("N"), "documents.json"));

    public static string Event(string documentId, string type, string? id = null, long? created = null) => JsonSerializer.Serialize(new
    {
        @event = new { id = id ?? Guid.NewGuid().ToString(), created = created ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), eventType = type, environment = "Test" },
        data = new { @object = "document", documentId, status = type },
    });

    public static string Sig(string body, string secret, long t) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{body}"))).ToLowerInvariant();

    public static HttpRequestMessage Signed(string body, string secret = Secret, long? t = null)
    {
        var ts = t ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var msg = new HttpRequestMessage(HttpMethod.Post, "/webhooks/boldsign") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        msg.Headers.Add("X-BoldSign-Event", "Completed");
        msg.Headers.Add("X-BoldSign-Signature", $"t={ts}, s0={Sig(body, secret, ts)}");
        return msg;
    }

    /// <summary>Starts the app on an in-memory test server.</summary>
    public static async Task<(WebApplication App, HttpClient Client)> Start(Config cfg, BoldSignService boldsign, Store store, Func<Microsoft.AspNetCore.Http.HttpContext, Signer>? currentUser = null)
    {
        var app = Routes.Build(cfg, boldsign, store, b => b.WebHost.UseTestServer(), currentUser);
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (app, client);
    }
}
