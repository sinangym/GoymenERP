using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Central read composition for Inventory-owned quantities and Product-owned display facts. The
/// Product port is invoked once for the bounded Inventory page; neither owner gains the other's
/// authority and no fallback identity or technical value is synthesized.
/// </summary>
public static class InventoryStockPositionDisplayHost
{
    public const string LocationDisplayContractVersion = "2.0";

    public static InventoryStockPositionRequestScope CaptureScope(
        ICompanyContext company,
        ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(user);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId) || !user.CanAccessCompany(company.CompanyId))
        {
            throw Forbidden();
        }

        return new(company.CompanyId, user.UserId);
    }

    public static async Task<InventoryStockPositionPageV1> ComposeAuthorizedAsync(
        InventoryStockPositionRequestScope scope,
        InventoryStockPositionsV1 source,
        IProductStockDisplayReferencesV1 products,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ComposeAsync(source, products, cancellationToken);
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A Product refusal must not bypass the final Inventory fence. If Inventory authority
            // disappeared while Product was awaited, the public answer is the code-less 403.
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            throw;
        }
    }

    public static async Task<InventoryStockPositionLocationPageV2> ComposeWithLocationAuthorizedAsync(
        InventoryStockPositionRequestScope scope,
        InventoryStockPositionsV1 source,
        IProductStockDisplayReferencesV1 products,
        IOrganizationLocationDisplayReaderV1 locations,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Items);
            ArgumentNullException.ThrowIfNull(locations);
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            if (source.Items.Count == 0)
            {
                return new([], source.SearchWindowFull, source.NextCursor);
            }

            var productPage = await ComposeAsync(source, products, cancellationToken);
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);

            var locationIds = source.Items.Select(item => item.LocationId).Distinct().ToArray();
            if (locationIds.Any(id => id == Guid.Empty))
            {
                throw InvalidDisplayComposition();
            }

            var locationPage = await locations.ReadAsync(scope.CompanyId, locationIds, cancellationToken);
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            if (locationPage.Count != locationIds.Length
                || locationPage.Select(item => item.LocationId).Distinct().Count() != locationPage.Count
                || locationPage.Any(item => item.LocationId == Guid.Empty
                    || string.IsNullOrWhiteSpace(item.LocationCode)
                    || string.IsNullOrWhiteSpace(item.LocationName)))
            {
                throw InvalidDisplayComposition();
            }

            var byLocation = locationPage.ToDictionary(item => item.LocationId);
            if (locationIds.Any(id => !byLocation.ContainsKey(id)))
            {
                throw InvalidDisplayComposition();
            }

            return new(
                [.. productPage.Items.Select(item => new InventoryStockPositionLocationDisplayV2(
                    item.CompanyId, item.LabelId, item.LabelVersion, item.LabelCode, item.ProductId,
                    item.ProductVersion, item.Classification, item.Lot, item.HeatNumber, item.CoilNumber,
                    item.DocumentStatus, item.OwnerKind, item.OwnerId, item.CustodianKind, item.CustodianId,
                    item.LocationId, item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                    item.ActiveAllocationKg, item.AvailableKg, item.ProductDisplay,
                    byLocation[item.LocationId]))],
                productPage.SearchWindowFull,
                productPage.NextCursor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            throw;
        }
    }

    public static InventoryStockPositionLegacyPageV1 Legacy(InventoryStockPositionsV1 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Items);
        return new([.. source.Items.Select(item => new InventoryStockPositionLegacyV1(
            item.CompanyId, item.LabelId, item.LabelVersion, item.LabelCode, item.ProductId,
            item.Lot, item.HeatNumber, item.CoilNumber, item.DocumentStatus, item.OwnerKind,
            item.OwnerId, item.CustodianKind, item.CustodianId, item.LocationId, item.PhysicalKg,
            item.FreePhysicalKg, item.BlockedPhysicalKg, item.ActiveAllocationKg, item.AvailableKg))],
            source.SearchWindowFull);
    }

    public static async Task<InventoryStockPositionOwnerPageV3> ComposeWithOwnerAuthorizedAsync(
        InventoryStockPositionRequestScope scope,
        InventoryStockPositionsV1 source,
        IProductStockDisplayReferencesV1 products,
        IOrganizationLocationDisplayReaderV1 locations,
        IInventoryStockOwnerDisplaySourceV1 owners,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Items);
            ArgumentNullException.ThrowIfNull(owners);
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            if (source.Items.Count == 0)
            {
                return new([], source.SearchWindowFull, source.NextCursor);
            }

            var locationPage = await ComposeWithLocationAuthorizedAsync(
                scope, source, products, locations, company, user, authority, cancellationToken);
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);

            var partyIds = locationPage.Items
                .Where(item => string.Equals(item.OwnerKind, "Counterparty", StringComparison.Ordinal))
                .Select(item => item.OwnerId)
                .Distinct()
                .Order()
                .ToArray();
            if (partyIds.Length > 100 || partyIds.Any(id => id == Guid.Empty)
                || locationPage.Items.Any(item => item.OwnerKind is not "Company" and not "Counterparty"))
            {
                throw InvalidDisplayComposition();
            }

            IReadOnlyDictionary<Guid, InventoryStockOwnerDisplayV1> byParty =
                new Dictionary<Guid, InventoryStockOwnerDisplayV1>();
            if (partyIds.Length > 0)
            {
                var answer = await owners.GetAuthorizedBatchAsync(
                    new(scope.CompanyId, scope.ActorId, partyIds),
                    cancellationToken);
                await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
                if (answer.Outcome == InventoryStockOwnerDisplayBatchOutcomeV1.Denied)
                {
                    throw Forbidden();
                }
                if (answer.Outcome != InventoryStockOwnerDisplayBatchOutcomeV1.Found
                    || answer.Items.Count != partyIds.Length
                    || answer.Items.Select(item => item.PartyId).Distinct().Count() != answer.Items.Count
                    || answer.Items.Any(item => item.PartyId == Guid.Empty
                        || string.IsNullOrWhiteSpace(item.Code)
                        || string.IsNullOrWhiteSpace(item.LegalName)))
                {
                    throw InvalidDisplayComposition();
                }

                byParty = answer.Items.ToDictionary(item => item.PartyId);
                if (partyIds.Any(id => !byParty.ContainsKey(id)))
                {
                    throw InvalidDisplayComposition();
                }
            }

            return new(
                [.. locationPage.Items.Select(item => new InventoryStockPositionOwnerDisplayV3(
                    item.CompanyId, item.LabelId, item.LabelVersion, item.LabelCode, item.ProductId,
                    item.ProductVersion, item.Classification, item.Lot, item.HeatNumber, item.CoilNumber,
                    item.DocumentStatus, item.OwnerKind, item.OwnerId, item.CustodianKind, item.CustodianId,
                    item.LocationId, item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                    item.ActiveAllocationKg, item.AvailableKg, item.ProductDisplay, item.LocationDisplay,
                    item.OwnerKind == "Counterparty" ? byParty[item.OwnerId] : null))],
                locationPage.SearchWindowFull,
                locationPage.NextCursor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await RequireFreshInventoryAuthorityAsync(scope, company, user, authority, cancellationToken);
            throw;
        }
    }

    public static async Task<InventoryStockPositionPageV1> ComposeAsync(
        InventoryStockPositionsV1 source,
        IProductStockDisplayReferencesV1 products,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Items);
        ArgumentNullException.ThrowIfNull(products);

        if (source.Items.Count == 0)
        {
            return new([], source.SearchWindowFull, source.NextCursor);
        }

        if (source.Items.Any(item => item.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(item.ProductVersion)))
        {
            throw Unavailable();
        }

        var groups = source.Items.GroupBy(item => item.ProductId).ToArray();
        if (groups.Any(group => group.Select(item => item.ProductVersion)
                .Distinct(StringComparer.Ordinal).Count() != 1))
        {
            throw Unavailable();
        }

        var selections = groups.Select(group => new ProductStockDisplaySelectionV1(
            group.Key, group.First().ProductVersion!)).ToArray();
        var productPage = await products.ReadAsync(new(selections), cancellationToken);
        if (productPage.Items.Count != selections.Length
            || productPage.Items.Select(item => item.ProductId).Distinct().Count() != productPage.Items.Count)
        {
            throw Unavailable();
        }

        var byProduct = productPage.Items.ToDictionary(item => item.ProductId);
        foreach (var selection in selections)
        {
            if (!byProduct.TryGetValue(selection.ProductId, out var item)
                || !string.Equals(item.ExpectedProductVersion, selection.ExpectedProductVersion,
                    StringComparison.Ordinal))
            {
                throw Unavailable();
            }
        }

        return new(
            [.. source.Items.Select(item => View(item, byProduct[item.ProductId]))],
            source.SearchWindowFull,
            source.NextCursor);
    }

    private static InventoryStockPositionDisplayV1 View(
        InventoryStockPositionV1 item,
        ProductStockDisplayResultV1 product) => new(
        item.CompanyId,
        item.LabelId,
        item.LabelVersion,
        item.LabelCode,
        item.ProductId,
        item.ProductVersion,
        item.Classification,
        item.Lot,
        item.HeatNumber,
        item.CoilNumber,
        item.DocumentStatus,
        item.OwnerKind,
        item.OwnerId,
        item.CustodianKind,
        item.CustodianId,
        item.LocationId,
        item.PhysicalKg,
        item.FreePhysicalKg,
        item.BlockedPhysicalKg,
        item.ActiveAllocationKg,
        item.AvailableKg,
        product);

    private static InventoryStockPositionException Unavailable() =>
        new(StatusCodes.Status503ServiceUnavailable, InventoryStockPositionCodesV1.Unavailable);

    private static InvalidOperationException InvalidDisplayComposition() =>
        new("The display owner returned an incomplete or inconsistent batch.");

    private static async Task RequireFreshInventoryAuthorityAsync(
        InventoryStockPositionRequestScope expected,
        ICompanyContext company,
        ICurrentUser user,
        IErpAuthorityRevalidationService authority,
        CancellationToken cancellationToken)
    {
        RequireIdentity(expected, company, user);
        foreach (var permission in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            PermissionScope measured;
            try
            {
                measured = await authority.RevalidatePermissionScopeAsync(
                    permission, expected.CompanyId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                RequireIdentity(expected, company, user);
                throw new InventoryStockPositionException(StatusCodes.Status503ServiceUnavailable,
                    InventoryStockPositionCodesV1.Unavailable, exception);
            }

            RequireIdentity(expected, company, user);
            if (!measured.IsGranted || !measured.IsCompanyWide)
            {
                throw Forbidden();
            }
        }
    }

    private static void RequireIdentity(InventoryStockPositionRequestScope expected,
        ICompanyContext company, ICurrentUser user)
    {
        if (!company.IsResolved || company.CompanyId != expected.CompanyId || expected.CompanyId == Guid.Empty
            || !user.IsAuthenticated || !string.Equals(user.UserId, expected.ActorId, StringComparison.Ordinal)
            || !user.CanAccessCompany(expected.CompanyId))
        {
            throw Forbidden();
        }
    }

    private static InventoryStockPositionException Forbidden() =>
        new(StatusCodes.Status403Forbidden, InventoryStockPositionCodesV1.Forbidden);
}

public sealed record InventoryStockPositionRequestScope(Guid CompanyId, string ActorId);

/// <summary>
/// Compatibility response for clients that have not explicitly selected the Product display contract.
/// Its exact field set is the previously published ST-01 response.
/// </summary>
public sealed record InventoryStockPositionLegacyPageV1(
    IReadOnlyList<InventoryStockPositionLegacyV1> Items,
    bool SearchWindowFull);

public sealed record InventoryStockPositionLegacyV1(
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    InventoryLotV1 Lot,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg);

public sealed record InventoryStockPositionPageV1(
    IReadOnlyList<InventoryStockPositionDisplayV1> Items,
    bool SearchWindowFull,
    string? NextCursor = null);

public sealed record InventoryStockPositionDisplayV1(
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    string? ProductVersion,
    string? Classification,
    InventoryLotV1 Lot,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg,
    ProductStockDisplayResultV1 ProductDisplay);

public sealed record InventoryStockPositionLocationPageV2(
    IReadOnlyList<InventoryStockPositionLocationDisplayV2> Items,
    bool SearchWindowFull,
    string? NextCursor = null);

public sealed record InventoryStockPositionLocationDisplayV2(
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    string? ProductVersion,
    string? Classification,
    InventoryLotV1 Lot,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg,
    ProductStockDisplayResultV1 ProductDisplay,
    OrganizationLocationDisplayV1 LocationDisplay);

public sealed record InventoryStockPositionOwnerPageV3(
    IReadOnlyList<InventoryStockPositionOwnerDisplayV3> Items,
    bool SearchWindowFull,
    string? NextCursor = null);

public sealed record InventoryStockPositionOwnerDisplayV3(
    Guid CompanyId,
    Guid LabelId,
    string LabelVersion,
    string LabelCode,
    Guid ProductId,
    string? ProductVersion,
    string? Classification,
    InventoryLotV1 Lot,
    string? HeatNumber,
    string? CoilNumber,
    string DocumentStatus,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string PhysicalKg,
    string FreePhysicalKg,
    string BlockedPhysicalKg,
    string ActiveAllocationKg,
    string AvailableKg,
    ProductStockDisplayResultV1 ProductDisplay,
    OrganizationLocationDisplayV1 LocationDisplay,
    InventoryStockOwnerDisplayV1? OwnerDisplay);
