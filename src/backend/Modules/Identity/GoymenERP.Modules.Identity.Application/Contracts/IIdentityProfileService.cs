using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Identity.Application.Contracts;

public interface IIdentityProfileService
{
    Task<IdentityProfile?> GetCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>GET /api/v1/identity/me</c>. ID-2 adds <see cref="MustChangePassword"/> (spec "/me ... mustChangePassword alanı
/// eklenir") and <see cref="LocalCredentialVersion"/>, the decimal text a password change sends as its expected
/// version; both are required in the payload, the version is <c>null</c> for a user without a local credential.
/// </summary>
public sealed record IdentityProfile(
    Guid UserId,
    string DisplayName,
    string? Email,
    IReadOnlyList<Guid> AllowedCompanyIds,
    [property: JsonRequired] string? AuthenticationSessionBinding = null,
    [property: JsonRequired] bool MustChangePassword = false,
    [property: JsonRequired] string? LocalCredentialVersion = null);
