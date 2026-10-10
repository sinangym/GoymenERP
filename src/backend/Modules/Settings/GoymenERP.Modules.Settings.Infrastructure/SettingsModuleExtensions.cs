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
