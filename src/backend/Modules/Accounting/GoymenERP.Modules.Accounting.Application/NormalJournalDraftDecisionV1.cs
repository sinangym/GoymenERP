using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Approval-only owner read. It neither selects a period nor grants period-list access.</summary>
public interface INormalJournalDraftDecisionReadV1
{
    Task<AccountingApiResult<NormalJournalDraftDecisionDetailV1>> ReadDecisionAsync(
        Guid draftId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}

public sealed record NormalJournalDraftDecisionDetailV1(
    NormalJournalDraftViewV1 Draft, string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public bool? IsCreditSource { get; init; }
}

/// <summary>Internal provenance of the exact persisted native command; never serialized in the public view.</summary>
public sealed record NormalJournalDraftApprovalReceiptV1(
    Guid CompanyId, Guid DraftId, string CommandName, string IdempotencyKeyHash, string PayloadHash,
    Guid ActorId, long ExpectedVersion, long ResultVersion, Guid PeriodId, Guid PostedJournalId,
    string OriginalCorrelationId, DateTimeOffset ApprovedAtUtc, string SourceFactsDigest, string OriginalResultDigest)
{
    [JsonIgnore] public bool? IsCreditSource { get; init; }
}

public sealed record NormalJournalDraftDecisionSourceV1(NormalJournalDraftRecordV1 Record, bool IsCreditSource);

/// <summary>Unsupported stores cannot provide a partially fenced decision snapshot.</summary>
public interface IAccountingNormalJournalDraftDecisionStoreV1
{
    Task<NormalJournalDraftDecisionSourceV1?> ReadNormalDraftDecisionAsync(
        Guid draftId, Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
