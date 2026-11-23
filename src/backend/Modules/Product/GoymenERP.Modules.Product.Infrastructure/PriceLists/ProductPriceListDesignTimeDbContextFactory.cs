using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>Model generation only. Never enables a Product issuer, opens a SQL connection or applies a migration.</summary>
public sealed class ProductPriceListDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProductPriceListDbContext>
{
    public ProductPriceListDbContext CreateDbContext(string[] args) => new ProductPriceListDbContext(
        new DbContextOptionsBuilder<ProductPriceListDbContext>().UseSqlServer(
            "Server=localhost;Database=GoymenERP_DesignTime;Integrated Security=true;TrustServerCertificate=true",
            sql => sql.MigrationsHistoryTable(ProductPriceListDbContext.MigrationsHistoryTable, ProductPriceListDbContext.Schema)).Options,
        new DesignTimeCompanyContext());
    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;
        public Guid CompanyId => Guid.Empty;
        public void Set(Guid companyId) => throw new InvalidOperationException("Design-time Product lists do not resolve a company.");
    }
}
