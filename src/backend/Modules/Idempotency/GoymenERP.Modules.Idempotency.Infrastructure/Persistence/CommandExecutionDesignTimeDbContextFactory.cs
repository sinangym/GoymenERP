using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence;

public sealed class CommandExecutionDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CommandExecutionDbContext>
{
    public CommandExecutionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CommandExecutionDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpDesign;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "idempotency"))
            .Options;

        return new CommandExecutionDbContext(options, new CompanyContext());
    }
}
