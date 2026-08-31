using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Counterparty.Infrastructure;

public static class CounterpartyModuleExtensions
{
    public static IServiceCollection AddCounterpartyModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null,
        bool allowEphemeralCursorKey = false)
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
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.AddScoped<ICounterpartyReadService, CounterpartyReadService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICounterpartyReadSqlObserver, NullCounterpartyReadSqlObserver>();
        services.AddSingleton(new CounterpartyCursorRuntimeOptions(
            allowEphemeralCursorKey,
            configuration["Counterparty:CursorSigningKey"]));
        services.AddSingleton<ICounterpartyCursorCodec, CounterpartyCursorCodec>();
        services.AddScoped<ICounterpartyCommandService, CounterpartyCommandService>();
        services.AddScoped<ICounterpartyCompanyAccountLinkService, CounterpartyCompanyAccountLinkService>();

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
