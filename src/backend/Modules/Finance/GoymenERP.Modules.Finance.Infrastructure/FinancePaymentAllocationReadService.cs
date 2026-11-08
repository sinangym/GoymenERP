using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// F3a / API-20: the payment allocations' independently-authorized, company-scoped list and detail, fenced by the same
/// allocation read permission as the receipt allocations (no new grant).
/// </summary>
public sealed class FinancePaymentAllocationReadService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions) : IFinancePaymentAllocationReadService
{
    private const int WindowSize = 50;

    public async Task<ReadResult<FinancePaymentAllocationPage>> ListAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (await FenceAsync(context, null, cancellationToken) is { } refused)
        {
            return new ReadResult<FinancePaymentAllocationPage>(refused, null);
        }

        var rows = await dbContext.PaymentAllocations.AsNoTracking()
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(WindowSize + 1)
            .ToListAsync(cancellationToken);

        if (await FenceAsync(context, null, cancellationToken) is { } fenced)
        {
            return new ReadResult<FinancePaymentAllocationPage>(fenced, null);
        }

        return new ReadResult<FinancePaymentAllocationPage>(ReadOutcome.Found,
            new FinancePaymentAllocationPage(context.CompanyId, [.. rows.Take(WindowSize).Select(View)], rows.Count > WindowSize));
    }

    public async Task<ReadResult<FinancePaymentAllocationDetail>> GetAuthorizedAsync(
        FinanceContext context,
        Guid allocationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (allocationId == Guid.Empty)
        {
            return new ReadResult<FinancePaymentAllocationDetail>(ReadOutcome.NotFound, null);
        }
        if (await FenceAsync(context, allocationId, cancellationToken) is { } refused)
        {
            return new ReadResult<FinancePaymentAllocationDetail>(refused, null);
        }

        var allocation = await dbContext.PaymentAllocations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == allocationId, cancellationToken);
        if (await FenceAsync(context, allocationId, cancellationToken) is { } afterAllocation)
        {
            return new ReadResult<FinancePaymentAllocationDetail>(afterAllocation, null);
        }
        if (allocation is null)
        {
            return new ReadResult<FinancePaymentAllocationDetail>(ReadOutcome.NotFound, null);
        }

        var openItem = await dbContext.OpenItems.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == allocation.OpenItemId, cancellationToken);
        if (await FenceAsync(context, allocationId, cancellationToken) is { } afterOpenItem)
        {
            return new ReadResult<FinancePaymentAllocationDetail>(afterOpenItem, null);
        }
        if (openItem is null || openItem.CompanyId != allocation.CompanyId
            || openItem.CurrencyId != allocation.CurrencyId)
        {
            return new ReadResult<FinancePaymentAllocationDetail>(ReadOutcome.Unavailable, null);
        }

        return new ReadResult<FinancePaymentAllocationDetail>(ReadOutcome.Found,
            new FinancePaymentAllocationDetail(View(allocation), openItem.Version.ToString(CultureInfo.InvariantCulture)));
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

    private static FinancePaymentAllocationView View(FinancePaymentAllocationRow row) =>
        FinancePaymentAllocationService.View(row);
}
