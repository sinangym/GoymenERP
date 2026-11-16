namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>KIM-D20's narrow pending-company recovery: the role is resolved from the configured Admin identity.</summary>
public sealed record AssignFirstCompanyAdminV1(Guid UserId, long ExpectedCompanyVersion, string Reason);
public interface IIdentityCompanySetupServiceV1
{
    Task<bool> IsActiveFounderAsync(CancellationToken cancellationToken = default);
    Task<IdentityAccessCommandResultV1> AssignFirstAdminAsync(AssignFirstCompanyAdminV1 request,
        IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
