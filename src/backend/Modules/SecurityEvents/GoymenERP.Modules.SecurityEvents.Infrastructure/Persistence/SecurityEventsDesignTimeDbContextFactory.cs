using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;

public sealed class SecurityEventsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SecurityEventsDbContext>
{
    public SecurityEventsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SecurityEventsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SecurityEventModelBuilderExtensions.Schema))
            .Options;

        return new SecurityEventsDbContext(options, new CompanyContext());
    }
}
