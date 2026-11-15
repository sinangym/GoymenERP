using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Audit.Infrastructure;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(
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

        // F-023 (F023-1, F023-4): the settings fail closed at startup, before any service is registered.
        var refusedAttemptRetention = RefusedAttemptRetentionOptions.FromConfiguration(configuration);
        var refusedAttemptStandaloneWrite = RefusedAttemptStandaloneWriteOptions.FromConfiguration(configuration);
        var refusedAttemptAggregation = RefusedAttemptAggregationOptions.FromConfiguration(configuration);

        services.AddDbContext<AuditDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
        services.AddScoped<AuditEventCollector>();
        services.AddScoped<IAuditEventCollector>(provider =>
            provider.GetRequiredService<AuditEventCollector>());
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddSingleton(refusedAttemptRetention);
        services.AddSingleton(refusedAttemptStandaloneWrite);
        services.AddSingleton(refusedAttemptAggregation);
        services.TryAddSingleton(TimeProvider.System);
        // KIM-D11: disabled by the shipped empty setting; it admits every refusal until Sinan's values are entered.
        services.AddSingleton<RefusedAttemptAggregator>();
        services.AddScoped<IRefusedAttemptRecorderV1, RefusedAttemptRecorder>();

        return services;
    }

    public static async Task InitializeAuditModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

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
