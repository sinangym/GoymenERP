using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// WP13: known-id display only over Product-owned storage, under the producing Purchasing order read (the Logistics document display's
/// shape). No price, role, lifecycle predicate, cross-module table or new global Product authority is exposed.
/// </summary>
public sealed class ProductPurchasingDocumentDisplay(ProductDbContext db, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority) : IProductPurchasingDocumentDisplayV1
{
    public async Task<ProductPurchasingDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        ProductPurchasingDocumentDisplayV1 Closed(ProductPurchasingDocumentDisplayOutcomeV1 outcome) => new(outcome, companyId, productId, null, null, null);
        async Task<bool> Allowed()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same()) return false;
            var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.PurchasingOrdersRead, companyId, cancellationToken);
            return Same() && scope.IsGranted;
        }
        bool Same() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
        try
        {
            if (!await Allowed()) return Closed(ProductPurchasingDocumentDisplayOutcomeV1.Forbidden);
            if (productId == Guid.Empty) return Closed(ProductPurchasingDocumentDisplayOutcomeV1.NotFound);
            var row = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
            if (!await Allowed()) return Closed(ProductPurchasingDocumentDisplayOutcomeV1.Forbidden);
            if (row is null) return Closed(ProductPurchasingDocumentDisplayOutcomeV1.NotFound);
            if (row.Version < 1 || string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.Name))
                return Closed(ProductPurchasingDocumentDisplayOutcomeV1.Unavailable);
            return new(ProductPurchasingDocumentDisplayOutcomeV1.Available, companyId, productId,
                row.Version.ToString(CultureInfo.InvariantCulture), row.Code, row.Name);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Closed(ProductPurchasingDocumentDisplayOutcomeV1.Unavailable); }
    }
}

public static class ProductPurchasingDocumentDisplayRegistration
{
    public static IServiceCollection AddProductPurchasingDocumentDisplay(this IServiceCollection services,
        Func<IServiceProvider, bool> runtimeEnabled)
    {
        ArgumentNullException.ThrowIfNull(runtimeEnabled);
        services.AddScoped<ProductPurchasingDocumentDisplay>();
        services.AddScoped<IProductPurchasingDocumentDisplayV1>(provider => runtimeEnabled(provider)
            ? provider.GetRequiredService<ProductPurchasingDocumentDisplay>() : new Unavailable());
        return services;
    }

    private sealed class Unavailable : IProductPurchasingDocumentDisplayV1
    {
        public Task<ProductPurchasingDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductPurchasingDocumentDisplayV1(ProductPurchasingDocumentDisplayOutcomeV1.Unavailable,
                companyId, productId, null, null, null));
        }
    }
}
