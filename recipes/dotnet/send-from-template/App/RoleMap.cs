namespace Recipe;

/// <summary>A role as the template defines it.</summary>
public sealed record TemplateRole(string Name, int Index);

/// <summary>A template role filled by a signer from the app, as sent to /v1/template/send.</summary>
public sealed record RoleSigner(int RoleIndex, string SignerRole, string SignerName, string SignerEmail);

/// <summary>
/// Maps each template role to a signer from your application. Edit SignerForRole for your data model: here every role
/// is filled by the demo user (SIGNER_NAME, SIGNER_EMAIL), so a one-role template works as is.
/// </summary>
public static class RoleMap
{
    public static Signer SignerForRole(TemplateRole role, Signer user) => user;

    /// <summary>Builds the role list for /v1/template/send. Fails loudly if a role has no signer.</summary>
    public static List<RoleSigner> Map(IEnumerable<TemplateRole> templateRoles, Signer user) => templateRoles.Select(role =>
    {
        var signer = SignerForRole(role, user);
        if (string.IsNullOrEmpty(signer.Email)) throw new ArgumentException($"No signer for template role \"{role.Name}\"");
        return new RoleSigner(role.Index, role.Name, signer.Name, signer.Email);
    }).ToList();

    /// <summary>Compares the configured roles with the template's own roles (from GET /v1/template/properties).</summary>
    public static (List<TemplateRole> MissingInTemplate, List<TemplateRole> NotConfigured) Compare(IEnumerable<TemplateRole> configured, IEnumerable<TemplateRole> fromTemplate)
    {
        static string Key(TemplateRole r) => $"{r.Index}:{r.Name.ToLowerInvariant()}";
        var want = configured.Select(Key).ToHashSet();
        var have = fromTemplate.Select(Key).ToHashSet();
        return (configured.Where(r => !have.Contains(Key(r))).ToList(), fromTemplate.Where(r => !want.Contains(Key(r))).ToList());
    }
}
