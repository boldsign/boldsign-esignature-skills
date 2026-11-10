using System.Text.RegularExpressions;

namespace Recipe;

/// <summary>Settings from the environment, with a git-ignored .env file as a fallback.</summary>
public sealed class Config
{
    public static readonly IReadOnlyDictionary<string, string> Regions = new Dictionary<string, string>
    {
        ["US"] = "https://api.boldsign.com",
        ["EU"] = "https://api-eu.boldsign.com",
        ["CA"] = "https://api-ca.boldsign.com",
        ["AU"] = "https://api-au.boldsign.com",
    };

    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string Region { get; set; } = "US";
    public string BaseUrl { get; set; } = Regions["US"];
    public int Port { get; set; } = 3000;
    public string AppBaseUrl { get; set; } = "http://localhost:3000";
    public string SignerName { get; set; } = "Test Signer";
    public string SignerEmail { get; set; } = "";
    public string StoreFile { get; set; } = "data/documents.json";
    public string TemplateId { get; set; } = "";
    public List<TemplateRole> TemplateRoles { get; set; } = new();

    /// <summary>Loads .env without overriding variables that are already set (hosted agents and servers set them as secrets).</summary>
    public static void LoadEnv(string path = ".env")
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var m = Regex.Match(line, @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$");
            if (m.Success && Environment.GetEnvironmentVariable(m.Groups[1].Value) is null)
                Environment.SetEnvironmentVariable(m.Groups[1].Value, Regex.Replace(m.Groups[2].Value, @"^(['""])(.*)\1$", "$2"));
        }
    }

    /// <summary>Reads BOLDSIGN_TEMPLATE_ROLES, "Employee:1, Manager:2", into roles.</summary>
    public static List<TemplateRole> ParseRoles(string value)
    {
        var roles = new List<TemplateRole>();
        foreach (var part in value.Split(','))
        {
            if (part.Trim() == "") continue;
            var m = Regex.Match(part, @"^\s*(.+?)\s*:\s*(\d+)\s*$");
            if (!m.Success) throw new ArgumentException($"BOLDSIGN_TEMPLATE_ROLES: \"{part.Trim()}\" is not Name:index");
            roles.Add(new TemplateRole(m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
        }
        return roles;
    }

    public static Config FromEnvironment(IDictionary<string, string>? env = null)
    {
        string Get(string k) => (env is null ? Environment.GetEnvironmentVariable(k) : env.TryGetValue(k, out var v) ? v : null) ?? "";
        var region = (Get("BOLDSIGN_REGION") is { Length: > 0 } r ? r : "US").ToUpperInvariant();
        if (!Regions.ContainsKey(region)) throw new ArgumentException($"BOLDSIGN_REGION must be one of {string.Join(", ", Regions.Keys)}");
        var port = int.TryParse(Get("PORT"), out var p) ? p : 3000;
        return new Config
        {
            ApiKey = Get("BOLDSIGN_API_KEY"),
            WebhookSecret = Get("BOLDSIGN_WEBHOOK_SECRET"),
            Region = region,
            BaseUrl = Regions[region],
            Port = port,
            AppBaseUrl = Get("APP_BASE_URL") is { Length: > 0 } u ? u : $"http://localhost:{port}",
            SignerName = Get("SIGNER_NAME") is { Length: > 0 } n ? n : "Test Signer",
            SignerEmail = Get("SIGNER_EMAIL"),
            StoreFile = Get("STORE_FILE") is { Length: > 0 } f ? f : "data/documents.json",
            TemplateId = Get("BOLDSIGN_TEMPLATE_ID"),
            TemplateRoles = ParseRoles(Get("BOLDSIGN_TEMPLATE_ROLES") is { Length: > 0 } tr ? tr : "Employee:1"),
        };
    }
}
