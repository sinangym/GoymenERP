using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure;

public static class SecurityEventsModuleExtensions
{
    public static IServiceCollection AddSecurityEventsModule(
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

        // Fail closed at startup: every retention class must be configured.
        var retention = SecurityEventRetentionResolver.FromConfiguration(configuration);
        // Fail closed at startup: aggregation window and threshold are both set or both unset (E7).
        var aggregation = SecurityEventAggregationOptions.FromConfiguration(configuration);

        services.AddDbContext<SecurityEventsDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SecurityEventModelBuilderExtensions.Schema)));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(retention);
        services.AddSingleton(aggregation);
        services.AddSingleton<ISecurityEventStore, StandaloneSecurityEventStore>();
        services.AddSingleton<ISecurityEventWriterV1, SecurityEventWriter>();

        return services;
    }

    public static async Task InitializeSecurityEventsModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>();

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
