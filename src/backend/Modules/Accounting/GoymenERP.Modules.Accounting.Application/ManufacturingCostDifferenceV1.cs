using GoymenERP.BuildingBlocks.Messaging;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Frozen Inventory amounts, not an instruction to recalculate old issues or to post without human approval.</summary>
public sealed record ManufacturingCostDifferenceOutputV1(Guid OutputId, Guid ReceiptMovementId, Guid LabelId,
    decimal NetQuantityKg, decimal? RemainingQuantityKg, decimal? ConsumedQuantityKg,
    decimal? TlUnitCostPerKg, decimal? RemainingTlValue, decimal? ConsumedTlDifference,
    decimal? UsdUnitCostPerKg, IReadOnlyList<ManufacturingCostDifferencePositionV1> Positions,
    decimal? OriginalTlAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingInventoryCostRatioV1? RemainingQuantityKgRatio = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingInventoryCostRatioV1? ConsumedQuantityKgRatio = null);

public sealed record ManufacturingCostDifferencePositionV1(Guid LocationId, decimal? QuantityKg, decimal? TlValue,
    bool IncludedInMovingAverage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingInventoryCostRatioV1? QuantityKgRatio = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AccountingInventoryCostRatioV1? TlValueRatio = null);

public sealed record ManufacturingCostDifferenceEventV1(Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string GenerationVersion, string ClosedOrderVersion, int State, string InputFingerprint,
    IReadOnlyList<ManufacturingCostDifferenceOutputV1> Outputs, Guid? CostDifferenceEventId,
    bool IsReplay, string OriginalCorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManufacturingConsumedLineageReferenceV1? ConsumedLineageReference = null);

/// <summary>Exact immutable Inventory history reference carried by the committed source event.</summary>
public sealed record ManufacturingConsumedLineageReferenceV1(string SourceVersion, Guid GenerationId,
    string GenerationVersion, string HistorySha256);

public static class ManufacturingCostDifferenceInboxV1
{
    public const string SourceName = "inventory.outbox.v1";
    public const string EventType = "inventory.manufacturing-cost-difference.proposed.v1";
    public const string ConsumerName = "accounting.manufacturing-cost-difference";
    public const string PostingPurpose = "manufacturing-consumed-cost-difference";
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } = [new(SourceName, EventType, 1)];
}

public sealed record ManufacturingCostDifferenceOutputViewV1(Guid OutputId, Guid ReceiptMovementId, Guid LabelId,
    string NetQuantityKg, string RemainingQuantityKg, string ConsumedQuantityKg, string TlUnitCostPerKg,
    string RemainingTlValue, string ConsumedTlDifference, string? UsdUnitCostPerKg, string OriginalTlAmount);

public sealed record ManufacturingCostDifferenceAccountV1(Guid AccountId, string Code, string Name);
public sealed record ManufacturingCostDifferenceAccountPlanV1(Guid PackageId, long PackageVersion, long? CatalogVersion,
    string SourceVersion, string Sha256, ManufacturingCostDifferenceAccountV1 Debit, ManufacturingCostDifferenceAccountV1 Credit);

public sealed record ManufacturingCostDifferenceViewV1(Guid ProposalId, Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string GenerationVersion, string Status, long Version, string SignedTlDifference,
    IReadOnlyList<ManufacturingCostDifferenceOutputViewV1> Outputs, DateTimeOffset SourceOccurredAtUtc,
    Guid? PeriodId, DateOnly? AccountingDate, DateOnly? TransactionDate, Guid? DebitAccountId, Guid? CreditAccountId,
    Guid? PreparedBy, Guid? ApprovedBy, Guid? JournalId, string? Reason, ManufacturingCostDifferenceAccountPlanV1? AccountPlan = null);

/// <summary>Both accounts and both dates are explicit human selections; no account or closed-period date is inferred.</summary>
public sealed record PrepareManufacturingCostDifferenceV1(Guid ProposalId, long ExpectedVersion,
    Guid PeriodId, DateOnly AccountingDate, DateOnly TransactionDate, Guid DebitAccountId, Guid CreditAccountId,
    string Reason);

public interface IManufacturingCostDifferenceStoreV1
{
    Task AcceptAsync(WorkInboxEventV1 envelope, ManufacturingCostDifferenceEventV1 source,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManufacturingCostDifferenceViewV1>> ListAsync(CancellationToken cancellationToken = default);
    Task<ManufacturingCostDifferenceViewV1?> FindAsync(Guid proposalId, CancellationToken cancellationToken = default);
    Task<ManufacturingCostDifferenceViewV1> PrepareAsync(PrepareManufacturingCostDifferenceV1 request,
        Guid actorId, Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
    Task<ManufacturingCostDifferenceViewV1> ApproveAsync(Guid proposalId, long expectedVersion, Guid actorId,
        Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}
