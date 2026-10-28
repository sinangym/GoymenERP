using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// MOD-01 / API-06: the read-only customer risk check of an order under <c>sales.drafts.read</c> (DEC-FIN-007 / DEC-FIN-009).
/// Read-only in every module: no attempt, audit or version effect, nothing cached or copied into Sales.
/// <list type="bullet">
/// <item><b>Open receivables, credit limit, collateral</b>: the customer-finance owner's answer through
/// <see cref="ISalesPartyRiskReadPortV1"/> (never its tables; SAT-D51: Finance owns the limit and collateral, Cari only shows
/// them). Open receivables are net of the customer's unallocated payments (SAT-D52). Not composed, failing or malformed → each
/// is "Unavailable".</item>
/// <item><b>Shipped, not invoiced</b>: invoices are S5 — "NoSource" (never zero), so the result cannot be "Within" yet.</item>
/// <item><b>Approved, not shipped</b> (SAT-D50: every internally approved order, customer acceptance or not): for every open
/// approved order of the same customer account (the store's <see cref="ISalesOrderDraftStore.ListOpenApprovedOrdersAsync"/>), per
/// line the approved quantity − the delivery owner's shipped total, not below zero. The approved basis is the current lines while
/// the current revision is approved, else the last approved revision's (never unapproved quantities). SAT-D46: the amount of what
/// remains enters VAT-INCLUDED; the VAT-included order totals are API-03's (<see cref="SalesOrderTotalsCalculator"/>), whose VAT
/// is "NoSource" until the SAT-D73/D74/D82 rate sources exist (MOD-03), so a remainder above zero is "Unavailable" (<c>vat_inclusive_amount_unavailable</c>) — the VAT-excluded net is never substituted. Nothing left (or no
/// open approved order) is a recorded zero. An unpriced line, a price in another unit, an unreadable delivery owner or more
/// orders than one read takes are "Unavailable" first, with their own reasons.</item>
/// <item><b>This order</b> (SAT-D48): the order-time check is gross risk + this order. An order whose current revision is
/// approved is already in the remainder (adds nothing); a cancelled or closed order adds nothing; any other order adds its
/// VAT-included total, which is API-03's — "Unavailable" while API-03's VAT has no source.</item>
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
        var thisOrder = ThisOrder(draft);

        // SAT-D53: the totals in the company's local currency (TL) and in the credit limit's currency; never the order's.
        var local = await RiskCurrencyAsync(scope, draft, SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
        var limitCurrency = party.CreditLimit is { State: SalesRiskInputState.Available, Amounts: [{ } limitAmount] }
            ? await RiskCurrencyAsync(scope, draft, limitAmount.CurrencyId, cancellationToken)
            : null;
        var evaluation = SalesCustomerRiskCalculator.Evaluate(local, limitCurrency, party.OpenReceivables, shippedNotInvoiced,
            remainder, thisOrder, party.CreditLimit);

        // The price read is asked last, after every other fence: the freshest word on whether amounts cross.
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var visible = disclosure == SalesPriceDisclosure.Visible;
        var scales = new Dictionary<Guid, int> { [draft.Header.CurrencyId] = Math.Min(draft.Money.Scale, Money.MaximumScale) };
        foreach (var known in new[] { local, limitCurrency })
        {
            if (known is not null)
            {
                scales[known.CurrencyId] = known.Scale;
            }
        }

        string Text(decimal amount, Guid currencyId) => AmountText(amount, scales.TryGetValue(currencyId, out var scale) ? scale : null);
        SalesDraftRiskFigureV1 Figure(string kind, string source, SalesRiskInput input) =>
            new(kind, source, FigureState(input.State), input.State == SalesRiskInputState.Available ? input.TaxBasis?.ToString() : null,
                visible && input.State == SalesRiskInputState.Available
                    ? [.. input.Amounts.Select(amount => new SalesDraftRiskAmountV1(amount.CurrencyId, Text(amount.Amount, amount.CurrencyId)))]
                    : null,
                input.State == SalesRiskInputState.Available ? null : input.ReasonCode);
        SalesDraftRiskTotalsV1? Totals(SalesRiskTotals? totals) => totals is null
            ? null
            : new SalesDraftRiskTotalsV1(totals.Currency.CurrencyId,
                visible && totals.GrossRisk is { } gross ? Text(gross.Amount, gross.CurrencyId) : null,
                visible && totals.GrossRiskWithThisOrder is { } total ? Text(total.Amount, total.CurrencyId) : null);

        return new SalesOrderDraftRiskCheckResponse(
            draft.Version.ToString(CultureInfo.InvariantCulture),
            draft.Header.PartyId,
            draft.Header.CompanyAccountId,
            evaluation.Outcome switch
            {
                SalesRiskOutcome.Within => SalesDraftRiskResultsV1.Within,
                SalesRiskOutcome.Over => SalesDraftRiskResultsV1.Over,
                _ => SalesDraftRiskResultsV1.Unavailable,
            },
            [.. evaluation.Reasons.Select(reason => new SalesDraftRiskReasonV1(reason.Component, reason.Code))],
            disclosure,
            Totals(evaluation.InLocalCurrency),
            Totals(evaluation.InLimitCurrency),
            [
                Figure(SalesDraftRiskFigureKindsV1.OpenReceivables, party.OpenReceivablesSource, party.OpenReceivables),
                Figure(SalesDraftRiskFigureKindsV1.ShippedNotInvoiced, SalesDraftRiskSourcesV1.Invoicing, shippedNotInvoiced),
                Figure(SalesDraftRiskFigureKindsV1.ApprovedUnshippedRemainder, SalesDraftRiskSourcesV1.ApprovedOrderRemainders, remainder),
            ],
            Figure(SalesDraftRiskFigureKindsV1.ThisOrder, SalesDraftRiskSourcesV1.ThisOrder, thisOrder),
            Figure(SalesDraftRiskFigureKindsV1.CreditLimit, party.CreditLimitSource, party.CreditLimit),
            Figure(SalesDraftRiskFigureKindsV1.Collateral, party.CollateralSource, party.Collateral));
    }

    /// <summary>
    /// SAT-D53: a risk total's currency with its approved money scale — the order's own snapshot when it is the order's currency,
    /// otherwise the Settings money profile of the Sales order operation for the order's business date (active and in effect).
    /// An owner that cannot answer gives null (that total is then not made), never a default scale; a lost authority stays a 403.
    /// </summary>
    private async Task<SalesRiskCurrency?> RiskCurrencyAsync(Scope scope, SalesOrderDraft draft, Guid currencyId,
        CancellationToken cancellationToken)
    {
        if (currencyId == draft.Header.CurrencyId)
        {
            return new SalesRiskCurrency(currencyId, Math.Min(draft.Money.Scale, Money.MaximumScale));
        }

        MoneyProfileSnapshot profile;
        try
        {
            profile = await ReadMoneyProfileAsync(scope, bindings.ReadPermission, currencyId, draft.Header.BusinessDate,
                cancellationToken);
        }
        catch (SalesDraftServiceException exception) when (exception.Status is not (401 or 403))
        {
            return null;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return null;
        }

        var day = draft.Header.BusinessDate;
        return profile.IsActive && profile.EffectiveFrom <= day && (profile.EffectiveThrough is null || profile.EffectiveThrough >= day)
            ? new SalesRiskCurrency(currencyId, Math.Min(profile.Scale, Money.MaximumScale))
            : null;
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

        var anythingLeft = false;
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

            if (OrderRemains(basis, recorded) is not { } remains)
            {
                return SalesRiskInput.Unavailable(basis.Reason!);
            }

            anythingLeft |= remains;
        }

        // SAT-D46: what remains enters VAT-included, from API-03's totals — whose VAT has no source yet (SAT-D73/D74/D82, MOD-03).
        // Never the VAT-excluded net.
        return anythingLeft
            ? SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.VatInclusiveAmountUnavailable)
            : SalesRiskInput.Available([], SalesRiskTaxBasis.IncludingTax);
    }

    /// <summary>
    /// SAT-D48: this order as far as it is not already in the approved remainder. Cancelled or closed: nothing. Current revision
    /// approved: already in the remainder. Never approved: its whole VAT-included total (SAT-D46). A revision of an approved order
    /// awaiting re-approval: its new VAT-included remaining amount REPLACES the old approved remainder already counted, so only the
    /// difference is added (DEC-FIN-007, no double count). Both need API-03's VAT-included totals — unavailable while its VAT has no
/// source.
    /// </summary>
    private static SalesRiskInput ThisOrder(SalesOrderDraft draft) =>
        draft.State != SalesDraftState.Draft || draft.ApprovalState == InternalApprovalState.Approved
            ? SalesRiskInput.Available([], SalesRiskTaxBasis.IncludingTax)
            : SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.VatInclusiveAmountUnavailable);

    /// <summary>The approved commitment's lines; <see cref="Reason"/> is set by <see cref="OrderRemains"/> on refusal.</summary>
    private sealed class RiskOrderBasis(IReadOnlyList<RiskBasisLine> lines)
    {
        public IReadOnlyList<RiskBasisLine> Lines { get; } = lines;
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
                [.. order.Lines.Select(line => new RiskBasisLine(line.Id, line.OrderUnit.UnitId, line.Quantity.Value, line.Commercial))]);
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

        return new RiskOrderBasis(lines);
    }

    /// <summary>
    /// Whether anything of the order's approved lines is left to ship (approved − shipped above zero); a line with something left
    /// must be priced in its order unit, or the order's remainder cannot be valued at all. Null with the reason set on refusal.
    /// </summary>
    private static bool? OrderRemains(RiskOrderBasis basis, IReadOnlyDictionary<Guid, (decimal Shipped, decimal Delivered)> recorded)
    {
        var remains = false;
        foreach (var line in basis.Lines)
        {
            var shipped = recorded.TryGetValue(line.LineId, out var entry) ? entry.Shipped : 0m;
            if (line.Quantity <= shipped)
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

            remains = true;
        }

        return remains;
    }

    // ---- wire helpers -----------------------------------------------------------------------------------------------------

    private static string FigureState(SalesRiskInputState state) => state switch
    {
        SalesRiskInputState.Available => SalesDraftRiskFigureStatesV1.Available,
        SalesRiskInputState.NoSource => SalesDraftRiskFigureStatesV1.NoSource,
        _ => SalesDraftRiskFigureStatesV1.Unavailable,
    };

    /// <summary>
    /// Exact invariant text: padded to the currency's approved money scale ("1250.00") when that scale is known here and the amount
    /// fits it; otherwise the exact value with no trailing zeros (an unknown currency's scale is not this read's to choose).
    /// </summary>
    private static string AmountText(decimal amount, int? scale) =>
        scale is { } known && Money.FitsScale(amount, known)
            ? amount.ToString("F" + known.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : SalesLinePriceCalculator.Format(amount);
}
