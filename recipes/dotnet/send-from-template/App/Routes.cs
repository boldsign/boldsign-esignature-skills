using System.Net;
using System.Text.Json;

namespace Recipe;

/// <summary>Routes: send from a BoldSign template (each role mapped to a signer from the app), a status page, and a verified BoldSign webhook.</summary>
public static class Routes
{
    private const string Style = "<style>body{font-family:system-ui;margin:2rem}td,th{border:1px solid #ccc;padding:.4rem .7rem}table{border-collapse:collapse}</style>";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static bool WantsHtml(HttpRequest r) => r.Headers.Accept.ToString() is var a && a.Contains("text/html") && !a.StartsWith("application/json");

    public static WebApplication Build(Config cfg, BoldSignService boldsign, Store store, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning).AddFilter("Recipe", LogLevel.Information);
        configure?.Invoke(builder);
        var app = builder.Build();
        var log = app.Logger;

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
            var result = Webhook.Apply(store, payload);
            log.LogInformation("webhook: {Result}", result);
            return Results.Ok(); // also for unknown documents, so BoldSign does not retry them
        });

        app.MapPost("/documents", async (HttpRequest req) =>
        {
            if (cfg.TemplateId == "") return Results.Json(new { error = "Set BOLDSIGN_TEMPLATE_ID" }, statusCode: 400);
            var input = await Input(req);
            var user = new Signer
            {
                Name = input.GetValueOrDefault("signerName") is { Length: > 0 } n ? n : cfg.SignerName,
                Email = input.GetValueOrDefault("signerEmail") is { Length: > 0 } m ? m : cfg.SignerEmail,
            };
            List<RoleSigner> roles;
            try { roles = RoleMap.Map(cfg.TemplateRoles, user); }
            catch (ArgumentException e) { return Results.Json(new { error = e.Message }, statusCode: 400); }
            var title = input.GetValueOrDefault("title") is { Length: > 0 } t ? t : "Offer letter";
            string documentId;
            try { documentId = boldsign.SendFromTemplate(cfg.TemplateId, title, roles); }
            catch (Exception e) // report the BoldSign error, never the request
            {
                var s = BoldSignService.ErrorSummary(e);
                log.LogError("send from template failed {Status} {Error}", s["status"], s["error"]);
                return Results.Json(new Dictionary<string, object?>(s) { ["message"] = "BoldSign send from template failed" }, statusCode: 502);
            }
            var record = store.Save(new DocumentRecord { DocumentId = documentId, Title = title, Signer = new Signer { Name = roles[0].SignerName, Email = roles[0].SignerEmail }, Roles = roles });
            return WantsHtml(req) ? Results.Redirect("/documents") : Results.Json(record, statusCode: 201);
        });

        app.MapGet("/documents", () =>
        {
            var rows = string.Concat(store.All().Select(d =>
                $"<tr><td>{E(d.Title)}</td><td>{E(d.Signer.Email)}</td><td><b>{E(d.Status)}</b>{(d.Error is null ? "" : $" ({E(d.Error)})")}</td><td><code>{E(d.DocumentId)}</code></td><td>"
                + (Webhook.Final.Contains(d.Status) ? "" :
                    $"<a href='/documents/{Uri.EscapeDataString(d.DocumentId)}/sign'>Sign now</a> <form method='post' action='/documents/{Uri.EscapeDataString(d.DocumentId)}/refresh' style='display:inline'><button>Refresh</button></form>")
                + "</td></tr>"));
            return Results.Content("<!doctype html><meta charset='utf-8'><title>Documents</title>" + Style
                + $"<h1>Documents</h1><form method='post' action='/documents'><button>Send the template to {E(cfg.SignerEmail is { Length: > 0 } s ? s : "SIGNER_EMAIL")}</button></form>"
                + $"<table><tr><th>Title</th><th>Signer</th><th>Status</th><th>BoldSign document ID</th><th></th></tr>{(rows == "" ? "<tr><td colspan=5>No documents yet</td></tr>" : rows)}</table>", "text/html; charset=utf-8");
        });

        app.MapGet("/documents.json", () => Results.Json(store.All()));

        app.MapGet("/documents/{id}/sign", (string id) =>
        {
            var d = store.Get(id);
            if (d is null) return Results.NotFound();
            try { return Results.Redirect(boldsign.SignLink(id, d.Signer.Email, $"{cfg.AppBaseUrl}/documents")); }
            catch (Exception e) { return Results.Json(new Dictionary<string, object?>(BoldSignService.ErrorSummary(e)) { ["message"] = "Could not get a signing link" }, statusCode: 502); }
        });

        app.MapPost("/documents/{id}/refresh", (string id) =>
        {
            var d = store.Get(id);
            if (d is null) return Results.NotFound();
            string status;
            try { status = boldsign.Status(id); }
            catch (Exception e) { return Results.Json(new Dictionary<string, object?>(BoldSignService.ErrorSummary(e)) { ["message"] = "Could not read the status" }, statusCode: 502); }
            // InProgress only replaces Queued: later webhook statuses (Sent, Viewed, Signed) are more precise
            if (!(status == "InProgress" && d.Status != "Queued") && !Webhook.Final.Contains(d.Status))
            {
                d.Status = status;
                store.Save(d);
            }
            return Results.Redirect("/documents");
        });

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
