using System.Net.Http.Json;
using BoldSign.Api;
using BoldSign.Model;
using static Recipe.Tests.Helpers;

namespace Recipe.Tests;

public class TemplateTests
{
    private static readonly Signer Ann = new() { Name = "Ann", Email = "ann@boldsign.dev" };

    [Fact]
    public void ParseRolesReadsNameIndexPairs()
    {
        Assert.Equal([new TemplateRole("Employee", 1), new TemplateRole("Manager", 2)], Config.ParseRoles("Employee:1, Manager:2"));
        Assert.Throws<ArgumentException>(() => Config.ParseRoles("Employee"));
    }

    [Fact]
    public void MapRolesMapsEachTemplateRoleToASigner()
    {
        Assert.Equal([new RoleSigner(1, "Employee", "Ann", "ann@boldsign.dev")], RoleMap.Map([new TemplateRole("Employee", 1)], Ann));
        var e = Assert.Throws<ArgumentException>(() => RoleMap.Map([new TemplateRole("Employee", 1)], new Signer { Name = "No email" }));
        Assert.Contains("Employee", e.Message);
    }

    [Fact]
    public void CompareRolesFindsMismatches()
    {
        var ok = RoleMap.Compare([new TemplateRole("Employee", 1)], [new TemplateRole("employee", 1)]);
        Assert.Empty(ok.MissingInTemplate);
        Assert.Empty(ok.NotConfigured);
        var d = RoleMap.Compare([new TemplateRole("Employee", 1)], [new TemplateRole("Manager", 1)]);
        Assert.Single(d.MissingInTemplate);
        Assert.Single(d.NotConfigured);
    }

    private sealed class StubService(Config cfg) : BoldSignService(cfg)
    {
        public readonly List<SendForSignFromTemplate> Calls = new();
        protected override DocumentCreated SdkSendFromTemplate(SendForSignFromTemplate request)
        {
            Calls.Add(request);
            if (Calls.Count < 2) throw new ApiException(400, "Invalid template id.");
            return new DocumentCreated { DocumentId = "doc-9" };
        }
    }

    [Fact]
    public void SendFromTemplateSendsRoleObjectsAndRetriesTheFirst400()
    {
        var svc = new StubService(Cfg());
        Assert.Equal("doc-9", svc.SendFromTemplate("tpl-1", "Offer", RoleMap.Map([new TemplateRole("Employee", 1)], Ann)));
        Assert.Equal(2, svc.Calls.Count);
        Assert.Equal("tpl-1", svc.Calls[1].TemplateId);
        var role = Assert.Single(svc.Calls[1].Roles);
        Assert.Equal((1, "Employee", "ann@boldsign.dev"), (role.RoleIndex, role.SignerRole, role.SignerEmail));
    }

    private sealed class FakeService(Config cfg) : BoldSignService(cfg)
    {
        public readonly List<(string TemplateId, List<RoleSigner> Roles)> Calls = new();
        public override string SendFromTemplate(string templateId, string title, List<RoleSigner> roles) { Calls.Add((templateId, roles)); return "doc-5"; }
    }

    [Fact]
    public async Task PostDocumentsSendsFromTheTemplateAndStoresQueued()
    {
        var store = NewStore();
        var fake = new FakeService(Cfg());
        var (app, client) = await Start(Cfg(), fake, store);
        await using var _ = app;
        var res = await client.PostAsJsonAsync("/documents", new { });
        Assert.Equal(201, (int)res.StatusCode);
        Assert.Equal("tpl-1", fake.Calls[0].TemplateId);
        Assert.Equal("signer@boldsign.dev", fake.Calls[0].Roles[0].SignerEmail);
        Assert.Equal("Queued", store.Get("doc-5")!.Status);
    }

    [Fact]
    public async Task PostDocumentsNeedsATemplateId()
    {
        var cfg = Cfg();
        cfg.TemplateId = "";
        var (app, client) = await Start(cfg, new FakeService(cfg), NewStore());
        await using var _ = app;
        Assert.Equal(400, (int)(await client.PostAsJsonAsync("/documents", new { })).StatusCode);
    }

    [Fact]
    public void RetrySoonDoesNotRetryA401()
    {
        var n = 0;
        Assert.Throws<ApiException>(() => BoldSignService.RetrySoon<string>(() => { n++; throw new ApiException(401, "unauthorized"); }, 5, 0.001));
        Assert.Equal(1, n);
    }
}
