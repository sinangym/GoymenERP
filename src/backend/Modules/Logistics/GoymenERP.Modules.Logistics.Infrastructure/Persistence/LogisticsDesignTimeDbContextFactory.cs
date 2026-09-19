using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed class LogisticsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LogisticsDbContext>
{
    public LogisticsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .Options;
        return new LogisticsDbContext(options, new DesignTimeCompanyContext());
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) => throw new InvalidOperationException("Design-time Logistics DbContext şirket bağlamı çözmez.");
    }
}
