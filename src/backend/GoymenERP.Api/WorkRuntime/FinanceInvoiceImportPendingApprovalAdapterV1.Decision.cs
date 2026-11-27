using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Api.WorkRuntime;

internal sealed partial class FinanceInvoiceImportPendingApprovalAdapterV1 : INativeApprovalDecisionReadAdapterV1
{
    public bool HasDecisionRead => owner is IFinanceOfficialInvoiceImportDecisionReadServiceV1;

    public async Task<WorkInboxSourceResultV1> ListDecisionPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        if (context.GrantScope is not null) return WorkInboxSourceResultV1.Denied();
        var readContext = new NativeApprovalReadContextV1(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId);
        var result = NativeApprovalReadQueryV1.ValidatePage(Kind, readContext, await ListAsync(readContext, null, token));
        if (result.Outcome == NativeApprovalReadOutcomeV1.Denied) return WorkInboxSourceResultV1.Denied();
        if (result is not { Outcome: NativeApprovalReadOutcomeV1.Found, Resource: { } page }) return WorkInboxSourceResultV1.Unavailable();
        var mapper = new NativeApprovalWorkInboxSourceV1(this);
        var items = page.Items.Take(maxCount).Select(mapper.Item).ToArray();
        if (items.Any(item => item is null)) return WorkInboxSourceResultV1.Unavailable();
        // Keep the existing read-only native detail link until the paired UI is delivered.
        return WorkInboxSourceResultV1.Found(page.CompanyId, items.Select(item => item!).ToArray(), page.NextCursor is not null || page.Items.Count > maxCount);
    }

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token)
    {
        if (context.GrantScope is not null) throw new UnauthorizedAccessException();
        if (owner is not IFinanceOfficialInvoiceImportDecisionReadServiceV1 decision) throw new InvalidOperationException("Import decision read is unavailable.");
        var finance = new FinanceContext(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId);
        var result = await decision.GetDecisionAsync(finance, id, token);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new UnauthorizedAccessException();
        if (result.Outcome == ReadOutcome.NotFound && result.Resource is null) return null;
        if (result.Outcome != ReadOutcome.Found || !FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.ValidDecisionDetail(finance, id, result.Resource))
            throw new InvalidOperationException("Import decision evidence is unavailable.");
        var b = result.Resource!.Batch;
        var native = new FinanceInvoiceImportNativeApprovalReadItemV1(b.BatchId, b.CompanyId, b.Status.ToString(), b.Version,
            b.BatchVersion, b.BatchChecksum, b.TemplateVersion, b.RowCount, b.UploadedBy, b.UploadedAtUtc);
        var item = new NativeApprovalWorkInboxSourceV1(this).Item(native)
            ?? throw new InvalidOperationException("Import decision item is invalid.");
        return new(b.CompanyId, item, b.Version, b.Status == FinanceOfficialInvoiceImportApprovalStatusV1.Pending,
            OfficialInvoiceImportProof: new(b.CompanyId, b.BatchId, b.BatchVersion, b.BatchChecksum, b.TemplateVersion,
                b.RowCount, b.UploadedBy, b.UploadedAtUtc, b.ApprovedBy, b.ApprovedAtUtc,
                FinanceOfficialInvoiceImportPendingApprovalReadServiceV1.DecisionPreviewDigest(result.Resource)));
    }
}
