using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public sealed class AuditDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;

        return new AuditDbContext(options, new CompanyContext());
    }
}
