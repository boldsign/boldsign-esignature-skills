using Recipe;

// dotnet run --project App            starts the app on http://127.0.0.1:PORT
// dotnet run --project App -- check   one read-only call to confirm the API key and region
Config.LoadEnv();
var cfg = Config.FromEnvironment();
var boldsign = new BoldSignService(cfg);

if (args.FirstOrDefault() == "check")
{
    if (cfg.ApiKey == "") { Console.Error.WriteLine("BOLDSIGN_API_KEY is not set (see .env.example)."); return 1; }
    try
    {
        boldsign.CheckConfig();
        Console.WriteLine($"OK: GET /v1/brand/list returned 200 on {cfg.BaseUrl} (region {cfg.Region}).");
        return 0;
    }
    catch (Exception e)
    {
        var status = BoldSignService.StatusOf(e);
        Console.Error.WriteLine($"GET /v1/brand/list on {cfg.BaseUrl} failed with HTTP {status?.ToString() ?? "error"}.");
        if (status == 401) Console.Error.WriteLine("401 usually means a wrong key or the wrong region. Regions: " + string.Join(", ", Config.Regions.Select(r => $"{r.Key} {r.Value}")));
        return 1;
    }
}
if (args.FirstOrDefault() == "sample-pdf")
{
    File.WriteAllBytes("sample.pdf", SamplePdf.Build());
    Console.WriteLine("Wrote sample.pdf (open it: the text tags are white, so only the labels show).");
    return 0;
}

if (cfg.ApiKey == "") Console.WriteLine("BOLDSIGN_API_KEY is not set: sending will fail. See .env.example.");
var app = Routes.Build(cfg, boldsign, new Store(cfg.StoreFile));
app.Urls.Add($"http://127.0.0.1:{cfg.Port}");
Console.WriteLine($"Listening on http://localhost:{cfg.Port}/documents (BoldSign region {cfg.Region})");
app.Run();
return 0;
