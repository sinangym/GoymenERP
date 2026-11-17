using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryPurchaseInvoiceCostStoreV1
{
    private readonly HashSet<Guid> firstCostFencedLabels = [];

    private async Task<InventoryPurchaseFirstCostRecognitionV1> RecognizeFirstCostAsync(
        InventoryPurchaseInvoiceCostProjectionV1 source, InventoryPurchaseInvoiceCostReceiptV1 share,
        Guid movementId, Guid rootLabelId, Guid receiptLocationId, string receiptQuantityKg, MovementCostStampRow stamp, CancellationToken token)
    {
        if (firstCostPrerequisites is null) throw Unavailable("purchase_invoice_cost_first_recognition_pending");
        var pending = await db.PendingMovementCosts.AsNoTracking().SingleOrDefaultAsync(x => x.MovementId == movementId
            && x.LineNumber == 1 && x.CurrencyId == Tl, token);
        var acceptedKg = decimal.Parse(receiptQuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (pending is null || pending.LabelId != rootLabelId || pending.ProductId != share.ProductId
            || pending.QuantityKg != acceptedKg || stamp.UnitCostPerKg is not null || stamp.UnitCostRatioJson is not null
            || stamp.Amount is not null || acceptedKg <= 0m)
            throw Unavailable("purchase_invoice_cost_first_source_unavailable");
        var lineage = await InventoryCostLineageReader.ReadAsync(db, source.CompanyId, share.ProductId, rootLabelId, token);
        RequireCompany(source.CompanyId);
        if (lineage.Outcome != InventoryCostLineageOutcome.Value || lineage.RemainingKg is null
            || lineage.NetShippedKg is null || lineage.NetShippedKg.Sign < 0
            || lineage.RemainingKg.Add(lineage.NetShippedKg).CompareTo(ExactCostRatio.Of(acceptedKg)) != 0)
            throw Unavailable("purchase_invoice_cost_first_physical_source_unavailable");
        if (lineage.Labels.Where(x => x.RootShare.Sign > 0).SelectMany(x => x.Positions)
            .Any(x => x.QuantityKg > 0m && (x.Owner.Kind != StockPartyKind.Company || x.Owner.Id != source.CompanyId)))
            throw Unavailable("purchase_invoice_cost_first_owner_unavailable");
        foreach (var label in lineage.Labels.Where(x => x.RootShare.Sign > 0).OrderBy(x => x.LabelId))
        {
            var actual = await db.Labels.SingleAsync(x => x.Id == label.LabelId, token);
            if (db.Entry(actual).OriginalValues.GetValue<long>(nameof(LabelRow.Version)) != label.Version
                || actual.ProductId != share.ProductId || actual.ProductVersion != label.ProductVersion)
                throw Unavailable("purchase_invoice_cost_first_physical_source_changed");
            if (firstCostFencedLabels.Add(label.LabelId))
            {
                actual.Version = checked(actual.Version + 1);
                actual.UpdatedAtUtc = clock.GetUtcNow();
            }
        }
        var physical = lineage.Labels.Where(x => x.RootShare.Sign > 0)
            .SelectMany(label => label.Positions.Where(x => x.QuantityKg > 0m).Select(x => new
            {
                x.LocationId,
                Included = x.Owner.Kind == StockPartyKind.Company && x.Owner.Id == source.CompanyId
                    && x.Custodian.Kind == StockPartyKind.Company && x.Custodian.Id == source.CompanyId,
                Quantity = label.RootShare.Multiply(x.QuantityKg)
            })).GroupBy(x => (x.LocationId, x.Included)).ToArray();
        // Separate custody classes at one location cannot be conflated into a cost-pool share.
        if (physical.Select(x => x.Key.LocationId).Distinct().Count() != physical.Length)
            throw Unavailable("purchase_invoice_cost_first_position_ambiguous");
        var prerequisites = await firstCostPrerequisites.ReadAsync(source.CompanyId, source.InvoiceDate,
            physical.Select(x => x.Key.LocationId).ToArray(), token);
        RequireCompany(source.CompanyId);
        if (prerequisites is null) throw Unavailable("purchase_invoice_cost_first_profile_unavailable");
        var profile = prerequisites.Profile;
        if (profile is null || profile.CompanyId != source.CompanyId || profile.CurrencyId != Tl
            || profile.Operation != "accounting.journal-amount.v1" || profile.Profile is not { Version: > 0 } identity
            || identity.ProfileId == Guid.Empty || profile.Scale is < 0 or > 28 || !profile.IsActive
            || profile.EffectiveFrom > source.InvoiceDate || profile.EffectiveThrough is { } through && through < source.InvoiceDate
            || prerequisites.LocationCodes.Count != physical.Length
            || physical.Any(x => !prerequisites.LocationCodes.ContainsKey(x.Key.LocationId)))
            throw Unavailable("purchase_invoice_cost_first_profile_unavailable");
        var stockShares = physical.Select(x => new PurchaseFirstCostStockShare(x.Key.LocationId.ToString("D"),
            prerequisites.LocationCodes[x.Key.LocationId], share.InvoiceLineNo,
            x.Aggregate(ExactCostRatio.Of(0m), (sum, position) => sum.Add(position.Quantity)))).ToArray();
        var invoicedKg = ExactCostRatio.Of(acceptedKg).Multiply(share.MatchedQuantity).Divide(share.AcceptedQuantity);
        var split = PurchaseFirstCostAllocationCalculator.Allocate(share.TlNetShare.Value(), ExactCostRatio.Of(acceptedKg),
            invoicedKg, lineage.NetShippedKg, stockShares, profile.Scale);
        var positions = new List<InventoryPurchaseFirstCostPositionV1>();
        foreach (var allocated in split.Stock)
        {
            var location = Guid.Parse(allocated.Key);
            var included = physical.Single(x => x.Key.LocationId == location).Key.Included;
            var actualEffect = ExactCostRatio.Of(0m);
            long version = 0;
            if (included)
            {
                var pool = await InventoryStore.ReadCostPoolAsync(db, company, location, share.ProductId, Tl, token);
                RequireCompany(source.CompanyId);
                if (!pool.IsConsistent) throw Unavailable("purchase_invoice_cost_first_pool_unavailable");
                var before = pool.ExactPosition ?? new ExactAverageCostPool(ExactCostRatio.Of(0m), 0m);
                var after = before.Add(allocated.InvoicedQuantityKg, allocated.TlValue);
                if (after.AverageCostPerKg >= 100_000_000_000_000m) throw Unavailable("purchase_invoice_cost_pool_overflow");
                version = await InventoryStore.WriteCostPoolAsync(db, company, clock, location, share.ProductId, Tl, pool, after, token);
                actualEffect = after.Value.Subtract(before.Value);
            }
            positions.Add(new(location, prerequisites.LocationCodes[location], Ratio(allocated.InvoicedQuantityKg),
                Ratio(allocated.TlValue), Ratio(actualEffect), Ratio(included ? actualEffect.Subtract(allocated.TlValue) : ExactCostRatio.Of(0m)), version, included));
        }
        var physicalSource = new InventoryPurchaseFirstCostPhysicalSourceV1(PurchaseFirstCostAllocationCalculator.Version,
            source.CompanyId, share.AcceptanceId, share.InvoiceLineId, share.InvoiceLineNo, movementId, rootLabelId,
            share.ProductId, share.MatchedQuantity, share.AcceptedQuantity, "receipt-root-proportional",
            Ratio(lineage.RemainingKg), Ratio(lineage.NetShippedKg), lineage.Labels.Where(x => x.RootShare.Sign > 0)
                .Select(label => new InventoryPurchaseFirstCostLabelV1(label.LabelId, label.Version, checked(label.Version + 1),
                    label.ProductVersion, Ratio(label.RootShare), label.Positions.Select(position =>
                        new InventoryPurchaseFirstCostPhysicalPositionV1(position.Owner.Kind, position.Owner.Id,
                            position.Custodian.Kind, position.Custodian.Id, position.LocationId, position.Block, position.QuantityKg)).ToArray())).ToArray(),
            lineage.Shipments.Select(shipment => new InventoryPurchaseFirstCostShipmentV1(shipment.ShipmentId, shipment.MovementId,
                shipment.LineNumber, shipment.LoadingInstructionLineId, shipment.LabelId, shipment.ProductId, shipment.ProductVersion,
                shipment.LocationId, shipment.QuantityKg, Ratio(shipment.RootShare), Ratio(shipment.NetRootQuantityKg),
                shipment.Returns.Select(item => new InventoryPurchaseFirstCostReturnV1(item.ReturnId, item.MovementId,
                    item.LineNumber, item.SourceIssueMovementId, item.QuantityKg, item.RecordedAtUtc)).ToArray(), shipment.RecordedAtUtc)).ToArray());
        var physicalJson = JsonSerializer.Serialize(physicalSource, Json);
        return new(PurchaseFirstCostAllocationCalculator.Version, share.AcceptanceId, share.InvoiceLineId, share.InvoiceLineNo,
            movementId, rootLabelId, share.ProductId, receiptLocationId, share.MatchedQuantity, share.AcceptedQuantity, Ratio(ExactCostRatio.Of(acceptedKg)),
            Ratio(invoicedKg), share.TlNetShare, Ratio(split.ConsumedQuantityKg), split.ConsumedTlAmount,
            Ratio(split.ConsumedExactTlAmount), Ratio(split.ConsumedRoundingResidue), profile.CurrencyId, identity.ProfileId, identity.Version,
            profile.Scale, source.InvoiceDate, physicalJson, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(physicalJson))), positions);
    }
}
