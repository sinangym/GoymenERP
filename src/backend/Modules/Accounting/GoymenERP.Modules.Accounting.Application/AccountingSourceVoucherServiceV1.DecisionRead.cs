using System.Globalization;
using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingSourceVoucherServiceV1 : IAccountingSourceVoucherDecisionReadV1
{
    public async Task<AccountingApiResult<AccountingSourceVoucherDecisionDetailV1>> ReadDecisionAsync(Guid voucherId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (voucherId == Guid.Empty) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "voucherId");
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<AccountingSourceVoucherDecisionDetailV1>();
        var captured = new ApprovalCapture(companyId, actor, actorText);
        try
        {
            await Fence(cancellationToken);
            if (store is not IAccountingSourceVoucherDecisionStoreV1 reader) return Unavailable();
            var source = await reader.ReadSourceVoucherDecisionAsync(voucherId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (!ValidSourceVoucherSource(source, companyId, voucherId)) return Unavailable();
            if (source.StoredVoucher.SourceActorId == actor) return Denied<AccountingSourceVoucherDecisionDetailV1>();
            return new(AccountingApiStatus.Ok, new(source.CurrentVoucher, source.CreatedAtUtc, source.SourceCancelled,
                source.PeriodOpen, SourceVoucherSourceFactsDigest(source), SourceVoucherPreviewDigest(source.CurrentVoucher,
                    source.CreatedAtUtc, source.SourceCancelled, source.PeriodOpen)), false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingSourceVoucherDecisionDetailV1>(); }
        catch (AccountingStoreException)
        {
            try { await Fence(cancellationToken); }
            catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingSourceVoucherDecisionDetailV1>(); }
            return Unavailable();
        }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await freshConsumerFence(ct); ct.ThrowIfCancellationRequested();
            // The owner check follows the final external await, including normal-completing revocation.
            await ApprovalFenceAsync(captured, ct); ct.ThrowIfCancellationRequested();
        }
        static AccountingApiResult<AccountingSourceVoucherDecisionDetailV1> Unavailable() => new(AccountingApiStatus.Unavailable, null, false, null);
    }
    private static object CommercialVoucher(AccountingSourceVoucherViewV1 v) => new
    {
        v.Id, v.CompanyId, v.DocumentKind, v.SourceDocumentId, v.InvoiceId, v.FinalizedVersion, v.SourceActorId,
        v.PartyId, v.DocumentCurrencyId, v.ExchangeRate, v.DocumentDate, v.DueDate, v.MappingVersion,
        v.Lines, v.OfficialInvoiceNo, v.FunctionalPayable,
    };
    public static string SourceVoucherSourceFactsDigest(AccountingSourceVoucherDecisionSourceV1 source) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new { Voucher = CommercialVoucher(source.StoredVoucher), source.CreatedAtUtc, source.CommercialAmounts }, Json));
    public static string SourceVoucherPreviewDigest(AccountingSourceVoucherViewV1 voucher, DateTimeOffset createdAtUtc,
        bool sourceCancelled, bool periodOpen) => AccountingApiService.Sha256(JsonSerializer.Serialize(new { voucher, createdAtUtc, sourceCancelled, periodOpen }, Json));
    private static bool ValidSourceVoucherSource(AccountingSourceVoucherDecisionSourceV1? source, Guid companyId, Guid id)
    {
        if (source?.StoredVoucher is not { } p || source.CurrentVoucher is not { } current || source.CommercialAmounts is null
            || companyId == Guid.Empty || p.CompanyId != companyId || p.Id != id || id == Guid.Empty
            || p.SourceDocumentId == Guid.Empty || p.InvoiceId == Guid.Empty || p.SourceActorId == Guid.Empty
            || p.PartyId == Guid.Empty || p.DocumentCurrencyId == Guid.Empty || p.FinalizedVersion < 1 || p.MappingVersion < 1
            || p.Version < 1 || source.CreatedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(p.DocumentKind)
            || p.Lines is not { Count: > 0 } || p.Lines.Any(x => x is null || x.LineNumber < 1 || x.AccountId == Guid.Empty
                || x.Side is not (1 or 2) || string.IsNullOrWhiteSpace(x.MappingKey) || !SourceVoucherAmount(x.FunctionalAmount))
            || p.Lines.Select(x => x.LineNumber).Distinct().Count() != p.Lines.Count || !SourceVoucherAmount(p.FunctionalPayable)
            || p.FunctionalPayable != source.CommercialAmounts.FunctionalPayable.ToString("0.############################", CultureInfo.InvariantCulture)
            || JsonSerializer.Serialize(CommercialVoucher(p), Json) != JsonSerializer.Serialize(CommercialVoucher(current), Json)
            || p.Status is not ("PeriodWaiting" or "AwaitingApproval" or "Posted" or "Cancelled")
            || current.Status is not ("PeriodWaiting" or "AwaitingApproval" or "Posted" or "Cancelled" or "CancellationReversalPending" or "CancelledByReversal")
            || p.Version != current.Version || p.PeriodId != current.PeriodId || p.JournalId != current.JournalId
            || p.ApprovedBy != current.ApprovedBy || p.ApprovedAtUtc != current.ApprovedAtUtc
            || source.SourceCancelled != (current.CancellationId is not null)
            || !source.SourceCancelled && (current.CancellationReason is not null || current.ReversalProposal is not null
                || current.Status != (p.Status is "AwaitingApproval" or "PeriodWaiting" ? source.PeriodOpen ? "AwaitingApproval" : "PeriodWaiting" : p.Status))) return false;
        return p.Status is "PeriodWaiting" or "AwaitingApproval"
            ? p.Version == 1 && p.JournalId is null && p.ApprovedBy is null && p.ApprovedAtUtc is null
            : p.Status == "Cancelled" && p.JournalId is null && p.ApprovedBy is null && p.ApprovedAtUtc is null
                || p.Status is "Posted" or "Cancelled" && p.Version >= 2 && p.JournalId == p.Id && p.PeriodId is { } period && period != Guid.Empty
                && p.ApprovedBy is { } checker && checker != Guid.Empty && checker != p.SourceActorId && p.ApprovedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero;
    }
    private static bool SourceVoucherAmount(string? text) => decimal.TryParse(text,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
        && text == amount.ToString("0.############################", CultureInfo.InvariantCulture);
}
