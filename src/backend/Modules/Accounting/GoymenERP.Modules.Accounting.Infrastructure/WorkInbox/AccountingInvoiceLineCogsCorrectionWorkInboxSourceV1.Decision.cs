using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Invoice line COGS decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Invoice line COGS decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.OriginalCorrelationId is not null || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.Resource is not { } detail
            || !InvoiceLineCogsCorrectionServiceV1.ValidCogsDecisionDetail(detail) || detail.Proposal.CompanyId != context.CompanyId || detail.Proposal.ProposalId != itemId
            || AccountingInvoiceLineCogsDecisionBindingV1.FromView(detail.Proposal) is not { } item)
            throw new InvalidOperationException("Invoice line COGS decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        var p = detail.Proposal;
        return new(context.CompanyId, item, p.Version.ToString(CultureInfo.InvariantCulture), p.Status == "Prepared" && detail.CurrentLineVersion == p.ExpectedLineVersion)
        { InvoiceLineCogsProof = new(context.CompanyId, itemId, p.InvoiceId, p.InvoiceLineId, p.FinalizedVersion, p.ExpectedLineVersion,
            p.PreparedBy, p.PreparedAtUtc, p.Status, detail.SourceFactsDigest, detail.PreviewDigest) };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Invoice line COGS decision authority unavailable.");
        }
    }
}
