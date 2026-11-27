using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1 : IInvoiceLineCogsDecisionStoreV1
{
    public async Task<InvoiceLineCogsDecisionSourceV1?> ReadCogsDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); token.ThrowIfCancellationRequested();
        await freshReadFence(token); token.ThrowIfCancellationRequested(); var companyId = Company();
        var row = await (from p in db.InvoiceLineCogsProposals.AsNoTracking()
                       where p.CompanyId == companyId && p.Id == proposalId
                       join r in db.InvoiceLineCogsRevisions.AsNoTracking().Where(x => x.CompanyId == companyId)
                           on new { p.CompanyId, ProposalId = p.Id } equals new { r.CompanyId, r.ProposalId } into decisions
                       from r in decisions.DefaultIfEmpty()
                       select new { Prepared = p, Approved = r, CurrentLineVersion = db.InvoiceLineCogsRevisions
                           .Where(x => x.CompanyId == companyId && x.InvoiceId == p.InvoiceId && x.InvoiceLineId == p.InvoiceLineId)
                           .Max(x => (long?)x.Version) ?? 0 }).SingleOrDefaultAsync(token);
        await freshReadFence(token); token.ThrowIfCancellationRequested(); RequireCompany(companyId);
        if (row is null) return null;
        try
        {
            var p = row.Prepared; var prepared = DecisionJson<InvoiceLineCogsCorrectionViewV1>(p.ViewJson);
            var source = DecisionJson<FinalizedInvoiceLineSourceV1>(p.SourceJson); ValidateSource(source);
            if (prepared.CompanyId != companyId || prepared.ProposalId != p.Id || prepared.InvoiceId != p.InvoiceId
                || prepared.InvoiceLineId != p.InvoiceLineId || prepared.ExpectedLineVersion != p.ExpectedLineVersion
                || prepared.PreparedBy != p.PreparedBy || prepared.PreparedAtUtc != p.PreparedAtUtc) return Malformed();
            var original = row.Approved is null ? prepared : DecisionJson<InvoiceLineCogsCorrectionViewV1>(row.Approved.ViewJson);
            var bound = false;
            if (row.Approved is { } r)
            {
                var revision = DecisionJson<InvoiceLineCogsRevisionViewV1>(r.RevisionJson);
                bound = p.ExpectedLineVersion < long.MaxValue && r.CompanyId == companyId && r.InvoiceId == p.InvoiceId && r.InvoiceLineId == p.InvoiceLineId
                    && r.ProposalId == p.Id && r.Version == p.ExpectedLineVersion + 1 && r.PreparedBy == p.PreparedBy
                    && r.ApprovedBy == original.ApprovedBy && r.ApprovedAtUtc == original.ApprovedAtUtc
                    && revision.Version == r.Version && revision.ProposalId == p.Id && revision.PreparedBy == p.PreparedBy
                    && revision.ApprovedBy == r.ApprovedBy && revision.ApprovedAtUtc == r.ApprovedAtUtc
                    && revision.Classification == original.Classification && Encode(revision.AccountPlan) == Encode(original.AccountPlan)
                    && revision.PeriodId == original.PeriodId && revision.TransactionDate == original.TransactionDate && revision.AccountingDate == original.AccountingDate
                    && revision.Reason == original.Reason && revision.ReversingCorrectionJournalId == original.ReversingCorrectionJournalId
                    && revision.ReplacementCorrectionJournalId == original.ReplacementCorrectionJournalId && Encode(revision.AttributedCorrections) == Encode(original.AttributedCorrections);
                if (!bound) return Malformed();
            }
            return new(prepared, original, FinalizedInvoiceLineReferenceV1.From(source), AccountingInventoryCostSourceValidationV1.Sha256(p.SourceJson), row.CurrentLineVersion, bound);
        }
        catch (JsonException) { return Malformed(); }
        catch (AccountingStoreException) { return Malformed(); }
        static InvoiceLineCogsDecisionSourceV1 Malformed() => new(null!, null!, null!, "", 0, false);
    }
    private static T DecisionJson<T>(string json) where T : class
    {
        var value = JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException();
        if (!JsonNode.DeepEquals(JsonNode.Parse(json), JsonSerializer.SerializeToNode(value, Json))) throw new JsonException();
        return value;
    }
}
