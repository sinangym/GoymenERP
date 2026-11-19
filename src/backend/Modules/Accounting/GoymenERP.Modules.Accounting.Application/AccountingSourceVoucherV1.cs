using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>B-04/KLN-D8..D17: source amounts and accounts are frozen; approval only posts that exact voucher.</summary>
public sealed record AccountingSourceVoucherViewV1(Guid Id, Guid CompanyId, string DocumentKind,
    Guid SourceDocumentId, Guid InvoiceId, long FinalizedVersion, Guid SourceActorId, Guid PartyId,
    Guid DocumentCurrencyId, string? ExchangeRate, DateOnly DocumentDate, DateOnly? DueDate,
    string Status, long Version, long MappingVersion, Guid? PeriodId, Guid? JournalId,
    Guid? ApprovedBy, DateTimeOffset? ApprovedAtUtc, string? CancellationReason,
    IReadOnlyList<AccountingSourceVoucherLineViewV1> Lines, string? OfficialInvoiceNo, string FunctionalPayable,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? CancellationId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    AccountingJournalReversalProposalViewV1? ReversalProposal = null);

public sealed record AccountingSourceVoucherLineViewV1(int LineNumber, string MappingKey, int Side,
    Guid AccountId, string FunctionalAmount, int? SourceLineNumber, string? VatRateCode, string? AccountCode = null, string? AccountName = null);
public sealed record AccountingSourceVoucherListV1(Guid CompanyId, IReadOnlyList<AccountingSourceVoucherViewV1> Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveAccountingSourceVoucherV1(long ExpectedVersion);
public sealed record ProposeAccountingSourceVoucherReversalV1(long ExpectedVersion, Guid ProposalId,
    Guid PeriodId, DateOnly TransactionDate, DateOnly AccountingDate, DateOnly? ValueDate, string Reason);

public interface IAccountingSourceVoucherStoreV1
{
    Task<IReadOnlyList<AccountingSourceVoucherViewV1>> ListAsync(CancellationToken cancellationToken = default);
    Task<AccountingSourceVoucherViewV1?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountingSourceVoucherViewV1> ApproveAsync(Guid id, long expectedVersion, Guid actorId,
        Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
    Task<AccountingSourceVoucherViewV1> ProposeCancellationReversalAsync(Guid id,
        ProposeAccountingSourceVoucherReversalV1 request, Guid actorId,
        Func<CancellationToken, Task> freshAuthorityFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public static class AccountingSourceVoucherRulesV1
{
    public static void RequireApproval(int status, Guid sourceActorId, Guid actorId, bool sourceCancelled)
    {
        if (actorId == Guid.Empty || sourceActorId == Guid.Empty || sourceActorId == actorId)
            throw new AccountingStoreException("source_voucher_approval_refused", "Kaynak faturayı kesinleştiren kişi fişini onaylayamaz.");
        if (status != 1 || sourceCancelled)
            throw new AccountingStoreException("source_voucher_not_approvable", "Kaynak fiş onaya açık değil.");
    }
}
