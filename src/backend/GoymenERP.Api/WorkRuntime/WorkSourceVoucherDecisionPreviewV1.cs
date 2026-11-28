using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Purpose-only human whitelist. Native wire/hash stay unchanged; all Int64 values cross Work as canonical text.</summary>
public sealed record WorkSourceVoucherPreviewV1(WorkSourceVoucherViewV1 Voucher, DateTimeOffset CreatedAtUtc,
    bool SourceCancelled, bool PeriodOpen)
{
    internal static WorkSourceVoucherPreviewV1 FromNative(AccountingSourceVoucherDecisionDetailV1 detail) =>
        new(WorkSourceVoucherViewV1.FromNative(detail.Voucher), detail.CreatedAtUtc, detail.SourceCancelled, detail.PeriodOpen);
}
public sealed record WorkSourceVoucherViewV1(Guid Id, Guid CompanyId, string DocumentKind,
    Guid SourceDocumentId, Guid InvoiceId, string FinalizedVersion, Guid SourceActorId, Guid PartyId,
    Guid DocumentCurrencyId, string? ExchangeRate, DateOnly DocumentDate, DateOnly? DueDate,
    string Status, string Version, string MappingVersion, Guid? PeriodId, Guid? JournalId,
    Guid? ApprovedBy, DateTimeOffset? ApprovedAtUtc, string? CancellationReason,
    IReadOnlyList<AccountingSourceVoucherLineViewV1> Lines, string? OfficialInvoiceNo, string FunctionalPayable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CancellationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkSourceVoucherReversalProposalV1? ReversalProposal)
{
    internal static WorkSourceVoucherViewV1 FromNative(AccountingSourceVoucherViewV1 value) => new(value.Id, value.CompanyId,
        value.DocumentKind, value.SourceDocumentId, value.InvoiceId, value.FinalizedVersion.ToString(CultureInfo.InvariantCulture),
        value.SourceActorId, value.PartyId, value.DocumentCurrencyId, value.ExchangeRate, value.DocumentDate, value.DueDate,
        value.Status, value.Version.ToString(CultureInfo.InvariantCulture), value.MappingVersion.ToString(CultureInfo.InvariantCulture),
        value.PeriodId, value.JournalId, value.ApprovedBy, value.ApprovedAtUtc, value.CancellationReason, value.Lines,
        value.OfficialInvoiceNo, value.FunctionalPayable, value.CancellationId,
        value.ReversalProposal is { } reversal ? WorkSourceVoucherReversalProposalV1.FromNative(reversal) : null);
}
// Explicit native public16 fields only; the owner's private cancellation-actor/receipt proof is not a Work wire field.
public sealed record WorkSourceVoucherReversalProposalV1(Guid ProposalId, Guid CompanyId, Guid SourceJournalId,
    Guid? ReversalJournalId, Guid PeriodId, DateOnly TransactionDate, DateOnly AccountingDate, DateOnly? ValueDate,
    string Reason, Guid ProposedBy, DateTimeOffset ProposedAtUtc, string Status, Guid? DecidedBy,
    DateTimeOffset? DecidedAtUtc, string? DecisionReason, int Version)
{
    internal static WorkSourceVoucherReversalProposalV1 FromNative(AccountingJournalReversalProposalViewV1 value) =>
        new(value.ProposalId, value.CompanyId, value.SourceJournalId, value.ReversalJournalId, value.PeriodId,
            value.TransactionDate, value.AccountingDate, value.ValueDate, value.Reason, value.ProposedBy, value.ProposedAtUtc,
            value.Status, value.DecidedBy, value.DecidedAtUtc, value.DecisionReason, value.Version);
}
