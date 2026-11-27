using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditLimitServiceV1
{
    private async Task<FinanceBankCreditLimitDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context, CommandMetadata metadata,
        CreditLimitDecisionV1 request, CreditLimitResultV1 result, CancellationToken token)
    {
        await ReceiptFence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey); var hash = DecisionPayloadHash(request);
        // Commands has no target/correlation/time columns. Target is bound through its original JSON and the exact owner row.
        var found = await (from command in db.CreditMovementCommands.AsNoTracking()
            from limit in db.CreditLimits.AsNoTracking()
            where command.CompanyId == context.CompanyId && command.Command == DecideCommand && command.KeyHash == key
                && command.ActorId == context.ActorId && command.PayloadHash == hash
                && limit.CompanyId == context.CompanyId && limit.Id == request.LimitId
            select new { Command = command, Limit = limit }).SingleOrDefaultAsync(token);
        await ReceiptFence();
        return found is null ? null : RestoreDecisionReceipt(found.Command, found.Limit, context, metadata, request, result);
        async Task ReceiptFence()
        {
            if (await Fence(context, FinanceAction.ApproveCredit, token) is { } refusal) throw new LimitDecisionRefusal(refusal);
        }
    }

    internal static FinanceBankCreditLimitDecisionReceiptV1? RestoreDecisionReceipt(FinanceCreditMovementCommandRow command,
        FinanceCreditLimitRow source, FinanceContext context, CommandMetadata metadata, CreditLimitDecisionV1 request, CreditLimitResultV1 result)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<CreditLimitResultV1>(command.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } view } || stored.Issues is not { Count: 0 }
                || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
                || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json), JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))
                || command.CompanyId == Guid.Empty || command.CompanyId != context.CompanyId || view.CompanyId != context.CompanyId
                || source.CompanyId != context.CompanyId || source.Id != request.LimitId || view.Id != request.LimitId || request.LimitId == Guid.Empty
                || command.Command != DecideCommand || command.KeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || command.PayloadHash != DecisionPayloadHash(request) || command.ActorId != context.ActorId || !DecisionActor(command.ActorId)
                || command.ActorId == source.ProposedBy || view.IsOwnProposal || view.SupersededById is not null || !ValidDecisionLimit(view, source.ProposedBy)
                || !ValidDecisionSourceRow(source, View(source, context.ActorId, null))
                || request.ExpectedVersion != "1" || view.Version != "2" || view.Status != (request.Approve ? CreditAccountStatus.Approved : CreditAccountStatus.Rejected)
                || (request.Approve ? request.Reason is not null || view.RejectionReason is not null : !Text(request.Reason, 1000) || view.RejectionReason != request.Reason)
                || view.DecidedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero
                || !Text(stored.OriginalCorrelationId, 128) || !result.IsReplay && stored.OriginalCorrelationId != context.CorrelationId
                || SourceFactsDigest(view, source.ProposedBy) != SourceFactsDigest(View(source, context.ActorId, null), source.ProposedBy)) return null;
            return new(context.CompanyId, view.Id, DecideCommand, command.KeyHash, command.PayloadHash, command.ActorId,
                request.ExpectedVersion, view.Version, stored.OriginalCorrelationId, view.DecidedAtUtc.Value,
                SourceFactsDigest(view, source.ProposedBy), OriginalReceiptDigest(stored));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException) { return null; }
    }
}
