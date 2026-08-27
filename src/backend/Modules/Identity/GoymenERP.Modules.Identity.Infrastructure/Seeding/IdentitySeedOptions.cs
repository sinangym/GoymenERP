namespace GoymenERP.Modules.Identity.Infrastructure.Seeding;

public sealed class IdentitySeedOptions
{
    public const string SectionName = "IdentitySeed";

    public List<IdentityUserSeed> Users { get; init; } = [];

    public List<IdentityRoleSeed> Roles { get; init; } = [];

    public List<IdentityAssignmentSeed> Assignments { get; init; } = [];
}

public sealed class IdentityUserSeed
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Email { get; init; }

    public List<ExternalIdentitySeed> ExternalIdentities { get; init; } = [];
}

public sealed class ExternalIdentitySeed
{
    public Guid Id { get; init; }

    public string Issuer { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;
}

public sealed class IdentityRoleSeed
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsSystem { get; init; }

    public List<string> Permissions { get; init; } = [];
}

public sealed class IdentityAssignmentSeed
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public Guid RoleId { get; init; }

    public Guid CompanyId { get; init; }

    public DateTimeOffset ValidFromUtc { get; init; }

    public DateTimeOffset? ValidUntilUtc { get; init; }

    public Guid? ApprovedByUserId { get; init; }

    public List<IdentityScopeSeed> Scopes { get; init; } = [];
}

public sealed class IdentityScopeSeed
{
    public string Type { get; init; } = string.Empty;

    public Guid Id { get; init; }
}
