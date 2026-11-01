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
/// <item><b>Realization</b> (SAT-D5, SAT-D15–D17, SAT-D29): the Logistics shipped totals through the AT-024
/// <see cref="ISalesOrderDeliveryReadPortV1"/> (never Logistics tables), judged by <see cref="SalesRealizationCalculator"/> with the
/// company's delivery tolerance read ONLY through <see cref="ISettingsSalesPolicyProviderV1"/> for the order's business date
/// (SAT-D29 b; the date Sales reads its other Settings profiles on). The tolerance is asked only when a line needs it. A missing,
/// refused, inactive, out-of-window, other-company or malformed policy is <c>PolicyUnavailable</c> — never a default; an
/// owner that is not composed, fails or answers a malformed value is <c>Unreadable</c> — never zero.</item>
/// <item><b>No source yet</b>: source/allocation strategy (MOD-05, API-11), invoicing (S5) and financial closure (MOD-04)
/// have no owner module on main: <c>NoSource</c>, never complete or zero. The invoicing axis carries the Sales-owned SAT-D103
/// gate (<see cref="InvoicingGate"/>) for the S5 invoicing owner to obey.</item>
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
        var lifecycle = Lifecycle(draft);
        var acceptance = CustomerAcceptanceAxis(draft);
        var (source, financialClosure) = (noSource, noSource);
        var (gate, priceBasis) = InvoicingGate(draft);
        var invoicing = new SalesDraftInvoicingAxisV1(SalesDraftStatusAxisStatesV1.NoSource, gate, priceBasis);
        // SAT-D32 "kapanmaya hazır": ALL seven axes complete ("tüm eksenler tamamlansa"). An axis that is NoSource, Unreadable or
        // PolicyUnavailable is never complete, so while source (MOD-05), invoicing (S5) and financial closure (MOD-04) have no
        // owner the flag is always false. A flag only — closing stays the user's action and nothing is closed here.
        var readyToClose = lifecycle == SalesDraftStatusAxisStatesV1.Active
            && draft.ApprovalState == InternalApprovalState.Approved
            && acceptance.State is SalesDraftStatusAxisStatesV1.Accepted or SalesDraftStatusAxisStatesV1.NotRequired
            && realization.State == SalesDraftStatusAxisStatesV1.Completed
            && IsComplete(source) && IsComplete(new SalesDraftStatusAxisV1(invoicing.State)) && IsComplete(financialClosure);
        return new SalesOrderDraftStatusResponse(
            draft.Version.ToString(CultureInfo.InvariantCulture),
            SalesDraftApiV1.RevisionText(draft.Revision),
            new SalesDraftStatusAxisV1(lifecycle),
            new SalesDraftStatusAxisV1(draft.ApprovalState.ToString()),
            acceptance,
            source,
            realization,
            invoicing,
            financialClosure,
            readyToClose);
    }

    /// <summary>
    /// SAT-D103 (EK13): the order's invoicing gate. The current revision approved → <c>Open</c> at that revision. A newer
    /// revision in <c>Preparing</c> / <c>PendingApproval</c> after an approved one was archived → <c>AwaitingReapproval</c>
    /// (invoicing waits for the decision; no price basis). That newer revision <c>Rejected</c> → <c>Open</c> at the LAST
    /// APPROVED revision ("red gelirse son onaylı fiyatla"). Anything else (never approved, withdrawn) → <c>NotApproved</c>.
    /// SAT-D114 (EK14): when customer acceptance is required, a basis revision the customer has not accepted is no basis —
    /// <c>AwaitingCustomerAcceptance</c>, no price basis (approved but not accepted; or rejected after an unaccepted one).
    /// The lifecycle, the customer acceptance and the shipments are the invoicing owner's own preconditions (S5 API-32).
    /// </summary>
    private static (string Gate, string? PriceBasisRevision) InvoicingGate(SalesOrderDraft draft) => draft.ApprovalState switch
    {
        // SAT-D114 (EK14) / SAT-D121 (EK17, PR #862): an internally approved revision the customer has not accepted (acceptance
        // required) is no basis — for EVERY revision type (price, quantity-only SAT-D104/D111, VAT/withholding SAT-D105), since
        // the gate reads only the approval state and the acceptance of the basis revision, never the kind of change.
        // SAT-D115 (EK14): the customer rejected the last approved revision — invoicing continues at the revision the customer last
        // accepted (its prices), never at the rejected one.
        InternalApprovalState.Approved or InternalApprovalState.Rejected when draft.CustomerAcceptedFallback is { } accepted =>
            (SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.RevisionText(accepted.Revision)),
        InternalApprovalState.Approved when !draft.IsCustomerAccepted => (SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance, null),
        InternalApprovalState.Approved => (SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.RevisionText(draft.Revision)),
        InternalApprovalState.Preparing or InternalApprovalState.PendingApproval when draft.ArchivedRevisions.Count > 0 =>
            (SalesDraftInvoicingGatesV1.AwaitingReapproval, null),
        InternalApprovalState.Rejected when draft.ArchivedRevisions.Count > 0 && !LastApprovedAccepted(draft) =>
            (SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance, null),
        InternalApprovalState.Rejected when draft.ArchivedRevisions.Count > 0 =>
            (SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.RevisionText(draft.ArchivedRevisions[^1].Revision)),
        _ => (SalesDraftInvoicingGatesV1.NotApproved, null),
    };

    /// <summary>The last approved (archived) revision was accepted by the customer, or acceptance is not required (pre-Q9).</summary>
    private static bool LastApprovedAccepted(SalesOrderDraft draft) =>
        !draft.CustomerAcceptanceRequired || draft.CustomerAcceptance?.Revision == draft.ArchivedRevisions[^1].Revision;

    /// <summary>
    /// Whether an owner-less axis (source, invoicing, financial closure) is complete. None of their owners exists, so no complete
    /// state exists: "NoSource" (like "Unreadable" or "PolicyUnavailable") is never complete. When an owner arrives, its
    /// complete state (DURUM_MAKINELERI "Tam kaynaklı" / "Tam" / "Kapalı") is named here explicitly.
    /// </summary>
    private static bool IsComplete(SalesDraftStatusAxisV1 axis) =>
        axis.State is not (SalesDraftStatusAxisStatesV1.NoSource or SalesDraftStatusAxisStatesV1.Unreadable
            or SalesDraftStatusAxisStatesV1.PolicyUnavailable)
        && OwnerAxisCompleteStates.Contains(axis.State);

    /// <summary>The complete states of the owner-less axes: none until an owner exists (fail-closed).</summary>
    private static readonly IReadOnlySet<string> OwnerAxisCompleteStates = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// SAT-D32: the stored terminal states (<c>Cancelled</c>, <c>Closed</c>) as they are; otherwise <c>Active</c> once the order
    /// was approved — its current revision approved, or an earlier approved revision archived by a revision now awaiting
    /// re-approval — else <c>Draft</c>. Never closed automatically.
    /// </summary>
    private static string Lifecycle(SalesOrderDraft draft) => draft.State switch
    {
        SalesDraftState.Draft when draft.ApprovalState == InternalApprovalState.Approved || draft.ArchivedRevisions.Count > 0 =>
            SalesDraftStatusAxisStatesV1.Active,
        _ => draft.State.ToString(),
    };

    private static SalesDraftCustomerAcceptanceAxisV1 CustomerAcceptanceAxis(SalesOrderDraft draft) => new(
        !draft.CustomerAcceptanceRequired
            ? SalesDraftStatusAxisStatesV1.NotRequired
            : draft.IsCustomerAccepted ? SalesDraftStatusAxisStatesV1.Accepted
            // SAT-D115: the current revision was rejected by the customer (DURUM_MAKINELERI "Vazgeçildi" of that revision).
            : draft.IsCustomerRejected(draft.Revision) ? SalesDraftStatusAxisStatesV1.Rejected
            : SalesDraftStatusAxisStatesV1.Pending,
        draft.CustomerAcceptance is null ? null : SalesDraftApiV1.RevisionText(draft.CustomerAcceptance.Revision));

    // ---- realization ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The quantities realization is measured against: the CURRENT lines while the current revision is approved (or the order
    /// was never approved — nothing can ship then), else — a revision awaiting re-approval — the LAST APPROVED revision's line
    /// quantities, never the new unapproved ones. Null (fail-closed) when that approved revision does not hold every current
    /// line in the same unit with an exact quantity.
    /// </summary>
    private sealed record MeasurementBasis(string Revision, IReadOnlyDictionary<Guid, (Quantity Quantity, string Text)> Lines);

    private static MeasurementBasis? Basis(SalesOrderDraft draft)
    {
        // SAT-D115 (EK14): after the customer rejected the last approved revision, realization is measured on the revision the
        // customer last accepted.
        var acceptedFallback = draft.CustomerAcceptedFallback;
        if (acceptedFallback is null && (draft.ApprovalState == InternalApprovalState.Approved || draft.ArchivedRevisions.Count == 0))
        {
            return new MeasurementBasis(SalesDraftApiV1.RevisionText(draft.Revision),
                draft.Lines.ToDictionary(line => line.Id, line => (line.Quantity, line.QuantityText)));
        }

        var approved = acceptedFallback ?? draft.ArchivedRevisions[^1];
        var lines = new Dictionary<Guid, (Quantity Quantity, string Text)>();
        foreach (var line in draft.Lines)
        {
            var archived = approved.Lines.FirstOrDefault(candidate => candidate.LineId == line.Id);
            if (archived is null || archived.OrderUnitId != line.OrderUnit.UnitId
                || !decimal.TryParse(archived.QuantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            try
            {
                lines[line.Id] = (Quantity.Of(value, line.Quantity.Unit), archived.QuantityText);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return new MeasurementBasis(SalesDraftApiV1.RevisionText(approved.Revision), lines);
    }

    private async Task<SalesDraftRealizationAxisV1> RealizationAsync(Scope scope, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        var ordered = draft.Lines.OrderBy(line => line.LineNo).ToArray();
        if (Basis(draft) is not { } basis)
        {
            // A revision awaits re-approval and the last approved quantities cannot be taken exactly: nothing is judged against
            // unapproved quantities (fail-closed) and no owner is asked.
            return new SalesDraftRealizationAxisV1(SalesDraftStatusAxisStatesV1.PendingReapproval, false, null, null,
                [.. ordered.Select(line => new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
                    null, null, SalesDraftStatusAxisStatesV1.PendingReapproval, null, null, null, false, false, null, null))]);
        }

        var recorded = await RecordedShipmentsAsync(scope, bindings.ReadPermission, draft, cancellationToken);
        if (recorded is null)
        {
            return Unreadable(ordered, basis);
        }

        // SAT-D33: approved customer returns are shown per line beside the shipped total, never netted against it; an
        // unreadable return owner shows no number there and does not take the realization down.
        var returned = await ApprovedReturnsAsync(scope, draft, cancellationToken);
        var closed = draft.State == SalesDraftState.Closed;
        var needsTolerance = ordered.Any(line => SalesRealizationCalculator.NeedsTolerance(recorded[line.Id].Shipped, closed));
        var policy = needsTolerance ? await ToleranceAsync(scope, bindings.ReadPermission, draft, cancellationToken) : null;
        var policyUnavailable = needsTolerance && policy is null;

        var lines = new List<(SalesDraftLineRealizationV1 Wire, LineRealizationState State, bool Warning)>(ordered.Length);
        foreach (var line in ordered)
        {
            var (shipped, delivered) = recorded[line.Id];
            var (basisQuantity, basisText) = basis.Lines[line.Id];
            if (policyUnavailable && SalesRealizationCalculator.NeedsTolerance(shipped, closed))
            {
                // No approved tolerance: the facts stay visible, nothing is judged (no direction, deviation, band or state).
                lines.Add((new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, basisText,
                    QuantityText(shipped), QuantityText(delivered), SalesDraftStatusAxisStatesV1.PolicyUnavailable, null, null, null,
                    false, false, ClosedRemainder(draft, line.Id), returned?.GetValueOrDefault(line.Id)), LineRealizationState.NotStarted,
                    false));
                continue;
            }

            LineRealization realization;
            try
            {
                // SAT-D30: no over-delivery approval record exists yet, so an approval-band over-delivery awaits approval. A line
                // that needs no tolerance (nothing shipped, open) has the same NotStarted shape in every branch.
                realization = SalesRealizationCalculator.Line(basisQuantity, shipped, closed, policy?.Tolerance, overDeliveryApproved: false);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
            {
                // A value the calculator cannot judge exactly is shown as no state, never as a plausible one.
                return Unreadable(ordered, basis);
            }

            var warning = SalesRealizationCalculator.IsCompletedWithWarning(realization);
            lines.Add((new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, basisText,
                QuantityText(shipped), QuantityText(delivered), realization.State.ToString(), realization.Direction.ToString(),
                QuantityText(realization.Deviation), realization.Band?.ToString(), warning, realization.ApprovedOverDelivery,
                ClosedRemainder(draft, line.Id), returned?.GetValueOrDefault(line.Id)), realization.State, warning));
        }

        return policyUnavailable
            ? new SalesDraftRealizationAxisV1(SalesDraftStatusAxisStatesV1.PolicyUnavailable, false, basis.Revision, null,
                [.. lines.Select(line => line.Wire)])
            : new SalesDraftRealizationAxisV1(SalesRealizationCalculator.Axis([.. lines.Select(line => line.State)]).ToString(),
                lines.Any(line => line.Warning), basis.Revision, policy?.Wire, [.. lines.Select(line => line.Wire)]);
    }

    /// <summary>
    /// The delivery owner's shipped and delivered totals per line as exact quantities of the line's unit, or null when the owner
    /// is not composed, fails or answers a malformed value (the AT-024 validation, the same port). A fence loss is 403. The fence
    /// re-measures <paramref name="permission"/>, the caller's own read name (the draft read, or the API-41 report read).
    /// </summary>
    private async Task<Dictionary<Guid, (Quantity Shipped, Quantity Delivered)>?> RecordedShipmentsAsync(Scope scope, string? permission,
        SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        if (deliveries is not { } owner)
        {
            return null;
        }

        SalesOrderDeliveryReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission,
                () => owner.ReadOrderDeliveriesAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        return RecordedQuantities(scope, draft, answer);
    }

    /// <summary>
    /// The owner's answer for <paramref name="draft"/> validated (the AT-024 rules of <see cref="RecordedDeliveries"/>) and taken as
    /// exact quantities of each line's unit; null when it is malformed. Shared by the status axis and the API-41 batch read.
    /// </summary>
    private static Dictionary<Guid, (Quantity Shipped, Quantity Delivered)>? RecordedQuantities(Scope scope, SalesOrderDraft draft,
        SalesOrderDeliveryReadAnswerV1? answer)
    {
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

    /// <summary>
    /// SAT-D33: the API-10 Logistics port's approved returned total per line (the customer-returns read's own validation), or
    /// null when that owner is not composed, fails or answers a malformed value. A fence loss is 403.
    /// </summary>
    private async Task<Dictionary<Guid, string>?> ApprovedReturnsAsync(Scope scope, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        if (customerReturns is not { } owner)
        {
            return null;
        }

        SalesOrderCustomerReturnReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.ReadPermission,
                () => owner.ReadApprovedReturnsAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        try
        {
            return CustomerReturns(scope, draft, answer).Lines.ToDictionary(line => line.LineId, line => line.ApprovedReturned);
        }
        catch (SalesDraftServiceException exception) when (exception.Status == 503)
        {
            return null;
        }
    }

    private sealed record ToleranceRead(SalesDeliveryTolerance Tolerance, SalesDraftDeliveryToleranceV1 Wire);

    /// <summary>
    /// SAT-D5: the company's delivery tolerance in force on the order's business date, read ONLY through the Settings Sales
    /// policy port; null (fail-closed, <c>PolicyUnavailable</c>) when the port is not composed or refuses, or when its answer is
    /// of another company, inactive, outside its own window or not a valid ladder. A fence loss is 403; the fence re-measures
    /// <paramref name="permission"/>, the caller's own read name (the draft read, or the API-41 report read).
    /// </summary>
    private async Task<ToleranceRead?> ToleranceAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        if (salesPolicies is not { } provider)
        {
            return null;
        }

        var on = draft.Header.BusinessDate;
        SalesDeliveryTolerancePolicyV1? policy;
        try
        {
            policy = await GuardedAsync(scope, permission,
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

    private static SalesDraftRealizationAxisV1 Unreadable(IReadOnlyList<SalesOrderDraftLine> ordered, MeasurementBasis basis) =>
        new(SalesDraftStatusAxisStatesV1.Unreadable, false, basis.Revision, null,
            [.. ordered.Select(line => new SalesDraftLineRealizationV1(line.Id, line.LineNo, line.OrderUnit.UnitId, basis.Lines[line.Id].Text,
                null, null, SalesDraftStatusAxisStatesV1.Unreadable, null, null, null, false, false, null, null))]);

    /// <summary>The recorded force-closure remainder of the line (No 30 / SAT-D12), verbatim; null when the order was not force-closed.</summary>
    private static string? ClosedRemainder(SalesOrderDraft draft, Guid lineId) =>
        draft.ForceClosure?.Lines.FirstOrDefault(line => line.LineId == lineId)?.ClosedRemainder;

    /// <summary>A percent as invariant decimal text with trailing zeros dropped (1.50 → "1.5"; never rounded).</summary>
    private static string PercentText(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}
