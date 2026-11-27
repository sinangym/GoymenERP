using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class PaymentReversalProposalService : IPaymentReversalDecisionPreviewReadV1
{
    public async Task<ReadResult<PaymentReversalDecisionPreviewV1>> ReadDecisionPreviewAsync(FinanceContext context,
        Guid proposalId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        token.ThrowIfCancellationRequested();
        if (await Fence() is { } before) return before;
        if (proposalId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        // Company-bound single statement: a missing source/event is unreadable, never a zero-valued payment.
        var found = await (from proposal in dbContext.PaymentReversalProposals.AsNoTracking()
            join payment in dbContext.ManualBankPayments.AsNoTracking() on proposal.PaymentId equals payment.Id
            join money in dbContext.OutgoingMoneyEvents.AsNoTracking() on payment.Id equals money.PaymentId
            join bank in dbContext.BankAccounts.AsNoTracking().Where(row => row.CompanyId == context.CompanyId)
                on payment.TreasuryAccountId equals bank.Id into banks
            from bank in banks.DefaultIfEmpty()
            where proposal.CompanyId == context.CompanyId && proposal.Id == proposalId
                && payment.CompanyId == context.CompanyId && money.CompanyId == context.CompanyId
            select new { Proposal = proposal, Payment = payment, Event = money, BankName = bank == null ? null : bank.DisplayName }).SingleOrDefaultAsync(token);
        if (await Fence() is { } after) return after;
        if (found is null) return new(ReadOutcome.Unavailable, null);
        var view = View(found.Proposal);
        if (!ValidProposalView(view) || !ValidRecordedPayment(found.Payment, found.Event)) return new(ReadOutcome.Unavailable, null);
        if (view.ProposedBy == context.ActorId) return new(ReadOutcome.Denied, null);
        var recordedPayment = found.Payment; var originalEvent = found.Event;
        return new(ReadOutcome.Found, new(context.CompanyId, view, recordedPayment.DocumentNumber, originalEvent.PartyId,
            originalEvent.CompanyAccountId, originalEvent.TreasuryAccountId, originalEvent.CurrencyId, originalEvent.AmountText, originalEvent.OccurredOn,
            originalEvent.ValueOn, originalEvent.Id, originalEvent.RecordedBy, originalEvent.RecordedAtUtc, PaymentFactsDigest(recordedPayment, originalEvent),
            FinanceWorkInboxRead.Name(found.BankName, originalEvent.TreasuryAccountId)));
        async Task<ReadResult<PaymentReversalDecisionPreviewV1>?> Fence()
        {
            await freshConsumerFence(token);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceAction.ApprovePaymentReversal, proposalId, token);
            await freshConsumerFence(token);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }

    internal static bool ValidProposalView(PaymentReversalProposalView view)
    {
        if (view.Id == Guid.Empty || view.PaymentId == Guid.Empty || string.IsNullOrWhiteSpace(view.ProposedBy)
            || view.ProposedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(view.Reason)
            || view.Reason != view.Reason.Trim() || view.Reason.Length > 1000) return false;
        if (view.Status == ReversalProposalStatus.Pending)
            return view.Version == "1" && view.DecidedBy is null && view.DecidedAtUtc is null
                && view.DecisionReason is null && view.ReversalEventId is null;
        if (view.Version != "2" || string.IsNullOrWhiteSpace(view.DecidedBy) || view.DecidedBy == view.ProposedBy
            || view.DecidedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero) return false;
        return view.Status switch
        {
            ReversalProposalStatus.Approved => view.ReversalEventId is { } id && id != Guid.Empty && view.DecisionReason is null,
            ReversalProposalStatus.Rejected => view.ReversalEventId is null && !string.IsNullOrWhiteSpace(view.DecisionReason)
                && view.DecisionReason == view.DecisionReason.Trim() && view.DecisionReason.Length <= 1000,
            _ => false,
        };
    }

    private static bool ValidRecordedPayment(ManualBankPaymentRow payment, OutgoingMoneyEventRow money) =>
        payment.CompanyId != Guid.Empty && payment.Id != Guid.Empty && money.Id != Guid.Empty
        && payment.Status == ManualBankPaymentStatuses.Recorded && money.CompanyId == payment.CompanyId && money.PaymentId == payment.Id
        && money.PartyId != Guid.Empty && money.CompanyAccountId != Guid.Empty && money.TreasuryAccountId != Guid.Empty && money.CurrencyId != Guid.Empty
        && money.PartyId == payment.PartyId && money.CompanyAccountId == payment.CompanyAccountId
        && money.TreasuryAccountId == payment.TreasuryAccountId && money.CurrencyId == payment.CurrencyId
        && money.AmountText == payment.AmountText && FinanceExactDecimalText.TryParsePositive(money.AmountText, out _)
        && money.ProfileId == payment.ProfileId && money.ProfileVersion == payment.ProfileVersion
        && money.OccurredOn == payment.OccurredOn && money.ValueOn == payment.ValueOn
        && !string.IsNullOrWhiteSpace(money.RecordedBy) && money.RecordedBy == payment.RecordedBy
        && money.RecordedAtUtc.Offset == TimeSpan.Zero && money.RecordedAtUtc == payment.RecordedAtUtc;

    private static string PaymentFactsDigest(ManualBankPaymentRow payment, OutgoingMoneyEventRow money) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            payment.CompanyId, PaymentId = payment.Id, payment.DocumentNumber, OriginalEvent = money,
        }, Json)))).ToLowerInvariant();

    public static bool MatchesDecisionItem(WorkInboxItemV1 item, PaymentReversalDecisionPreviewV1 view) =>
        view.CompanyId != Guid.Empty && ValidProposalView(view.Proposal)
        && FinanceManualBankPaymentReversalWorkInboxSourceV1.MatchesDecisionItem(item, view);

    public static string DecisionPreviewDigest(PaymentReversalDecisionPreviewV1 view) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(view, Json)))).ToLowerInvariant();
    public static string ApprovePayloadHash(ApprovePaymentReversalRequest request) => PayloadHash(ApproveCommand, request);
    public static string RejectPayloadHash(RejectPaymentReversalRequest request) => PayloadHash(RejectCommand, request);
}
