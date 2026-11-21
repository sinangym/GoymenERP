namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>KIM-D56 explicit group-authorized first-Admin selection: the role is resolved from the configured Admin identity.</summary>
public sealed record AssignFirstCompanyAdminV1(Guid UserId, long ExpectedCompanyVersion, string Reason);
public interface IIdentityCompanySetupServiceV1
{
    Task<bool> IsActiveFounderAsync(CancellationToken cancellationToken = default);
    Task<IdentityAccessCommandResultV1> AssignFirstAdminAsync(AssignFirstCompanyAdminV1 request,
        IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public sealed record FirstCompanyAdminCandidateV1(Guid UserId, string DisplayName);
public interface IIdentityFirstAdminCandidatesV1
{
    Task<IReadOnlyList<FirstCompanyAdminCandidateV1>> ReadCandidatesAsync(string? search,
        CancellationToken cancellationToken = default);
}
