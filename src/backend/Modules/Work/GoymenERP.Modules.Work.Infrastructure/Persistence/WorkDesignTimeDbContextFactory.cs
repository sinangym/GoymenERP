using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

public sealed class WorkDesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorkDbContext>
{
    public WorkDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema))
            .Options;
        return new WorkDbContext(options, new DesignTimeCompanyContext());
    }

    private sealed class DesignTimeCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;
        public Guid CompanyId => Guid.Empty;
        public void Set(Guid companyId) => throw new InvalidOperationException("Design-time Work DbContext does not resolve a company.");
    }
}
