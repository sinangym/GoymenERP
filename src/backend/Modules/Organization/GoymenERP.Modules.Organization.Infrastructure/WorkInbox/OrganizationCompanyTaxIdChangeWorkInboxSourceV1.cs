using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

/// <summary>
/// WP7-2 (AI-D31, AI-D32, YOL-D18; KIM-S23, KIM-D28): the company's own VKN change request waiting for another person's
/// decision, as Organization lists it for the Work inbox. A request waits while it is Pending — exactly what
/// <c>DecideOrganizationCompanyTaxIdChangeV1</c> accepts; it is decided on the "Şirket Vergi Numarası" screen by a holder of
/// <c>organization.company.tax-id.approve</c> granted company-wide, never by its preparer
/// (<see cref="CompanyTaxIdChangeRequest.IsRequestedBy"/>). A request prepared against a company version or VKN that has
/// moved since can only be rejected; its rule says so. The label is the proposed VKN (a company tax number, not personal data).
/// </summary>
internal sealed class OrganizationCompanyTaxIdChangeWorkInboxSourceV1(
    OrganizationDbContext database,
    WorkInboxCompanyWideFenceV1 fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "organization";
    public const string KindName = "organization.company-tax-id-change.v1";

    /// <summary>The company's VKN changes only when another person approves the request (hazırlayan ≠ onaylayan).</summary>
    public const string ApprovalRequiredRule = "organization.company-tax-id-change-approval-required";

    /// <summary>The company's VKN or version moved after the request was prepared: it can only be rejected.</summary>
    public const string StaleRule = "organization.company-tax-id-change-stale";

    /// <summary>The company has no VKN yet: this is its first VKN.</summary>
    public const string FirstVknRule = "organization.company-tax-id-first";

    /// <summary>The "Şirket Vergi Numarası" screen (<c>ORGANIZATION_ROUTE companyTaxId</c>).</summary>
    internal const string ScreenPath = "/ayarlar/organizasyon/sirket-vergi-no";

    private static readonly IReadOnlyList<string> Permissions = [OrganizationCompanyTaxIdPermissionsV1.Approve];

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => OrganizationCompanyTaxIdPermissionsV1.Approve;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } refused)
        {
            return refused;
        }

        List<CompanyTaxIdChangeRequest> rows;
        Company? company;
        int total;
        try
        {
            var pending = database.CompanyTaxIdChangeRequests.AsNoTracking()
                .Where(request => request.CompanyId == context.CompanyId && request.Status == CompanyTaxIdChangeStatus.Pending);
            total = await pending.CountAsync(cancellationToken);
            rows = await pending.OrderBy(request => request.RequestedAtUtc).ThenBy(request => request.Id)
                .Take(maxCount).ToListAsync(cancellationToken);
            company = await database.Companies.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == context.CompanyId, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.CheckAsync(context, Permissions, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();
        }

        if (await fence.CheckAsync(context, Permissions, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (company is null && rows.Count > 0)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }

        var items = new List<WorkInboxItemV1>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.RequestedBy) || string.IsNullOrWhiteSpace(row.ProposedVkn))
            {
                return WorkInboxSourceResultV1.Unavailable();
            }

            var rules = new List<WorkApprovalRuleResultV1>(3) { new(ApprovalRequiredRule, null, null, null, null) };
            if (row.CurrentVkn is null)
            {
                rules.Add(new(FirstVknRule, null, null, null, null));
            }

            if (!row.IsCurrentFor(company!))
            {
                rules.Add(new(StaleRule, null, null, null, null));
            }

            items.Add(new WorkInboxItemV1(ModuleName, KindName, row.Id, row.ProposedVkn, row.RequestedAtUtc.ToUniversalTime(),
                ScreenPath, row.RequestedBy, rules, []));
        }

        return WorkInboxSourceResultV1.Found(context.CompanyId, items, total > rows.Count);
    }
}
