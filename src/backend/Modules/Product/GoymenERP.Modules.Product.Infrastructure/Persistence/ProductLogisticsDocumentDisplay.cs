using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>Known-id display only over Product-owned storage, under the producing Logistics read. No price, role,
/// lifecycle predicate, cross-module table or new global Product authority is exposed.</summary>
public sealed class ProductLogisticsDocumentDisplay(ProductDbContext db, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority) : IProductLogisticsDocumentDisplayV1
{
    public async Task<ProductLogisticsDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        ProductLogisticsDocumentDisplayV1 Closed(ProductLogisticsDocumentDisplayOutcomeV1 outcome) => new(outcome, companyId, productId, null, null, null);
        async Task<bool> Allowed()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same()) return false;
            var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.LogisticsLoadingInstructionsRead, companyId, cancellationToken);
            return Same() && scope.IsGranted;
        }
        bool Same() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
        try
        {
            if (!await Allowed()) return Closed(ProductLogisticsDocumentDisplayOutcomeV1.Forbidden);
            if (productId == Guid.Empty) return Closed(ProductLogisticsDocumentDisplayOutcomeV1.NotFound);
            var row = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
            if (!await Allowed()) return Closed(ProductLogisticsDocumentDisplayOutcomeV1.Forbidden);
            if (row is null) return Closed(ProductLogisticsDocumentDisplayOutcomeV1.NotFound);
            if (row.Version < 1 || string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.Name))
                return Closed(ProductLogisticsDocumentDisplayOutcomeV1.Unavailable);
            return new(ProductLogisticsDocumentDisplayOutcomeV1.Available, companyId, productId,
                row.Version.ToString(CultureInfo.InvariantCulture), row.Code, row.Name);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Closed(ProductLogisticsDocumentDisplayOutcomeV1.Unavailable); }
    }
}
public static class ProductLogisticsDocumentDisplayRegistration
{
    public static IServiceCollection AddProductLogisticsDocumentDisplay(this IServiceCollection services,
        Func<IServiceProvider, bool> runtimeEnabled)
    {
        ArgumentNullException.ThrowIfNull(runtimeEnabled);
        services.AddScoped<ProductLogisticsDocumentDisplay>();
        services.AddScoped<IProductLogisticsDocumentDisplayV1>(provider => runtimeEnabled(provider)
            ? provider.GetRequiredService<ProductLogisticsDocumentDisplay>() : new Unavailable());
        return services;
    }
    private sealed class Unavailable : IProductLogisticsDocumentDisplayV1
    {
        public Task<ProductLogisticsDocumentDisplayV1> ReadAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ProductLogisticsDocumentDisplayV1(ProductLogisticsDocumentDisplayOutcomeV1.Unavailable,
                companyId, productId, null, null, null));
        }
    }
}
