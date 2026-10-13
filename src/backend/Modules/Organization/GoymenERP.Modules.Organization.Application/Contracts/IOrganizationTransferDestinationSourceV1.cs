namespace GoymenERP.Modules.Organization.Application.Contracts;

public sealed record OrganizationTransferDestinationV1(
    Guid LocationId,
    bool IsActive,
    bool IsExternal,
    Guid? OperatorPartyId,
    long LocationVersion);

public interface IOrganizationTransferDestinationSourceV1
{
    Task<OrganizationTransferDestinationV1?> ReadAsync(
        Guid companyId,
        Guid locationId,
        CancellationToken cancellationToken = default);
}
