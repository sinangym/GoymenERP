using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// MOD-01 / API-06: the read-only customer risk check of an order under <c>sales.drafts.read</c> (DEC-FIN-007 / DEC-FIN-009).
/// Read-only in every module: no attempt, audit or version effect, nothing cached or copied into Sales.
/// <list type="bullet">
/// <item><b>Open receivables, credit limit, collateral</b>: the customer-finance owner's answer through
/// <see cref="ISalesPartyRiskReadPortV1"/> (never its tables). Not composed, failing or malformed → each is "Unavailable".</item>
/// <item><b>Shipped, not invoiced</b>: invoices are S5 — "NoSource" (never zero), so the result cannot be "Within" yet.</item>
/// <item><b>Approved, not shipped</b>: for every open approved order of the same customer account (the store's
/// <see cref="ISalesOrderDraftStore.ListOpenApprovedOrdersAsync"/>), per line (approved quantity − the delivery owner's shipped
/// total, not below zero) × the net unit price (<see cref="SalesLinePriceCalculator"/>), each line amount rounded per SAT-D11 at
/// the order's money scale. The approved basis is the current lines while the current revision is approved, else the last
/// approved revision's (never unapproved quantities or prices). An unpriced line, a price in another unit, an unreadable delivery
/// owner or more orders than one read takes make it "Unavailable" — never a smaller sum. The amounts are net of taxes (no totals
/// calculator yet, API-03), so they are marked <c>ExcludingTax</c>.</item>
/// <item><b>The decision</b> is <see cref="SalesCustomerRiskCalculator"/>'s alone, in the order's currency at its money scale.</item>
/// <item><b>Amounts</b> are disclosed only with <c>sales.orders.price.read</c>; the states, sources, reasons and result always.</item>
/// <item><b>Fences</b> (the draft read's): before anything is read, after the draft, after the order list, after each owner.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    /// <summary>
    /// The most open approved orders of one customer one risk read takes (a technical response bound, not a business policy):
    /// above it the remainder is "Unavailable" rather than a truncated, smaller sum.
    /// </summary>
    public const int MaximumRiskOrders = 200;

    public async Task<SalesOrderDraftRiskCheckResponse> GetDraftRiskCheckAsync(Guid draftId, CancellationToken cancellationToken = default)
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

        var party = await PartyRiskAsync(scope, draft, cancellationToken);
        var remainder = await ApprovedRemainderAsync(scope, draft, cancellationToken);
        var shippedNotInvoiced = SalesRiskInput.NoSource(SalesDraftRiskReasonCodesV1.InvoicingOwnerMissing);
        var currencyId = draft.Header.CurrencyId;
        var scale = Math.Min(draft.Money.Scale, Money.MaximumScale);
        var evaluation = SalesCustomerRiskCalculator.Evaluate(currencyId, scale, party.OpenReceivables, shippedNotInvoiced, remainder,
            party.CreditLimit);

        // The price read is asked last, after every other fence: the freshest word on whether amounts cross.
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var visible = disclosure == SalesPriceDisclosure.Visible;
        SalesDraftRiskFigureV1 Figure(string kind, string source, SalesRiskInput input) =>
            new(kind, source, FigureState(input.State), input.State == SalesRiskInputState.Available ? input.TaxBasis?.ToString() : null,
                visible && input.State == SalesRiskInputState.Available
                    ? [.. input.Amounts.Select(amount => new SalesDraftRiskAmountV1(amount.CurrencyId, AmountText(amount.Amount, amount.CurrencyId, currencyId, scale)))]
                    : null,
                input.State == SalesRiskInputState.Available ? null : input.ReasonCode);

        return new SalesOrderDraftRiskCheckResponse(
            draft.Version.ToString(CultureInfo.InvariantCulture),
            draft.Header.PartyId,
            draft.Header.CompanyAccountId,
            currencyId,
            evaluation.Outcome.ToString(),
            [.. evaluation.Reasons.Select(reason => new SalesDraftRiskReasonV1(reason.Component, reason.Code))],
            disclosure,
            visible && evaluation.GrossRisk is { } gross ? AmountText(gross.Amount, gross.CurrencyId, currencyId, scale) : null,
            [
                Figure(SalesDraftRiskFigureKindsV1.OpenReceivables, party.OpenReceivablesSource, party.OpenReceivables),
                Figure(SalesDraftRiskFigureKindsV1.ShippedNotInvoiced, SalesDraftRiskSourcesV1.Invoicing, shippedNotInvoiced),
                Figure(SalesDraftRiskFigureKindsV1.ApprovedUnshippedRemainder, SalesDraftRiskSourcesV1.ApprovedOrderRemainders, remainder),
            ],
            Figure(SalesDraftRiskFigureKindsV1.CreditLimit, party.CreditLimitSource, party.CreditLimit),
            Figure(SalesDraftRiskFigureKindsV1.Collateral, party.CollateralSource, party.Collateral));
    }

    // ---- the customer-finance owner ---------------------------------------------------------------------------------------

    private sealed record PartyRiskInputs(
        SalesRiskInput OpenReceivables, string OpenReceivablesSource,
        SalesRiskInput CreditLimit, string CreditLimitSource,
        SalesRiskInput Collateral, string CollateralSource);

    private static PartyRiskInputs PartyRiskUnavailable(string reasonCode)
    {
        var unavailable = SalesRiskInput.Unavailable(reasonCode);
        const string source = SalesDraftRiskSourcesV1.PartyRiskOwner;
        return new PartyRiskInputs(unavailable, source, unavailable, source, unavailable, source);
    }

    private async Task<PartyRiskInputs> PartyRiskAsync(Scope scope, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        if (partyRisk is null)
        {
            // Not composed (the owner registers the port in its own module): every input unavailable, never an assumed zero.
            return PartyRiskUnavailable(SalesDraftRiskReasonCodesV1.PartyRiskProviderUnavailable);
        }

        SalesPartyRiskReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.ReadPermission,
                () => partyRisk.ReadPartyRiskAsync(draft.Header.PartyId, draft.Header.CompanyAccountId, cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return PartyRiskUnavailable(SalesDraftRiskReasonCodesV1.PartyRiskProviderUnavailable);
        }

        if (answer is null || answer.CompanyId != scope.CompanyId || answer.PartyId != draft.Header.PartyId
            || answer.CompanyAccountId != draft.Header.CompanyAccountId
            || !TryInput(answer.OpenReceivables, out var receivables) || !TryInput(answer.CreditLimit, out var limit)
            || !TryInput(answer.Collateral, out var collateral))
        {
            return PartyRiskUnavailable(SalesDraftRiskReasonCodesV1.PartyRiskAnswerInvalid);
        }

        return new PartyRiskInputs(receivables, answer.OpenReceivables.Source, limit, answer.CreditLimit.Source,
            collateral, answer.Collateral.Source);
    }

    /// <summary>One owner figure as a calculator input; false for a malformed figure (no source name, no amount list, a null amount).</summary>
    private static bool TryInput(SalesPartyRiskFigureV1? figure, out SalesRiskInput input)
    {
        input = null!;
        if (figure is null || string.IsNullOrWhiteSpace(figure.Source) || figure.Amounts is null || figure.Amounts.Any(amount => amount is null))
        {
            return false;
        }

        input = figure.State switch
        {
            SalesPartyRiskFigureStateV1.Available => SalesRiskInput.Available(
                [.. figure.Amounts.Select(amount => new SalesRiskAmount(amount.CurrencyId, amount.Amount))],
                figure.IncludesTax switch
                {
                    true => SalesRiskTaxBasis.IncludingTax,
                    false => SalesRiskTaxBasis.ExcludingTax,
                    null => null,
                }),
            SalesPartyRiskFigureStateV1.NoSource => SalesRiskInput.NoSource(figure.ReasonCode ?? SalesRiskReasonCodes.NoSource),
            _ => SalesRiskInput.Unavailable(figure.ReasonCode ?? SalesRiskReasonCodes.Unavailable),
        };
        return true;
    }

    // ---- the approved, not yet shipped remainder ------------------------------------------------------------------------

    /// <summary>One line of an order's approved commitment: its identity, unit, approved quantity and commercial fields.</summary>
    private sealed record RiskBasisLine(Guid LineId, Guid OrderUnitId, decimal Quantity, CommercialDraftFields Commercial);

    private async Task<SalesRiskInput> ApprovedRemainderAsync(Scope scope, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        IReadOnlyList<SalesOrderDraft> orders;
        try
        {
            orders = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListOpenApprovedOrdersAsync(draft.Header.PartyId, draft.Header.CompanyAccountId, MaximumRiskOrders + 1,
                    cancellationToken),
                cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (orders.Count > MaximumRiskOrders)
        {
            return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.OrderReadBoundExceeded);
        }

        var sums = new Dictionary<Guid, decimal>();
        foreach (var order in orders)
        {
            if (order.CompanyId != scope.CompanyId || order.Header.PartyId != draft.Header.PartyId
                || order.Header.CompanyAccountId != draft.Header.CompanyAccountId || order.State != SalesDraftState.Draft)
            {
                // The store answers only this customer's open orders of this company; anything else is a broken read.
                return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.OrderBasisUnreadable);
            }

            if (RiskBasis(order) is not { } basis)
            {
                return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.OrderBasisUnreadable);
            }

            if (deliveries is null)
            {
                // An open approved order exists but nobody can say what of it shipped: never "nothing shipped".
                return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable);
            }

            SalesOrderDeliveryReadAnswerV1? answer;
            try
            {
                answer = await GuardedAsync(scope, bindings.ReadPermission,
                    () => deliveries.ReadOrderDeliveriesAsync(order.Id, cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
            {
                return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable);
            }

            Dictionary<Guid, (decimal Shipped, decimal Delivered)> recorded;
            try
            {
                recorded = RecordedDeliveries(scope, order, answer);
            }
            catch (SalesDraftServiceException)
            {
                return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.DeliveryAnswerInvalid);
            }

            if (OrderRemainder(order, basis, recorded) is not { } amount)
            {
                return SalesRiskInput.Unavailable(basis.Reason!);
            }

            try
            {
                sums[order.Header.CurrencyId] = sums.TryGetValue(order.Header.CurrencyId, out var sum) ? sum + amount : amount;
            }
            catch (OverflowException)
            {
                return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.OrderAmountInvalid);
            }
        }

        return SalesRiskInput.Available(
            [.. sums.OrderBy(entry => entry.Key).Select(entry => new SalesRiskAmount(entry.Key, entry.Value))],
            SalesRiskTaxBasis.ExcludingTax);
    }

    /// <summary>The approved commitment's lines and money scale; <see cref="Reason"/> is set by <see cref="OrderRemainder"/> on refusal.</summary>
    private sealed class RiskOrderBasis(IReadOnlyList<RiskBasisLine> lines, int scale)
    {
        public IReadOnlyList<RiskBasisLine> Lines { get; } = lines;
        public int Scale { get; } = scale;
        public string? Reason { get; set; }
    }

    /// <summary>
    /// The current lines while the current revision is approved; else (a revision awaiting re-approval) the LAST APPROVED
    /// revision's lines, never the unapproved ones. Null (fail-closed) when an archived quantity is not exact text or an archived
    /// line that still exists is in another unit now.
    /// </summary>
    private static RiskOrderBasis? RiskBasis(SalesOrderDraft order)
    {
        if (order.ApprovalState == InternalApprovalState.Approved)
        {
            return new RiskOrderBasis(
                [.. order.Lines.Select(line => new RiskBasisLine(line.Id, line.OrderUnit.UnitId, line.Quantity.Value, line.Commercial))],
                order.Money.Scale);
        }

        if (order.ArchivedRevisions.Count == 0)
        {
            return null;
        }

        var approved = order.ArchivedRevisions[^1];
        var current = order.Lines.ToDictionary(line => line.Id);
        var lines = new List<RiskBasisLine>(approved.Lines.Count);
        foreach (var line in approved.Lines)
        {
            if (!SalesLinePriceCalculator.TryParseExact(line.QuantityText, out var quantity) || quantity < 0m
                || (current.TryGetValue(line.LineId, out var now) && now.OrderUnit.UnitId != line.OrderUnitId))
            {
                return null;
            }

            lines.Add(new RiskBasisLine(line.LineId, line.OrderUnitId, quantity, line.Commercial));
        }

        return new RiskOrderBasis(lines, approved.MoneyScale);
    }

    /// <summary>
    /// Σ round(max(0, approved − shipped) × net unit price) over the basis lines, each line rounded half away from zero at the
    /// order's money scale (SAT-D11: the line amount is rounded, the net unit price is not). Null with the reason set on refusal.
    /// </summary>
    private static decimal? OrderRemainder(SalesOrderDraft order, RiskOrderBasis basis,
        IReadOnlyDictionary<Guid, (decimal Shipped, decimal Delivered)> recorded)
    {
        var scale = Math.Min(basis.Scale, Money.MaximumScale);
        var total = 0m;
        foreach (var line in basis.Lines)
        {
            var shipped = recorded.TryGetValue(line.LineId, out var entry) ? entry.Shipped : 0m;
            var remaining = line.Quantity > shipped ? line.Quantity - shipped : 0m;
            if (remaining == 0m)
            {
                // Nothing of this line is left to ship: it adds nothing, priced or not (its shipped part is the next stage's).
                continue;
            }

            if (line.Commercial.UnitPrice is null)
            {
                basis.Reason = SalesDraftRiskReasonCodesV1.OrderLineUnpriced;
                return null;
            }

            if (line.Commercial.PriceUnitId != line.OrderUnitId)
            {
                basis.Reason = SalesDraftRiskReasonCodesV1.PriceUnitConversionUnavailable;
                return null;
            }

            try
            {
                var net = SalesLinePriceCalculator.NetUnitPrice(line.Commercial)!.Value;
                if (net < 0m)
                {
                    basis.Reason = SalesDraftRiskReasonCodesV1.OrderAmountInvalid;
                    return null;
                }

                total += Money.Round(remaining * net, order.Header.CurrencyId, scale).Amount;
            }
            catch (Exception exception) when (exception is OverflowException or FormatException or ArgumentException)
            {
                basis.Reason = SalesDraftRiskReasonCodesV1.OrderAmountInvalid;
                return null;
            }
        }

        return total;
    }

    // ---- wire helpers -----------------------------------------------------------------------------------------------------

    private static string FigureState(SalesRiskInputState state) => state switch
    {
        SalesRiskInputState.Available => SalesDraftRiskFigureStatesV1.Available,
        SalesRiskInputState.NoSource => SalesDraftRiskFigureStatesV1.NoSource,
        _ => SalesDraftRiskFigureStatesV1.Unavailable,
    };

    /// <summary>
    /// Exact invariant text: padded to the order's money scale ("1250.00") when the amount is in the order's currency and fits that
    /// scale; otherwise the exact value with no trailing zeros (another currency's scale is not this order's to choose).
    /// </summary>
    private static string AmountText(decimal amount, Guid amountCurrencyId, Guid currencyId, int scale) =>
        amountCurrencyId == currencyId && Money.FitsScale(amount, scale)
            ? amount.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : SalesLinePriceCalculator.Format(amount);
}
