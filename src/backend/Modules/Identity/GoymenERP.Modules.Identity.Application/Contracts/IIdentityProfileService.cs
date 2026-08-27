namespace GoymenERP.Modules.Identity.Application.Contracts;

public interface IIdentityProfileService
{
    Task<IdentityProfile?> GetCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed record IdentityProfile(
    Guid UserId,
    string DisplayName,
    string? Email,
    IReadOnlyList<Guid> AllowedCompanyIds);
