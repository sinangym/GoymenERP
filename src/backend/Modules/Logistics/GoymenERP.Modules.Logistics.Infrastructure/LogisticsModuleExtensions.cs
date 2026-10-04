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
        services.AddScoped(CreateInstructionService);
        services.AddScoped<ILogisticsInstructionService>(serviceProvider =>
            serviceProvider.GetRequiredService<LogisticsInstructionService>());
        services.AddScoped<ILogisticsRealizationServiceV1>(serviceProvider =>
            serviceProvider.GetRequiredService<LogisticsInstructionService>());
        services.AddScoped<IApprovedDispatchInvoiceSourceV1>(serviceProvider =>
            serviceProvider.GetRequiredService<LogisticsInstructionService>());
        // AT-024: Logistics answers the Sales-declared delivery read over its own company-filtered lines. Registered here,
        // in the owner's module (no host file), so that Sales reads recorded deliveries only through this port.
        services.AddScoped<ISalesOrderDeliveryReadPortV1, LogisticsSalesOrderDeliveryReadPort>();
        // DEC-SAL-005: Logistics answers the Sales-declared loading read (every instruction line of an order, instructed or
        // loaded included) for the cancellation preview; the owner's module registers it, as the delivery read above.
        services.AddScoped<ISalesOrderLoadingReadPortV1, LogisticsSalesOrderLoadingReadPort>();
        // AT-025: the operations work list — two separate sections over the Sales and Purchasing owners' read ports (registered by
        // those modules) and Logistics's own recorded totals; each section measures its own name.
        services.AddScoped<ILogisticsOrderLineProgressReader, LogisticsOrderLineProgressReader>();
        services.TryAddSingleton(new LogisticsOperationsWorkBindings());
        services.AddScoped<ILogisticsOperationsWorkService, LogisticsOperationsWorkService>();
        services.AddScoped<LogisticsCustomerReturnStore>();
        services.AddScoped<ILogisticsCustomerReturnStoreV1>(provider =>
            provider.GetRequiredService<LogisticsCustomerReturnStore>());
        services.AddScoped<ILogisticsCustomerReturnSourceReadV1, LogisticsCustomerReturnSourceReader>();
        services.AddScoped<ISalesOrderCustomerReturnReadPortV1, LogisticsSalesOrderCustomerReturnReadPort>();
        services.TryAddSingleton(new LogisticsCustomerReturnBindingsV1(
            LogisticsPermissionManifestV1.CustomerReturnsRequest,
            LogisticsPermissionManifestV1.CustomerReturnsDecision,
            LogisticsPermissionManifestV1.CustomerReturnsRead));
        services.AddScoped<ILogisticsCustomerReturnServiceV1, LogisticsCustomerReturnService>();
        return services;
    }

    // A named factory so that no registration statement of this module names the company context (architecture guard).
    private static LogisticsInstructionService CreateInstructionService(IServiceProvider serviceProvider) => new LogisticsInstructionService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetRequiredService<ILogisticsPartyAccountPortV1>(),
        serviceProvider.GetRequiredService<ISalesOrderLineReferencePortV1>(),
        serviceProvider.GetRequiredService<LoadingUnitVerifier>(),
        serviceProvider.GetRequiredService<ILogisticsInstructionStore>(),
        serviceProvider.GetRequiredService<LogisticsInstructionServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetService<ILogisticsRealizationTransactionRunnerV1>());

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
