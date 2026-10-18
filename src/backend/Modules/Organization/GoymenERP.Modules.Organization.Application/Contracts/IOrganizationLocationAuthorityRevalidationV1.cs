namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// The closed Organization permissions whose current row scope can be measured for exact locations.
/// This contract grants nothing and does not accept arbitrary permission names.
/// </summary>
public enum OrganizationLocationAuthorityAccessV1
{
    Read = 1,
    Write = 2,
}

public enum OrganizationLocationAuthorityOutcomeV1
{
    Denied = 0,
    Authorized = 1,
}

public static class OrganizationLocationAuthorityContractV1
{
    public const int MaximumLocationCount = 100;
}

/// <summary>
/// Company and actor are copies of server-resolved context. Location ids must be distinct, non-empty
/// identifiers already derived by the caller from its own authorized record or command.
/// </summary>
public sealed record OrganizationLocationAuthorityRequestV1(
    Guid CompanyId,
    string ActorId,
    OrganizationLocationAuthorityAccessV1 Access,
    IReadOnlyList<Guid> LocationIds);

/// <summary>
/// Re-measures existing Organization location authority and applies the module's canonical location
/// hierarchy. Invalid or mismatched input and insufficient authority answer <see cref="OrganizationLocationAuthorityOutcomeV1.Denied"/>.
/// Infrastructure failures and cancellation propagate so the caller can map them to its own unavailable
/// response without confusing an outage with a denial.
/// </summary>
public interface IOrganizationLocationAuthorityRevalidationV1
{
    Task<OrganizationLocationAuthorityOutcomeV1> RevalidateAsync(
        OrganizationLocationAuthorityRequestV1 request,
        CancellationToken cancellationToken = default);
}
