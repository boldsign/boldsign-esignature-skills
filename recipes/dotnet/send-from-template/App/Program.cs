using Recipe;

// dotnet run --project App            starts the app on http://127.0.0.1:PORT
// dotnet run --project App -- check   read-only checks: the API key and region, then the template and its roles
Config.LoadEnv();
var cfg = Config.FromEnvironment();
var boldsign = new BoldSignService(cfg);

if (args.FirstOrDefault() == "check")
{
    // Read-only checks before sending: the API key and region, then the template and its roles.
    if (cfg.ApiKey == "") { Console.Error.WriteLine("BOLDSIGN_API_KEY is not set (see .env.example)."); return 1; }
    try
    {
        boldsign.CheckConfig();
        Console.WriteLine($"OK: GET /v1/brand/list returned 200 on {cfg.BaseUrl} (region {cfg.Region}).");
    }
    catch (Exception e)
    {
        var status = BoldSignService.StatusOf(e);
        Console.Error.WriteLine($"GET /v1/brand/list on {cfg.BaseUrl} failed with HTTP {status?.ToString() ?? "error"}.");
        if (status == 401) Console.Error.WriteLine("401 usually means a wrong key or the wrong region. Regions: " + string.Join(", ", Config.Regions.Select(r => $"{r.Key} {r.Value}")));
        return 1;
    }
    if (cfg.TemplateId == "") { Console.Error.WriteLine("BOLDSIGN_TEMPLATE_ID is not set."); return 1; }
    List<TemplateRole> roles;
    try { roles = boldsign.TemplateRoles(cfg.TemplateId); }
    catch (Exception e)
    {
        Console.Error.WriteLine($"GET /v1/template/properties failed with HTTP {BoldSignService.StatusOf(e)?.ToString() ?? "error"}: check BOLDSIGN_TEMPLATE_ID (a template created seconds ago can need a moment).");
        return 1;
    }
    static string Fmt(IEnumerable<TemplateRole> rs) => string.Join(", ", rs.Select(r => $"{r.Name}:{r.Index}")) is { Length: > 0 } s ? s : "none";
    Console.WriteLine($"OK: template {cfg.TemplateId} has roles {Fmt(roles)}");
    var (missing, notConfigured) = RoleMap.Compare(cfg.TemplateRoles, roles);
    if (missing.Count > 0 || notConfigured.Count > 0)
    {
        Console.Error.WriteLine($"Role mismatch. Configured but not in the template: {Fmt(missing)}. In the template but not configured: {Fmt(notConfigured)}. Fix BOLDSIGN_TEMPLATE_ROLES.");
        return 1;
    }
    Console.WriteLine("OK: BOLDSIGN_TEMPLATE_ROLES matches the template.");
    return 0;
}

if (cfg.ApiKey == "") Console.WriteLine("BOLDSIGN_API_KEY is not set: sending will fail. See .env.example.");
var app = Routes.Build(cfg, boldsign, new Store(cfg.StoreFile));
app.Urls.Add($"http://127.0.0.1:{cfg.Port}");
Console.WriteLine($"Listening on http://localhost:{cfg.Port}/documents (BoldSign region {cfg.Region})");
app.Run();
return 0;
