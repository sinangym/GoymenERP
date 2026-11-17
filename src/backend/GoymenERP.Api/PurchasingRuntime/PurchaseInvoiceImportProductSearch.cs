using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>The actual Product operational-selection owner remains authoritative; Import + PriceRead
/// is additionally measured by the Purchasing caller. This adapter grants no selection permission.</summary>
public sealed class PurchaseInvoiceImportProductSearch(IProductPurchaseCandidatesV1 source, IProductItemTypeReaderV1 types,
    ICompanyContext company, ICurrentUser user) : IPurchaseInvoiceImportProductSearchV1, IPurchasingProductItemTypePortV1
{
    public async Task<(IReadOnlyList<PurchaseInvoiceImportProductV1> Items, bool SearchWindowFull)> SearchAsync(string? q, DateOnly on, CancellationToken token)
    {
        var id = company.IsResolved ? company.CompanyId : Guid.Empty; var actor = user.UserId;
        if (!Same()) throw new UnauthorizedAccessException();
        ProductPurchaseCandidatesV1 answer;
        try { answer = await source.SearchAsync(new(q, on, 25), token); }
        catch (ProductOperationException error) when (error.StatusCode == 403) { throw new PurchasingServiceException(403, "forbidden"); }
        catch (SettingsPrerequisiteException error) when (error.Code == "settings_access_denied") { throw new PurchasingServiceException(403, "forbidden"); }
        if (!Same()) throw new UnauthorizedAccessException();
        var items = new List<PurchaseInvoiceImportProductV1>();
        foreach (var x in answer.Items)
        {
            var type = await GetAsync(x.ProductId, token);
            if (!Same() || type.Outcome == PurchasingProductItemTypeOutcomeV1.Denied) throw new PurchasingServiceException(403, "forbidden");
            if (type.Outcome != PurchasingProductItemTypeOutcomeV1.Found || type.ItemType is null || type.Version is not > 0)
                throw new PurchasingServiceException(503, "service_unavailable");
            items.Add(new(x.ProductId, x.ProductVersion, x.Code, x.Name, x.UnitId, x.UnitCode, x.ProfileId, x.ProfileVersion,
                type.ItemType.Value.ToString(), type.Version.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return (items, answer.SearchWindowFull);
        bool Same() => id != Guid.Empty && company.IsResolved && company.CompanyId == id && user.IsAuthenticated
            && !string.IsNullOrWhiteSpace(actor) && actor == user.UserId && user.CanAccessCompany(id);
    }

    public async Task<PurchasingProductItemTypeSourceV1> GetAsync(Guid productId, CancellationToken token = default)
    {
        var id = company.IsResolved ? company.CompanyId : Guid.Empty; var actor = user.UserId;
        bool Same() => id != Guid.Empty && company.IsResolved && company.CompanyId == id && user.IsAuthenticated
            && !string.IsNullOrWhiteSpace(actor) && actor == user.UserId && user.CanAccessCompany(id);
        PurchasingProductItemTypeSourceV1 Answer(PurchasingProductItemTypeOutcomeV1 outcome,
            PurchasingProductItemTypeKindV1? kind = null, long? version = null) => new(outcome, id, productId, kind, version);
        if (!Same()) return Answer(PurchasingProductItemTypeOutcomeV1.Denied);
        ProductItemTypeReadV1 value;
        try { value = await types.GetAsync(productId, token); }
        catch (UnauthorizedAccessException) { return Answer(PurchasingProductItemTypeOutcomeV1.Denied); }
        catch (ProductItemTypeException error) when (error.StatusCode == 403) { return Answer(PurchasingProductItemTypeOutcomeV1.Denied); }
        catch (ProductOperationException error) when (error.StatusCode == 403) { return Answer(PurchasingProductItemTypeOutcomeV1.Denied); }
        if (!Same() || value?.Outcome == ProductItemTypeOutcomeV1.Denied) return Answer(PurchasingProductItemTypeOutcomeV1.Denied);
        var kind = value?.ItemType switch
        {
            ProductItemTypesV1.Steel => PurchasingProductItemTypeKindV1.Steel,
            ProductItemTypesV1.Scrap => PurchasingProductItemTypeKindV1.Scrap,
            ProductItemTypesV1.Service => PurchasingProductItemTypeKindV1.Service,
            _ => (PurchasingProductItemTypeKindV1?)null,
        };
        return value?.Outcome == ProductItemTypeOutcomeV1.Found && value.Version is > 0 && kind is not null
            ? Answer(PurchasingProductItemTypeOutcomeV1.Found, kind, value.Version)
            : Answer(PurchasingProductItemTypeOutcomeV1.Unavailable);
    }
}
public static class PurchaseInvoiceImportProductSearchComposition
{
    public static IServiceCollection AddPurchaseInvoiceImportProductSearch(this IServiceCollection services)
    {
        services.TryAddScoped<PurchaseInvoiceImportProductSearch>();
        services.TryAddScoped<IPurchaseInvoiceImportProductSearchV1>(provider => provider.GetRequiredService<PurchaseInvoiceImportProductSearch>());
        services.TryAddScoped<IPurchasingProductItemTypePortV1>(provider => provider.GetRequiredService<PurchaseInvoiceImportProductSearch>());
        return services;
    }
}
