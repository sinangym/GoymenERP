using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditMovementServiceV1
{
    private async Task<FinanceCreditMovementDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context, CommandMetadata metadata,
        CreditMovementDecisionV1 request, CreditMovementResultV1 result, CancellationToken token)
    {
        await ReceiptFence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey); var hash = DecisionPayloadHash(request);
        // Exact native command key/actor/payload and exact retained source row; no account history scan.
        var found = await (from command in db.CreditMovementCommands.AsNoTracking()
            from movement in db.CreditMovements.AsNoTracking()
            where command.CompanyId == context.CompanyId && command.Command == DecideCommand && command.KeyHash == key
                && command.ActorId == context.ActorId && command.PayloadHash == hash
                && movement.CompanyId == context.CompanyId && movement.Id == request.MovementId
            select new { Command = command, Movement = movement }).SingleOrDefaultAsync(token);
        await ReceiptFence();
        var receipt = found is null ? null : RestoreDecisionReceipt(found.Command, found.Movement, context, metadata, request, result);
        if (receipt is not null && result.Resource!.Source.BankEffect is { } bank)
        {
            await ReceiptFence();
            var bankName = await db.BankAccounts.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == bank.BankAccountId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(token);
            await ReceiptFence();
            // Existing formatter supplies Name(id) when missing; no history reconstruction or general bank-card grant.
            receipt = receipt with { BankDisplayName = bankName };
        }
        return receipt;
        async Task ReceiptFence()
        {
            token.ThrowIfCancellationRequested();
            if (await Fence(context, FinanceAction.ApproveCredit, token) is { } refusal) throw new MovementDecisionRefusal(refusal);
            token.ThrowIfCancellationRequested();
        }
    }

    internal static FinanceCreditMovementDecisionReceiptV1? RestoreDecisionReceipt(FinanceCreditMovementCommandRow command,
        FinanceCreditMovementRow source, FinanceContext context, CommandMetadata metadata, CreditMovementDecisionV1 request, CreditMovementResultV1 result)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<CreditMovementResultV1>(command.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } view } || stored.Issues is not { Count: 0 }
                || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
                || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json), JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))
                || command.CompanyId == Guid.Empty || command.CompanyId != context.CompanyId || view.CompanyId != context.CompanyId
                || source.CompanyId != context.CompanyId || source.Id != request.MovementId || view.Id != request.MovementId || request.MovementId == Guid.Empty
                || command.Command != DecideCommand || command.KeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || command.PayloadHash != DecisionPayloadHash(request) || command.ActorId != context.ActorId || !DecisionActor(command.ActorId)
                || command.ActorId == source.ProposedBy || view.IsOwnProposal || !ValidDecisionMovement(view, source.ProposedBy)
                || !ValidDecisionMovement(View(source, context.ActorId), source.ProposedBy) || !ValidDecisionSourceRow(source, View(source, context.ActorId))
                || request.ExpectedVersion != "1" || view.Version != "2" || view.ReversedByMovementId is not null
                || view.Status != (request.Approve ? CreditMovementStatusV1.Approved : CreditMovementStatusV1.Rejected)
                || source.Status != (int)view.Status || source.DecidedBy != command.ActorId || source.DecidedAtUtc != view.DecidedAtUtc
                || source.RejectionReason != view.RejectionReason
                || (request.Approve ? request.Reason is not null || view.RejectionReason is not null : !FinanceCreditMovementPolicyV1.Text(request.Reason, 1000) || view.RejectionReason != request.Reason)
                || view.DecidedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero
                || !FinanceCreditMovementPolicyV1.Text(stored.OriginalCorrelationId, 128) || !result.IsReplay && stored.OriginalCorrelationId != context.CorrelationId
                || SourceFactsDigest(view, source.ProposedBy) != SourceFactsDigest(View(source, context.ActorId), source.ProposedBy)) return null;
            return new(context.CompanyId, view.Id, DecideCommand, command.KeyHash, command.PayloadHash, command.ActorId,
                request.ExpectedVersion, view.Version, stored.OriginalCorrelationId, view.DecidedAtUtc.Value,
                SourceFactsDigest(view, source.ProposedBy), OriginalReceiptDigest(stored));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException) { return null; }
    }
}
