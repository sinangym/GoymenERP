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
public sealed class PurchaseInvoiceImportProductSearch(IProductPurchaseCandidatesV1 source, ICompanyContext company, ICurrentUser user)
    : IPurchaseInvoiceImportProductSearchV1
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
        return (answer.Items.Select(x => new PurchaseInvoiceImportProductV1(x.ProductId, x.ProductVersion, x.Code, x.Name,
            x.UnitId, x.UnitCode, x.ProfileId, x.ProfileVersion)).ToArray(), answer.SearchWindowFull);
        bool Same() => id != Guid.Empty && company.IsResolved && company.CompanyId == id && user.IsAuthenticated
            && !string.IsNullOrWhiteSpace(actor) && actor == user.UserId && user.CanAccessCompany(id);
    }
}
public static class PurchaseInvoiceImportProductSearchComposition
{
    public static IServiceCollection AddPurchaseInvoiceImportProductSearch(this IServiceCollection services)
    { services.TryAddScoped<IPurchaseInvoiceImportProductSearchV1, PurchaseInvoiceImportProductSearch>(); return services; }
}
