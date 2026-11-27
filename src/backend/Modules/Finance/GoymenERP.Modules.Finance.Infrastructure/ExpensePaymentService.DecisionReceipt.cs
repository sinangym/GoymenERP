using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class ExpensePaymentService
{
    private async Task<ExpensePaymentDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context, CommandMetadata metadata,
        ApproveExpensePaymentRequest request, ExpensePaymentCommandResult result, CancellationToken token)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var row = await dbContext.ExpensePaymentCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId
            && x.CommandName == ApproveCommand && x.PaymentId == request.PaymentId && x.IdempotencyKeyHash == key && x.ActorId == context.ActorId, token);
        await Fence();
        return row is null ? null : RestoreDecisionReceipt(row, context, metadata, request, result);
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApproveExpensePayment, request.PaymentId, token) is { } refusal)
                throw new ExpenseApprovalRefusal(refusal);
        }
    }

    internal static ExpensePaymentDecisionReceiptV1? RestoreDecisionReceipt(ExpensePaymentCommandRow row, FinanceContext context,
        CommandMetadata metadata, ApproveExpensePaymentRequest request, ExpensePaymentCommandResult result)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<ExpensePaymentCommandResult>(row.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } view } || stored.Issues is not { Count: 0 }
                || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
                || !JsonNode.DeepEquals(JsonNode.Parse(row.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json), JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))
                || row.CompanyId == Guid.Empty || row.CompanyId != context.CompanyId || view.CompanyId != context.CompanyId
                || request.PaymentId == Guid.Empty || view.Id != request.PaymentId || row.PaymentId != request.PaymentId
                || row.CommandName != ApproveCommand || row.IdempotencyKeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || row.PayloadHash != ApprovePayloadHash(request) || row.ActorId != context.ActorId || string.IsNullOrWhiteSpace(row.ActorId)
                || row.CorrelationId != stored.OriginalCorrelationId || string.IsNullOrWhiteSpace(row.CorrelationId)
                || !result.IsReplay && row.CorrelationId != context.CorrelationId || row.RecordedAtUtc.Offset != TimeSpan.Zero
                || view.Status != ExpensePaymentStatus.Approved || view.ApprovedBy != row.ActorId || view.ApprovedAtUtc != row.RecordedAtUtc
                || !ValidDecisionPayment(view) || !DecisionVersion(request.ExpectedVersion, out var expected) || expected == long.MaxValue
                || view.Version != VersionText(expected + 1)) return null;
            return new(context.CompanyId, view.Id, ApproveCommand, row.IdempotencyKeyHash, row.PayloadHash, row.ActorId,
                request.ExpectedVersion, view.Version, row.CorrelationId, row.RecordedAtUtc, DraftFactsDigest(view), OriginalReceiptDigest(stored));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return null; }
    }
}
