using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Idempotency.Infrastructure;

public static class IdempotencyModuleExtensions
{
    public static IServiceCollection AddIdempotencyModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<CommandExecutionDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "idempotency")));
        return services;
    }

    public static async Task InitializeIdempotencyModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CommandExecutionDbContext>();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
