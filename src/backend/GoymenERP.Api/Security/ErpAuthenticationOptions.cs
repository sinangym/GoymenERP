namespace GoymenERP.Api.Security;

public sealed class ErpAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Mode { get; init; } = AuthenticationModes.OidcJwtBearer;

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
