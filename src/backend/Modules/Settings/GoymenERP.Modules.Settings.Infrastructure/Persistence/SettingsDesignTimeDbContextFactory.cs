using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
            .Options;
        return new SettingsDbContext(options, new CompanyContext());
    }
}
