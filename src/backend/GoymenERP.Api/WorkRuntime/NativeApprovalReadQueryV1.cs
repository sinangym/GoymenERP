using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.WorkRuntime;

internal sealed class NativeApprovalReadQueryV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly ICorrelationContext correlationContext;
    private readonly IReadOnlyDictionary<string, INativeApprovalReadAdapterV1> adapters;
    private readonly bool registrationIsValid;

    public NativeApprovalReadQueryV1(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        ICorrelationContext correlationContext,
        IEnumerable<INativeApprovalReadAdapterV1> adapters)
    {
        this.companyContext = companyContext;
        this.currentUser = currentUser;
        this.correlationContext = correlationContext;

        var byKind = new Dictionary<string, INativeApprovalReadAdapterV1>(StringComparer.Ordinal);
        var valid = true;
        foreach (var adapter in adapters)
        {
            if (adapter is null
                || string.IsNullOrWhiteSpace(adapter.Kind)
                || !NativeApprovalKindsV1.IsKnown(adapter.Kind)
                || !byKind.TryAdd(adapter.Kind, adapter))
            {
                valid = false;
                continue;
            }
        }

        this.adapters = byKind;
        registrationIsValid = valid;
    }

    public Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
        string? kind,
        string? cursor,
        CancellationToken cancellationToken) =>
        ListAsync(kind, cursor, includeInventoryLabelCode: false, cancellationToken);

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
        string? kind,
        string? cursor,
        bool includeInventoryLabelCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedKind = kind ?? NativeApprovalKindsV1.FinanceOfficialInvoiceImport;
        if (!NativeApprovalKindsV1.IsKnown(selectedKind))
        {
            return NotFound<NativeApprovalReadPageV1>();
        }
        if (!TryCapture(includeInventoryLabelCode, out var context, out var refusal))
        {
            return new(refusal, null);
        }
        if (!registrationIsValid || !adapters.TryGetValue(selectedKind, out var adapter))
        {
            return Unavailable<NativeApprovalReadPageV1>();
        }

        var result = await adapter.ListAsync(context, cursor, cancellationToken);
        if (!SameContext(context))
        {
            return Denied<NativeApprovalReadPageV1>();
        }
        return ValidatePage(selectedKind, context, result);
    }

    public Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
        string kind,
        Guid approvalId,
        CancellationToken cancellationToken) =>
        GetAsync(kind, approvalId, includeInventoryLabelCode: false, cancellationToken);

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
        string kind,
        Guid approvalId,
        bool includeInventoryLabelCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeApprovalKindsV1.IsKnown(kind) || approvalId == Guid.Empty)
        {
            return NotFound<NativeApprovalReadDetailV1>();
        }
        if (!TryCapture(includeInventoryLabelCode, out var context, out var refusal))
        {
            return new(refusal, null);
        }
        if (!registrationIsValid || !adapters.TryGetValue(kind, out var adapter))
        {
            return Unavailable<NativeApprovalReadDetailV1>();
        }

        var result = await adapter.GetAsync(context, approvalId, cancellationToken);
        if (!SameContext(context))
        {
            return Denied<NativeApprovalReadDetailV1>();
        }
        return ValidateDetail(kind, approvalId, context, result);
    }

    private static NativeApprovalReadResultV1<NativeApprovalReadPageV1> ValidatePage(
        string kind,
        NativeApprovalReadContextV1 context,
        NativeApprovalReadResultV1<NativeApprovalReadPageV1> result)
    {
        if (result.Outcome != NativeApprovalReadOutcomeV1.Found)
        {
            return StrictFailure<NativeApprovalReadPageV1>(result);
        }
        if (result.Resource is not { } page
            || page.CompanyId != context.CompanyId
            || page.Items is null
            || page.NextCursor is { Length: 0 })
        {
            return Unavailable<NativeApprovalReadPageV1>();
        }

        foreach (var item in page.Items)
        {
            if (!ValidItem(kind, context, item))
            {
                return Unavailable<NativeApprovalReadPageV1>();
            }
        }
        return result;
    }

    private static NativeApprovalReadResultV1<NativeApprovalReadDetailV1> ValidateDetail(
        string kind,
        Guid approvalId,
        NativeApprovalReadContextV1 context,
        NativeApprovalReadResultV1<NativeApprovalReadDetailV1> result)
    {
        if (result.Outcome != NativeApprovalReadOutcomeV1.Found)
        {
            return StrictFailure<NativeApprovalReadDetailV1>(result);
        }
        return result.Resource is { Item: { } item }
            && item.ApprovalId == approvalId
            && ValidItem(kind, context, item)
                ? result
                : Unavailable<NativeApprovalReadDetailV1>();
    }

    private static bool ValidItem(
        string kind,
        NativeApprovalReadContextV1 context,
        NativeApprovalReadItemV1? item) =>
        item is not null
        && string.Equals(item.Kind, kind, StringComparison.Ordinal)
        && item.ApprovalId != Guid.Empty
        && item.CompanyId == context.CompanyId
        && !string.IsNullOrWhiteSpace(item.Status)
        && !string.IsNullOrWhiteSpace(item.Version)
        && !string.IsNullOrWhiteSpace(item.SubmittedBy)
        && (!context.IncludeInventoryLabelCode
            || kind is not (NativeApprovalKindsV1.InventoryStockCount
                or NativeApprovalKindsV1.InventoryStockScrap)
            || item switch
            {
                InventoryStockCountNativeApprovalReadItemV1 count =>
                    !string.IsNullOrWhiteSpace(count.LabelCode),
                InventoryStockScrapNativeApprovalReadItemV1 scrap =>
                    !string.IsNullOrWhiteSpace(scrap.LabelCode),
                _ => false,
            });

    private static NativeApprovalReadResultV1<T> StrictFailure<T>(NativeApprovalReadResultV1<T> result)
        where T : class =>
        result.Resource is null
        && result.Outcome is NativeApprovalReadOutcomeV1.NotFound
            or NativeApprovalReadOutcomeV1.Denied
            or NativeApprovalReadOutcomeV1.Unavailable
                ? result
                : Unavailable<T>();

    private bool TryCapture(
        bool includeInventoryLabelCode,
        out NativeApprovalReadContextV1 context,
        out NativeApprovalReadOutcomeV1 refusal)
    {
        context = null!;
        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            refusal = NativeApprovalReadOutcomeV1.Denied;
            return false;
        }
        if (!correlationContext.IsResolved || string.IsNullOrWhiteSpace(correlationContext.CorrelationId))
        {
            refusal = NativeApprovalReadOutcomeV1.Unavailable;
            return false;
        }

        context = new NativeApprovalReadContextV1(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            includeInventoryLabelCode);
        refusal = default;
        return true;
    }

    private bool SameContext(NativeApprovalReadContextV1 captured) =>
        companyContext.IsResolved
        && companyContext.CompanyId == captured.CompanyId
        && captured.CompanyId != Guid.Empty
        && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, captured.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(captured.CompanyId)
        && correlationContext.IsResolved
        && string.Equals(correlationContext.CorrelationId, captured.CorrelationId, StringComparison.Ordinal)
        && string.Equals(correlationContext.CausationId, captured.CausationId, StringComparison.Ordinal);

    private static NativeApprovalReadResultV1<T> NotFound<T>() where T : class =>
        new(NativeApprovalReadOutcomeV1.NotFound, null);

    private static NativeApprovalReadResultV1<T> Denied<T>() where T : class =>
        new(NativeApprovalReadOutcomeV1.Denied, null);

    private static NativeApprovalReadResultV1<T> Unavailable<T>() where T : class =>
        new(NativeApprovalReadOutcomeV1.Unavailable, null);
}
