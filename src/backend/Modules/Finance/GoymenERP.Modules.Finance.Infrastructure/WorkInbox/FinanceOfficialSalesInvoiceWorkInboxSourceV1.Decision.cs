using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceOfficialSalesInvoiceWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        await Fence(token);
        if (owner is not IFinanceOfficialSalesInvoiceDecisionReadV1 reader) throw new InvalidOperationException("Official invoice decision owner is unavailable.");
        var result = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId, Fence, token);
        await Fence(token);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new UnauthorizedAccessException();
        if (result.Outcome == ReadOutcome.NotFound && result.Resource is null) return null;
        if (result.Outcome != ReadOutcome.Found || !FinanceOfficialSalesInvoiceServiceV1.ValidDecisionView(context.CompanyId, itemId, result.Resource))
            throw new InvalidOperationException("Official invoice decision evidence is unavailable.");
        var v = result.Resource!;
        // Preserve the legacy native caption, amount rule, references and link while the purpose is staged.
        var item = Item(new() { Id = v.InvoiceId, CompanyId = v.CompanyId, OfficialNo = v.OfficialNo,
            CustomerPartyId = v.CustomerPartyId, CurrencyId = v.CurrencyId, PdfDocumentId = v.PdfDocumentId,
            RecordedBy = v.RecordedBy, RecordedAtUtc = v.RecordedAtUtc, PayableAmountText = v.PayableAmount }, true)
            ?? throw new InvalidOperationException("Official invoice decision item is invalid.");
        return new(context.CompanyId, item, v.Version, v.Status == FinanceOfficialSalesInvoiceStatusV1.PendingApproval,
            OfficialSalesInvoiceProof: new(v.CompanyId, v.InvoiceId, v.OfficialNo, v.CustomerPartyId, v.CurrencyId,
                v.PdfDocumentId, v.RecordedBy, v.RecordedAtUtc, v.Status.ToString(),
                FinanceOfficialSalesInvoiceServiceV1.RecordedFactsDigest(v), FinanceOfficialSalesInvoiceServiceV1.DecisionPreviewDigest(v)));

        async Task Fence(CancellationToken ct)
        {
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refused = await fence.CheckAsync(context, ApproverPermission, ct);
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Official invoice decision authority is unavailable.");
        }
    }
}
