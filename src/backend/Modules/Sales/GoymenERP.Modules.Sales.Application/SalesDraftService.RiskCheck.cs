using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
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
/// remains enters as the PAYABLE amount (SAT-D183: VAT included, withheld VAT deducted), from API-03's calculator (<see cref="SalesOrderTotalsCalculator"/>) with each
/// basis line's own tax snapshot (MOD-03 → Sales; an archived revision's lines with the tax they were approved with); a line
/// without a usable tax makes the remainder "Unavailable" (<c>vat_inclusive_amount_unavailable</c>) — the VAT-excluded net is never
/// substituted. One amount per order in its own currency (another currency than the check's is SAT-D47's). Nothing left (or no
/// open approved order) is a recorded zero. An unpriced line, a price in another unit, an unreadable delivery owner or more
/// orders than one read takes are "Unavailable" first, with their own reasons.</item>
/// <item><b>This order</b> (SAT-D48): the order-time check is gross risk + this order. An order whose current revision is
/// approved is already in the remainder (adds nothing); a cancelled or closed order adds nothing; any other order adds its
/// VAT-included total (a revision awaiting re-approval: only its increase over the approved remainder), from API-03's calculator
/// with the lines' own tax — "Unavailable" when a line has no usable tax.</item>
/// <item><b>The decision</b> is <see cref="SalesCustomerRiskCalculator"/>'s alone, in the order's currency at its money scale.</item>
/// <item><b>Amounts</b> are disclosed only with <c>sales.orders.price.read</c>; the states, sources, reasons and result always.</item>
/// <item><b>Fences</b> (the draft read's): before anything is read, after the draft, after the order list, after each owner.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService : ISalesOrderRiskOutcomeSource
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

        var (party, remainder, shippedNotInvoiced, thisOrder, local, limitCurrency, evaluation) =
            await EvaluateRiskAsync(scope, bindings.ReadPermission, draft, cancellationToken);

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
    /// EK56 SAT-D284 (<see cref="ISalesOrderRiskOutcomeSource"/>): the same deterministic evaluation as the risk check, read under the
    /// calling command's own permission (the risk exception request's, never <c>sales.drafts.read</c>; SAT-D162). Only the outcome
    /// leaves: no amount is disclosed, and the price read is not asked. A lost authority stays a 401/403; an owner that cannot
    /// answer makes the outcome <c>Unavailable</c>, never <c>Within</c>.
    /// </summary>
    public async Task<SalesRiskOutcome> EvaluateOrderRiskOutcomeAsync(SalesOrderDraft order, string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(permission, cancellationToken);
        if (scope.CompanyId != order.CompanyId)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        var (_, _, _, _, _, _, evaluation) = await EvaluateRiskAsync(scope, permission, order, cancellationToken);
        return evaluation.Outcome;
    }

    /// <summary>The inputs and the calculator's evaluation of one order, every owner read fenced on <paramref name="permission"/>.</summary>
    private async Task<(PartyRiskInputs Party, SalesRiskInput Remainder, SalesRiskInput ShippedNotInvoiced, SalesRiskInput ThisOrder,
        SalesRiskCurrency? Local, SalesRiskCurrency? LimitCurrency, SalesRiskEvaluation Evaluation)> EvaluateRiskAsync(Scope scope,
        string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        var party = await PartyRiskAsync(scope, permission, draft, cancellationToken);
        var remainder = await ApprovedRemainderAsync(scope, permission, draft, cancellationToken);
        var shippedNotInvoiced = SalesRiskInput.NoSource(SalesDraftRiskReasonCodesV1.InvoicingOwnerMissing);
        var thisOrder = await ThisOrderAsync(scope, permission, draft, cancellationToken);

        // SAT-D53: the totals in the company's local currency (TL) and in the credit limit's currency; never the order's.
        var local = await RiskCurrencyAsync(scope, permission, draft, SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
        var limitCurrency = party.CreditLimit is { State: SalesRiskInputState.Available, Amounts: [{ } limitAmount] }
            ? await RiskCurrencyAsync(scope, permission, draft, limitAmount.CurrencyId, cancellationToken)
            : null;
        var rates = await RiskExchangeRatesAsync(scope, permission, limitCurrency,
            [party.OpenReceivables, shippedNotInvoiced, remainder, thisOrder], cancellationToken);
        var evaluation = SalesCustomerRiskCalculator.Evaluate(local, limitCurrency, party.OpenReceivables, shippedNotInvoiced,
            remainder, thisOrder, party.CreditLimit, rates);
        return (party, remainder, shippedNotInvoiced, thisOrder, local, limitCurrency, evaluation);
    }

    /// <summary>
    /// SAT-D53: a risk total's currency with its approved money scale — the order's own snapshot when it is the order's currency,
    /// otherwise the Settings money profile of the Sales order operation for the order's business date (active and in effect).
    /// An owner that cannot answer gives null (that total is then not made), never a default scale; a lost authority stays a 403.
    /// </summary>
    private async Task<SalesRiskCurrency?> RiskCurrencyAsync(Scope scope, string? permission, SalesOrderDraft draft, Guid currencyId,
        CancellationToken cancellationToken)
    {
        if (currencyId == draft.Header.CurrencyId)
        {
            return new SalesRiskCurrency(currencyId, Math.Min(draft.Money.Scale, Money.MaximumScale));
        }

        MoneyProfileSnapshot profile;
        try
        {
            profile = await ReadMoneyProfileAsync(scope, permission, currencyId, draft.Header.BusinessDate,
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

    /// <summary>
    /// SAT-D47 / SAT-D200 / SAT-D203 (DEC-FIN-010; SAT-D54/D55/D170): the check day's TCMB rate into TL of every foreign currency
    /// the totals need — each foreign amount's currency and a foreign credit limit's currency — from the Settings rate-day port
    /// for the CHECK day (the server's Europe/Istanbul business day), computed on every check and never frozen (SAT-D172).
    /// The calculator derives TL → foreign (÷) and foreign → foreign (via TL) from them. A rate the port cannot resolve ("kur
    /// bulunamadı/belirlenemedi"), an absent port or a failing one leaves it out, so the amounts needing it stay
    /// <c>exchange_rate_unavailable</c> (SAT-D80 "hesaplanamadı"); a lost authority stays a 403.
    /// </summary>
    private async Task<SalesRiskExchangeRates> RiskExchangeRatesAsync(Scope scope, string? permission, SalesRiskCurrency? limitCurrency,
        IEnumerable<SalesRiskInput> inputs, CancellationToken cancellationToken)
    {
        var local = SettingsCurrencyIdentitiesV1.TurkishLira;
        var rates = new Dictionary<Guid, ExchangeRate>();
        if (exchangeRates is null)
        {
            return new SalesRiskExchangeRates(local, rates);
        }

        var currencies = inputs
            .Where(input => input.State == SalesRiskInputState.Available)
            .SelectMany(input => input.Amounts)
            .Where(amount => amount is not null)
            .Select(amount => amount!.CurrencyId)
            .Append(limitCurrency?.CurrencyId ?? Guid.Empty)
            .Where(currency => currency != local && currency != Guid.Empty)
            .Distinct()
            .ToArray();
        if (currencies.Length == 0)
        {
            return new SalesRiskExchangeRates(local, rates);
        }

        var checkDay = ServerBusinessDay();
        foreach (var currency in currencies)
        {
            ExchangeRateResolutionV1 resolution;
            try
            {
                resolution = await GuardedAsync(scope, permission,
                    () => exchangeRates.ResolveAsync(checkDay, currency, local, cancellationToken), cancellationToken);
            }
            catch (SettingsPrerequisiteException exception) when (exception.Code == ExchangeRateProblemCodesV1.AccessDenied)
            {
                throw new SalesDraftServiceException(403, "forbidden");
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
            {
                continue;
            }

            if (resolution.State == ExchangeRateResolutionStatesV1.Resolved && resolution.SourceCurrencyId == currency
                && resolution.TargetCurrencyId == local && resolution.RateDay == checkDay
                && decimal.TryParse(resolution.SellingRate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                && value > 0m)
            {
                rates[currency] = ExchangeRate.Of(currency, local, value);
            }
        }

        return new SalesRiskExchangeRates(local, rates);
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

    private async Task<PartyRiskInputs> PartyRiskAsync(Scope scope, string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        if (partyRisk is null)
        {
            // Not composed (the owner registers the port in its own module): every input unavailable, never an assumed zero.
            return PartyRiskUnavailable(SalesDraftRiskReasonCodesV1.PartyRiskProviderUnavailable);
        }

        SalesPartyRiskReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission,
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
    private sealed record RiskBasisLine(Guid LineId, Guid OrderUnitId, decimal Quantity, CommercialDraftFields Commercial,
        SalesLineTaxSnapshot? Tax);

    private async Task<SalesRiskInput> ApprovedRemainderAsync(Scope scope, string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        IReadOnlyList<SalesOrderDraft> orders;
        try
        {
            orders = await GuardedAsync(scope, permission,
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

        var amounts = new List<SalesRiskAmount>();
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

            var (recorded, unread) = await RecordedForRiskAsync(scope, permission, order, cancellationToken);
            if (recorded is null)
            {
                return SalesRiskInput.Unavailable(unread!);
            }

            if (OrderRemains(basis, recorded) is not { } remains)
            {
                return SalesRiskInput.Unavailable(basis.Reason!);
            }

            if (!remains)
            {
                continue;
            }

            // SAT-D46 / SAT-D183: what remains enters as the payable amount (VAT included, withheld VAT deducted), from the one
            // totals calculator with the basis lines' own tax snapshots — never the VAT-excluded net; a line without a usable tax
            // makes the remainder unavailable.
            if (RemainingPayable(basis, recorded) is not { } payable)
            {
                return SalesRiskInput.Unavailable(basis.Reason!);
            }

            if (payable.Amount > 0m)
            {
                amounts.Add(payable);
            }
        }

        return SalesRiskInput.Available(amounts, SalesRiskTaxBasis.IncludingTax);
    }

    /// <summary>
    /// What the delivery owner recorded as shipped / delivered per line of <paramref name="order"/>; null with the reason when it
    /// cannot say (not composed, failing or malformed) — never "nothing shipped".
    /// </summary>
    private async Task<(Dictionary<Guid, (decimal Shipped, decimal Delivered)>? Recorded, string? Reason)> RecordedForRiskAsync(Scope scope,
        string? permission, SalesOrderDraft order, CancellationToken cancellationToken)
    {
        if (deliveries is null)
        {
            return (null, SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable);
        }

        SalesOrderDeliveryReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, permission,
                () => deliveries.ReadOrderDeliveriesAsync(order.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return (null, SalesDraftRiskReasonCodesV1.DeliveryProviderUnavailable);
        }

        try
        {
            return (RecordedDeliveries(scope, order, answer), null);
        }
        catch (SalesDraftServiceException)
        {
            return (null, SalesDraftRiskReasonCodesV1.DeliveryAnswerInvalid);
        }
    }

    /// <summary>
    /// SAT-D46 / SAT-D183: the PAYABLE amount (VAT included, the withheld VAT deducted — what the customer owes us) of what of
    /// <paramref name="basis"/> is left to ship — each line's approved
    /// quantity − shipped, above zero — from <see cref="SalesOrderTotalsCalculator.CalculateWithLineTax(IReadOnlyList{SalesTotalsLineInput}, Guid, int)"/>
    /// with each basis line's own tax snapshot, in the basis' currency at its money scale. Null with the reason set when it cannot be
    /// computed: an unpriced line, a price in another unit, a line without a usable tax (<c>vat_inclusive_amount_unavailable</c>) or
    /// an amount beyond the exact range.
    /// </summary>
    private static SalesRiskAmount? RemainingPayable(RiskOrderBasis basis, IReadOnlyDictionary<Guid, (decimal Shipped, decimal Delivered)> recorded)
    {
        var inputs = new List<SalesTotalsLineInput>(basis.Lines.Count);
        foreach (var line in basis.Lines)
        {
            var shipped = recorded.TryGetValue(line.LineId, out var entry) ? entry.Shipped : 0m;
            if (line.Quantity > shipped)
            {
                inputs.Add(new SalesTotalsLineInput(line.LineId, inputs.Count + 1, line.OrderUnitId, line.Quantity - shipped,
                    line.Commercial, line.Tax));
            }
        }

        if (inputs.Count == 0)
        {
            return new SalesRiskAmount(basis.CurrencyId, 0m);
        }

        SalesOrderTotals totals;
        try
        {
            totals = SalesOrderTotalsCalculator.CalculateWithLineTax(inputs, basis.CurrencyId, Math.Min(basis.MoneyScale, Money.MaximumScale));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or SalesRuleException)
        {
            basis.Reason = SalesDraftRiskReasonCodesV1.OrderBasisUnreadable;
            return null;
        }

        var document = totals.Document;
        if (document.State != SalesDocumentTotalState.Computed)
        {
            // An amount beyond the exact decimal range is no amount (never a truncated one): the calculator's amount_invalid.
            basis.Reason = document.UnpricedLineCount > 0 ? SalesDraftRiskReasonCodesV1.OrderLineUnpriced
                : document.PriceUnitNotOrderUnitLineCount > 0 ? SalesDraftRiskReasonCodesV1.PriceUnitConversionUnavailable
                : document.State == SalesDocumentTotalState.AmountOutOfRange || document.AmountOutOfRangeLineCount > 0
                    ? SalesRiskReasonCodes.AmountInvalid
                    : SalesDraftRiskReasonCodesV1.OrderBasisUnreadable;
            return null;
        }

        if (document.TaxState != SalesTaxState.Computed || document.PayableAmount is not { } payable)
        {
            basis.Reason = SalesDraftRiskReasonCodesV1.VatInclusiveAmountUnavailable;
            return null;
        }

        // SAT-D183 (EK26): the amount the customer pays US — VAT included, the withheld VAT deducted (e.g. 100.000 + %20 with 5/10
        // → 110.000); without withholding it is the gross (SAT-D46 unchanged).
        return new SalesRiskAmount(basis.CurrencyId, payable.Amount);
    }

    /// <summary>
    /// SAT-D48: this order as far as it is not already in the approved remainder. Cancelled or closed: nothing. Current revision
    /// approved: already in the remainder. Never approved: its whole VAT-included total (SAT-D46). A revision of an approved order
    /// awaiting re-approval: its new VAT-included remaining amount REPLACES the old approved remainder already counted, so only the
    /// difference is added (DEC-FIN-007, no double count); a revision that lowers the amount adds nothing (the gross risk is never
    /// reduced by an amount not yet approved — fail-safe). Every amount is the one calculator's with the lines' own tax snapshots
    /// (MOD-03 → Sales); a line without a usable tax makes it unavailable (<c>vat_inclusive_amount_unavailable</c>).
    /// </summary>
    private async Task<SalesRiskInput> ThisOrderAsync(Scope scope, string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        if (draft.State != SalesDraftState.Draft || draft.ApprovalState == InternalApprovalState.Approved)
        {
            return SalesRiskInput.Available([], SalesRiskTaxBasis.IncludingTax);
        }

        var current = CurrentBasis(draft);
        if (draft.ArchivedRevisions.Count == 0)
        {
            // Never approved: nothing of it shipped (the line port refused it), so the whole order is what it adds.
            return RemainingPayable(current, new Dictionary<Guid, (decimal Shipped, decimal Delivered)>()) is { } whole
                ? SalesRiskInput.Available(whole.Amount > 0m ? [whole] : [], SalesRiskTaxBasis.IncludingTax)
                : SalesRiskInput.Unavailable(current.Reason!);
        }

        if (RiskBasis(draft) is not { } approved)
        {
            return SalesRiskInput.Unavailable(SalesDraftRiskReasonCodesV1.OrderBasisUnreadable);
        }

        var (recorded, unread) = await RecordedForRiskAsync(scope, permission, draft, cancellationToken);
        if (recorded is null)
        {
            return SalesRiskInput.Unavailable(unread!);
        }

        if (RemainingPayable(current, recorded) is not { } revised)
        {
            return SalesRiskInput.Unavailable(current.Reason!);
        }

        if (RemainingPayable(approved, recorded) is not { } counted)
        {
            return SalesRiskInput.Unavailable(approved.Reason!);
        }

        if (revised.CurrencyId != counted.CurrencyId)
        {
            // A currency revision awaiting approval: the two amounts are not comparable without a rate (SAT-D47).
            return SalesRiskInput.Unavailable(SalesRiskReasonCodes.ExchangeRateUnavailable);
        }

        var added = revised.Amount - counted.Amount;
        return SalesRiskInput.Available(added > 0m ? [new SalesRiskAmount(revised.CurrencyId, added)] : [], SalesRiskTaxBasis.IncludingTax);
    }

    /// <summary>The order's CURRENT lines as a basis (its current currency and money scale).</summary>
    private static RiskOrderBasis CurrentBasis(SalesOrderDraft order) =>
        new([.. order.Lines.Select(line => new RiskBasisLine(line.Id, line.OrderUnit.UnitId, line.Quantity.Value, line.Commercial, line.Tax))],
            order.Header.CurrencyId, order.Money.Scale);

    /// <summary>
    /// The approved commitment's lines with the currency and money scale they are valued in; <see cref="Reason"/> is set by
    /// <see cref="OrderRemains"/> / <see cref="RemainingPayable"/> on refusal.
    /// </summary>
    private sealed class RiskOrderBasis(IReadOnlyList<RiskBasisLine> lines, Guid currencyId, int moneyScale)
    {
        public IReadOnlyList<RiskBasisLine> Lines { get; } = lines;
        public Guid CurrencyId { get; } = currencyId;
        public int MoneyScale { get; } = moneyScale;
        public string? Reason { get; set; }
    }

    /// <summary>
    /// SAT-D115 / SAT-D166 (EK23) "kabul edilen son revizyona göre": when the customer rejected the last approved
    /// revision, the revision the customer last ACCEPTED — the commitment that ships and is invoiced; the rejected revision is
    /// no commitment (a revision still awaiting the acceptance is, SAT-D50). Else the current lines while the current revision is
    /// approved; else (a revision awaiting re-approval) the LAST APPROVED revision's lines, never the unapproved ones. Null
    /// (fail-closed) when an archived quantity is not exact text or an archived line that still exists is in another unit now.
    /// </summary>
    private static RiskOrderBasis? RiskBasis(SalesOrderDraft order)
    {
        if (order.CustomerAcceptedFallback is { } accepted)
        {
            // K3-REVIEW-B round 2 P3-2 (fail-closed): an accepted revision under another customer or currency than the header is
            // not this customer's commitment in this currency (the order ships nothing then) — never counted under the header.
            return SalesOrderDraft.HeaderDiffersFrom(order.Header, accepted) ? null : ArchivedBasis(order, accepted);
        }

        if (order.ApprovalState == InternalApprovalState.Approved)
        {
            return CurrentBasis(order);
        }

        return order.ArchivedRevisions.Count == 0 ? null : ArchivedBasis(order, order.ArchivedRevisions[^1]);
    }

    /// <summary>An archived approved revision's lines, with the same exact-text and unit checks for every archived basis.</summary>
    private static RiskOrderBasis? ArchivedBasis(SalesOrderDraft order, ApprovedRevisionSnapshot approved)
    {
        var current = order.Lines.ToDictionary(line => line.Id);
        var lines = new List<RiskBasisLine>(approved.Lines.Count);
        foreach (var line in approved.Lines)
        {
            if (!SalesLinePriceCalculator.TryParseExact(line.QuantityText, out var quantity) || quantity < 0m
                || (current.TryGetValue(line.LineId, out var now) && now.OrderUnit.UnitId != line.OrderUnitId))
            {
                return null;
            }

            // MOD-03 → Sales: the archived line's tax as it was approved (SAT-D115/D166: the accepted revision's own tax).
            lines.Add(new RiskBasisLine(line.LineId, line.OrderUnitId, quantity, line.Commercial, line.Tax));
        }

        // The archived revision is valued in the currency and money scale it was approved with.
        return new RiskOrderBasis(lines, approved.Header?.CurrencyId ?? order.Header.CurrencyId, approved.MoneyScale);
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
