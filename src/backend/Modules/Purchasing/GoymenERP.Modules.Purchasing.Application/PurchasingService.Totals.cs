using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// A2a-2 (API-20 <c>GET orders/{id}/totals</c>, plan TEK-01): the order's totals, read-only, under <c>purchasing.orders.read</c>.
/// <list type="bullet">
/// <item><b>Amounts</b> come from the shared <see cref="CommercialTaxTotalsCalculator"/> (the Sales rules, pinned to the Sales
/// calculator by a golden parity test): the line amount from the exact net (discount, then the maturity surcharge — ALS-D75), VAT per
/// line rounded once on that amount — the surcharge included (ALS-D77 "Vade farkı dahil hesaplansın": a cash price with a surcharge
/// and the same price typed as one deferred price give the same VAT; it replaces ALS-D38's EK3 note) — gross = net + VAT.</item>
/// <item><b>VAT rate</b> (ALS-D74 "Ürün türü varsayılanı"): the item type's default from the Settings line tax port for buyer situation
/// <c>Normal</c> on the order's business date; <c>Exempt</c>/<c>ZeroRated</c> answer 0 %. A product without an item type or without a
/// default in force (Settings <c>Unresolved</c>, with its reason) or an unreadable port is <c>NotComputable</c> for that line — the document VAT is then
/// <c>NotComputable</c> with the count; nothing is guessed. Supplier exemptions come later (ALS-D74).</item>
/// <item><b>Withholding</b> (D4, configurable like Sales; interpretation, labelled): the item type's default withholding share from the same
/// Settings answer (buyer situation <c>Normal</c>), applied per line from the rounded line VAT (SAT-D75). The VUK 232 amount threshold is
/// the Settings dated value (never a number in code; the owner records 12.000 TL there): the whole order's gross (VAT included, never
/// split) must EXCEED it, otherwise no line is withheld (<c>NotApplied</c>). A withholding line with no threshold in force, an unreadable
/// threshold or a threshold in another currency than the order is <c>NotComputable</c> with that reason — never a guessed amount (the
/// plan's fail-closed rule; the mali müşavir confirms before go-live, N-20). A line whose item type carries no withholding is
/// <c>NotApplied</c> without reading the threshold. Supplier exemptions come later (ALS-D74).</item>
/// <item><b>PRS-02</b>: without <c>purchasing.orders.price.read</c> every state is <c>Withheld</c>, every amount null, no line.</item>
/// </list>
/// The rate is read at request time for the order's business date (no snapshot is stored: the order's VAT is not recorded before the
/// invoice slice); a Settings record in force on a past date never changes, so a past order's answer is stable.
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseOrderTotalsView> GetOrderTotalsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        StoredPurchaseOrder? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        if (stored is null)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        var order = stored.Order;
        var version = order.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(disclosure, PurchasingPriceDisclosureV1.Visible, StringComparison.Ordinal))
        {
            const string withheld = PurchaseOrderTotalsStatesV1.Withheld;
            return new PurchaseOrderTotalsView(order.Id, version, PurchasingPriceDisclosureV1.Withheld, CurrencyView(order.Money),
                withheld, 0, null, withheld, 0, null, null, withheld, null, null, null, []);
        }

        var general = order.Lines.Where(line => line.Kind == PurchaseOrderLineKind.General).ToList();
        var taxes = await LineTaxesAsync(scope, general, stored.EffectiveOn, cancellationToken);
        var scale = order.Money?.Scale ?? 0;
        var inputs = general.Select(line => new CommercialTotalsLineInput(line.Id, line.Quantity.Value,
            line.Price is { } price
                ? new CommercialLinePriceInput(price.UnitPrice, price.DiscountPercent, price.DiscountAmount, price.DeferralPercent, price.DeferralAmount)
                : null,
            taxes[line.Id])).ToList();
        var totals = CommercialTaxTotalsCalculator.Calculate(inputs, Math.Min(scale, Money.MaximumScale));
        if (totals.Document.GrossAmount is { } gross && inputs.Any(input => input.Tax.Withholding == CommercialWithholdingState.Computed))
        {
            var decision = await WithholdingThresholdDecisionAsync(scope, stored.EffectiveOn, order.Money?.CurrencyCode, gross, cancellationToken);
            if (decision.Outcome != WithholdingThresholdOutcome.Exceeded)
            {
                inputs = [.. inputs.Select(input => input.Tax.Withholding != CommercialWithholdingState.Computed ? input : input with
                {
                    Tax = decision.Outcome == WithholdingThresholdOutcome.NotExceeded
                        ? CommercialLineTaxInput.Vat(input.Tax.VatPercent!.Value, null)
                        : CommercialLineTaxInput.VatWithWithholdingNotComputable(input.Tax.VatPercent!.Value, decision.Reason!),
                })];
                totals = CommercialTaxTotalsCalculator.Calculate(inputs, Math.Min(scale, Money.MaximumScale));
            }
        }

        var document = totals.Document;
        var lineNumbers = order.Lines.Select((line, index) => (line.Id, Number: index + 1)).ToDictionary(pair => pair.Id, pair => pair.Number);
        return new PurchaseOrderTotalsView(order.Id, version, PurchasingPriceDisclosureV1.Visible, CurrencyView(order.Money),
            State(document.State), document.UnpricedLineCount, Amount(document.NetAmount),
            State(document.VatState), document.VatNotComputableLineCount, Amount(document.VatAmount), Amount(document.GrossAmount),
            State(document.WithholdingState),
            document.WithholdingState == CommercialWithholdingState.NotComputable
                ? totals.Lines.Select(line => line.WithholdingReason).FirstOrDefault(reason => reason is not null) ?? PurchaseOrderTaxReasonsV1.TaxSourceInvalid
                : null,
            Amount(document.WithheldVatAmount), Amount(document.PayableAmount),
            [.. totals.Lines.Select(line => new PurchaseOrderLineTotalView(line.LineId, lineNumbers[line.LineId], State(line.State),
                Amount(line.NetUnitPrice), Amount(line.NetAmount), State(line.VatState), line.VatReason, Amount(line.VatPercent),
                Amount(line.VatAmount), Amount(line.GrossAmount), State(line.WithholdingState)))]);
    }

    /// <summary>
    /// The tax input of each general line (ALS-D74; see the class remarks). One Settings read per product; withholding is the item type's
    /// share (D4), the threshold is applied on the document by the caller. A denied actor is 403; an unavailable or absent port makes the VAT <c>NotComputable</c>.
    /// </summary>
    private async Task<Dictionary<Guid, CommercialLineTaxInput>> LineTaxesAsync(Scope scope, IReadOnlyList<PurchaseOrderLine> lines,
        DateOnly on, CancellationToken cancellationToken)
    {
        var byProduct = new Dictionary<Guid, CommercialLineTaxInput>();
        var result = new Dictionary<Guid, CommercialLineTaxInput>();
        foreach (var line in lines)
        {
            if (line.Price is null)
            {
                // No base: the calculator answers Unpriced whatever the tax says; no Settings read for it.
                result[line.Id] = CommercialLineTaxInput.VatNotComputable(PurchaseOrderTaxReasonsV1.TaxSourceUnavailable);
                continue;
            }

            if (!byProduct.TryGetValue(line.ProductId, out var tax))
            {
                tax = await ProductTaxAsync(scope, line.ProductId, on, cancellationToken);
                byProduct[line.ProductId] = tax;
            }

            result[line.Id] = tax;
        }

        return result;
    }

    private async Task<CommercialLineTaxInput> ProductTaxAsync(Scope scope, Guid productId, DateOnly on, CancellationToken cancellationToken)
    {
        if (lineTaxes is not { } resolver)
        {
            return CommercialLineTaxInput.VatNotComputable(PurchaseOrderTaxReasonsV1.TaxSourceUnavailable);
        }

        SettingsTaxDefaultResolutionV1 answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.ReadPermission,
                () => resolver.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, on, null, cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == "settings_access_denied")
        {
            throw new PurchasingServiceException(403, "forbidden");
        }
        catch (SettingsPrerequisiteException)
        {
            return CommercialLineTaxInput.VatNotComputable(PurchaseOrderTaxReasonsV1.TaxSourceUnavailable);
        }

        if (answer is null || answer.CompanyId != scope.CompanyId || (answer.ProductId is { } answered && answered != productId))
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
        }

        if (!string.Equals(answer.State, TaxResolutionStatesV1.Resolved, StringComparison.Ordinal))
        {
            return CommercialLineTaxInput.VatNotComputable(
                string.IsNullOrWhiteSpace(answer.Reason) ? PurchaseOrderTaxReasonsV1.TaxSourceInvalid : answer.Reason);
        }

        decimal? percent = answer.VatState switch
        {
            TaxResolutionStatesV1.Resolved => answer.VatPercent is { } value and >= 0m and <= 100m ? value : null,
            TaxResolutionStatesV1.Exempt or TaxResolutionStatesV1.ZeroRated => 0m,
            _ => null,
        };
        if (percent is not { } vat)
        {
            return CommercialLineTaxInput.VatNotComputable(PurchaseOrderTaxReasonsV1.TaxSourceInvalid);
        }

        // Withholding comes from the same answer: Resolved with a share in (0, 100] (the withheld part of the VAT, in percent),
        // NotApplied, or anything else (unknown state, a share outside the range) which is unreadable, not "none".
        return answer.WithholdingState switch
        {
            TaxResolutionStatesV1.NotApplied or TaxResolutionStatesV1.Exempt => CommercialLineTaxInput.Vat(vat, null),
            TaxResolutionStatesV1.Resolved when answer.WithholdingPercent is > 0m and <= 100m =>
                CommercialLineTaxInput.Vat(vat, answer.WithholdingPercent.Value / 100m),
            _ => CommercialLineTaxInput.VatWithWithholdingNotComputable(vat, PurchaseOrderTaxReasonsV1.TaxSourceInvalid),
        };
    }

    private enum WithholdingThresholdOutcome { Exceeded, NotExceeded, NotComputable }

    private readonly record struct WithholdingThresholdDecision(WithholdingThresholdOutcome Outcome, string? Reason);

    /// <summary>
    /// D4: compares the order's gross (VAT included) with the Settings withholding threshold in force on the order's date. Strictly
    /// above it → withholding applies. The threshold must be in the order's own currency (no conversion before A2a-KUR).
    /// </summary>
    private async Task<WithholdingThresholdDecision> WithholdingThresholdDecisionAsync(Scope scope, DateOnly on, string? orderCurrency,
        decimal gross, CancellationToken cancellationToken)
    {
        if (lineTaxes is not { } resolver)
        {
            return new(WithholdingThresholdOutcome.NotComputable, PurchaseOrderTaxReasonsV1.TaxSourceUnavailable);
        }

        SettingsWithholdingThresholdResolutionV1 threshold;
        try
        {
            threshold = await GuardedAsync(scope, bindings.ReadPermission, () => resolver.GetWithholdingThresholdAsync(on, cancellationToken),
                cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == "settings_access_denied")
        {
            throw new PurchasingServiceException(403, "forbidden");
        }
        catch (SettingsPrerequisiteException)
        {
            return new(WithholdingThresholdOutcome.NotComputable, PurchaseOrderTaxReasonsV1.TaxSourceUnavailable);
        }

        if (threshold is null || threshold.CompanyId != scope.CompanyId)
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
        }

        if (!string.Equals(threshold.State, TaxResolutionStatesV1.Resolved, StringComparison.Ordinal))
        {
            return new(WithholdingThresholdOutcome.NotComputable,
                string.IsNullOrWhiteSpace(threshold.Reason) ? PurchaseOrderTaxReasonsV1.WithholdingThresholdMissing : threshold.Reason);
        }

        if (threshold.Amount is not { } amount || amount < 0m)
        {
            return new(WithholdingThresholdOutcome.NotComputable, PurchaseOrderTaxReasonsV1.TaxSourceInvalid);
        }

        if (orderCurrency is null || !string.Equals(threshold.CurrencyCode, orderCurrency, StringComparison.Ordinal))
        {
            return new(WithholdingThresholdOutcome.NotComputable, PurchaseOrderTaxReasonsV1.WithholdingThresholdCurrencyMismatch);
        }

        return new(gross > amount ? WithholdingThresholdOutcome.Exceeded : WithholdingThresholdOutcome.NotExceeded, null);
    }

    private static string State(Enum state) => state.ToString();

    private static string? Amount(decimal? value) => value is { } amount ? CommercialLinePriceCalculator.Format(amount) : null;
}
