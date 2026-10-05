using System.Globalization;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// AT-018 / AT-021 on the order line (user decision 2026-09-30): the Product price list price is the line's REFERENCE and
/// its default until the person changes it; the person may overwrite the price, give a discount (percent or amount) and a
/// maturity surcharge (vade farkı, percent or amount); the reference and the adjustments stay visible and the net is the
/// one calculator's (<see cref="SalesLinePriceCalculator"/>). Sales asks the Product owner through
/// <see cref="IProductQuantityPriceReferenceReaderV1"/> (the port carries no authority: Sales measures its own price names, fenced
/// after every awaited owner read) in the draft's currency (Settings money profile's code) on the draft's business date.
/// </summary>
public sealed partial class SalesDraftService
{
    /// <summary>Every line's reference, adjustments and net, under the draft read AND the price read (else 403).</summary>
    public async Task<SalesOrderDraftLinePricingResponse> GetDraftLinePricingAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var draft = await LoadDraftAsync(scope, bindings.ReadPermission, draftId, cancellationToken);
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            // The pricing IS the price: without the price read nothing of it is disclosed (K3 P2).
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new SalesOrderDraftLinePricingResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            [.. draft.Lines.Select(line => Pricing(line.Id, line.LineNo, line.Commercial))]);
    }

    /// <summary>The Product owner's list prices for one line of the draft, under the price-write name.</summary>
    public async Task<SalesOrderDraftPriceReferencesResponse> GetLinePriceReferencesAsync(Guid draftId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.PricePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (draftId == Guid.Empty || lineId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var reader = priceReferences ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PriceListProviderUnavailable);
        var draft = await LoadDraftAsync(scope, permission, draftId, cancellationToken);
        var line = draft.Lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new SalesDraftServiceException(404, "not_found");
        var currencyCode = await CurrencyCodeAsync(scope, permission, draft, cancellationToken);
        // The Product owner does not convert quantities. The current accepted Sales path prices in the exact order unit;
        // future cross-unit pricing needs an owner-approved conversion contract rather than reusing this quantity.
        var priceUnitId = line.OrderUnit.UnitId;
        var answers = await GuardedAsync(scope, permission,
            () => reader.SearchAsync(line.Product.ProductId, priceUnitId, line.Quantity.Value, currencyCode,
                draft.Money.EffectiveOn, cancellationToken), cancellationToken);
        return new SalesOrderDraftPriceReferencesResponse(line.Id, currencyCode, draft.Money.EffectiveOn,
            [.. answers.Where(answer => answer.ProductId == line.Product.ProductId && answer.PriceUnitId == priceUnitId
                    && string.Equals(answer.VatBasis, "ExcludingVat", StringComparison.Ordinal))
                .Select(answer => Wire(Snapshot(answer)))]);
    }

    /// <summary>
    /// The commercial revision's references: for every request line that names a list version, the owner's price copied as
    /// the line's reference — the draft read, the currency code and each owner answer under the price-write fence. A version
    /// that no longer prices the line (or another currency) is a 422 on <c>lines[i].priceReference</c>, not recorded. Nothing
    /// is read when no line names one.
    /// </summary>
    private async Task<(IReadOnlyDictionary<Guid, PriceReferenceSnapshot> References, Scope Scope)> ResolvePriceReferencesAsync(Scope scope,
        string permission, Guid draftId, SalesDraftCommercialRevisionRequest request, CancellationToken cancellationToken)
    {
        var named = request.Lines.Select((line, index) => (line, index)).Where(pair => pair.line.PriceReference is not null).ToArray();
        if (named.Length == 0)
        {
            return (new Dictionary<Guid, PriceReferenceSnapshot>(), scope);
        }

        var reader = priceReferences ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PriceListProviderUnavailable);
        var draft = await LoadDraftAsync(scope, permission, draftId, cancellationToken);
        var currencyCode = await CurrencyCodeAsync(scope, permission, draft, cancellationToken);
        var references = new Dictionary<Guid, PriceReferenceSnapshot>();
        var errors = new List<SalesDraftFieldError>();
        foreach (var (line, index) in named)
        {
            var draftLine = draft.Lines.SingleOrDefault(candidate => candidate.Id == line.LineId);
            if (draftLine is null || line.PriceUnitId is not { } priceUnit)
            {
                // Not a line of this draft, or no price to reference: the domain answers it (lines mismatch / pairing).
                continue;
            }

            var reference = line.PriceReference!;
            if (priceUnit != draftLine.OrderUnit.UnitId)
            {
                // No quantity conversion is owned by Sales. A price in another unit cannot be tier-selected safely.
                errors.Add(new($"lines[{index}].priceReference", SalesDraftProblemCodesV1.PriceReferenceUnavailable));
                continue;
            }

            var answer = await GuardedAsync(scope, permission,
                () => reader.GetAsync(reference.PriceListId, reference.VersionNo, draftLine.Product.ProductId, priceUnit,
                    draftLine.Quantity.Value, draft.Money.EffectiveOn, cancellationToken), cancellationToken);
            if (answer is null
                || answer.PriceListId != reference.PriceListId
                || answer.VersionNo != reference.VersionNo
                || answer.ProductId != draftLine.Product.ProductId
                || answer.PriceUnitId != priceUnit
                || !string.Equals(answer.Channel, "Sales", StringComparison.Ordinal)
                || !string.Equals(answer.VatBasis, "ExcludingVat", StringComparison.Ordinal)
                || answer.ValidFrom > draft.Money.EffectiveOn
                || (answer.ValidThrough is { } validThrough && validThrough < draft.Money.EffectiveOn)
                || answer.LowerQuantity > draftLine.Quantity.Value
                || (answer.UpperQuantity is { } upperQuantity && draftLine.Quantity.Value >= upperQuantity))
            {
                errors.Add(new($"lines[{index}].priceReference", SalesDraftProblemCodesV1.PriceReferenceUnavailable));
                continue;
            }

            if (!string.Equals(answer.CurrencyCode, currencyCode, StringComparison.Ordinal))
            {
                errors.Add(new($"lines[{index}].priceReference", SalesDraftProblemCodesV1.PriceReferenceCurrencyMismatch));
                continue;
            }

            references[line.LineId] = Snapshot(answer);
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(422, "validation_failed", errors);
        }

        return (references, await RequireStillAsync(scope, permission, cancellationToken));
    }

    private async Task<SalesOrderDraft> LoadDraftAsync(Scope scope, string? permission, Guid draftId, CancellationToken cancellationToken)
    {
        try
        {
            // Another company's draft is null under the tenant filter: 404, never 403 (#257).
            return await GuardedAsync(scope, permission, () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken)
                ?? throw new SalesDraftServiceException(404, "not_found");
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>The draft currency's ISO code, from the Settings money profile the draft was created under (same operation, same date).</summary>
    private async Task<string> CurrencyCodeAsync(Scope scope, string? permission, SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        MoneyProfileV1 profile;
        try
        {
            profile = await GuardedAsync(scope, permission,
                () => settings.GetMoneyProfileAsync(draft.Money.CurrencyId, SalesMoneyOperations.OrderDraftV1, draft.Money.EffectiveOn,
                    cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw Settings(exception, "currencyId", SettingsPrerequisiteCodesV1.MoneyProfileNotFound, SalesDraftProblemCodesV1.UnsupportedCurrency);
        }

        if (profile.CompanyId != scope.CompanyId || profile.CurrencyId != draft.Money.CurrencyId || string.IsNullOrWhiteSpace(profile.CurrencyCode))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        return profile.CurrencyCode;
    }

    private static PriceReferenceSnapshot Snapshot(ProductPriceReferenceV1 answer) =>
        new(answer.PriceListId, answer.PriceListName, answer.VersionNo, answer.ValidFrom, answer.ValidThrough, answer.CurrencyCode,
            answer.PriceUnitId, SalesLinePriceCalculator.Format(answer.UnitPrice),
            answer.FloorUnitPrice is { } floor ? SalesLinePriceCalculator.Format(floor) : null, answer.VatBasis,
            SalesLinePriceCalculator.Format(answer.LowerQuantity),
            answer.UpperQuantity is { } upper ? SalesLinePriceCalculator.Format(upper) : null);

    private static SalesDraftPriceReferenceV1 Wire(PriceReferenceSnapshot reference) =>
        new(reference.PriceListId, reference.PriceListName, reference.VersionNo, reference.ValidFrom, reference.ValidThrough,
            reference.CurrencyCode, reference.PriceUnitId, reference.UnitPrice, reference.FloorUnitPrice, reference.VatBasis,
            reference.LowerQuantity, reference.UpperQuantity);

    private static SalesDraftLinePricingV1 Pricing(Guid lineId, int lineNo, CommercialDraftFields commercial) =>
        new(lineId, lineNo, commercial.UnitPrice, commercial.PriceUnitId, commercial.PriceSource,
            commercial.Reference is { } reference ? Wire(reference) : null, commercial.DiscountPercent, commercial.DiscountAmount,
            commercial.SurchargePercent, commercial.SurchargeAmount, SalesLinePriceCalculator.NetUnitPriceText(commercial),
            commercial.FloorException is { } exception
                ? new SalesDraftPriceFloorExceptionV1(exception.RequestedBy, exception.RequestedAtUtc, exception.ApprovedBy,
                    exception.ApprovedAtUtc, exception.Reason)
                : null);
}
