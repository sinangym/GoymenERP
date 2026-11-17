using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>KIM-D56: registration is separate from human group-authority assignment.</summary>
public static class CompanySetupPermissionManifestV1
{
    public const string OpenCompany = ErpPermissions.OrganizationCompaniesOpen;
    public const string AssignFirstAdmin = ErpPermissions.IdentityCompanyFirstAdminAssign;
    public static IReadOnlyList<string> Names { get; } = [OpenCompany, AssignFirstAdmin];
}

public sealed record CompanySetupCapabilitiesV1(bool CanOpenCompany, bool CanAssignFirstAdmin);
public sealed record CompanySetupCompanyV1(Guid CompanyId, string Code, string LegalName, string LifecycleState, long Version);
public sealed record OpenCompanyV1(Guid CompanyId, string Code, string LegalName, string Vkn, string ExpectedVersion, string Reason);
public sealed record CompanyOpeningMetadataV1(string ActorId, string IdempotencyKey, string CorrelationId,
    string? CausationId, string SourceChannel, string? ClientIpAddress, string? UserAgent);
public sealed record CompanyOpeningAnswerV1(int StatusCode, string? Code, CompanySetupCompanyV1? Company);
public sealed record CompanyOpeningResultV1(CompanyOpeningAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

public interface IOrganizationCompanyOpeningV1
{
    Task<CompanySetupCapabilitiesV1> ReadCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanySetupCompanyV1>> ReadPendingAsync(CancellationToken cancellationToken = default);
    Task<CompanyOpeningResultV1> OpenAsync(OpenCompanyV1 request, CompanyOpeningMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}
