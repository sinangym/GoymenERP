using GoymenERP.BuildingBlocks.Messaging;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Actual purchase identities below one already rounded invoice-line component. No production identity is fabricated.</summary>
public sealed record PurchaseFirstCostLineageV1(Guid CompanyId, Guid SourceEventId, Guid InvoiceId,
    long InvoiceVersion, Guid AdmissionId, Guid InvoiceLineId, int InvoiceLineNumber, Guid ProductId,
    string ComponentKey, string SourceProofSha256, AccountingInventoryCostRecordedRoundingV1 RecordedRounding,
    IReadOnlyList<PurchaseFirstCostReceiptV1> Receipts);

public sealed record PurchaseFirstCostReceiptV1(Guid AcceptanceId, Guid ReceiptMovementId, Guid RootLabelId,
    Guid ReceiptLocationId, AccountingInventoryCostRatioV1 InvoicedFraction,
    AccountingInventoryCostRatioV1 ConsumedQuantityKg, AccountingInventoryCostRatioV1 ConsumedExactTlAmount,
    string PhysicalSourceSha256, IReadOnlyList<PurchaseFirstCostShipmentV1> Shipments);

/// <summary>Net root quantity is before the invoice fraction. Physical quantity and returns remain the immutable owner facts.</summary>
public sealed record PurchaseFirstCostShipmentV1(Guid ShipmentId, Guid MovementId, int MovementLineNumber,
    Guid LoadingInstructionLineId, Guid ProductId, Guid LabelId, decimal PhysicalQuantityKg,
    AccountingInventoryCostRatioV1 RootShare, AccountingInventoryCostRatioV1 NetRootQuantityKg,
    DateTimeOffset RecordedAtUtc, long? ProvenOriginalSequence,
    IReadOnlyList<PurchaseFirstCostReturnV1> Returns, string PhysicalProofJson);

public sealed record PurchaseFirstCostReturnV1(Guid ReturnId, Guid MovementId, int MovementLineNumber,
    Guid SourceIssueMovementId, decimal QuantityKg, DateTimeOffset RecordedAtUtc);

/// <summary>Normalizes the accepted immutable owner proof; never looks up today's graph or invents a source for old evidence.</summary>
public interface IPurchaseFirstCostLineageReaderV1
{
    Task<PurchaseFirstCostLineageV1> ReadAsync(WorkInboxEventV1 envelope, AccountingInventoryCostSourceV1 source,
        AccountingInventoryCostComponentV1 component, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default);
}

/// <summary>Indexed exact candidate only. It is not a posted amount or permission to create another journal.</summary>
public sealed record PurchaseFirstCostCandidateV1(Guid CompanyId, Guid SourceEventId, Guid InvoiceLineId,
    Guid AcceptanceId, Guid ReceiptMovementId, Guid RootLabelId, Guid ProductId,
    AccountingInventoryCostRatioV1 InvoicedRootShare, AccountingInventoryCostRatioV1 InvoicedNetQuantityKg,
    AccountingInventoryCostRatioV1 ExactTlAmount, PurchaseFirstCostShipmentV1 Shipment,
    string SourceProofSha256, string PhysicalSourceSha256, string Fingerprint);

/// <summary>A real human-approved source. RoundedNoCost has no journal; its exact source and different approver remain evidence.</summary>
public sealed record PurchaseFirstCostApprovalV1(Guid CompanyId, Guid SourceEventId, Guid InvoiceLineId,
    Guid ProposalId, long ProposalVersion, Guid? JournalId, string Status, string SignedAmount,
    Guid CurrencyId, Guid MoneyProfileId, long MoneyProfileVersion, int MoneyScale,
    Guid CogsAccountId, Guid CounterAccountId, DateOnly AccountingDate,
    Guid PreparedBy, Guid ApprovedBy, DateTimeOffset ApprovedAtUtc,
    AccountingInventoryCostRecordedRoundingV1 RecordedRounding, string SourceProofSha256,
    string Fingerprint);

public sealed record PostedPurchaseCostShareV1(PurchaseFirstCostApprovalV1 Approval,
    Guid ShipmentId, Guid MovementId, int MovementLineNumber, Guid ProductId, decimal PhysicalQuantityKg,
    string SignedAmount, AccountingInventoryCostRatioV1 ExactSignedAmount,
    AccountingInventoryCostRatioV1 ExactSignedResidue, IReadOnlyList<PurchaseFirstCostCandidateV1> Candidates,
    string Fingerprint);

public sealed record PurchaseFirstCostPostedAttributionV1(PurchaseFirstCostApprovalV1 Approval,
    IReadOnlyList<PostedPurchaseCostShareV1> Shares);

/// <summary>Proves individual purchase roots only; the consumer must prove full physical coverage across all actual source kinds.</summary>
public sealed record PurchaseShipmentCostAttributionReadV1(Guid CompanyId, Guid ShipmentId, Guid MovementId,
    int MovementLineNumber, Guid ProductId, decimal QuantityKg, bool HasPendingCandidate,
    IReadOnlyList<PostedPurchaseCostShareV1> Shares);

public interface IPostedPurchaseShipmentCostReaderV1
{
    Task<PurchaseShipmentCostAttributionReadV1> ReadProvenSharesAsync(Guid companyId, Guid shipmentId, Guid movementId,
        int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default);
}
