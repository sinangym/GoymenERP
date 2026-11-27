using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOpeningBalanceService
{
    private async Task<OpeningBalanceDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context, CommandMetadata metadata,
        Guid openingId, string expectedVersion, string? reason, string command, OpeningBalanceCommandResult result, CancellationToken token)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var receipt = await dbContext.OpeningBalanceCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId
            && x.CommandName == command && x.OpeningBalanceId == openingId && x.IdempotencyKeyHash == key && x.ActorId == context.ActorId, token);
        await Fence();
        if (receipt is null) return null;
        var stored = RestoreStoredDecision(receipt, result);
        if (stored?.Resource is not { } original) return null;
        var isReversal = command is ApproveReversalCommand or RejectReversalCommand;
        var movementKind = isReversal ? ReversalMovement : OpeningMovement;
        OpeningBalanceRow? opening; OpeningBalanceReversalRow? reversal = null; OpeningBalanceMovementRow? movement;
        if (isReversal)
        {
            if (original.Reversal is not { } target) return null;
            // Retained requested reversal + parent + its immutable movement, never the latest pending reversal.
            var facts = await (from r in dbContext.OpeningBalanceReversals.AsNoTracking()
                join o in dbContext.OpeningBalances.AsNoTracking() on new { r.CompanyId, Id = r.OpeningBalanceId } equals new { o.CompanyId, o.Id }
                from m in dbContext.OpeningBalanceMovements.AsNoTracking().Where(m => m.CompanyId == context.CompanyId && m.OpeningBalanceId == openingId && m.Kind == movementKind).DefaultIfEmpty()
                where r.CompanyId == context.CompanyId && r.Id == target.Id && o.CompanyId == context.CompanyId && o.Id == openingId
                select new { Opening = o, Reversal = r, Movement = m }).SingleOrDefaultAsync(token);
            opening = facts?.Opening; reversal = facts?.Reversal; movement = facts?.Movement;
        }
        else
        {
            var facts = await (from o in dbContext.OpeningBalances.AsNoTracking()
                from m in dbContext.OpeningBalanceMovements.AsNoTracking().Where(m => m.CompanyId == context.CompanyId && m.OpeningBalanceId == openingId && m.Kind == movementKind).DefaultIfEmpty()
                where o.CompanyId == context.CompanyId && o.Id == openingId
                select new { Opening = o, Movement = m }).SingleOrDefaultAsync(token);
            opening = facts?.Opening; movement = facts?.Movement;
        }
        await Fence();
        return opening is null ? null : RestoreDecisionReceipt(receipt, opening, reversal, movement, context, metadata, openingId, expectedVersion, reason, command, result);
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApproveOpeningBalance, token) is { } refusal) throw new OpeningDecisionRefusal(refusal);
            token.ThrowIfCancellationRequested();
        }
    }

    private static OpeningBalanceCommandResult? RestoreStoredDecision(OpeningBalanceCommandRow row, OpeningBalanceCommandResult result)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<OpeningBalanceCommandResult>(row.ResultJson, Json);
            return stored is { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: not null, Issues.Count: 0 }
                && result.Outcome == CommandOutcome.Succeeded && result.Issues is { Count: 0 }
                && JsonNode.DeepEquals(JsonNode.Parse(row.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json), JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json)) ? stored : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException) { return null; }
    }

    internal static OpeningBalanceDecisionReceiptV1? RestoreDecisionReceipt(OpeningBalanceCommandRow receipt,
        OpeningBalanceRow opening, OpeningBalanceReversalRow? reversal, OpeningBalanceMovementRow? movement,
        FinanceContext context, CommandMetadata metadata, Guid openingId, string expectedVersion, string? reason,
        string command, OpeningBalanceCommandResult result)
    {
        try
        {
            var stored = RestoreStoredDecision(receipt, result);
            var isReversal = command is ApproveReversalCommand or RejectReversalCommand;
            var approve = command is ApproveCommand or ApproveReversalCommand;
            if (command is not (ApproveCommand or RejectCommand or ApproveReversalCommand or RejectReversalCommand)
                || stored?.Resource is not { } original || !DecisionVersion(expectedVersion, out var expected) || expected == long.MaxValue
                || openingId == Guid.Empty || opening.Id != openingId || opening.CompanyId != context.CompanyId || !ValidDecisionOpeningRow(opening)
                || original.Id != openingId || original.CompanyId != context.CompanyId || receipt.CompanyId != context.CompanyId
                || receipt.OpeningBalanceId != openingId || receipt.CommandName != command || receipt.ActorId != context.ActorId || !OwnerIdentity(receipt.ActorId)
                || receipt.IdempotencyKeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || receipt.PayloadHash != DecisionPayloadHash(command, openingId, expectedVersion, reason)
                || !OwnerIdentity(receipt.CorrelationId) || receipt.CorrelationId != stored.OriginalCorrelationId
                || !result.IsReplay && receipt.CorrelationId != context.CorrelationId || receipt.RecordedAtUtc.Offset != TimeSpan.Zero
                || OpeningFactsDigest(original, opening.ProposedBy) != OpeningFactsDigest(View(opening, context.ActorId), opening.ProposedBy)
                || original.IsOwnProposal != (opening.ProposedBy == context.ActorId) || original.DecidedAtUtc != opening.DecidedAtUtc
                || original.RejectionReason != opening.RejectionReason || !OriginalOpeningStateMatches(original, opening)) return null;
            string resultVersion; string? reversalDigest = null; Guid? reversalId = null;
            if (isReversal)
            {
                if (reversal is null || original.Reversal is not { } target || !ValidDecisionReversalRow(reversal, opening)
                    || target.Id != reversal.Id || target.IsOwnProposal || reversal.ProposedBy == context.ActorId
                    || reversal.DecidedBy != context.ActorId || reversal.DecidedAtUtc != receipt.RecordedAtUtc
                    || target.DecidedAtUtc != receipt.RecordedAtUtc || target.RejectionReason != reversal.RejectionReason
                    || target.Status != (approve ? OpeningBalanceReversalStatus.Approved : OpeningBalanceReversalStatus.Rejected)
                    || reversal.Status != (approve ? Approved : Rejected) || target.Version != VersionText(expected + 1)
                    || target.Version != VersionText(reversal.Version) || (approve ? reason is not null : target.RejectionReason != reason)
                    || ReversalFactsDigest(openingId, target, reversal.ProposedBy) != ReversalFactsDigest(openingId, View(opening, context.ActorId, reversal).Reversal!, reversal.ProposedBy)) return null;
                resultVersion = target.Version; reversalId = target.Id; reversalDigest = ReversalFactsDigest(openingId, target, reversal.ProposedBy);
            }
            else
            {
                if (original.Reversal is not null || reversal is not null || opening.ProposedBy == context.ActorId
                    || opening.DecidedBy != context.ActorId || opening.DecidedAtUtc != receipt.RecordedAtUtc
                    || original.DecidedAtUtc != receipt.RecordedAtUtc || original.Version != VersionText(expected + 1)
                    || original.Status != (approve ? OpeningBalanceStatus.Approved : OpeningBalanceStatus.Rejected)
                    || (approve ? reason is not null : original.RejectionReason != reason)) return null;
                resultVersion = original.Version;
            }
            if (approve && !MovementMatches(movement, opening, isReversal ? ReversalMovement : OpeningMovement, context.ActorId, receipt.RecordedAtUtc)) return null;
            return new(context.CompanyId, openingId, reversalId, command, receipt.IdempotencyKeyHash, receipt.PayloadHash, receipt.ActorId,
                expectedVersion, resultVersion, receipt.CorrelationId, receipt.RecordedAtUtc, OpeningFactsDigest(original, opening.ProposedBy), reversalDigest, OriginalReceiptDigest(stored));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException) { return null; }
    }

    private static string DecisionPayloadHash(string command, Guid id, string version, string? reason) => command switch
    {
        ApproveCommand => ApprovePayloadHash(new(id, version)), RejectCommand => RejectPayloadHash(new(id, version, reason!)),
        ApproveReversalCommand => ApproveReversalPayloadHash(new(id, version)), RejectReversalCommand => RejectReversalPayloadHash(new(id, version, reason!)),
        _ => throw new InvalidOperationException("Unknown opening balance decision."),
    };
    private static bool OriginalOpeningStateMatches(OpeningBalanceView original, OpeningBalanceRow current) =>
        DecisionVersion(original.Version, out var version) && (original.Status == StatusOf(current.Status) && version == current.Version
            || original.Status == OpeningBalanceStatus.Approved && current.Status == Reversed && version < long.MaxValue && current.Version == version + 1);
    private static bool MovementMatches(OpeningBalanceMovementRow? movement, OpeningBalanceRow opening, int kind, string actor, DateTimeOffset at) =>
        movement is not null && movement.Id != Guid.Empty && movement.CompanyId == opening.CompanyId && movement.OpeningBalanceId == opening.Id
        && movement.Kind == kind && movement.TargetKind == opening.TargetKind && movement.TargetId == opening.TargetId && movement.PartyId == opening.PartyId
        && movement.CurrencyId == opening.CurrencyId && movement.Direction == opening.Direction && movement.AmountText == opening.AmountText
        && movement.OccurredOn == opening.AsOfDate && movement.RecordedBy == actor && movement.RecordedAtUtc == at;
}
