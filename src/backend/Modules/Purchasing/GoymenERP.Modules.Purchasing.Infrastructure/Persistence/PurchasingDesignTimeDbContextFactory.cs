using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchasingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PurchasingDbContext>
{
    public PurchasingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options;
        return new PurchasingDbContext(options, new DesignTimeCompanyContext());
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) => throw new InvalidOperationException("Design-time Purchasing DbContext şirket bağlamı çözmez.");
    }
}
