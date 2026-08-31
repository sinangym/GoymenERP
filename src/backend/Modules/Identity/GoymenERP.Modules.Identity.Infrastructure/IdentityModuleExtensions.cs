using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using GoymenERP.Modules.Identity.Infrastructure.Services;
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

        services.Configure<IdentitySeedOptions>(
            configuration.GetSection(IdentitySeedOptions.SectionName));
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
        services.AddScoped<IExternalIdentityResolver, ExternalIdentityResolver>();
        services.AddScoped<IErpAuthorizationService, ErpAuthorizationService>();
        services.TryAddSingleton<IAuthorizationSnapshotReadObserver,
            NullAuthorizationSnapshotReadObserver>();
        services.AddScoped<IIdentityProfileService, IdentityProfileService>();
        services.AddScoped<ISystemRolePermissionReconciler, SystemRolePermissionReconciler>();
        services.AddScoped<IdentityDataSeeder>();
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
    IServiceScopeFactory scopeFactory)
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

        var isReadCommittedSnapshotOn = await dbContext.Database
            .SqlQueryRaw<int>(
                """
                SELECT CONVERT(int, [is_read_committed_snapshot_on]) AS [Value]
                FROM [sys].[databases]
                WHERE [database_id] = DB_ID()
                """)
            .SingleAsync(cancellationToken);
        if (isReadCommittedSnapshotOn != 1)
        {
            throw new InvalidOperationException(
                "Identity authorization snapshot requires SQL Server READ_COMMITTED_SNAPSHOT ON.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
