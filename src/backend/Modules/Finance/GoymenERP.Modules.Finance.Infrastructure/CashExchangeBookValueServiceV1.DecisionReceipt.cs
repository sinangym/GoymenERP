using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class CashExchangeBookValueServiceV1
{
    private async Task<CashExchangeBookValueDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context, CommandMetadata metadata,
        DecideCashExchangeBookValueV1 request, CashExchangeBookValueCommandResultV1 result, CancellationToken ct)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var command = await db.CashExchangeBookValueCommands.AsNoTracking().SingleOrDefaultAsync(c => c.CompanyId == context.CompanyId && c.Command == DecideCommand && c.KeyHash == key, ct);
        await Fence();
        if (command is null) return null;
        var facts = await (from p in db.CashExchangeBookValueProposals.AsNoTracking()
            join d in db.CashExchangeBookValueDecisions.AsNoTracking() on new { p.CompanyId, ProposalId = p.Id } equals new { d.CompanyId, d.ProposalId }
            join t in db.CashTransfers.AsNoTracking() on new { p.CompanyId, Id = p.TransferId } equals new { t.CompanyId, t.Id }
            where p.CompanyId == context.CompanyId && p.Id == request.ProposalId && d.CompanyId == context.CompanyId && t.CompanyId == context.CompanyId
            select new { Proposal = p, Decision = d, Transfer = t }).SingleOrDefaultAsync(ct);
        await Fence();
        return facts is null ? null : RestoreDecisionReceipt(command, facts.Proposal, facts.Decision, CashTransferService.View(facts.Transfer), context, metadata, request, result);
        async Task Fence()
        {
            ct.ThrowIfCancellationRequested();
            if (!await Allowed(context, CashExchangeBookValuePermissionsV1.Approve, ct)) throw new InvalidOperationException("Book-value receipt authority lost.");
            ct.ThrowIfCancellationRequested();
        }
    }

    internal static CashExchangeBookValueDecisionReceiptV1? RestoreDecisionReceipt(CashExchangeBookValueCommandRow command,
        CashExchangeBookValueProposalRow proposal, CashExchangeBookValueDecisionRow decision, CashTransferView transfer,
        FinanceContext context, CommandMetadata metadata, DecideCashExchangeBookValueV1 request, CashExchangeBookValueCommandResultV1 result)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<CashExchangeBookValueCommandResultV1>(command.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, Resource: not null, IsReplay: false, Code: null }
                || result is not { Outcome: CommandOutcome.Succeeded, Resource: not null, Code: null }
                || command.CompanyId != context.CompanyId || command.Command != DecideCommand || command.ActorId != context.ActorId
                || command.KeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey) || command.PayloadHash != DecidePayloadHash(request)
                || !Text(stored.OriginalCorrelationId, 128) || !result.IsReplay && stored.OriginalCorrelationId != context.CorrelationId
                || proposal.Id == Guid.Empty || proposal.Id != request.ProposalId || proposal.CompanyId != context.CompanyId
                || proposal.PreparedBy == context.ActorId || decision.DecidedBy != context.ActorId || request.ExpectedVersion != "1"
                || decision.Approved != request.Approve || decision.Reason != request.Reason || transfer.CompanyId != context.CompanyId
                || transfer.Id != proposal.TransferId || Hash(JsonSerializer.Serialize(transfer, Json)) != proposal.TransferSourceHash
                || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json), JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))) return null;
            var actual = View(proposal, decision);
            if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(actual, Json), JsonSerializer.SerializeToNode(stored.Resource, Json))) return null;
            return new(context.CompanyId, proposal.Id, proposal.TransferId, DecideCommand, command.KeyHash, command.PayloadHash, command.ActorId,
                request.ExpectedVersion, actual.Version, stored.OriginalCorrelationId, decision.DecidedAtUtc, ProposalFactsDigest(actual), OriginalReceiptDigest(stored));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException) { return null; }
    }
}
