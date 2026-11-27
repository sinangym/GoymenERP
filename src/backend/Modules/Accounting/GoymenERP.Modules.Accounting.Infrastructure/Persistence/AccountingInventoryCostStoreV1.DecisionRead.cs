using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1 : IAccountingInventoryCostDecisionStoreV1
{
    private Func<CancellationToken, Task>? decisionReadFence;
    private bool decisionReadOwnedAtEntry;
    public async Task<AccountingInventoryCostDecisionSourceV1?> ReadInventoryCostDecisionAsync(Guid proposalId,
        AccountingInventoryCostKindV1 kind, long? resultVersion, Func<CancellationToken, Task> freshReadFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); token.ThrowIfCancellationRequested();
        if (decisionReadFence is not null) throw Refuse("inventory_cost_source_unavailable");
        var captured = Company();
        async Task Fence(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); RequireCompany(captured); await freshReadFence(ct); ct.ThrowIfCancellationRequested(); RequireCompany(captured); }
        decisionReadOwnedAtEntry = IsOwnedApproval;
        decisionReadFence = Fence;
        try
        {
            var query = db.InventoryCostProposals.AsNoTracking().Where(x => x.CompanyId == captured && x.Id == proposalId);
            var row = resultVersion is { } selected
                ? await ApprovalRead(() => query.SingleOrDefaultAsync(x => x.Version == selected, token), token)
                : await ApprovalRead(() => query.OrderByDescending(x => x.Version).FirstOrDefaultAsync(token), token);
            if (row is null) return null;
            var p = await ApprovalRead(() => ReadStored(row, token), token);
            if (p.Kind != kind) return null;
            var preparedRow = p.Status == "Prepared" ? row : p.Version > 2
                ? await ApprovalRead(() => query.SingleOrDefaultAsync(x => x.Version == p.Version - 1, token), token) : null;
            if (preparedRow is null) return Malformed();
            var before = await ApprovalRead(() => ReadStored(preparedRow, token), token);
            var source = await ApprovalRead(() => db.InventoryCostSources.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == captured && x.EventId == p.EventId, token), token);
            if (source is null) return Malformed();
            // Source and proposal JSON are retained owner facts, never a live plan or a later proposal's view.
            if (!DecisionJsonMatches(row.ViewJson, p) || !DecisionJsonMatches(preparedRow.ViewJson, before)) return Malformed();
            var journalBound = false; string? journalCorrelation = null;
            if (p.Status == "Posted" && p.JournalId is { } journalId && p.Preparation is { } preparation)
            {
                var journal = await ApprovalRead(() => db.Journals.AsNoTracking().Where(x => x.CompanyId == captured && x.Id == journalId)
                    .Select(x => new { x.SourceId, x.PostingPurpose, x.PeriodId, x.CurrencyId, x.TransactionDate, x.AccountingDate, x.StoredBy, x.CorrelationId })
                    .SingleOrDefaultAsync(token), token);
                // Header identity; full native result is trusted only through genuine Commands/owned commit, not a new line calculator.
                var sourceId = kind == AccountingInventoryCostKindV1.SalesInvoicedShipment ? p.InvoiceId : p.ProposalId;
                var purpose = kind == AccountingInventoryCostKindV1.SalesInvoicedShipment && p.Component.InvoiceLineId is { } line
                    ? AccountingInventoryCostTraceV1.SalesPurpose(line, p.InvoiceVersion) : AccountingInventoryCostTraceV1.PurchaseDifferencePurpose;
                journalBound = journal is not null && journal.SourceId == sourceId && journal.PostingPurpose == purpose
                    && journal.PeriodId == preparation.PeriodId && journal.CurrencyId == preparation.CurrencyId
                    && journal.TransactionDate == preparation.TransactionDate && journal.AccountingDate == preparation.AccountingDate
                    && Guid.TryParse(journal.StoredBy, out var actor) && actor == p.ApprovedBy;
                journalCorrelation = journalBound ? journal!.CorrelationId : null;
            }
            // Current cancellation never blocks proving a retained original result version.
            var cancelled = resultVersion is null && await ApprovalRead(() => Cancellation(p, token), token) is not null;
            await Fence(token);
            return new(before, p, preparedRow.RecordedAtUtc, source.SourceJson,
                AccountingInventoryCostSourceValidationV1.Sha256(source.SourceJson), cancelled, journalBound, journalCorrelation);
        }
        catch (JsonException) { await Fence(token); return Malformed(); }
        finally { decisionReadFence = null; decisionReadOwnedAtEntry = false; }
        static AccountingInventoryCostDecisionSourceV1 Malformed() => new(null!, null!, default, "", "", false, false, null);
    }
    private static bool DecisionJsonMatches<T>(string raw, T value) =>
        JsonNode.DeepEquals(JsonNode.Parse(raw), JsonSerializer.SerializeToNode(value, Json));
}
