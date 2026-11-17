using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.AccountingRuntime;

public static partial class AccountingPurchaseInvoiceCostSourceV1
{
    private static AccountingInventoryCostRatioV1 ReadFirstRecognitions(InventoryPurchaseInvoiceCostApplicationResultV1 source,
        HashSet<(Guid, Guid)> receiptIds, List<AccountingInventoryCostComponentV1> components, AccountingInventoryCostRatioV1 sum)
    {
        var lineIds = new HashSet<Guid>();
        foreach (var line in source.InitialRecognitions ?? [])
        {
            if (line is null || line.AllocationVersion != PurchaseFirstCostAllocationCalculator.Version
                || line.InvoiceLineId == Guid.Empty || !lineIds.Add(line.InvoiceLineId) || line.InvoiceLineNo < 1
                || line.ProductId == Guid.Empty || line.Receipts is not { Count: > 0 and <= 10000 }
                || line.Positions is not { Count: <= 65536 } || line.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
                || line.MoneyProfileId == Guid.Empty || line.MoneyProfileVersion < 1 || line.MoneyScale is < 0 or > 28
                || line.MoneyEffectiveOn != source.InvoiceDate || line.ConsumedTlAmount < 0) throw Invalid();
            var zero = ExactCostRatio.Of(0m);
            var invoice = zero; var kg = zero; var consumedKg = zero; var consumed = zero;
            var roots = new HashSet<Guid>(); var movements = new HashSet<Guid>();
            var expectedPositions = new Dictionary<Guid, (ExactCostRatio Kg, bool Included)>();
            var hasActualReturnedShipment = false;
            foreach (var receipt in line.Receipts)
            {
                if (receipt is null || receipt.AcceptanceId == Guid.Empty || receipt.ReceiptMovementId == Guid.Empty
                    || receipt.RootLabelId == Guid.Empty || receipt.ReceiptLocationId == Guid.Empty
                    || !roots.Add(receipt.RootLabelId) || !movements.Add(receipt.ReceiptMovementId)
                    || !receiptIds.Add((line.InvoiceLineId, receipt.AcceptanceId))
                    || receipt.MatchedQuantity <= 0 || receipt.AcceptedQuantity < receipt.MatchedQuantity
                    || !Hash(receipt.PhysicalSourceSha256) || string.IsNullOrWhiteSpace(receipt.PhysicalSourceJson)
                    || AccountingInventoryCostSourceValidationV1.Sha256(receipt.PhysicalSourceJson) != receipt.PhysicalSourceSha256)
                    throw Invalid();
                InventoryPurchaseFirstCostPhysicalSourceV1? physical;
                try { physical = JsonSerializer.Deserialize<InventoryPurchaseFirstCostPhysicalSourceV1>(receipt.PhysicalSourceJson, Json); }
                catch (JsonException) { throw Invalid(); }
                if (physical is null || physical.SourceVersion != line.AllocationVersion || physical.CompanyId != source.CompanyId
                    || physical.AcceptanceId != receipt.AcceptanceId || physical.InvoiceLineId != line.InvoiceLineId
                    || physical.InvoiceLineNo != line.InvoiceLineNo || physical.ReceiptMovementId != receipt.ReceiptMovementId
                    || physical.RootLabelId != receipt.RootLabelId || physical.ProductId != line.ProductId
                    || physical.MatchedQuantity != receipt.MatchedQuantity || physical.AcceptedQuantity != receipt.AcceptedQuantity
                    || physical.AllocationBasis != "receipt-root-proportional" || physical.Labels is not { Count: > 0 and <= 65536 }
                    || physical.Shipments is not { Count: <= 65536 }) throw Invalid();
                var accepted = Value(receipt.AcceptedPhysicalKg); var invoiced = Value(receipt.InvoicedPhysicalKg);
                var net = Value(physical.NetShippedRootQuantityKg); var remaining = Value(physical.RemainingRootQuantityKg);
                var receiptValue = Value(receipt.InvoiceTlValue);
                if (accepted.Sign <= 0 || invoiced.Sign <= 0 || net.Sign < 0 || remaining.Sign < 0 || receiptValue.Sign < 0
                    || net.Add(remaining).CompareTo(accepted) != 0
                    || accepted.Multiply(receipt.MatchedQuantity).Divide(receipt.AcceptedQuantity).CompareTo(invoiced) != 0
                    || net.Multiply(receipt.MatchedQuantity).Divide(receipt.AcceptedQuantity).CompareTo(Value(receipt.ConsumedQuantityKg)) != 0
                    || receiptValue.Multiply(net).Divide(accepted).CompareTo(Value(receipt.ConsumedExactTlAmount)) != 0)
                    throw Invalid();
                RequirePhysicalTotals(source.CompanyId, line.ProductId, physical, remaining, net);
                hasActualReturnedShipment |= physical.Shipments.Any(shipment => shipment.PhysicalQuantityKg > 0
                    && Value(shipment.NetRootQuantityKg).Sign == 0 && shipment.Returns.Count > 0);
                var fraction = ExactCostRatio.Of(receipt.MatchedQuantity).Divide(receipt.AcceptedQuantity);
                foreach (var label in physical.Labels)
                foreach (var position in label.Positions.Where(x => x.QuantityKg > 0))
                {
                    var quantity = Value(label.RootShare).Multiply(position.QuantityKg).Multiply(fraction);
                    var included = position.CustodianKind == StockPartyKind.Company && position.CustodianId == source.CompanyId;
                    if (expectedPositions.TryGetValue(position.LocationId, out var prior))
                    {
                        if (prior.Included != included) throw Invalid();
                        expectedPositions[position.LocationId] = (prior.Kg.Add(quantity), included);
                    }
                    else expectedPositions.Add(position.LocationId, (quantity, included));
                }
                invoice = invoice.Add(receiptValue); kg = kg.Add(invoiced);
                consumedKg = consumedKg.Add(Value(receipt.ConsumedQuantityKg));
                consumed = consumed.Add(Value(receipt.ConsumedExactTlAmount));
            }
            if (invoice.CompareTo(Value(line.InvoiceTlValue)) != 0 || kg.CompareTo(Value(line.InvoicedPhysicalKg)) != 0
                || consumedKg.CompareTo(Value(line.ConsumedQuantityKg)) != 0
                || consumed.CompareTo(Value(line.ConsumedExactTlAmount)) != 0
                || consumed.Subtract(ExactCostRatio.Of(line.ConsumedTlAmount)).CompareTo(Value(line.ConsumedRoundingResidue)) != 0)
                throw Invalid();
            var locations = new HashSet<Guid>(); var positionKg = zero; var positionValue = zero;
            foreach (var position in line.Positions)
            {
                if (position is null || position.LocationId == Guid.Empty || !locations.Add(position.LocationId)
                    || string.IsNullOrWhiteSpace(position.LocationCode) || position.PoolVersion < 0
                    || Value(position.InvoicedQuantityKg).Sign <= 0 || Value(position.TlValue).Sign < 0
                    || !expectedPositions.TryGetValue(position.LocationId, out var expectedPosition)
                    || expectedPosition.Included != position.IncludedInCompanyPool
                    || expectedPosition.Kg.CompareTo(Value(position.InvoicedQuantityKg)) != 0) throw Invalid();
                if (position.IncludedInCompanyPool)
                {
                    if (Value(position.TlValue).Add(Value(position.AverageRoundingResidue))
                        .CompareTo(Value(position.RecordedPoolValueEffect)) != 0) throw Invalid();
                }
                else if (position.PoolVersion != 0 || Value(position.RecordedPoolValueEffect).Sign != 0
                    || Value(position.AverageRoundingResidue).Sign != 0) throw Invalid();
                positionKg = positionKg.Add(Value(position.InvoicedQuantityKg)); positionValue = positionValue.Add(Value(position.TlValue));
            }
            if (locations.Count != expectedPositions.Count || positionKg.Add(consumedKg).CompareTo(kg) != 0
                || (positionKg.Sign > 0 ? positionValue.Add(ExactCostRatio.Of(line.ConsumedTlAmount)).CompareTo(invoice) != 0
                    : positionValue.Sign != 0 || consumed.CompareTo(invoice) != 0)) throw Invalid();
            var amount = AccountingInventoryCostAmountsV1.FromDecimal(line.ConsumedTlAmount);
            var rounding = new AccountingInventoryCostRecordedRoundingV1(line.CurrencyId, line.MoneyProfileId,
                line.MoneyProfileVersion, line.MoneyScale, new(Ratio(line.ConsumedExactTlAmount),
                    line.ConsumedTlAmount.ToString("0.############################", CultureInfo.InvariantCulture),
                    Ratio(line.ConsumedRoundingResidue), AccountingInventoryCostRoundingV1Rules.Version));
            if (!AccountingInventoryCostRoundingV1Rules.IsValidRecorded(amount, rounding)) throw Invalid();
            sum = AccountingInventoryCostAmountsV1.Add(sum, amount);
            if (consumed.Sign == 0 && !hasActualReturnedShipment) continue;
            var proof = JsonSerializer.Serialize(new { source.CompanyId, source.InvoiceId, source.InvoiceVersion, source.SourceEventId,
                source.AdmissionId, source.InvoiceDate, source.OfficialDocument, source.SourceSha256, source.CostDifferenceEventId, line }, Json);
            components.Add(new(AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(line.InvoiceLineId), line.ProductId,
                null, null, null, amount, proof, AccountingInventoryCostSourceValidationV1.Sha256(proof), RecordedRounding: rounding));
        }
        return sum;
    }

    private static ExactCostRatio Value(InventoryExactCostRatioV1 value) { _ = Ratio(value); return value.Value(); }

    private static void RequirePhysicalTotals(Guid companyId, Guid productId,
        InventoryPurchaseFirstCostPhysicalSourceV1 physical, ExactCostRatio remaining, ExactCostRatio shipped)
    {
        var labels = new HashSet<Guid>(); var issues = new HashSet<(Guid, int)>();
        var actualRemaining = ExactCostRatio.Of(0m); var actualShipped = ExactCostRatio.Of(0m);
        foreach (var label in physical.Labels)
        {
            if (label is null || label.LabelId == Guid.Empty || !labels.Add(label.LabelId) || label.VersionBefore < 1
                || label.VersionBefore == long.MaxValue || label.VersionAfter != label.VersionBefore + 1
                || string.IsNullOrWhiteSpace(label.ProductVersion) || Value(label.RootShare).Sign <= 0
                || Value(label.RootShare).CompareTo(ExactCostRatio.Of(1m)) > 0 || label.Positions is not { Count: <= 65536 }) throw Invalid();
            foreach (var position in label.Positions)
            {
                if (position is null || position.OwnerKind != StockPartyKind.Company || position.OwnerId != companyId
                    || position.LocationId == Guid.Empty || position.QuantityKg < 0 || position.CustodianId == Guid.Empty
                    || !Enum.IsDefined(position.CustodianKind) || !Enum.IsDefined(position.Block)) throw Invalid();
                actualRemaining = actualRemaining.Add(Value(label.RootShare).Multiply(position.QuantityKg));
            }
        }
        foreach (var shipment in physical.Shipments)
        {
            if (shipment is null || shipment.ShipmentId == Guid.Empty || shipment.MovementId == Guid.Empty
                || shipment.MovementLineNumber < 1 || !issues.Add((shipment.MovementId, shipment.MovementLineNumber))
                || shipment.ProductId != productId || shipment.LabelId == Guid.Empty || shipment.LoadingInstructionLineId == Guid.Empty
                || shipment.LocationId == Guid.Empty || shipment.PhysicalQuantityKg <= 0 || shipment.RecordedAtUtc == default
                || shipment.RecordedAtUtc.Offset != TimeSpan.Zero || shipment.Returns is not { Count: <= 65536 }
                || Value(shipment.RootShare).Sign <= 0 || Value(shipment.NetRootQuantityKg).Sign < 0) throw Invalid();
            if (shipment.Returns.Any(x => x is null) || shipment.Returns.Select(x => x.ReturnId).Distinct().Count() != shipment.Returns.Count) throw Invalid();
            var returned = shipment.Returns.Sum(x => x.QuantityKg);
            if (shipment.Returns.Any(x => x.ReturnId == Guid.Empty || x.MovementId == Guid.Empty || x.MovementLineNumber < 1
                || x.SourceIssueMovementId != shipment.MovementId || x.QuantityKg <= 0 || x.RecordedAtUtc == default)
                || returned > shipment.PhysicalQuantityKg
                || Value(shipment.RootShare).Multiply(shipment.PhysicalQuantityKg - returned).CompareTo(Value(shipment.NetRootQuantityKg)) != 0)
                throw Invalid();
            actualShipped = actualShipped.Add(Value(shipment.NetRootQuantityKg));
        }
        if (actualRemaining.CompareTo(remaining) != 0 || actualShipped.CompareTo(shipped) != 0) throw Invalid();
    }
}
