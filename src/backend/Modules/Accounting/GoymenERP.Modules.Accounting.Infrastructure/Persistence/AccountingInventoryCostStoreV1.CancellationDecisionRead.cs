using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;
namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingInventoryCostStoreV1 : IAccountingInventoryCostCancellationDecisionStoreV1
{
    public async Task<AccountingInventoryCostCancellationDecisionDetailV1?> ReadCancellationDecisionAsync(Guid proposalId,
        long? preparedCancellationVersion, Func<CancellationToken, Task> freshReadFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshReadFence); token.ThrowIfCancellationRequested();
        if (decisionReadFence is not null) throw Refuse("inventory_cost_source_unavailable");
        var captured = Company();
        async Task Fence(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); RequireCompany(captured); await freshReadFence(ct); ct.ThrowIfCancellationRequested(); RequireCompany(captured); }
        decisionReadOwnedAtEntry = IsOwnedApproval; decisionReadFence = Fence;
        try
        {
            var versions = db.InventoryCostCancellationVersions.AsNoTracking().Where(x => x.CompanyId == captured && x.InventoryCostProposalId == proposalId);
            var selected = preparedCancellationVersion is { } version
                ? await ApprovalRead(() => versions.SingleOrDefaultAsync(x => x.Version == version, token), token)
                : await ApprovalRead(() => versions.OrderByDescending(x => x.Version).FirstOrDefaultAsync(token), token);
            if (selected is null) return null;
            var preparedRow = selected.Status == 1 ? selected : selected.Status == 2 && selected.Version > 1
                ? await ApprovalRead(() => versions.SingleOrDefaultAsync(x => x.Version == selected.Version - 1, token), token) : null;
            if (preparedRow is null || preparedRow.Status != 1) throw Refuse("inventory_cost_source_conflict");
            var originalRow = await ApprovalRead(() => db.InventoryCostProposals.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == captured
                && x.Id == proposalId && x.Version == preparedRow.InitialProposalVersion, token), token);
            if (originalRow is null) throw Refuse("inventory_cost_source_conflict");
            var original = await ApprovalRead(() => ReadStored(originalRow, token), token);
            var prepared = ReadCancellationVersion(preparedRow, original);
            if (!DecisionJsonMatches(preparedRow.ViewJson, prepared) || !DecisionJsonMatches(originalRow.ViewJson, original)) throw Refuse("inventory_cost_source_conflict");
            var next = prepared.Version < long.MaxValue
                ? await ApprovalRead(() => versions.SingleOrDefaultAsync(x => x.Version == prepared.Version + 1, token), token) : null;
            // A re-preparation has a new immutable Id; it never becomes the original M+1 answer.
            var current = next is { Status: 2 } && next.Id == prepared.Id && next.InitialProposalVersion == preparedRow.InitialProposalVersion
                ? ReadCancellationVersion(next, original) : prepared;
            if (current != prepared && !DecisionJsonMatches(next!.ViewJson, current)) throw Refuse("inventory_cost_source_conflict");
            var cancellation = await ApprovalRead(() => db.SourceInvoiceCancellations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == captured
                && x.CancellationId == prepared.CancellationId, token), token);
            if (cancellation is null) throw Refuse("inventory_cost_source_conflict");
            var latest = await ApprovalRead(() => versions.OrderByDescending(x => x.Version).Select(x => x.Version).FirstAsync(token), token);
            var latestOuter = await ApprovalRead(() => db.InventoryCostProposals.AsNoTracking().Where(x => x.CompanyId == captured && x.Id == proposalId)
                .OrderByDescending(x => x.Version).Select(x => x.Version).FirstAsync(token), token);
            var source = new AccountingInventoryCostCancellationSourceV1(cancellation.CompanyId, cancellation.CancellationId, cancellation.DocumentKind,
                cancellation.SourceDocumentId, cancellation.InvoiceId, cancellation.FinalizedVersion, cancellation.CancelledBy, cancellation.CancelledAtUtc,
                cancellation.Reason, cancellation.RequestHash, cancellation.CorrelationId, cancellation.RecordedAtUtc);
            var detail = new AccountingInventoryCostCancellationDecisionDetailV1(original, prepared, current, preparedRow.RecordedAtUtc, source, "", "")
                { Pending = latest == prepared.Version && latestOuter == original.Version && current.Status == "Prepared" };
            detail = detail with { SourceFactsDigest = AccountingInventoryCostServiceV1.CancellationSourceFactsDigest(detail),
                PreviewDigest = AccountingInventoryCostServiceV1.CancellationPreviewDigest(detail) };
            await Fence(token);
            return detail;
        }
        catch (JsonException) { await Fence(token); throw Refuse("inventory_cost_source_unavailable"); }
        finally { decisionReadFence = null; decisionReadOwnedAtEntry = false; }
    }
}
