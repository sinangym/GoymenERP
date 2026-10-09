using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed class SalesOrderDraftPendingApprovalReadServiceV1(
    ISalesDraftService drafts,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : ISalesOrderDraftPendingApprovalReadServiceV1
{
    private const int PageSize = 50;
    private const int MaximumCursorLength = 8192;
    private const string PendingApproval = "PendingApproval";
    private const string DraftState = "Draft";

    public async Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>> ListAsync(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (await FenceAsync<SalesOrderDraftPendingApprovalPageV1>(context, cancellationToken) is { } refused)
        {
            return refused;
        }

        SalesOrderDraftListResponse source;
        try
        {
            source = await drafts.GetDraftListAsync(
                new SalesOrderDraftListQuery(PageSize, cursor, PendingApproval), cancellationToken);
        }
        catch (SalesDraftServiceException exception)
        {
            return await OwnerFailureAsync<SalesOrderDraftPendingApprovalPageV1>(
                context, exception, cursorRead: true, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await UnavailableAfterOwnerFailureAsync<SalesOrderDraftPendingApprovalPageV1>(
                context, cancellationToken);
        }

        if (await FenceAsync<SalesOrderDraftPendingApprovalPageV1>(context, cancellationToken) is { } afterList)
        {
            return afterList;
        }
        if (source is null || source.Items is null || source.Items.Count > PageSize
            || source.NextCursor is not null
            && (string.IsNullOrWhiteSpace(source.NextCursor) || source.NextCursor.Length > MaximumCursorLength))
        {
            return Unavailable<SalesOrderDraftPendingApprovalPageV1>();
        }

        var items = new List<SalesOrderDraftPendingApprovalItemV1>(source.Items.Count);
        var seen = new HashSet<Guid>();
        foreach (var summary in source.Items)
        {
            if (!ValidSummary(context, summary) || !seen.Add(summary.Id))
            {
                return Unavailable<SalesOrderDraftPendingApprovalPageV1>();
            }
            if (await FenceAsync<SalesOrderDraftPendingApprovalPageV1>(context, cancellationToken) is { } beforeDetail)
            {
                return beforeDetail;
            }

            SalesOrderDraftDetailView detail;
            try
            {
                detail = await drafts.GetDraftDetailAsync(summary.Id, cancellationToken);
            }
            catch (SalesDraftServiceException exception)
            {
                return await OwnerFailureAsync<SalesOrderDraftPendingApprovalPageV1>(
                    context, exception, cursorRead: false, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return await UnavailableAfterOwnerFailureAsync<SalesOrderDraftPendingApprovalPageV1>(
                    context, cancellationToken);
            }

            if (await FenceAsync<SalesOrderDraftPendingApprovalPageV1>(context, cancellationToken) is { } afterDetail)
            {
                return afterDetail;
            }
            if (!TryItem(context, detail, summary, out var item, out var restricted))
            {
                return Unavailable<SalesOrderDraftPendingApprovalPageV1>();
            }
            if (!restricted)
            {
                items.Add(item);
            }
        }

        return new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
            new SalesOrderDraftPendingApprovalPageV1(context.CompanyId, items, source.NextCursor));
    }

    public async Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalDetailV1>> GetAsync(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (draftId == Guid.Empty)
        {
            return NotFound<SalesOrderDraftPendingApprovalDetailV1>();
        }
        if (await FenceAsync<SalesOrderDraftPendingApprovalDetailV1>(context, cancellationToken) is { } refused)
        {
            return refused;
        }

        SalesOrderDraftDetailView detail;
        try
        {
            detail = await drafts.GetDraftDetailAsync(draftId, cancellationToken);
        }
        catch (SalesDraftServiceException exception)
        {
            return await OwnerFailureAsync<SalesOrderDraftPendingApprovalDetailV1>(
                context, exception, cursorRead: false, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await UnavailableAfterOwnerFailureAsync<SalesOrderDraftPendingApprovalDetailV1>(
                context, cancellationToken);
        }

        if (await FenceAsync<SalesOrderDraftPendingApprovalDetailV1>(context, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (!TryItem(context, detail, summary: null, out var item, out var restricted))
        {
            return Unavailable<SalesOrderDraftPendingApprovalDetailV1>();
        }
        return restricted
            ? NotFound<SalesOrderDraftPendingApprovalDetailV1>()
            : new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
                new SalesOrderDraftPendingApprovalDetailV1(item));
    }

    private async Task<SalesOrderDraftPendingApprovalReadResultV1<T>?> FenceAsync<T>(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        CancellationToken cancellationToken) where T : class
    {
        if (!SameContext(context))
        {
            return Denied<T>();
        }

        try
        {
            var read = await authority.RevalidatePermissionScopeAsync(
                SalesPermissionManifestV1.DraftsRead, context.CompanyId, cancellationToken);
            if (!Allowed(read) || !SameContext(context))
            {
                return Denied<T>();
            }
            var approve = await authority.RevalidatePermissionScopeAsync(
                SalesPermissionManifestV1.OrdersApprove, context.CompanyId, cancellationToken);
            return Allowed(approve) && SameContext(context) ? null : Denied<T>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Unavailable<T>();
        }
    }

    private async Task<SalesOrderDraftPendingApprovalReadResultV1<T>> OwnerFailureAsync<T>(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        SalesDraftServiceException exception,
        bool cursorRead,
        CancellationToken cancellationToken) where T : class
    {
        if (await FenceAsync<T>(context, cancellationToken) is { } refused)
        {
            return refused;
        }
        return exception.Status switch
        {
            401 or 403 => Denied<T>(),
            404 => NotFound<T>(),
            400 when cursorRead => NotFound<T>(),
            _ => Unavailable<T>(),
        };
    }

    private async Task<SalesOrderDraftPendingApprovalReadResultV1<T>> UnavailableAfterOwnerFailureAsync<T>(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        CancellationToken cancellationToken) where T : class =>
        await FenceAsync<T>(context, cancellationToken) ?? Unavailable<T>();

    private bool SameContext(SalesOrderDraftPendingApprovalReadContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.ActorId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    private static bool Allowed(PermissionScope scope) => scope.IsGranted && scope.IsCompanyWide;

    private static bool ValidSummary(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        SalesOrderDraftSummaryView? summary) =>
        summary is not null
        && summary.Id != Guid.Empty
        && summary.CompanyId == context.CompanyId
        && !string.IsNullOrWhiteSpace(summary.DocumentNo)
        && string.Equals(summary.State, DraftState, StringComparison.Ordinal)
        && string.Equals(summary.ApprovalState, PendingApproval, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(summary.Version)
        && !string.IsNullOrWhiteSpace(summary.Revision);

    private static bool TryItem(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        SalesOrderDraftDetailView? detail,
        SalesOrderDraftSummaryView? summary,
        out SalesOrderDraftPendingApprovalItemV1 item,
        out bool restricted)
    {
        item = null!;
        restricted = false;
        if (detail is null || !ValidSummary(context, detail.Draft)
            || summary is not null && (detail.Draft.Id != summary.Id
                || detail.Draft.CompanyId != summary.CompanyId
                || !string.Equals(detail.Draft.DocumentNo, summary.DocumentNo, StringComparison.Ordinal)
                || !string.Equals(detail.Draft.Version, summary.Version, StringComparison.Ordinal)
                || !string.Equals(detail.Draft.Revision, summary.Revision, StringComparison.Ordinal))
            || detail.Approval is null
            || string.IsNullOrWhiteSpace(detail.Approval.SubmittedBy)
            || detail.Approval.SubmittedAtUtc is null
            || detail.Approval.DecidedBy is not null
            || detail.Approval.DecidedAtUtc is not null
            || detail.Approval.RejectionReason is not null
            || detail.IsOwnDecisionRestricted && !detail.IsOwnDraft)
        {
            return false;
        }

        restricted = detail.IsOwnDecisionRestricted;
        item = new(detail.Draft.Id, detail.Draft.CompanyId, detail.Draft.DocumentNo,
            detail.Draft.Version, detail.Draft.Revision, detail.Approval.SubmittedBy,
            detail.Approval.SubmittedAtUtc.Value, detail.IsOwnDraft);
        return true;
    }

    private static SalesOrderDraftPendingApprovalReadResultV1<T> Denied<T>() where T : class =>
        new(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, null);

    private static SalesOrderDraftPendingApprovalReadResultV1<T> NotFound<T>() where T : class =>
        new(SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound, null);

    private static SalesOrderDraftPendingApprovalReadResultV1<T> Unavailable<T>() where T : class =>
        new(SalesOrderDraftPendingApprovalReadOutcomeV1.Unavailable, null);
}
