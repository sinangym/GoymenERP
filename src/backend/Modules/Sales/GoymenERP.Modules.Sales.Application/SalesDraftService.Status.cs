using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// DEC-SAL-001 / API-08: the order's seven status axes under <c>sales.drafts.read</c>. Read-only in every module: no attempt,
/// audit or version effect, nothing cached or copied into Sales.
/// <list type="bullet">
/// <item><b>From the Sales aggregate</b>: lifecycle (<c>Draft</c> / <c>Cancelled</c> / <c>Closed</c>, as stored — "Aktif" is not
/// derived), internal approval (the stored state) and customer acceptance (<c>NotRequired</c> / <c>Pending</c> /
/// <c>Accepted</c> of the current revision).</item>
/// <item><b>Realization</b> (SAT-D5, SAT-D15–D17, SAT-D24): the Logistics shipped totals through the AT-024
/// <see cref="ISalesOrderDeliveryReadPortV1"/> (never Logistics tables), judged by <see cref="SalesRealizationCalculator"/> with the
/// company's delivery tolerance read ONLY through <see cref="ISettingsSalesPolicyProviderV1"/> for the order's business date
/// (SAT-D24 b; the date Sales reads its other Settings profiles on). The tolerance is asked only when a line needs it. A missing,
/// refused, inactive, out-of-window, other-company or malformed policy is <c>PolicyUnavailable</c> — never a default; an
/// owner that is not composed, fails or answers a malformed value is <c>Unreadable</c> — never zero.</item>
/// <item><b>No source yet</b>: source/allocation strategy (MOD-05, API-11), invoicing (S5) and financial closure (MOD-04)
/// have no owner module on main: <c>NoSource</c>, never complete or zero.</item>
/// <item><b>Fences</b> (the draft read's): before anything is read, after the draft read, after the delivery owner and after the
/// Settings answer. Another company's draft is 404 (tenant filter) and no owner is asked for it.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftStatusResponse> GetDraftStatusAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            // Another company's draft is null under the tenant filter: 404, and no owner is ever asked for it.
            throw new SalesDraftServiceException(404, "not_found");
        }

        var realization = await RealizationAsync(scope, draft, cancellationToken);
        var noSource = new SalesDraftStatusAxisV1(SalesDraftStatusAxisStatesV1.NoSource);
        return new SalesOrderDraftStatusResponse(
            draft.Version.ToString(CultureInfo.InvariantCulture),
            SalesDraftApiV1.RevisionText(draft.Revision),
            new SalesDraftStatusAxisV1(draft.State.ToString()),
            new SalesDraftStatusAxisV1(draft.ApprovalState.ToString()),
            CustomerAcceptanceAxis(draft),
            noSource,
            realization,
            noSource,
            noSource);
    }

    private static SalesDraftCustomerAcceptanceAxisV1 CustomerAcceptanceAxis(SalesOrderDraft draft) => new(
        !draft.CustomerAcceptanceRequired
            ? SalesDraftStatusAxisStatesV1.NotRequired
            : draft.IsCustomerAccepted ? SalesDraftStatusAxisStatesV1.Accepted : SalesDraftStatusAxisStatesV1.Pending,
        draft.CustomerAcceptance is null ? null : SalesDraftApiV1.RevisionText(draft.CustomerAcceptance.Revision));

    // ---- realization ----------------------------------------------------------------------------------------------------

    private async Task<SalesDraftRealizationAxisV1> RealizationAsync(Scope scope, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        var ordered = draft.Lines.OrderBy(line => line.LineNo).ToArray();
        var recorded = await RecordedShipmentsAsync(scope, draft, cancellationToken);
        if (recorded is null)
        {
            return Unreadable(ordered);
        }

        var closed = draft.State == SalesDraftState.Closed;
        var needsTolerance = ordered.Any(line => SalesRealizationCalculator.NeedsTolerance(recorded[line.Id].Shipped, closed));
        var policy = needsTolerance ? await ToleranceAsync(scope, draft, cancellationToken) : null;
        if (needsTolerance && policy is null)
        {
            return new SalesDraftRealizationAxisV1(SalesDraftStatusAxisStatesV1.PolicyUnavailable, false, null,
                [.. ordered.Select(line =>
                {
                    var (shipped, delivered) = recorded[line.Id];
                    var state = SalesRealizationCalculator.NeedsTolerance(shipped, closed)
                        ? SalesDraftStatusAxisStatesV1.PolicyUnavailable
                        : SalesDraftStatusAxisStatesV1.NotStarted;
                    return new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
                        QuantityText(shipped), QuantityText(delivered), state, null, null, null, false, ClosedRemainder(draft, line.Id));
                })]);
        }

        var lines = new List<(SalesDraftLineRealizationV1 Wire, LineRealizationState State, bool Warning)>(ordered.Length);
        foreach (var line in ordered)
        {
            var (shipped, delivered) = recorded[line.Id];
            LineRealization realization;
            try
            {
                realization = SalesRealizationCalculator.Line(line.Quantity, shipped, closed, policy?.Tolerance);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
            {
                // A value the calculator cannot judge exactly is shown as no state, never as a plausible one.
                return Unreadable(ordered);
            }

            var warning = SalesRealizationCalculator.IsCompletedWithWarning(realization);
            lines.Add((new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
                QuantityText(shipped), QuantityText(delivered), realization.State.ToString(), realization.Direction.ToString(),
                QuantityText(realization.Deviation), realization.Band?.ToString(), warning, ClosedRemainder(draft, line.Id)),
                realization.State, warning));
        }

        return new SalesDraftRealizationAxisV1(SalesRealizationCalculator.Axis([.. lines.Select(line => line.State)]).ToString(),
            lines.Any(line => line.Warning), policy?.Wire, [.. lines.Select(line => line.Wire)]);
    }

    /// <summary>
    /// The delivery owner's shipped and delivered totals per line as exact quantities of the line's unit, or null when the owner
    /// is not composed, fails or answers a malformed value (the AT-024 validation, the same port). A fence loss is 403.
    /// </summary>
    private async Task<Dictionary<Guid, (Quantity Shipped, Quantity Delivered)>?> RecordedShipmentsAsync(Scope scope,
        SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        if (deliveries is not { } owner)
        {
            return null;
        }

        SalesOrderDeliveryReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.ReadPermission,
                () => owner.ReadOrderDeliveriesAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        try
        {
            var totals = RecordedDeliveries(scope, draft, answer);
            var result = new Dictionary<Guid, (Quantity Shipped, Quantity Delivered)>();
            foreach (var line in draft.Lines)
            {
                var (shipped, delivered) = totals.GetValueOrDefault(line.Id);
                // Ö2 in the line's own dimension: kilograms ≤ 3 decimals, pieces whole — never rounded to fit.
                result[line.Id] = (Quantity.Of(shipped, line.Quantity.Unit), Quantity.Of(delivered, line.Quantity.Unit));
            }

            return result;
        }
        catch (Exception exception) when (exception is ArgumentException
            || exception is SalesDraftServiceException { Status: 503 })
        {
            return null;
        }
    }

    private sealed record ToleranceRead(SalesDeliveryTolerance Tolerance, SalesDraftDeliveryToleranceV1 Wire);

    /// <summary>
    /// SAT-D5: the company's delivery tolerance in force on the order's business date, read ONLY through the Settings Sales
    /// policy port; null (fail-closed, <c>PolicyUnavailable</c>) when the port is not composed or refuses, or when its answer is
    /// of another company, inactive, outside its own window or not a valid ladder. A fence loss is 403.
    /// </summary>
    private async Task<ToleranceRead?> ToleranceAsync(Scope scope, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        if (salesPolicies is not { } provider)
        {
            return null;
        }

        var on = draft.Header.BusinessDate;
        SalesDeliveryTolerancePolicyV1? policy;
        try
        {
            policy = await GuardedAsync(scope, bindings.ReadPermission,
                () => provider.GetSalesDeliveryToleranceAsync(on, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        if (policy is null || policy.Profile is null || policy.CompanyId != scope.CompanyId || !policy.IsActive
            || policy.Profile.ProfileId == Guid.Empty || policy.Profile.Version <= 0
            || policy.EffectiveFrom > on || policy.EffectiveThrough is DateOnly through && through < on)
        {
            return null;
        }

        try
        {
            var tolerance = new SalesDeliveryTolerance(policy.WarnPercent, policy.ApprovalPercent, policy.BlockPercent);
            return new ToleranceRead(tolerance, new SalesDraftDeliveryToleranceV1(policy.Profile.ProfileId, policy.Profile.Version,
                PercentText(policy.WarnPercent), PercentText(policy.ApprovalPercent), PercentText(policy.BlockPercent),
                policy.EffectiveFrom, policy.EffectiveThrough, on));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static SalesDraftRealizationAxisV1 Unreadable(IReadOnlyList<SalesOrderDraftLine> ordered) =>
        new(SalesDraftStatusAxisStatesV1.Unreadable, false, null,
            [.. ordered.Select(line => new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
                null, null, SalesDraftStatusAxisStatesV1.Unreadable, null, null, null, false, null))]);

    /// <summary>The recorded force-closure remainder of the line (No 30 / SAT-D12), verbatim; null when the order was not force-closed.</summary>
    private static string? ClosedRemainder(SalesOrderDraft draft, Guid lineId) =>
        draft.ForceClosure?.Lines.FirstOrDefault(line => line.LineId == lineId)?.ClosedRemainder;

    /// <summary>A percent as invariant decimal text with trailing zeros dropped (1.50 → "1.5"; never rounded).</summary>
    private static string PercentText(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}
