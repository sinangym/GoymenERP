namespace GoymenERP.Api.Security;

public sealed class BootstrapIdentityOptions
{
    public const string SectionName = "BootstrapIdentity";

    public string HeaderName { get; init; } = "X-Development-User";

    public List<BootstrapUser> Users { get; init; } = [];
}

public sealed class BootstrapUser
{
    public string UserId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public List<Guid> AllowedCompanyIds { get; init; } = [];
}
