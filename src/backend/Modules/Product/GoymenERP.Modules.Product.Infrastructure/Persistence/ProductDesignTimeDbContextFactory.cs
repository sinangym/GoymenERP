using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed class ProductDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
{
    public ProductDbContext CreateDbContext(string[] args) => new ProductDbContext(
        new DbContextOptionsBuilder<ProductDbContext>().UseSqlServer(
            "Server=localhost;Database=GoymenERP_DesignTime;Integrated Security=true;TrustServerCertificate=true",
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "product")).Options);
}
