namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Approval-purpose disclosure only; it grants no Prepare or general journal read permission.</summary>
public interface IManufacturingCostDifferenceDecisionReadV1
{
    Task<AccountingApiResult<ManufacturingCostDifferenceDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record ManufacturingCostDifferenceDecisionDetailV1(ManufacturingCostDifferenceViewV1 Proposal,
    DateTimeOffset PreparedAtUtc, string SourceFactsDigest, string PreviewDigest);

/// <summary>Retained owner facts, not a reconstruction from today's account plan.</summary>
public sealed record ManufacturingCostDifferenceDecisionSourceV1(ManufacturingCostDifferenceViewV1 Proposal,
    string SourceJson, string SourceHash, string SourceCorrelationId, Guid? CurrencyId,
    DateTimeOffset? PreparedAtUtc, DateTimeOffset? ApprovedAtUtc, bool JournalBound, string? JournalCorrelationId);
public interface IManufacturingCostDifferenceDecisionStoreV1
{
    Task<ManufacturingCostDifferenceDecisionSourceV1?> ReadCostDifferenceDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
public sealed record ManufacturingCostDifferenceApprovalReceiptV1(Guid CompanyId, Guid ProposalId, Guid JournalId,
    Guid PreparedBy, DateTimeOffset PreparedAtUtc, string CommandName, string IdempotencyKeyHash, string PayloadHash,
    Guid ActorId, long ExpectedVersion, long ResultVersion, string OriginalCorrelationId, DateTimeOffset ApprovedAtUtc,
    string SourceFactsDigest, string OriginalResultDigest);
