using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>
/// Validates the explicit Product, location and party assertions of a Manufacturing output
/// immediately before the shared Manufacturing/Inventory transaction is staged. Inventory remains
/// authoritative for Counterparty ownership and custody through the immutable issued-input trace.
/// </summary>
internal sealed class ManufacturingInventoryOutputAdmission(
    IProductStockEligibilityV1 products,
    IOrganizationLocationStatusReaderV1 locations)
    : IManufacturingInventoryOutputAdmission
{
    public async Task ValidateAsync(
        Guid companyId,
        ManufacturingInventoryOutputCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (companyId == Guid.Empty)
        {
            throw Invalid("companyId");
        }

        ValidatePartyAssertion(companyId, command.OwnerKind, command.OwnerId, "ownerId");
        ValidatePartyAssertion(companyId, command.CustodianKind, command.CustodianId, "custodianId");

        var effectiveOn = DateOnly.FromDateTime(command.OccurredAtUtc.UtcDateTime);
        ProductStockEligibilityV1 product;
        try
        {
            product = await products.EvaluateAsync(
                new ProductStockSelectionV1(command.ProductId, command.ExpectedProductVersion, effectiveOn),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProductOperationException exception) when (exception.StatusCode is StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied);
        }
        catch (ProductOperationException exception)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unavailable, exception.Code);
        }
        catch (Exception)
        {
            throw Unavailable();
        }

        if (product.Snapshot is null)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unprocessable, InventoryOpeningCodesV1.ProductIneligible, "productId");
        }

        if (product.Errors is null || product.Errors.Count != 0
            || product.Snapshot.StockUnit.CompanyId != companyId
            || product.Snapshot.ProductId != command.ProductId
            || !string.Equals(product.Snapshot.ProductVersion, command.ExpectedProductVersion, StringComparison.Ordinal)
            || product.Snapshot.EffectiveOn != effectiveOn)
        {
            throw Unavailable();
        }

        OrganizationLocationStatusV1? location;
        try
        {
            location = await locations.ReadAsync(companyId, command.LocationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw Unavailable();
        }

        if (location is null || location.LocationId != command.LocationId)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unprocessable, InventoryOpeningCodesV1.LocationNotFound, "locationId");
        }

        if (!location.IsActive)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unprocessable, InventoryOpeningCodesV1.LocationInactive, "locationId");
        }
    }

    private static void ValidatePartyAssertion(
        Guid companyId,
        StockPartyKind kind,
        Guid partyId,
        string field)
    {
        if (kind == StockPartyKind.Company)
        {
            if (partyId != companyId)
            {
                throw Invalid(field);
            }

            return;
        }

        // This only admits a non-empty assertion to the shared transaction. Inventory compares it
        // with the owner/custodian captured from the issued parent label and rejects any mismatch
        // before recording the output; Manufacturing does not grant or select a Counterparty here.
        if (kind == StockPartyKind.Counterparty && partyId != Guid.Empty)
        {
            return;
        }

        throw Invalid(field);
    }

    private static ManufacturingInventoryRealizationPortException Invalid(string field) =>
        new(ManufacturingApiStatus.Invalid, InventoryManufacturingRealizationCodesV1.InvalidRequest, field);

    private static ManufacturingInventoryRealizationPortException Unavailable() =>
        new(ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable);
}
