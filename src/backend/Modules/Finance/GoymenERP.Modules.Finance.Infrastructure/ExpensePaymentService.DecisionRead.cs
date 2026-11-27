using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class ExpensePaymentService : IExpensePaymentDecisionReadV1
{
    public static string ApprovePayloadHash(ApproveExpensePaymentRequest request) => Hash(request);
    public static string DecisionPreviewDigest(ExpensePaymentDecisionDetailV1 detail) => Hash(detail);
    public static string DraftFactsDigest(ExpensePaymentView view) => Hash(new
    { view.CompanyId, view.Id, view.Preparation, view.PreparedBy, view.PreparedAtUtc });
    public static string OriginalReceiptDigest(ExpensePaymentCommandResult result) =>
        Hash(result with { IsReplay = false, DecisionReceipt = null });

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, ExpensePaymentDecisionDetailV1? detail)
    {
        if (item is null || detail?.Payment is not { } view || !ValidDecisionPayment(view)
            || detail.Revisers is null || detail.DraftFactsDigest != DraftFactsDigest(view)) return false;
        var expected = FinanceExpensePaymentWorkInboxSourceV1.DecisionItem(detail);
        return expected is not null && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
            && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link && item.Scope is null
            && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References.SequenceEqual(expected.References)
            && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    }

    public async Task<ReadResult<ExpensePaymentDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid paymentId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (await Fence() is { } before) return before;
        if (paymentId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        var row = await dbContext.ExpensePayments.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId && x.Id == paymentId, cancellationToken);
        if (await Fence() is { } afterRow) return afterRow;
        if (row is null) return new(ReadOutcome.NotFound, null);
        var view = View(row); var capturedVersion = row.Version; var capturedStatus = row.Status;
        if (!ValidDecisionPayment(view)) return new(ReadOutcome.Unavailable, null);
        var revisions = await FinanceDraftRevisers.ExpensePaymentsAsync(dbContext, context.CompanyId, [paymentId], cancellationToken);
        if (await Fence() is { } afterRevisers) return afterRevisers;
        if (revisions is null || FinanceDraftRevisers.ExcludedBesides(revisions, paymentId, view.PreparedBy) is null)
            return new(ReadOutcome.Unavailable, null);
        var revisers = revisions.GetValueOrDefault(paymentId) ?? [];
        if (view.PreparedBy == context.ActorId || revisers.Contains(context.ActorId, StringComparer.Ordinal)) return new(ReadOutcome.Denied, null);
        var name = view.Preparation.SourceKind == ExpensePaymentSourceKind.Bank
            ? await dbContext.BankAccounts.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == view.Preparation.TreasuryAccountId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : await dbContext.CashAccounts.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == view.Preparation.TreasuryAccountId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        if (await Fence() is { } afterLabel) return afterLabel;
        var final = await dbContext.ExpensePayments.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == paymentId)
            .Select(x => new { x.Version, x.Status }).SingleOrDefaultAsync(cancellationToken);
        if (await Fence() is { } afterFinal) return afterFinal;
        return final is null || final.Version != capturedVersion || final.Status != capturedStatus ? new(ReadOutcome.Unavailable, null)
            : new(ReadOutcome.Found, new(view, FinanceWorkInboxRead.Name(name, view.Preparation.TreasuryAccountId), revisers, DraftFactsDigest(view)));
        async Task<ReadResult<ExpensePaymentDecisionDetailV1>?> Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceAction.ApproveExpensePayment, paymentId, cancellationToken);
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }

    internal static bool DecisionVersion(string? text, out long value) => long.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out value) && value > 0 && VersionText(value) == text;
    internal static bool ValidDecisionPayment(ExpensePaymentView view)
    {
        if (view.Id == Guid.Empty || view.CompanyId == Guid.Empty || view.Preparation is not { Money: { }, Profile: { } } preparation
            || !DecisionVersion(view.Version, out _) || string.IsNullOrWhiteSpace(view.PreparedBy) || view.PreparedAtUtc.Offset != TimeSpan.Zero
            || ExpensePaymentValidator.ValidateShape(preparation).Count != 0 || !FinanceExactDecimalText.TryParsePositive(preparation.Money.Amount, out _)
            || string.IsNullOrWhiteSpace(preparation.Profile.ProfileId) || string.IsNullOrWhiteSpace(preparation.Profile.Version)) return false;
        if (view.Status == ExpensePaymentStatus.Draft)
            return view.ApprovedBy is null && view.ApprovedAtUtc is null && view.RecordedBy is null && view.RecordedAtUtc is null && !view.PaymentConfirmed;
        if (string.IsNullOrWhiteSpace(view.ApprovedBy) || view.ApprovedBy == view.PreparedBy || view.ApprovedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero) return false;
        return view.Status switch
        {
            ExpensePaymentStatus.Approved => view.RecordedBy is null && view.RecordedAtUtc is null && !view.PaymentConfirmed,
            ExpensePaymentStatus.Recorded => !string.IsNullOrWhiteSpace(view.RecordedBy) && view.RecordedAtUtc is { Offset: var recordedOffset }
                && recordedOffset == TimeSpan.Zero && view.PaymentConfirmed,
            _ => false,
        };
    }
}
