using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Exact physical attribution candidates. This does not allocate a rounded journal amount or choose a remainder recipient.</summary>
public static class PurchaseFirstCostCandidatesV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly AccountingInventoryCostRatioV1 Zero = new("0", "1");
    private static readonly AccountingInventoryCostRatioV1 One = new("1", "1");

    public static IReadOnlyList<PurchaseFirstCostCandidateV1> Build(PurchaseFirstCostLineageV1 source)
    {
        if (source.CompanyId == Guid.Empty || source.SourceEventId == Guid.Empty || source.InvoiceId == Guid.Empty
            || source.InvoiceVersion < 1 || source.AdmissionId == Guid.Empty || source.InvoiceLineId == Guid.Empty
            || source.InvoiceLineNumber < 1 || source.ProductId == Guid.Empty || source.Receipts is not { Count: > 0 and <= 10000 }
            || source.ComponentKey != AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(source.InvoiceLineId)
            || !Hash(source.SourceProofSha256) || source.RecordedRounding is not { } recorded
            || !decimal.TryParse(recorded.Rounding.RoundedSignedAmount, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var rounded)
            || !AccountingInventoryCostRoundingV1Rules.IsValidRecorded(AccountingInventoryCostAmountsV1.FromDecimal(rounded), recorded)) throw Invalid();
        var receiptIds = new HashSet<Guid>(); var roots = new HashSet<Guid>(); var receipts = new HashSet<Guid>();
        var result = new List<PurchaseFirstCostCandidateV1>(); var total = Zero;
        foreach (var receipt in source.Receipts)
        {
            if (receipt.AcceptanceId == Guid.Empty || !receiptIds.Add(receipt.AcceptanceId)
                || receipt.ReceiptMovementId == Guid.Empty || !receipts.Add(receipt.ReceiptMovementId)
                || receipt.RootLabelId == Guid.Empty || !roots.Add(receipt.RootLabelId) || receipt.ReceiptLocationId == Guid.Empty
                || !Hash(receipt.PhysicalSourceSha256) || receipt.Shipments is not { Count: <= 65536 }
                || Sign(receipt.InvoicedFraction) <= 0 || Compare(receipt.InvoicedFraction, One) > 0
                || Sign(receipt.ConsumedQuantityKg) < 0 || Sign(receipt.ConsumedExactTlAmount) < 0) throw Invalid();
            var physicalKeys = new HashSet<(Guid, int)>(); var net = Zero;
            foreach (var shipment in receipt.Shipments)
            {
                RequireShipment(shipment, source.ProductId);
                if (!physicalKeys.Add((shipment.MovementId, shipment.MovementLineNumber))) throw Invalid();
                net = Add(net, shipment.NetRootQuantityKg);
            }
            if (Multiply(net, receipt.InvoicedFraction) != receipt.ConsumedQuantityKg
                || net == Zero && receipt.ConsumedExactTlAmount != Zero) throw Invalid();
            var assigned = Zero;
            foreach (var shipment in receipt.Shipments)
            {
                var exact = net == Zero ? Zero : Multiply(receipt.ConsumedExactTlAmount, Divide(shipment.NetRootQuantityKg, net));
                var candidate = new PurchaseFirstCostCandidateV1(source.CompanyId, source.SourceEventId, source.InvoiceLineId,
                    receipt.AcceptanceId, receipt.ReceiptMovementId, receipt.RootLabelId, source.ProductId,
                    Multiply(shipment.RootShare, receipt.InvoicedFraction), Multiply(shipment.NetRootQuantityKg, receipt.InvoicedFraction),
                    exact, shipment, source.SourceProofSha256, receipt.PhysicalSourceSha256, "");
                candidate = candidate with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(candidate, Json)) };
                result.Add(candidate); assigned = Add(assigned, exact);
            }
            if (assigned != receipt.ConsumedExactTlAmount) throw Invalid();
            total = Add(total, assigned);
        }
        if (total != recorded.Rounding.ExactSignedAmount) throw Invalid();
        return result.OrderBy(x => x.AcceptanceId).ThenBy(x => x.Shipment.MovementId).ThenBy(x => x.Shipment.MovementLineNumber).ToArray();
    }

    private static void RequireShipment(PurchaseFirstCostShipmentV1 value, Guid productId)
    {
        if (value.ShipmentId == Guid.Empty || value.MovementId == Guid.Empty || value.MovementLineNumber < 1
            || value.LoadingInstructionLineId == Guid.Empty || value.ProductId != productId || value.LabelId == Guid.Empty
            || value.PhysicalQuantityKg <= 0 || Sign(value.RootShare) <= 0 || Compare(value.RootShare, One) > 0
            || Sign(value.NetRootQuantityKg) < 0 || value.RecordedAtUtc == default || value.RecordedAtUtc.Offset != TimeSpan.Zero
            || value.ProvenOriginalSequence is <= 0 || value.Returns is not { Count: <= 65536 }
            || string.IsNullOrWhiteSpace(value.PhysicalProofJson)) throw Invalid();
        var returned = Zero; var keys = new HashSet<Guid>(); var movementKeys = new HashSet<(Guid, int)>();
        foreach (var item in value.Returns)
        {
            if (item.ReturnId == Guid.Empty || !keys.Add(item.ReturnId) || item.MovementId == Guid.Empty
                || item.MovementLineNumber < 1 || !movementKeys.Add((item.MovementId, item.MovementLineNumber))
                || item.SourceIssueMovementId != value.MovementId || item.QuantityKg <= 0
                || item.RecordedAtUtc < value.RecordedAtUtc || item.RecordedAtUtc.Offset != TimeSpan.Zero) throw Invalid();
            returned = Add(returned, AccountingInventoryCostAmountsV1.FromDecimal(item.QuantityKg));
        }
        var gross = AccountingInventoryCostAmountsV1.FromDecimal(value.PhysicalQuantityKg);
        if (Compare(returned, gross) > 0 || Multiply(Add(gross, Multiply(returned, new("-1", "1"))), value.RootShare) != value.NetRootQuantityKg)
            throw Invalid();
    }

    private static AccountingInventoryCostRatioV1 Divide(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right)
    {
        if (!AccountingInventoryCostAmountsV1.TryRead(left, out var n, out var d)
            || !AccountingInventoryCostAmountsV1.TryRead(right, out var rn, out var rd) || rn <= 0) throw Invalid();
        n *= rd; d *= rn; var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
        return new((n / gcd).ToString(CultureInfo.InvariantCulture), (d / gcd).ToString(CultureInfo.InvariantCulture));
    }
    private static int Sign(AccountingInventoryCostRatioV1 ratio) => AccountingInventoryCostAmountsV1.TryRead(ratio, out var n, out _) ? n.Sign : throw Invalid();
    private static int Compare(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right)
    {
        if (!AccountingInventoryCostAmountsV1.TryRead(left, out var n, out var d)
            || !AccountingInventoryCostAmountsV1.TryRead(right, out var rn, out var rd)) throw Invalid();
        return (n * rd).CompareTo(rn * d);
    }
    private static AccountingInventoryCostRatioV1 Add(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right) => AccountingInventoryCostAmountsV1.Add(left, right);
    private static AccountingInventoryCostRatioV1 Multiply(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right) => AccountingInventoryCostAmountsV1.Multiply(left, right);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static AccountingStoreException Invalid() => new("purchase_cost_attribution_source_unavailable", "İlk alış maliyetinin kesin fiziksel payları doğrulanamadı.");
}
