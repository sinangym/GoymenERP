namespace GoymenERP.Modules.Identity.Application.Contracts;

public interface IExternalIdentityResolver
{
    Task<ResolvedIdentity?> ResolveAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);
}

public sealed record ResolvedIdentity(
    Guid UserId,
    string DisplayName,
    string? Email,
    IReadOnlySet<Guid> AllowedCompanyIds);
