using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>One actual posted economic share. Zero retains its approval witness without a fabricated journal.</summary>
public sealed record InventoryCostCancellationComponentV1(string EconomicKey, string Origin,
    Guid? SourceJournalId, Guid JournalSourceId, string PostingPurpose, Guid CurrencyId,
    string JournalTotal, string SignedAmount, Guid CogsAccountId, Guid CounterAccountId,
    string SourceFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReversingJournalId = null);

/// <summary>Append-only cancellation preparation/approval, separate from the immutable initial cost proposal.</summary>
public sealed record InventoryCostCancellationV1(Guid Id, Guid CompanyId, Guid InventoryCostProposalId,
    Guid CancellationId, long Version, string Status, long InvoiceLineRevision, Guid PeriodId,
    DateOnly AccountingDate, DateOnly TransactionDate, string Reason, Guid PreparedBy,
    DateTimeOffset PreparedAtUtc, IReadOnlyList<InventoryCostCancellationComponentV1> Components,
    IReadOnlyList<InventoryCostCancellationMoneyProfileV1> MoneyProfiles,
    Guid? ApprovedBy = null, DateTimeOffset? ApprovedAtUtc = null);

public sealed record InventoryCostCancellationMoneyProfileV1(Guid CurrencyId, Guid ProfileId, long Version, int Scale);

public sealed record ApproveInventoryCostCancellationV1(long ExpectedVersion, long ExpectedCancellationVersion);
