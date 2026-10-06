using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>F-03's independently-authorized, company-scoped allocation list and detail.</summary>
public sealed class FinanceAllocationReadService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions) : IFinanceAllocationReadService
{
    private const int WindowSize = 50;

    public async Task<ReadResult<FinanceAllocationPage>> ListAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (await FenceAsync(context, null, cancellationToken) is { } refused)
        {
            return new ReadResult<FinanceAllocationPage>(refused, null);
        }

        var rows = await dbContext.Allocations.AsNoTracking()
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(WindowSize + 1)
            .ToListAsync(cancellationToken);

        if (await FenceAsync(context, null, cancellationToken) is { } fenced)
        {
            return new ReadResult<FinanceAllocationPage>(fenced, null);
        }

        return new ReadResult<FinanceAllocationPage>(ReadOutcome.Found,
            new FinanceAllocationPage(context.CompanyId, [.. rows.Take(WindowSize).Select(View)], rows.Count > WindowSize));
    }

    public async Task<ReadResult<FinanceAllocationDetail>> GetAuthorizedAsync(
        FinanceContext context,
        Guid allocationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (allocationId == Guid.Empty)
        {
            return new ReadResult<FinanceAllocationDetail>(ReadOutcome.NotFound, null);
        }
        if (await FenceAsync(context, allocationId, cancellationToken) is { } refused)
        {
            return new ReadResult<FinanceAllocationDetail>(refused, null);
        }

        var allocation = await dbContext.Allocations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == allocationId, cancellationToken);
        if (await FenceAsync(context, allocationId, cancellationToken) is { } afterAllocation)
        {
            return new ReadResult<FinanceAllocationDetail>(afterAllocation, null);
        }
        if (allocation is null)
        {
            return new ReadResult<FinanceAllocationDetail>(ReadOutcome.NotFound, null);
        }

        var openItem = await dbContext.OpenItems.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == allocation.OpenItemId, cancellationToken);
        if (await FenceAsync(context, allocationId, cancellationToken) is { } afterOpenItem)
        {
            return new ReadResult<FinanceAllocationDetail>(afterOpenItem, null);
        }
        if (openItem is null || openItem.CompanyId != allocation.CompanyId
            || openItem.CurrencyId != allocation.CurrencyId)
        {
            return new ReadResult<FinanceAllocationDetail>(ReadOutcome.Unavailable, null);
        }

        return new ReadResult<FinanceAllocationDetail>(ReadOutcome.Found,
            new FinanceAllocationDetail(View(allocation), openItem.Version.ToString(CultureInfo.InvariantCulture)));
    }

    private async Task<ReadOutcome?> FenceAsync(
        FinanceContext context,
        Guid? allocationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return ReadOutcome.Denied;
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadAllocation, allocationId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;
        }
        return SameContext(context) ? null : ReadOutcome.Denied;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private static FinanceAllocationView View(FinanceAllocationRow row) => new(
        row.Id, row.CompanyId, row.SourceId, row.OpenItemId, row.CurrencyId, row.AmountText,
        (FinanceAllocationStatus)row.Status, row.Version.ToString(CultureInfo.InvariantCulture),
        row.Reason, row.CreatedBy, row.CreatedAtUtc, row.ReleasedBy, row.ReleasedAtUtc, row.ReleaseReason);
}
