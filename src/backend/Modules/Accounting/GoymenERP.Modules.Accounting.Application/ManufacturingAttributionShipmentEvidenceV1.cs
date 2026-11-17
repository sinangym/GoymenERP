namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Validates the captured physical return facts. It does not value a return or create a financial reversal.</summary>
public static class ManufacturingAttributionShipmentEvidenceV1
{
    public static bool IsValid(ManufacturingAttributionShipmentV1? shipment)
    {
        if (shipment is null || shipment.ShipmentId == Guid.Empty || shipment.MovementId == Guid.Empty
            || shipment.MovementLineNumber < 1 || shipment.LoadingInstructionLineId == Guid.Empty
            || shipment.ProductId == Guid.Empty || shipment.LabelId == Guid.Empty || shipment.PhysicalQuantityKg <= 0
            || shipment.RecordedAtUtc == default || shipment.RecordedAtUtc.Offset != TimeSpan.Zero
            || shipment.ProvenOriginalSequence is < 1 || string.IsNullOrWhiteSpace(shipment.PhysicalProofJson)
            || !AccountingInventoryCostAmountsV1.TryRead(shipment.RootShare, out var rootN, out var rootD)
            || rootN <= 0 || rootN > rootD
            || !AccountingInventoryCostAmountsV1.TryRead(shipment.NetRootQuantityKg, out var netN, out _)
            || netN < 0 || shipment.HasReturns != (shipment.Returns is { Count: > 0 })
            || shipment.Returns?.Count > 65536) return false;
        var returned = AccountingInventoryCostAmountsV1.FromDecimal(0m);
        var ids = new HashSet<Guid>();
        var movements = new HashSet<(Guid, int)> { (shipment.MovementId, shipment.MovementLineNumber) };
        foreach (var item in shipment.Returns ?? [])
        {
            if (item is null || item.ReturnId == Guid.Empty || !ids.Add(item.ReturnId) || item.MovementId == Guid.Empty
                || item.MovementLineNumber < 1 || !movements.Add((item.MovementId, item.MovementLineNumber))
                || item.SourceIssueMovementId != shipment.MovementId || item.QuantityKg <= 0
                || item.RecordedAtUtc == default || item.RecordedAtUtc.Offset != TimeSpan.Zero) return false;
            returned = AccountingInventoryCostAmountsV1.Add(returned, AccountingInventoryCostAmountsV1.FromDecimal(item.QuantityKg));
        }
        var net = AccountingInventoryCostAmountsV1.Add(AccountingInventoryCostAmountsV1.FromDecimal(shipment.PhysicalQuantityKg),
            AccountingInventoryCostAmountsV1.Multiply(returned, AccountingInventoryCostAmountsV1.FromDecimal(-1m)));
        return AccountingInventoryCostAmountsV1.TryRead(net, out var remaining, out _) && remaining >= 0
            && AccountingInventoryCostAmountsV1.Multiply(net, shipment.RootShare) == shipment.NetRootQuantityKg;
    }
}
