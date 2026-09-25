using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>
/// Validates the explicit Product, location and company-party facts of a Manufacturing output
/// immediately before the shared Manufacturing/Inventory transaction is staged. Counterparty
/// identities remain fail-closed until Manufacturing has an accepted owner-backed reference port.
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

        ValidateCompanyParty(companyId, command.OwnerKind, command.OwnerId, "ownerId");
        ValidateCompanyParty(companyId, command.CustodianKind, command.CustodianId, "custodianId");

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

        if (product.Snapshot.ProductId != command.ProductId
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

    private static void ValidateCompanyParty(
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

        // Customer-owned and subcontract custody are valid Inventory dimensions, but accepting
        // an arbitrary Counterparty id here would bypass its owner. No accepted Manufacturing
        // candidate/validation port exists yet, so this path remains retryably unavailable.
        throw Unavailable();
    }

    private static ManufacturingInventoryRealizationPortException Invalid(string field) =>
        new(ManufacturingApiStatus.Invalid, InventoryManufacturingRealizationCodesV1.InvalidRequest, field);

    private static ManufacturingInventoryRealizationPortException Unavailable() =>
        new(ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable);
}
