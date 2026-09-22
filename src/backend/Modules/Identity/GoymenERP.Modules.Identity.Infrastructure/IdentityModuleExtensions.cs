using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.SqlServer;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Modules.Identity.Infrastructure;

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(
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

        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        var rcsiBinding = SqlServerRcsiReadinessBinding.CreateFromLogicalEndpoints(
            "identity-authorization-snapshot-startup",
            connectionStringBuilder.InitialCatalog,
            new[]
            {
                connectionStringBuilder.DataSource,
                connectionStringBuilder.FailoverPartner,
            }.Where(logicalEndpoint => !string.IsNullOrWhiteSpace(logicalEndpoint)));

        services.Configure<IdentitySeedOptions>(
            configuration.GetSection(IdentitySeedOptions.SectionName));
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
        services.AddScoped<IExternalIdentityResolver, ExternalIdentityResolver>();
        // One instance per scope behind all three ports: a revalidation has to refresh the very
        // cache the cached path reads, while active-role reads deliberately bypass permission
        // membership and the cache.
        services.AddScoped<ErpAuthorizationService>();
        services.AddScoped<IErpAuthorizationService>(
            provider => provider.GetRequiredService<ErpAuthorizationService>());
        services.AddScoped<IErpAuthorityRevalidationService>(
            provider => provider.GetRequiredService<ErpAuthorizationService>());
        services.AddScoped<IActiveRoleCodeReader>(
            provider => provider.GetRequiredService<ErpAuthorizationService>());
        services.TryAddSingleton<IAuthorizationSnapshotReadObserver,
            NullAuthorizationSnapshotReadObserver>();
        services.TryAddSingleton<IPermissionScopeReadObserver,
            NullPermissionScopeReadObserver>();
        services.AddScoped<IIdentityProfileService, IdentityProfileService>();
        services.AddScoped<ISystemRolePermissionReconciler, SystemRolePermissionReconciler>();
        services.AddScoped<IdentityDataSeeder>();
        services.AddSingleton(rcsiBinding);
        services.AddSingleton<SqlServerRcsiReadinessProbe>();
        if (!OpenApiDocumentGenerationMode.IsActive)
        {
            services.AddHostedService<AuthorizationSnapshotDatabaseReadinessService>();
        }

        return services;
    }

    public static async Task InitializeIdentityModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        var seeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}

internal sealed class AuthorizationSnapshotDatabaseReadinessService(
    IServiceScopeFactory scopeFactory,
    SqlServerRcsiReadinessProbe readinessProbe,
    SqlServerRcsiReadinessBinding readinessBinding)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        var evidence = await readinessProbe.ProbeAsync(
            dbContext.Database.GetDbConnection(),
            readinessBinding,
            cancellationToken);
        if (!evidence.IsReady)
        {
            throw new InvalidOperationException(
                SqlServerRcsiReadinessPolicy.GetStartupFailureMessage(evidence));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
