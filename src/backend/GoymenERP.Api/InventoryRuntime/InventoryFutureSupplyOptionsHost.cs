using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>
/// F017-d / STK-S18: authorized Inventory choice read over the existing owner ports. Supplier identities come only
/// from Purchasing's approved general lines; the Cari record-display source is never a user Party search or eligibility proof.
/// No price, currency, discount or payment field enters the consumer response.
/// </summary>
public sealed class InventoryFutureSupplyOptionsHost(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IInventoryFutureDemandSourceV1 demand,
    IPurchaseFutureSupplyReadPortV1 purchasing, IInventoryFutureAllocationReadPortV1 allocations,
    ICounterpartyPurchasingRecordPartyDisplaySourceV1 suppliers, ISettingsUnitIdentityCatalogV1 units)
{
    public async Task<InventoryFutureSupplyOptionsV1> ReadAsync(Guid salesOrderId, Guid salesOrderLineId, string? cursor,
        CancellationToken cancellationToken = default)
    {
        var companyId = company.CompanyId;
        var actor = user.UserId;
        async Task Fence()
        {
            if (!user.IsAuthenticated) throw Error(401);
            if (string.IsNullOrWhiteSpace(actor) || !company.IsResolved || company.CompanyId != companyId || companyId == Guid.Empty
                || !user.CanAccessCompany(companyId) || !string.Equals(user.UserId, actor, StringComparison.Ordinal)) throw Error(403);
            var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryFutureAllocationWrite, companyId, cancellationToken);
            if (!scope.IsGranted || !scope.IsCompanyWide || !company.IsResolved || company.CompanyId != companyId
                || !user.IsAuthenticated || !string.Equals(user.UserId, actor, StringComparison.Ordinal) || !user.CanAccessCompany(companyId)) throw Error(403);
        }
        await Fence();
        if (salesOrderId == Guid.Empty || salesOrderLineId == Guid.Empty || cursor is { Length: > 4096 }) throw Error(400);
        try
        {
            var source = await demand.GetDemandLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
            if (source.Outcome is InventoryFutureDemandSourceOutcomeV1.NotFound) throw new InventoryAllocationException(404, InventoryFutureAllocationCodesV1.InvalidReference);
            if (source.Outcome is InventoryFutureDemandSourceOutcomeV1.NotApproved) throw new InventoryAllocationException(422, InventoryFutureAllocationCodesV1.InvalidReference);
            if (source is not { Outcome: InventoryFutureDemandSourceOutcomeV1.Open, Line: { } line }
                || line.OrderId != salesOrderId || line.LineId != salesOrderLineId || line.ProductId == Guid.Empty
                || line.UnitId == Guid.Empty
                || !InventoryFutureAllocationTextV1.TryVersion(line.ProductVersion, out _)
                || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(line.OpenQuantityText, out _)) throw Error(503);
            PurchaseFutureSupplyPageV1 page;
            try { page = await purchasing.ListAllocatableLinesAsync(line.ProductId, 25, cursor, cancellationToken); }
            catch (ArgumentException) { throw Error(400); }
            if (page.Items.Count > 25 || page.Items.Select(item => item.LineId).Distinct().Count() != page.Items.Count
                || page.NextCursor is { Length: > 4096 }
                || page.Items.Any(item => item.CompanyId != companyId || item.ProductId != line.ProductId || item.OrderId == Guid.Empty
                    || item.LineId == Guid.Empty || item.SupplierPartyId == Guid.Empty || item.UnitId == Guid.Empty || item.LineNo < 1
                    || item.Kind != "General" || !InventoryFutureAllocationTextV1.TryPositiveQuantity(item.AllocatableText, out _))) throw Error(503);
            var partyIds = page.Items.Select(item => item.SupplierPartyId).Distinct().ToArray();
            IReadOnlyList<CounterpartyPurchasingRecordPartyDisplayV1> names = [];
            if (partyIds.Length > 0)
            {
                var display = await suppliers.ReadForAuthorizedPurchasingRecordsAsync(new(companyId, actor!, partyIds), cancellationToken);
                if (display.Outcome == CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied) throw Error(403);
                if (display.Outcome != CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Found) throw Error(503);
                names = display.Items;
                if (names.Count != partyIds.Length || names.Select(item => item.PartyId).Distinct().Count() != names.Count
                    || names.Any(item => !partyIds.Contains(item.PartyId) || string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.LegalName))) throw Error(503);
            }
            var byParty = names.ToDictionary(item => item.PartyId);
            var byUnit = new Dictionary<Guid, string>();
            foreach (var unitId in page.Items.Select(item => item.UnitId).Append(line.UnitId).Distinct())
            {
                var unit = await units.ReadAsync(unitId, cancellationToken);
                if (unit is null || unit.UnitId != unitId || string.IsNullOrWhiteSpace(unit.Code)) throw Error(503);
                byUnit.Add(unitId, unit.Code);
            }
            var items = new List<InventoryFutureSupplyOptionV1>();
            foreach (var item in page.Items)
            {
                var reserved = await allocations.GetAllocatedOnPurchaseLineAsync(item.LineId, cancellationToken);
                var active = FutureSupplyQuantity.Zero;
                if (reserved.Outcome == InventoryFutureAllocationReadOutcomeV1.Present)
                {
                    if (reserved.UnitId != item.UnitId || !InventoryFutureAllocationTextV1.TryPositiveQuantity(reserved.QuantityText, out active)) throw Error(503);
                }
                else if (reserved.Outcome != InventoryFutureAllocationReadOutcomeV1.None
                    || reserved.QuantityText is not null || reserved.UnitId is not null) throw Error(503);
                if (!InventoryFutureAllocationTextV1.TryPositiveQuantity(item.AllocatableText, out var allocatable) || active > allocatable) throw Error(503);
                var available = allocatable - active;
                var party = byParty[item.SupplierPartyId];
                items.Add(new(item.OrderId, item.LineId, item.LineNo, party.Code, party.LegalName, item.UnitId, byUnit[item.UnitId],
                    allocatable.ToString(), active.ToString(), available.ToString()));
            }
            await Fence();
            return new(companyId, salesOrderId, salesOrderLineId, line.ProductId, line.ProductVersion, line.UnitId,
                byUnit[line.UnitId], line.OpenQuantityText, items, page.NextCursor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            await Fence();
            if (exception is InventoryAllocationException) throw;
            throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, innerException: exception);
        }
    }

    private static InventoryAllocationException Error(int status) => new(status,
        status == 400 ? InventoryFutureAllocationCodesV1.ValidationFailed : InventoryFutureAllocationCodesV1.ServiceUnavailable);
}
