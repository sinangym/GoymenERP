using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class PaymentReversalProposalService
{
    private async Task<PaymentReversalDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context,
        CommandMetadata metadata, Guid proposalId, string expectedVersion, string? reason, string command,
        PaymentReversalProposalCommandResult result, CancellationToken token)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var paymentId = result.Resource!.PaymentId;
        // One company-scoped query, exact original command key. Source facts are the immutable recorded payment/event,
        // never today's proposal state or a replacement proposal. No history scan or new receipt persistence.
        var found = await (from receipt in dbContext.Commands.AsNoTracking()
            join payment in dbContext.ManualBankPayments.AsNoTracking() on receipt.ManualBankPaymentId equals (Guid?)payment.Id
            join money in dbContext.OutgoingMoneyEvents.AsNoTracking() on payment.Id equals money.PaymentId
            where receipt.CompanyId == context.CompanyId && payment.CompanyId == context.CompanyId
                && money.CompanyId == context.CompanyId && receipt.CommandName == command
                && receipt.IdempotencyKeyHash == key && receipt.ActorId == context.ActorId && payment.Id == paymentId
            select new { Receipt = receipt, Payment = payment, Event = money }).SingleOrDefaultAsync(token);
        await Fence();
        if (found is null || !ValidRecordedPayment(found.Payment, found.Event)) return null;
        return RestoreDecisionReceipt(found.Receipt, context, metadata, proposalId, expectedVersion, reason, command,
            result, PaymentFactsDigest(found.Payment, found.Event));
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApprovePaymentReversal, proposalId, token) is { } refusal)
                throw new PaymentDecisionRefusal(refusal);
        }
    }

    internal static PaymentReversalDecisionReceiptV1? RestoreDecisionReceipt(FinanceCommandRow row,
        FinanceContext context, CommandMetadata metadata, Guid proposalId, string expectedVersion, string? reason,
        string command, PaymentReversalProposalCommandResult result, string recordedFactsDigest)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<PaymentReversalProposalCommandResult>(row.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } view }
                || stored.Issues is not { Count: 0 } || result.Outcome != CommandOutcome.Succeeded
                || result.Issues is not { Count: 0 } || command is not (ApproveCommand or RejectCommand)
                || !JsonNode.DeepEquals(JsonNode.Parse(row.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json),
                    JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))
                || context.CompanyId == Guid.Empty || row.CompanyId != context.CompanyId
                || proposalId == Guid.Empty || view.Id != proposalId || view.PaymentId == Guid.Empty
                || row.ManualBankPaymentId != view.PaymentId || row.CommandName != command
                || row.IdempotencyKeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || row.ActorId != context.ActorId || string.IsNullOrWhiteSpace(row.ActorId)
                || row.CorrelationId != stored.OriginalCorrelationId || string.IsNullOrWhiteSpace(row.CorrelationId)
                || !result.IsReplay && row.CorrelationId != context.CorrelationId
                || expectedVersion != "1" || view.Version != "2" || !ValidProposalView(view)
                || view.DecidedBy != row.ActorId || view.DecidedAtUtc != row.RecordedAtUtc
                || row.RecordedAtUtc.Offset != TimeSpan.Zero
                || recordedFactsDigest is not { Length: 64 } || !recordedFactsDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
            var approved = command == ApproveCommand;
            if (approved ? view.Status != ReversalProposalStatus.Approved || reason is not null
                : view.Status != ReversalProposalStatus.Rejected || string.IsNullOrWhiteSpace(reason)
                    || reason.Trim().Length > 1000 || view.DecisionReason != reason.Trim()) return null;
            // Native reject hashes the ORIGINAL request (including whitespace), while its view trims the reason.
            // Never reconstruct that hash from the trimmed decision view.
            var payload = approved ? PayloadHash(command, new ApprovePaymentReversalRequest(proposalId, expectedVersion))
                : PayloadHash(command, new RejectPaymentReversalRequest(proposalId, expectedVersion, reason!));
            if (row.PayloadHash != payload) return null;
            return new(context.CompanyId, proposalId, view.PaymentId, command, row.IdempotencyKeyHash, payload,
                row.ActorId, expectedVersion, view.Version, row.CorrelationId, row.RecordedAtUtc, recordedFactsDigest);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return null; }
    }
}
