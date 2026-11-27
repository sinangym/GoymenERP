using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class CashExchangeBookValueServiceV1 : ICashExchangeBookValueDecisionReadV1
{
    public static string DecidePayloadHash(DecideCashExchangeBookValueV1 request) => Hash(JsonSerializer.Serialize(request, Json));
    public static string ProposalFactsDigest(CashExchangeBookValueViewV1 value) => Hash(JsonSerializer.Serialize(value with
        { Status = CashExchangeBookValueStatusV1.Pending, Version = "1", DecidedBy = null, DecidedAtUtc = null, DecisionReason = null }, Json));
    public static string DecisionPreviewDigest(CashExchangeBookValueDecisionDetailV1 value) => Hash(JsonSerializer.Serialize(value, Json));
    public static string OriginalReceiptDigest(CashExchangeBookValueCommandResultV1 value) => Hash(JsonSerializer.Serialize(value with { IsReplay = false, DecisionReceipt = null }, Json));

    public async Task<ReadResult<CashExchangeBookValueDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        if (!await Fence()) return new(ReadOutcome.Denied, null);
        if (proposalId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        try
        {
            var facts = await (from p in db.CashExchangeBookValueProposals.AsNoTracking()
                join t in db.CashTransfers.AsNoTracking() on new { p.CompanyId, Id = p.TransferId } equals new { t.CompanyId, t.Id }
                from d in db.CashExchangeBookValueDecisions.AsNoTracking().Where(d => d.CompanyId == context.CompanyId && d.ProposalId == p.Id).DefaultIfEmpty()
                where p.CompanyId == context.CompanyId && p.Id == proposalId && t.CompanyId == context.CompanyId
                select new { Proposal = p, Transfer = t, Decision = d }).SingleOrDefaultAsync(ct);
            if (!await Fence()) return new(ReadOutcome.Denied, null);
            if (facts is null) return new(ReadOutcome.NotFound, null);
            var proposal = View(facts.Proposal, facts.Decision);
            if (proposal.PreparedBy == context.ActorId) return new(ReadOutcome.Denied, null);
            var transfer = JsonSerializer.Deserialize<CashTransferView>(facts.Proposal.TransferSourceJson, Json) ?? throw new JsonException();
            if (proposal.CompanyId != context.CompanyId || proposal.Id != proposalId || facts.Transfer.CompanyId != context.CompanyId
                || Hash(JsonSerializer.Serialize(CashTransferService.View(facts.Transfer), Json)) != proposal.TransferSourceHash) throw new JsonException();
            var detail = new CashExchangeBookValueDecisionDetailV1(transfer, proposal);
            var proof = new CashExchangeBookValueDecisionProofV1(context.CompanyId, proposalId, proposal.TransferId, proposal.PreparedBy,
                proposal.PreparedAtUtc, proposal.Status.ToString(), proposal.Version, proposal.TransferSourceHash, ProposalFactsDigest(proposal), DecisionPreviewDigest(detail));
            // Proposal/source are append-only; a decision appearing during this read must not be mixed into old Pending facts.
            var terminal = await db.CashExchangeBookValueDecisions.AsNoTracking().AnyAsync(d => d.CompanyId == context.CompanyId && d.ProposalId == proposalId, ct);
            if (!await Fence()) return new(ReadOutcome.Denied, null);
            if (terminal != (facts.Decision is not null)) return new(ReadOutcome.Unavailable, null);
            return new(ReadOutcome.Found, detail with { DecisionProof = proof });
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Data.Common.DbException)
        { return new(await Fence() ? ReadOutcome.Unavailable : ReadOutcome.Denied, null); }
        async Task<bool> Fence()
        {
            ct.ThrowIfCancellationRequested();
            await freshConsumerFence(ct); ct.ThrowIfCancellationRequested();
            var allowed = await Allowed(context, CashExchangeBookValuePermissionsV1.Approve, ct);
            await freshConsumerFence(ct); ct.ThrowIfCancellationRequested();
            if (!allowed || !Same(context)) return false;
            var releaseAllowed = await Allowed(context, CashExchangeBookValuePermissionsV1.Approve, ct);
            ct.ThrowIfCancellationRequested();
            return releaseAllowed && Same(context);
        }
    }

    private bool DecisionCleanEntry() => db.Database.IsRelational() && System.Transactions.Transaction.Current is null
        && db.Database.CurrentTransaction is null && !db.ChangeTracker.HasChanges() && !auditWriter.HasPendingDrafts;
}
