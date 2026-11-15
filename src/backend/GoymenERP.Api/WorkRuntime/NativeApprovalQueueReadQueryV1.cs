using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>The readiness of one source in a Work queue answer.</summary>
internal static class NativeApprovalQueueSourceStatesV1
{
    public const string Ready = "Ready";
    public const string Unavailable = "Unavailable";
}

/// <summary>
/// One source the caller may read: its names, its state in this answer, and whether its owner holds more pending
/// records than the queue shows. A source that denied the caller is not listed at all.
/// </summary>
internal sealed record NativeApprovalQueueSourceV1(
    string Module,
    string Kind,
    string State,
    bool HasMore);

/// <summary>
/// The Work queue (B-25): the pending approvals of every connected source the caller may see, in the caller's company,
/// oldest first. Each item links to its owner's own screen; nothing here decides anything.
/// </summary>
internal sealed record NativeApprovalQueuePageV1(
    Guid CompanyId,
    IReadOnlyList<NativeApprovalQueueItemV1> Items,
    IReadOnlyList<NativeApprovalQueueSourceV1> Sources);

/// <summary>
/// Reads the Work queue across every registered <see cref="INativeApprovalQueueAdapterV1"/> and, through
/// <see cref="NativeApprovalTypedQueueSourceV1"/>, every typed D-05 source (<see cref="INativeApprovalReadAdapterV1"/>).
/// The company, actor and correlation are captured once and re-checked after every source; any drift denies the whole
/// read. Each answer is judged strictly: a malformed answer marks only that source unavailable (none of its items are
/// shown), a denial hides the source, and a registration that is not exact (an invalid name, a kind served twice)
/// makes the whole queue unavailable.
/// </summary>
internal sealed class NativeApprovalQueueReadQueryV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly ICorrelationContext correlationContext;
    private readonly IReadOnlyList<RegisteredSource> sources;
    private readonly bool registrationIsValid;

    public NativeApprovalQueueReadQueryV1(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        ICorrelationContext correlationContext,
        IEnumerable<INativeApprovalQueueAdapterV1> moduleSources,
        IEnumerable<INativeApprovalReadAdapterV1> typedSources)
    {
        this.companyContext = companyContext;
        this.currentUser = currentUser;
        this.correlationContext = correlationContext;

        var valid = true;
        var candidates = new List<INativeApprovalQueueAdapterV1>();
        foreach (var typed in typedSources)
        {
            if (typed is null
                || string.IsNullOrWhiteSpace(typed.Kind)
                || !NativeApprovalKindsV1.IsKnown(typed.Kind))
            {
                valid = false;
                continue;
            }
            candidates.Add(new NativeApprovalTypedQueueSourceV1(typed));
        }
        foreach (var source in moduleSources)
        {
            if (source is null)
            {
                valid = false;
                continue;
            }
            candidates.Add(source);
        }

        var byKind = new Dictionary<string, RegisteredSource>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var module = candidate.Module;
            var kind = candidate.Kind;
            if (!NativeApprovalQueueRulesV1.IsValidKind(module, kind)
                || !byKind.TryAdd(kind, new RegisteredSource(module, kind, candidate)))
            {
                valid = false;
            }
        }

        sources = byKind.Values
            .OrderBy(source => source.Module, StringComparer.Ordinal)
            .ThenBy(source => source.Kind, StringComparer.Ordinal)
            .ToArray();
        registrationIsValid = valid;
    }

    public async Task<NativeApprovalReadResultV1<NativeApprovalQueuePageV1>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCapture(out var context, out var refusal))
        {
            return new(refusal, null);
        }
        if (!registrationIsValid)
        {
            return Unavailable();
        }

        var items = new List<NativeApprovalQueueItemV1>();
        var states = new List<NativeApprovalQueueSourceV1>();
        foreach (var source in sources)
        {
            var result = await source.Adapter.ListPendingAsync(
                context, NativeApprovalQueueRulesV1.MaxItemsPerSource, cancellationToken);
            if (!SameContext(context))
            {
                return Denied();
            }

            if (result is { Outcome: NativeApprovalQueueOutcomeV1.Denied, Items: null })
            {
                continue;
            }
            if (result is { Outcome: NativeApprovalQueueOutcomeV1.Found }
                && Accept(source, context, result) is { } accepted)
            {
                items.AddRange(accepted);
                states.Add(new(source.Module, source.Kind, NativeApprovalQueueSourceStatesV1.Ready, result.HasMore));
                continue;
            }
            states.Add(new(source.Module, source.Kind, NativeApprovalQueueSourceStatesV1.Unavailable, false));
        }

        if (sources.Count > 0 && states.Count == 0)
        {
            return Denied();
        }
        if (states.Count > 0
            && states.All(state => state.State == NativeApprovalQueueSourceStatesV1.Unavailable))
        {
            return Unavailable();
        }

        var ordered = items
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Module, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.ItemId)
            .ToArray();
        return new(NativeApprovalReadOutcomeV1.Found,
            new NativeApprovalQueuePageV1(context.CompanyId, ordered, states));
    }

    private static IReadOnlyList<NativeApprovalQueueItemV1>? Accept(
        RegisteredSource source,
        NativeApprovalQueueContextV1 context,
        NativeApprovalQueueResultV1 result)
    {
        if (result.CompanyId != context.CompanyId
            || result.Items is not { } items
            || items.Count > NativeApprovalQueueRulesV1.MaxItemsPerSource)
        {
            return null;
        }

        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item is null
                || !string.Equals(item.Module, source.Module, StringComparison.Ordinal)
                || !string.Equals(item.Kind, source.Kind, StringComparison.Ordinal)
                || item.ItemId == Guid.Empty
                || item.CreatedAtUtc == default
                || item.CreatedAtUtc.Offset != TimeSpan.Zero
                || !NativeApprovalQueueRulesV1.IsValidLink(item.Link)
                || !seen.Add(item.ItemId))
            {
                return null;
            }
        }
        return items;
    }

    private bool TryCapture(
        out NativeApprovalQueueContextV1 context,
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

        context = new NativeApprovalQueueContextV1(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId);
        refusal = default;
        return true;
    }

    private bool SameContext(NativeApprovalQueueContextV1 captured) =>
        companyContext.IsResolved
        && companyContext.CompanyId == captured.CompanyId
        && captured.CompanyId != Guid.Empty
        && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, captured.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(captured.CompanyId)
        && correlationContext.IsResolved
        && string.Equals(correlationContext.CorrelationId, captured.CorrelationId, StringComparison.Ordinal)
        && string.Equals(correlationContext.CausationId, captured.CausationId, StringComparison.Ordinal);

    private static NativeApprovalReadResultV1<NativeApprovalQueuePageV1> Denied() =>
        new(NativeApprovalReadOutcomeV1.Denied, null);

    private static NativeApprovalReadResultV1<NativeApprovalQueuePageV1> Unavailable() =>
        new(NativeApprovalReadOutcomeV1.Unavailable, null);

    private sealed record RegisteredSource(string Module, string Kind, INativeApprovalQueueAdapterV1 Adapter);
}

/// <summary>
/// Lists one typed D-05 source (<see cref="INativeApprovalReadAdapterV1"/>) in the Work queue: the first page of the
/// owner's own pending read, judged by the same rules as the "Onaylarım" list, each record linked to its Work detail
/// screen. The module name is the kind's first segment.
/// </summary>
internal sealed class NativeApprovalTypedQueueSourceV1 : INativeApprovalQueueAdapterV1
{
    /// <summary>The frontend route of a Work approval detail (<c>WORK_ROUTE.approvalDetail</c>) without its parameters.</summary>
    internal const string WorkApprovalDetailLinkPrefix = "/onaylarim/";

    private readonly INativeApprovalReadAdapterV1 inner;

    public NativeApprovalTypedQueueSourceV1(INativeApprovalReadAdapterV1 inner)
    {
        this.inner = inner;
        Kind = inner.Kind;
        var separator = Kind.IndexOf('.', StringComparison.Ordinal);
        Module = separator > 0 ? Kind[..separator] : string.Empty;
    }

    public string Module { get; }

    public string Kind { get; }

    public async Task<NativeApprovalQueueResultV1> ListPendingAsync(
        NativeApprovalQueueContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        var readContext = new NativeApprovalReadContextV1(
            context.CompanyId,
            context.UserId,
            context.CorrelationId,
            context.CausationId);
        var result = NativeApprovalReadQueryV1.ValidatePage(
            Kind,
            readContext,
            await inner.ListAsync(readContext, cursor: null, cancellationToken));

        if (result.Outcome == NativeApprovalReadOutcomeV1.Denied)
        {
            return NativeApprovalQueueResultV1.Denied();
        }
        if (result is not { Outcome: NativeApprovalReadOutcomeV1.Found, Resource: { } page })
        {
            return NativeApprovalQueueResultV1.Unavailable();
        }

        var items = page.Items
            .Take(maxCount)
            .Select(item => new NativeApprovalQueueItemV1(
                Module,
                item.ApprovalId,
                Kind,
                item.SubmittedAtUtc.ToUniversalTime(),
                $"{WorkApprovalDetailLinkPrefix}{Kind}/{item.ApprovalId:D}"))
            .ToArray();
        return NativeApprovalQueueResultV1.Found(
            page.CompanyId,
            items,
            page.NextCursor is not null || page.Items.Count > maxCount);
    }
}
