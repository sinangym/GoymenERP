using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Logistics.Infrastructure;

/// <summary>
/// Logistics's module registration (LOG-4a): the DbContext, the store, the unit verifier and the
/// instruction service. The host calls it (the central writer's step) with the audit interceptor through
/// <paramref name="configureDbContext"/>, as for every module, and supplies the Cari account port
/// (<see cref="ILogisticsPartyAccountPortV1"/>), the Settings provider, the Sales line reference port
/// (registered by the Sales module) and the central authority. The shell capability provider is registered
/// by the host once the Logistics names are in the catalogue.
/// </summary>
public static class LogisticsModuleExtensions
{
    public static IServiceCollection AddLogisticsModule(this IServiceCollection services, IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<LogisticsDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<LogisticsStore>();
        services.AddScoped<ILogisticsInstructionStore>(serviceProvider => serviceProvider.GetRequiredService<LogisticsStore>());
        services.AddScoped<LoadingUnitVerifier>();
        services.TryAddSingleton(new LogisticsInstructionServiceBindings());
        services.AddScoped<ILogisticsInstructionService>(CreateInstructionService);
        return services;
    }

    // A named factory so that no registration statement of this module names the company context (architecture guard).
    private static LogisticsInstructionService CreateInstructionService(IServiceProvider serviceProvider) => new(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetRequiredService<ILogisticsPartyAccountPortV1>(),
        serviceProvider.GetRequiredService<ISalesOrderLineReferencePortV1>(),
        serviceProvider.GetRequiredService<LoadingUnitVerifier>(),
        serviceProvider.GetRequiredService<ILogisticsInstructionStore>(),
        serviceProvider.GetRequiredService<LogisticsInstructionServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>());

    /// <summary>Applies Logistics's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeLogisticsModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LogisticsDbContext>();
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
