using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOpeningBalanceService : IFinanceOpeningBalanceDecisionReadV1
{
    public static string ApprovePayloadHash(OpeningBalanceApproveRequest request) => PayloadHash(ApproveCommand, request);
    public static string RejectPayloadHash(OpeningBalanceRejectRequest request) => PayloadHash(RejectCommand, request);
    public static string ApproveReversalPayloadHash(OpeningBalanceReversalApproveRequest request) => PayloadHash(ApproveReversalCommand, request);
    public static string RejectReversalPayloadHash(OpeningBalanceReversalRejectRequest request) => PayloadHash(RejectReversalCommand, request);
    public static string DecisionPreviewDigest(OpeningBalanceDecisionDetailV1 detail) => PayloadHash("finance.opening-balance.decision-preview.v1", detail);
    public static string OriginalReceiptDigest(OpeningBalanceCommandResult result) => PayloadHash("finance.opening-balance.original-receipt.v1", result with { IsReplay = false, DecisionReceipt = null });
    public static string OpeningFactsDigest(OpeningBalanceView view, string openingProposedBy) => PayloadHash("finance.opening-balance.facts.v1", new
    { view.CompanyId, view.Id, view.TargetKind, view.TargetId, view.PartyId, view.CurrencyId, view.Direction, view.Amount, view.AsOfDate, view.OpeningPeriodVersion, openingProposedBy, view.ProposedAtUtc });
    public static string ReversalFactsDigest(Guid openingId, OpeningBalanceReversalView view, string reversalProposedBy) => PayloadHash("finance.opening-balance.reversal-facts.v1", new
    { openingId, view.Id, view.Reason, view.OpeningPeriodVersion, reversalProposedBy, view.ProposedAtUtc });

    public Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadOpeningDecisionAsync(FinanceContext context, Guid openingId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken) =>
        ReadDecisionAsync(context, openingId, reversal: false, freshConsumerFence, cancellationToken);
    public Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadReversalDecisionAsync(FinanceContext context, Guid reversalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken) =>
        ReadDecisionAsync(context, reversalId, reversal: true, freshConsumerFence, cancellationToken);

    private async Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid id,
        bool reversal, Func<CancellationToken, Task> consumerFence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(consumerFence); token.ThrowIfCancellationRequested();
        if (await Fence() is { } entry) return entry;
        if (id == Guid.Empty) return new(ReadOutcome.NotFound, null);
        OpeningBalanceReversalRow? target = null;
        if (reversal)
        {
            target = await dbContext.OpeningBalanceReversals.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId && x.Id == id, token);
            if (await Fence() is { } afterTarget) return afterTarget;
            if (target is null) return new(ReadOutcome.NotFound, null);
        }
        var openingId = target?.OpeningBalanceId ?? id;
        var opening = await dbContext.OpeningBalances.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId && x.Id == openingId, token);
        if (await Fence() is { } afterOpening) return afterOpening;
        if (opening is null) return new(reversal ? ReadOutcome.Unavailable : ReadOutcome.NotFound, null);
        if (!ValidDecisionOpeningRow(opening) || target is not null && !ValidDecisionReversalRow(target, opening)) return new(ReadOutcome.Unavailable, null);
        // FNS-D85 excludes only the reversal proposer, never the opening's proposer/approver as a new policy.
        var maker = target?.ProposedBy ?? opening.ProposedBy;
        if (maker == context.ActorId) return new(ReadOutcome.Denied, null);
        var openingVersion = opening.Version; var openingStatus = opening.Status;
        var targetVersion = target?.Version; var targetStatus = target?.Status;
        var view = View(opening, context.ActorId, target);
        var detail = new OpeningBalanceDecisionDetailV1(view);
        var proof = new OpeningBalanceDecisionProofV1(context.CompanyId, opening.Id, target?.Id, opening.ProposedBy,
            maker, target?.ProposedAtUtc ?? opening.ProposedAtUtc, target is null ? view.Status.ToString() : view.Reversal!.Status.ToString(),
            target is null ? view.Version : view.Reversal!.Version, OpeningFactsDigest(view, opening.ProposedBy),
            target is null ? null : ReversalFactsDigest(opening.Id, view.Reversal!, target.ProposedBy), DecisionPreviewDigest(detail));
        if (target is not null)
        {
            var final = await (from r in dbContext.OpeningBalanceReversals.AsNoTracking()
                join o in dbContext.OpeningBalances.AsNoTracking() on new { r.CompanyId, Id = r.OpeningBalanceId } equals new { o.CompanyId, o.Id }
                where r.CompanyId == context.CompanyId && r.Id == id && o.CompanyId == context.CompanyId && o.Id == openingId
                select new { OpeningVersion = o.Version, OpeningStatus = o.Status, ReversalVersion = r.Version, ReversalStatus = r.Status }).SingleOrDefaultAsync(token);
            if (await Fence() is { } afterFinal) return afterFinal;
            if (final is null || final.OpeningVersion != openingVersion || final.OpeningStatus != openingStatus
                || final.ReversalVersion != targetVersion || final.ReversalStatus != targetStatus) return new(ReadOutcome.Unavailable, null);
        }
        else
        {
            var final = await dbContext.OpeningBalances.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == openingId)
                .Select(x => new { x.Version, x.Status }).SingleOrDefaultAsync(token);
            if (await Fence() is { } afterFinal) return afterFinal;
            if (final is null || final.Version != openingVersion || final.Status != openingStatus) return new(ReadOutcome.Unavailable, null);
        }
        return new(ReadOutcome.Found, detail with { DecisionProof = proof });
        async Task<ReadResult<OpeningBalanceDecisionDetailV1>?> Fence()
        {
            token.ThrowIfCancellationRequested();
            await consumerFence(token);
            token.ThrowIfCancellationRequested();
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceAction.ApproveOpeningBalance, token);
            token.ThrowIfCancellationRequested();
            await consumerFence(token);
            token.ThrowIfCancellationRequested();
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            if (refusal is null) refusal = await FenceAsync(context, FinanceAction.ApproveOpeningBalance, token);
            token.ThrowIfCancellationRequested();
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }

    internal static bool DecisionVersion(string? text, out long value) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value > 0 && VersionText(value) == text;
    private static bool ValidDecisionOpeningRow(OpeningBalanceRow row)
    {
        if (row.CompanyId == Guid.Empty || row.Id == Guid.Empty || row.TargetId == Guid.Empty || row.CurrencyId == Guid.Empty || row.Version <= 0
            || !OwnerIdentity(row.ProposedBy) || row.ProposedAtUtc.Offset != TimeSpan.Zero
            || !IsCanonical(row.OpeningPeriodVersion) || row.OpeningPeriodVersion.Length > PeriodVersionMaximumLength
            || !FinancePartyCreditLimitService.IsExactAmount(row.AmountText)) return false;
        if (row.TargetKind == PartyAccountKind ? row.PartyId is null || row.PartyId == Guid.Empty || row.Direction is not (DebitDirection or CreditDirection)
            : row.TargetKind is not (CashAccountKind or BankAccountKind) || row.PartyId is not null || row.Direction is not null) return false;
        return row.Status == Proposed ? row.DecidedBy is null && row.DecidedAtUtc is null && row.RejectionReason is null
            : row.Status is Approved or Rejected or Reversed && OwnerIdentity(row.DecidedBy) && row.DecidedBy != row.ProposedBy
                && row.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero
                && (row.Status == Rejected ? row.RejectionReason is { } rejection && IsCanonical(rejection) && rejection.Length <= ReasonMaximumLength : row.RejectionReason is null);
    }
    private static bool ValidDecisionReversalRow(OpeningBalanceReversalRow row, OpeningBalanceRow opening) => row.Id != Guid.Empty
        && row.CompanyId == opening.CompanyId && row.OpeningBalanceId == opening.Id && row.Version > 0 && OwnerIdentity(row.ProposedBy)
        && row.ProposedAtUtc.Offset == TimeSpan.Zero && IsCanonical(row.Reason) && row.Reason.Length <= ReasonMaximumLength
        && IsCanonical(row.OpeningPeriodVersion) && row.OpeningPeriodVersion.Length <= PeriodVersionMaximumLength
        && (row.Status == Proposed ? row.DecidedBy is null && row.DecidedAtUtc is null && row.RejectionReason is null && opening.Status == Approved
            : row.Status is Approved or Rejected && OwnerIdentity(row.DecidedBy) && row.DecidedBy != row.ProposedBy
                && row.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero
                && (row.Status == Rejected ? row.RejectionReason is { } rejection && IsCanonical(rejection) && rejection.Length <= ReasonMaximumLength
                    && opening.Status is Approved or Reversed : row.RejectionReason is null && opening.Status == Reversed));
    private static bool OwnerIdentity(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= IdentityMaximumLength;
}
