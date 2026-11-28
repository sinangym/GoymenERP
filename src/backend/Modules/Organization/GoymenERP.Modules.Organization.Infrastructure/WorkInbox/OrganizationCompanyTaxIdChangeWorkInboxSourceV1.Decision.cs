using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

internal sealed partial class OrganizationCompanyTaxIdChangeWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Company tax-id decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Company tax-id decision answer unavailable.");
        if (answer.Detail is null && answer.Status == OrganizationCompanyTaxIdDecisionReadStatusV1.Denied) throw new UnauthorizedAccessException();
        if (answer.Detail is null && answer.Status == OrganizationCompanyTaxIdDecisionReadStatusV1.NotFound) return null;
        if (answer.Status != OrganizationCompanyTaxIdDecisionReadStatusV1.Found || answer.Detail is not { } detail
            || detail.CompanyId != context.CompanyId || detail.Request?.Id != itemId
            || !OrganizationCompanyTaxIdDecisionBindingV1.ValidDetail(detail))
            throw new InvalidOperationException("Company tax-id decision facts unavailable.");
        var view = detail.Request;
        var item = OrganizationCompanyTaxIdDecisionBindingV1.Item(view.Id, view.ProposedVkn, view.RequestedBy,
            view.RequestedAtUtc, view.CurrentVkn is null, detail.IsCurrent, view.Status == "pending")!;
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, detail.CompanyVersion.ToString(CultureInfo.InvariantCulture), view.Status == "pending")
        { OrganizationCompanyTaxIdProof = new(detail.CompanyId, view.Id, detail.CompanyVersion, detail.CompanyVersionAtRequest,
            view.RequestedBy, view.RequestedAtUtc, view.Status, detail.IsCurrent, view.CurrentVkn is null, view.ProposedVkn,
            detail.SourceFactsDigest, detail.PreviewDigest) };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, Permissions, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Company tax-id decision authority unavailable.");
        }
    }
}
