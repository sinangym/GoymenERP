using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// B-22a: Inventory's transfer destination seam over Organization's company-scoped location read
/// (<see cref="IOrganizationTransferDestinationSourceV1"/>). It grants no Organization read and decides nothing beyond
/// the pairing: a missing or foreign-company location is <see cref="InventoryTransferDestinationStatusV1.NotFound"/>, a
/// passive one <see cref="InventoryTransferDestinationStatusV1.Inactive"/>. Custody follows DEC-ORG-004: an external
/// location is held by its operator party (a counterparty), an internal location by the transaction company itself;
/// any other custodian, an unknown kind or an external location without an operator is a context mismatch. A source
/// fault is not caught here: the transfer command measures it and answers 503 without a stock effect.
/// </summary>
public sealed class OrganizationInventoryTransferDestinationPort(IOrganizationTransferDestinationSourceV1 source)
    : IInventoryTransferDestinationPortV1
{
    public async Task<InventoryTransferDestinationStatusV1> CheckAsync(
        Guid companyId,
        Guid locationId,
        string custodianKind,
        Guid custodianId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || locationId == Guid.Empty)
        {
            return InventoryTransferDestinationStatusV1.NotFound;
        }

        var location = await source.ReadAsync(companyId, locationId, cancellationToken);
        if (location is null || location.LocationId != locationId)
        {
            return InventoryTransferDestinationStatusV1.NotFound;
        }

        if (!location.IsActive)
        {
            return InventoryTransferDestinationStatusV1.Inactive;
        }

        return IsExpectedCustodian(companyId, location, custodianKind, custodianId)
            ? InventoryTransferDestinationStatusV1.Active
            : InventoryTransferDestinationStatusV1.ContextMismatch;
    }

    private static bool IsExpectedCustodian(
        Guid companyId,
        OrganizationTransferDestinationV1 location,
        string? custodianKind,
        Guid custodianId)
    {
        if (custodianId == Guid.Empty)
        {
            return false;
        }

        if (location.IsExternal)
        {
            return location.OperatorPartyId is { } operatorPartyId
                && operatorPartyId != Guid.Empty
                && string.Equals(custodianKind, nameof(StockPartyKind.Counterparty), StringComparison.Ordinal)
                && custodianId == operatorPartyId;
        }

        return string.Equals(custodianKind, nameof(StockPartyKind.Company), StringComparison.Ordinal)
            && custodianId == companyId;
    }
}
