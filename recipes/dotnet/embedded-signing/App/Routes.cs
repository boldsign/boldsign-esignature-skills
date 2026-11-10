using System.Net;
using System.Text.Json;

namespace Recipe;

/// <summary>
/// Routes: create a document for the signed-in user, sign it inside the app in an iframe, a redirect page for when
/// signing ends, and a verified BoldSign webhook.
/// </summary>
public static class Routes
{
    private const string Style = "<style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}iframe{width:100%;height:80vh;border:1px solid #ccc}</style>";
    private const string DefaultCsp = "default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static bool WantsHtml(HttpRequest r) => r.Headers.Accept.ToString() is var a && a.Contains("text/html") && !a.StartsWith("application/json");

    private static IResult Page(HttpContext ctx, string title, string body, string csp = DefaultCsp)
    {
        ctx.Response.Headers.ContentSecurityPolicy = csp;
        return Results.Content($"<!doctype html><meta charset='utf-8'><title>{E(title)}</title>{Style}{body}", "text/html; charset=utf-8");
    }

    /// <param name="currentUser">
    /// Returns the signed-in user. This recipe uses one demo user from SIGNER_NAME and SIGNER_EMAIL: replace it with your
    /// application's authentication. Signing links go to whoever it returns.
    /// </param>
    public static WebApplication Build(Config cfg, BoldSignService boldsign, Store store, Action<WebApplicationBuilder>? configure = null, Func<HttpContext, Signer>? currentUser = null)
    {
        currentUser ??= _ => new Signer { Name = cfg.SignerName, Email = cfg.SignerEmail };
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning).AddFilter("Recipe", LogLevel.Information);
        configure?.Invoke(builder);
        var app = builder.Build();
        var log = app.Logger;
        DocumentRecord? Owned(HttpContext ctx, string id) => store.Get(id) is { } d && d.Signer.Email == currentUser(ctx).Email ? d : null;

        app.MapPost("/webhooks/boldsign", async (HttpRequest req) =>
        {
            if (req.Headers["X-BoldSign-Event"] == "Verification") return Results.Ok(); // BoldSign's URL check
            if (cfg.WebhookSecret == "") return Results.Text("Webhook secret not configured", statusCode: 500);
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms); // raw bytes, exactly as sent
            var raw = ms.ToArray();
            if (!Webhook.Verify(raw, req.Headers["X-BoldSign-Signature"], cfg.WebhookSecret))
            {
                log.LogWarning("webhook: signature check failed");
                return Results.Text("Invalid signature", statusCode: 401);
            }
            JsonElement payload;
            try { payload = JsonDocument.Parse(raw).RootElement; } catch (JsonException) { return Results.Text("Invalid JSON", statusCode: 400); }
            log.LogInformation("webhook: {Result}", Webhook.Apply(store, payload));
            return Results.Ok(); // also for unknown documents, so BoldSign does not retry them
        });

        app.MapPost("/documents", async (HttpContext ctx) =>
        {
            var user = currentUser(ctx);
            if (user.Email == "") return Results.Json(new { error = "No signed-in user (set SIGNER_EMAIL)" }, statusCode: 400);
            var input = await Input(ctx.Request);
            var title = input.GetValueOrDefault("title") is { Length: > 0 } t ? t : "Policy acknowledgement";
            string documentId;
            try { documentId = boldsign.CreateDocument(title, SamplePdf.Build(title, user.Name), user.Name, user.Email); }
            catch (Exception e) // report the BoldSign error, never the request
            {
                var s = BoldSignService.ErrorSummary(e);
                log.LogError("create failed {Status} {Error}", s["status"], s["error"]);
                return Results.Json(new Dictionary<string, object?>(s) { ["message"] = "BoldSign create failed" }, statusCode: 502);
            }
            store.Save(new DocumentRecord { DocumentId = documentId, Title = title, Signer = user });
            var signingPage = $"/documents/{Uri.EscapeDataString(documentId)}/sign";
            return WantsHtml(ctx.Request) ? Results.Redirect(signingPage) : Results.Json(new { documentId, status = "Queued", signingPage }, statusCode: 201);
        });

        app.MapGet("/documents/{id}/sign", (HttpContext ctx, string id) =>
        {
            var d = Owned(ctx, id);
            if (d is null) return Results.NotFound();
            var complete = $"/signing/complete?documentId={Uri.EscapeDataString(id)}";
            if (Webhook.Final.Contains(d.Status)) return Results.Redirect(complete);
            string link;
            try { link = boldsign.SignLink(id, d.Signer.Email, cfg.AppBaseUrl + complete); }
            catch (Exception e) { return Results.Json(new Dictionary<string, object?>(BoldSignService.ErrorSummary(e)) { ["message"] = "Could not get a signing link" }, statusCode: 502); }
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Page(ctx, $"Sign: {d.Title}", $"<h1>{E(d.Title)}</h1><p><a href='/documents'>Back to documents</a></p><iframe src=\"{E(link)}\" title='BoldSign signing' allow='camera'></iframe>",
                "default-src 'self'; style-src 'unsafe-inline'; frame-src https://app.boldsign.com https://*.boldsign.com");
        });

        // Where BoldSign sends the signer when signing ends (redirect URL). Breaks out of the iframe and shows the status.
        app.MapGet("/signing/complete", (HttpContext ctx, string? documentId) =>
        {
            var d = documentId is null ? null : store.Get(documentId);
            var info = d is null ? "Signing finished." : $"Document <code>{E(d.DocumentId)}</code>: status <b>{E(d.Status)}</b>. It changes to Completed when BoldSign's webhook arrives.";
            return Page(ctx, "Signing finished", "<script>if (window.top !== window.self) window.top.location.href = window.location.href;</script>"
                + $"<h1>Thank you</h1><p>{info}</p><p><a href='/documents'>Back to documents</a></p>");
        });

        app.MapGet("/documents", (HttpContext ctx) =>
        {
            var me = currentUser(ctx);
            var rows = string.Concat(store.All().Where(d => d.Signer.Email == me.Email).Select(d =>
                $"<tr><td>{E(d.Title)}</td><td><b>{E(d.Status)}</b>{(d.Error is null ? "" : $" ({E(d.Error)})")}</td><td><code>{E(d.DocumentId)}</code></td><td>"
                + (Webhook.Final.Contains(d.Status) ? "" : $"<a href='/documents/{Uri.EscapeDataString(d.DocumentId)}/sign'>Open signing page</a>") + "</td></tr>"));
            return Page(ctx, "My documents", $"<h1>My documents</h1><p>Signed in as {E(me.Name)} ({E(me.Email)})</p>"
                + "<form method='post' action='/documents'><button>Create a document and sign it here</button></form>"
                + $"<table><tr><th>Title</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>{(rows == "" ? "<tr><td colspan=4>No documents yet</td></tr>" : rows)}</table>");
        });

        app.MapGet("/documents.json", (HttpContext ctx) => Results.Json(store.All().Where(d => d.Signer.Email == currentUser(ctx).Email)));
        app.MapGet("/", () => Results.Redirect("/documents"));
        return app;
    }

    /// <summary>JSON body or form fields.</summary>
    private static async Task<Dictionary<string, string>> Input(HttpRequest req)
    {
        if (req.HasFormContentType) return (await req.ReadFormAsync()).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        try
        {
            var doc = await JsonDocument.ParseAsync(req.Body);
            return doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!);
        }
        catch (JsonException) { return new(); }
    }
}
