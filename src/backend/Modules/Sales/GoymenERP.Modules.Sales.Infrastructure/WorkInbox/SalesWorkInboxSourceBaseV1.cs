using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the fence every Sales Work inbox source shares, in the style of
/// <c>SalesOrderDraftPendingApprovalReadServiceV1</c>. Before and after the owner read: the resolved company is the caller's,
/// the authenticated user is the caller and may access the company, and the source's approver permission is granted
/// company-wide. Anything else is <see cref="WorkInboxSourceResultV1.Denied"/>; a read failure (not a cancellation) is
/// <see cref="WorkInboxSourceResultV1.Unavailable"/>. Price figures (floor, net, discount) are shown only to a caller who also
/// holds the Sales price-read permission company-wide (the owner's price disclosure); otherwise the rule carries no numbers.
/// </summary>
internal abstract class SalesWorkInboxSourceBaseV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority) : IWorkInboxSourceV1
{
    public const string ModuleName = "sales";

    public string Module => ModuleName;

    public abstract string Kind { get; }

    public abstract string ApproverPermission { get; }

    public WorkApprovalDurationClassV1 DurationClass => WorkApprovalDurationClassV1.BusinessDays;

    /// <summary>Whether the source's rules carry price figures that the owner discloses only under its price-read permission.</summary>
    protected virtual bool CarriesPriceFigures => false;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(maxCount, 1, NativeApprovalQueueRulesV1.MaxItemsPerSource);

        try
        {
            if (!await AllowedAsync(context, ApproverPermission, cancellationToken))
            {
                return WorkInboxSourceResultV1.Denied();
            }

            var disclosePrices = CarriesPriceFigures
                && await AllowedAsync(context, SalesPermissionManifestV1.OrdersPriceRead, cancellationToken);
            var (items, hasMore) = await ReadAsync(context.CompanyId, take, disclosePrices, cancellationToken);
            if (!await AllowedAsync(context, ApproverPermission, cancellationToken))
            {
                return WorkInboxSourceResultV1.Denied();
            }

            return WorkInboxSourceResultV1.Found(context.CompanyId, items, hasMore);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }
    }

    /// <summary>
    /// The owner read for <paramref name="companyId"/>: at most <paramref name="take"/> items, oldest first, and whether more
    /// wait. Price figures go into the rules only when <paramref name="disclosePrices"/> is true.
    /// </summary>
    protected abstract Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken);

    private async Task<bool> AllowedAsync(WorkInboxSourceContextV1 context, string permission, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return false;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
        return scope is { IsGranted: true, IsCompanyWide: true } && SameContext(context);
    }

    private bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);
}
