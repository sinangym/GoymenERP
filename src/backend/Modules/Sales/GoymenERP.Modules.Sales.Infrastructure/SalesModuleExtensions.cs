using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Sales.Infrastructure;

/// <summary>
/// Sales's module registration: the DbContext, the store and the draft service of the API slice.
/// The host calls it (the central writer's step) with the audit interceptor through
/// <paramref name="configureDbContext"/>, as for every module, and supplies the Cari account port,
/// the Settings provider, the optional Product eligibility port and the central authority. The
/// shell capability provider is registered by the host once the Sales names are in the catalogue.
/// </summary>
public static class SalesModuleExtensions
{
    public static IServiceCollection AddSalesModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<SalesDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SalesStore>();
        services.AddScoped<ISalesOrderDraftStore>(serviceProvider => serviceProvider.GetRequiredService<SalesStore>());
        // The draft use cases (Sales API slice). The host supplies the Cari account port, the Settings
        // provider, the optional Product eligibility port and the central authority; the shell
        // capability provider is registered by the host once the Sales names are in the catalogue.
        services.TryAddSingleton(new SalesDraftServiceBindings());
        services.AddScoped<ISalesDraftService>(serviceProvider => new SalesDraftService(
            serviceProvider.GetRequiredService<ICompanyContext>(),
            serviceProvider.GetRequiredService<ICurrentUser>(),
            serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
            serviceProvider.GetRequiredService<ICorrelationContext>(),
            serviceProvider.GetRequiredService<ISalesPartyAccountPortV1>(),
            serviceProvider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            serviceProvider.GetService<IProductSalesEligibilityV1>(),
            serviceProvider.GetRequiredService<ISalesOrderDraftStore>(),
            serviceProvider.GetRequiredService<SalesDraftServiceBindings>(),
            serviceProvider.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>Applies Sales's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeSalesModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

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
