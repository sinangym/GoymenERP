using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

public sealed class CounterpartyDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CounterpartyDbContext>
{
    public CounterpartyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? "Server=localhost,1433;Database=GoymenERP;User Id=sa;Password=Your_password123;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "counterparty"))
            .Options;

        return new CounterpartyDbContext(options);
    }
}
