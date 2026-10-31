using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Settings.Infrastructure;

public static class SettingsModuleExtensions
{
    public static IServiceCollection AddSettingsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        var connection = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");

        services.AddDbContext<SettingsDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.Replace(ServiceDescriptor.Scoped<ISettingsManufacturingToleranceProviderV1,
            SettingsManufacturingToleranceProviderV1>());
        services.TryAddSingleton(TimeProvider.System);
        // SAT-D61…D64: the Sales policy port keeps the approved catalog for every read and adds the company's limit-group
        // store for the discount limit (SettingsSalesPolicyProviderV1 states the precedence; a contradiction fails closed).
        services.Replace(ServiceDescriptor.Scoped<ISettingsSalesPolicyProviderV1, SettingsSalesPolicyProviderV1>());
        // SAT-D63/D66: the actor's limit groups in the server company, read by the host's Sales adapter.
        services.AddScoped<ISettingsSalesDiscountLimitGroupMembershipReaderV1,
            SettingsSalesDiscountLimitGroupMembershipReaderV1>();
        // SAT-D65…D70: the limit administration. The person directory is the host's Identity adapter; until it is
        // composed the people search and the binding write answer 503.
        services.AddScoped<ISettingsSalesDiscountLimitAdministrationV1, SettingsSalesDiscountLimitAdministration>();
        // MOD-03 tax master (SAT-D83/D133/D135/D139/D140): the administration and the line tax port Sales consumes. The product's
        // item type comes from the host's Product adapter (ISettingsProductItemTypeSourceV1); until it is composed the
        // product-based read answers 503.
        services.AddScoped<ISettingsTaxMasterAdministrationV1, SettingsTaxMasterAdministration>();
        services.AddScoped<ISettingsLineTaxResolverV1, SettingsTaxMasterReader>();
        return services;
    }

    public static async Task InitializeSettingsModuleAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        if (database.Database.IsRelational()) await database.Database.MigrateAsync(cancellationToken);
        else await database.Database.EnsureCreatedAsync(cancellationToken);
    }
}
