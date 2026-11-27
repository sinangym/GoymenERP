using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditLimitServiceV1 : IFinanceBankCreditLimitDecisionReadV1
{
    public static string DecisionPayloadHash(CreditLimitDecisionV1 request) => FinanceCreditAccountService.PayloadHash(DecideCommand, request);
    public static string SourceFactsDigest(CreditLimitViewV1 view, string proposedBy) => FinanceCreditAccountService.PayloadHash(
        "finance.bank-credit-limit.recorded-facts.v1", new { view.Id, view.CompanyId, view.LenderPartyId, view.LenderCompanyPartyAccountId,
            view.Group, view.CurrencyId, view.Source, ProposedBy = proposedBy, view.ProposedAtUtc });
    public static string DecisionPreviewDigest(FinanceBankCreditLimitDecisionDetailV1 detail) =>
        FinanceCreditAccountService.PayloadHash("finance.bank-credit-limit.decision-preview.v1", detail);
    public static string OriginalReceiptDigest(CreditLimitResultV1 result) => FinanceCreditAccountService.PayloadHash(
        "finance.bank-credit-limit.original-receipt.v1", result with { IsReplay = false, DecisionReceipt = null });

    public async Task<ReadResult<FinanceBankCreditLimitDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid limitId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (await FreshReadFence() is { } before) return before;
        if (limitId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        var row = await db.CreditLimits.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId && x.Id == limitId, cancellationToken);
        if (await FreshReadFence() is { } afterRow) return afterRow;
        if (row is null) return new(ReadOutcome.NotFound, null);
        if (row.ProposedBy == context.ActorId) return new(ReadOutcome.Denied, null);
        CreditLimitViewV1 view;
        try { view = View(row, context.ActorId, null); }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException) { return new(ReadOutcome.Unavailable, null); }
        if (!ValidDecisionLimit(view, row.ProposedBy) || !ValidDecisionSourceRow(row, view)) return new(ReadOutcome.Unavailable, null);
        // Exact previous-limit FK lookup, not a scan of another lender's workspace or principal exposures.
        var next = await db.CreditLimits.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.PreviousLimitId == limitId && x.Status == 2)
            .Select(x => x.Id).Take(2).ToArrayAsync(cancellationToken);
        if (await FreshReadFence() is { } afterNext) return afterNext;
        if (next.Length > 1) return new(ReadOutcome.Unavailable, null);
        var final = await db.CreditLimits.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == limitId)
            .Select(x => new { x.Version, x.Status, x.SourceJson, x.ProposedBy, x.ProposedAtUtc, x.CreditAccountId, x.LenderPartyId, x.LenderCompanyPartyAccountId, x.CreditGroup, x.CurrencyId, x.AmountText, x.PreviousLimitId, x.Revision }).SingleOrDefaultAsync(cancellationToken);
        if (await FreshReadFence() is { } afterFinal) return afterFinal;
        if (final is null || final.Version != row.Version || final.Status != row.Status || final.SourceJson != row.SourceJson
            || final.ProposedBy != row.ProposedBy || final.ProposedAtUtc != row.ProposedAtUtc
            || final.CreditAccountId != row.CreditAccountId || final.LenderPartyId != row.LenderPartyId || final.LenderCompanyPartyAccountId != row.LenderCompanyPartyAccountId
            || final.CreditGroup != row.CreditGroup || final.CurrencyId != row.CurrencyId || final.AmountText != row.AmountText
            || final.PreviousLimitId != row.PreviousLimitId || final.Revision != row.Revision) return new(ReadOutcome.Unavailable, null);
        view = view with { SupersededById = next.Length == 1 ? next[0] : null };
        return new(ReadOutcome.Found, new(view, row.ProposedBy, SourceFactsDigest(view, row.ProposedBy)));
        async Task<ReadResult<FinanceBankCreditLimitDecisionDetailV1>?> FreshReadFence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            await freshConsumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same(context)) return new(ReadOutcome.Denied, null);
            var refusal = await Fence(context, FinanceAction.ApproveCredit, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await freshConsumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same(context)) return new(ReadOutcome.Denied, null);
            if (refusal is null) refusal = await Fence(context, FinanceAction.ApproveCredit, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }

    internal static bool ValidDecisionLimit(CreditLimitViewV1 view, string proposedBy)
    {
        if (view.Id == Guid.Empty || view.CompanyId == Guid.Empty || view.LenderPartyId == Guid.Empty || view.LenderCompanyPartyAccountId == Guid.Empty
            || view.CurrencyId == Guid.Empty || !Enum.IsDefined(view.Group) || !DecisionActor(proposedBy) || view.ProposedAtUtc.Offset != TimeSpan.Zero
            || view.Source is not { } source || source.CreditAccountId == Guid.Empty || !Text(source.ExpectedCreditVersion, 64)
            || !Text(source.Amount, 128) || !FinanceExactDecimalText.TryParseNonNegative(source.Amount, out _) || !Text(source.Reason, 1000)
            || source.SourceDocument is not { } document || !Text(document.Reference, 200) || !Text(document.Revision, 100) || document.DocumentDate == default
            || source.ExpectedCurrentLimitId == Guid.Empty || (source.ExpectedCurrentLimitId is null ? source.ExpectedCurrentLimitVersion is not null : !Text(source.ExpectedCurrentLimitVersion, 64))) return false;
        return view.Status switch
        {
            CreditAccountStatus.Proposed => view.Version == "1" && view.DecidedAtUtc is null && view.RejectionReason is null,
            CreditAccountStatus.Approved => view.Version == "2" && view.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && view.RejectionReason is null,
            CreditAccountStatus.Rejected => view.Version == "2" && view.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && Text(view.RejectionReason, 1000),
            _ => false,
        };
    }
    private static bool ValidDecisionSourceRow(Persistence.FinanceCreditLimitRow row, CreditLimitViewV1 view) =>
        row.CreditAccountId == view.Source.CreditAccountId && row.AmountText == view.Source.Amount
        && row.PreviousLimitId == view.Source.ExpectedCurrentLimitId && row.Revision > 0;
    // Native Start bounds actor length; do not add a new normalization or clock-order policy to historic facts.
    private static bool DecisionActor(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}
