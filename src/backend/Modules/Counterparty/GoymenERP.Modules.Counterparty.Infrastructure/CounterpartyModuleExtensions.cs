using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Counterparty.Infrastructure;

public static class CounterpartyModuleExtensions
{
    public static IServiceCollection AddCounterpartyModule(
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

        services.AddDbContext<CounterpartyDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "counterparty"));
            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddScoped<IPartyCodeAllocator, SqlPartyCodeAllocator>();
        services.AddScoped<ICounterpartyReadService, CounterpartyReadService>();
        services.AddScoped<ICounterpartyCommandService, CounterpartyCommandService>();

        return services;
    }

    public static async Task InitializeCounterpartyModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();

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
