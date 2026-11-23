using GoymenERP.Modules.Reporting.Domain;
using GoymenERP.Modules.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Reporting.Infrastructure;

public static class ReportingModuleExtensions
{
    /// <summary>
    /// WP14: registers the report centre's store and service. Every source defaults to "not connected"; the host replaces the
    /// ones an owner answers (TryAdd here, so the host's registrations win whatever the order). No permission is granted.
    /// </summary>
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Desteklenmeyen veritabani saglayicisi: {provider}");
        var connectionString = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SQL Server baglanti dizesi DB_CONNECTION_STRING secret/env degeriyle tanimlanmalidir.");

        services.AddDbContext<ReportingDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ReportingDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ReportingNotConnectedSourcesV1>();
        services.TryAddScoped<IReportingStockAgingSourceV1>(sp => sp.GetRequiredService<ReportingNotConnectedSourcesV1>());
        services.TryAddScoped<IReportingOtifSourceV1>(sp => sp.GetRequiredService<ReportingNotConnectedSourcesV1>());
        services.TryAddScoped<IReportingRecallSourceV1>(sp => sp.GetRequiredService<ReportingNotConnectedSourcesV1>());
        services.TryAddScoped<IReportingIntercompanySourceV1>(sp => sp.GetRequiredService<ReportingNotConnectedSourcesV1>());
        services.TryAddScoped<IReportingTrialBalanceSourceV1>(sp => sp.GetRequiredService<ReportingNotConnectedSourcesV1>());
        services.TryAddScoped<IReportingPartyAgingSourceV1>(sp => sp.GetRequiredService<ReportingNotConnectedSourcesV1>());
        services.AddScoped<IReportingServiceV1, ReportingServiceV1>();
        return services;
    }

    public static async Task InitializeReportingModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        if (dbContext.Database.IsRelational()) await dbContext.Database.MigrateAsync(cancellationToken);
        else await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
