using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

public sealed class CounterpartyDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CounterpartyDbContext>
{
    public CounterpartyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "counterparty"))
            .Options;

        return new CounterpartyDbContext(options, new CompanyContext());
    }
}
