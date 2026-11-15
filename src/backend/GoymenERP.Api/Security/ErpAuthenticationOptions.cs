namespace GoymenERP.Api.Security;

public sealed class ErpAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public const string ProvidersKey = "Providers";

    public string Mode { get; init; } = AuthenticationModes.OidcJwtBearer;

    /// <summary>
    /// ID-6: the list of enabled sign-in schemes (for example <c>LocalAccount</c> and <c>OidcJwtBearer</c>). When the
    /// list has entries it replaces <see cref="Mode"/>; OIDC is on only when the list names it. An empty list keeps
    /// the single-scheme <see cref="Mode"/> behavior.
    /// </summary>
    public string[] Providers { get; init; } = [];

    public OidcJwtBearerOptions Oidc { get; init; } = new();
}

public sealed class OidcJwtBearerOptions
{
    public string Authority { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; } = true;
}

public static class AuthenticationModes
{
    public const string DevelopmentBootstrap = "DevelopmentBootstrap";

    public const string OidcJwtBearer = "OidcJwtBearer";

    /// <summary>ID-1 local accounts: <c>Identity:LocalAccount</c> settings and a server-validated session cookie.</summary>
    public const string LocalAccount = "LocalAccount";
}
