namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Historical physical facts only. Monetary amounts come exclusively from the actual posted Accounting source.</summary>
public sealed record ManufacturingAttributionShipmentV1(Guid ShipmentId, Guid MovementId, int MovementLineNumber,
    Guid LoadingInstructionLineId, Guid ProductId, Guid LabelId, decimal PhysicalQuantityKg,
    AccountingInventoryCostRatioV1 RootShare, AccountingInventoryCostRatioV1 NetRootQuantityKg,
    DateTimeOffset RecordedAtUtc, long? ProvenOriginalSequence, bool HasReturns, string PhysicalProofJson);
public sealed record ManufacturingAttributionOutputV1(Guid OutputId, Guid ReceiptMovementId, Guid LabelId,
    decimal NetQuantityKg, AccountingInventoryCostRatioV1 ConsumedRootQuantityKg, string LineageSha256,
    IReadOnlyList<ManufacturingAttributionShipmentV1> Shipments);
public sealed record ManufacturingAttributionLineageV1(Guid CompanyId, Guid SourceEventId, Guid GenerationId,
    string GenerationVersion, string HistorySha256, IReadOnlyList<ManufacturingAttributionOutputV1> Outputs);

public interface IManufacturingAttributionLineageReaderV1
{
    Task<ManufacturingAttributionLineageV1> ReadAsync(ManufacturingCostDifferenceEventV1 source,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}

/// <summary>One real output's signed money share; the original aggregate and its selected accounts remain unchanged.</summary>
public sealed record PostedManufacturingCostShareV1(Guid CompanyId, Guid SourceEventId, Guid OutputId,
    Guid GenerationId, string GenerationVersion, Guid ReceiptMovementId, Guid JournalId, Guid CurrencyId,
    Guid CogsAccountId, Guid CounterAccountId, string JournalTotal, string SignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedAmount, string SignedRoundingResidue,
    Guid MoneyProfileId, long MoneyProfileVersion, int MoneyScale, DateOnly ProfileAccountingDate,
    ManufacturingAttributionShipmentV1 Shipment, string SourcePayloadSha256, string LineageSha256,
    string HistorySha256, string Fingerprint);

/// <summary>Original unknown cost can be resolved only by whole physical coverage of already posted source components.</summary>
public sealed record PostedManufacturingShipmentCostV1(Guid CompanyId, Guid ShipmentId, Guid MovementId,
    int MovementLineNumber, Guid ProductId, decimal QuantityKg, IReadOnlyList<PostedManufacturingCostShareV1> Shares);
public interface IPostedManufacturingShipmentCostReaderV1
{
    Task<PostedManufacturingShipmentCostV1?> ReadAsync(Guid companyId, Guid shipmentId, Guid movementId,
        int movementLineNumber, Guid productId, decimal quantityKg,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
