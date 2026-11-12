using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Recipe;

/// <summary>
/// BoldSign webhook verification and status rules.
/// X-BoldSign-Signature: t=&lt;unix seconds&gt;, s0=&lt;hex&gt;[, s1=&lt;hex&gt;]
/// Signature: hex(HMAC_SHA256(webhook secret, t + "." + raw body)); s1 is present while an old secret is still valid.
/// https://developers.boldsign.com/webhooks/verify-webhook-events/
/// </summary>
public static class Webhook
{
    public const int ToleranceSeconds = 300;
    public static readonly HashSet<string> Final = ["Completed", "Declined", "Expired", "Revoked", "SendFailed"];
    private static readonly HashSet<string> Statuses = ["Sent", "Viewed", "Signed", "Completed", "Declined", "Expired", "Revoked", "SendFailed", "DeliveryFailed"];

    public static bool Verify(byte[] rawBody, string? header, string secret, long? now = null)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret)) return false;
        var timestamp = "";
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length != 2) return false;
            var (key, value) = (kv[0].Trim(), kv[1].Trim().ToLowerInvariant());
            if (key == "t") timestamp = value;
            else if (Regex.IsMatch(key, @"^s\d+$")) signatures.Add(value);
        }
        if (!long.TryParse(timestamp, out var t) || timestamp.Any(c => !char.IsAsciiDigit(c)) || signatures.Count == 0) return false;
        if (Math.Abs((now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()) - t) > ToleranceSeconds) return false;
        var message = Encoding.UTF8.GetBytes(timestamp + ".").Concat(rawBody).ToArray();
        var expected = Encoding.ASCII.GetBytes(Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message)).ToLowerInvariant());
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), expected));
    }

    /// <summary>Applies a verified event to the stored document. Returns what happened, for logging and tests.</summary>
    public static string Apply(Store store, JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return "ignored";
        if (Str(data, "object") != "document") return "ignored";
        var record = store.Get(Str(data, "documentId") ?? "");
        if (record is null) return "unknown-document";
        payload.TryGetProperty("event", out var ev);
        var id = ev.ValueKind == JsonValueKind.Object ? Str(ev, "id") : null;
        if (id is not null && record.Events.Any(e => e.Id == id)) return "duplicate";
        long? created = ev.ValueKind == JsonValueKind.Object && ev.TryGetProperty("created", out var c) && c.TryGetInt64(out var cv) ? cv : null;
        if (record.LastEventCreated is { } last && created is { } cr && cr < last) return "stale";
        var type = ev.ValueKind == JsonValueKind.Object ? Str(ev, "eventType") ?? "" : "";
        record.Events.Add(new EventRecord { Id = id, Type = type, Created = created });
        record.LastEventCreated = created ?? record.LastEventCreated;
        if (Statuses.Contains(type) && !Final.Contains(record.Status)) record.Status = type;
        if (type == "SendFailed") record.Error = Str(data, "errorMessage") ?? "Send failed";
        store.Save(record);
        return "applied";
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
