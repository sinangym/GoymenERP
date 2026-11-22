using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.SqlServer;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
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
        IConfiguration configuration,
        bool enableAuthorizationSnapshotDatabaseReadiness = true,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
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
        // ID-3: Identity owns its state; canonical audit writes use the Audit owner's
        // transaction port. This context never maps or writes another module's Audit table.
        services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity"));
            configureDbContext?.Invoke(serviceProvider, options);
        });
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
        // AT-004 read-only access view.
        services.AddScoped<IIdentityAccessReadService, IdentityAccessReadService>();
        // ID-3 role assignment, revocation and the Critical role request (KIM-D1/D5/D15/D17). It grants nothing by
        // itself: who holds identity.roles.manage and identity.access.approve stays a human gate.
        services.AddScoped<IdentityAccessAssignmentService>();
        services.AddScoped<IIdentityAccessAssignmentService>(sp => sp.GetRequiredService<IdentityAccessAssignmentService>());
        services.AddScoped<IIdentityUserManagementServiceV1>(sp => sp.GetRequiredService<IdentityAccessAssignmentService>());
        services.AddScoped<ICompanySetupGroupAuthorityV1, SqlCompanySetupGroupAuthorityV1>();
        services.AddScoped<IdentityCompanySetupService>();
        services.AddScoped<IIdentityCompanySetupServiceV1>(sp => sp.GetRequiredService<IdentityCompanySetupService>());
        services.AddScoped<IIdentityFirstAdminCandidatesV1>(sp => sp.GetRequiredService<IdentityCompanySetupService>());
        // MOD-08 user directory: a company-fenced, read-only display-name port for another module's central adapter.
        services.AddScoped<IUserDirectoryReadPortV1, UserDirectoryReadPortV1>();
        // SAT-D66: the same directory answers in which companies a person is an active member (multi-company binding).
        services.AddScoped<IUserCompanyMembershipReadPortV1, UserDirectoryReadPortV1>();
        services.AddScoped<ICompanySingleActiveMemberReadPortV1, UserDirectoryReadPortV1>();
        services.AddScoped<IUserPermissionHolderReadPortV1, UserDirectoryReadPortV1>();
        // WP2: the holders of one permission in the resolved company, for Work's approval follow-up cycle (system work).
        services.AddScoped<IPermissionHolderListPortV1, PermissionHolderListPortV1>();
        // ID-1 local account login. The settings are validated (fail-closed) by the host only when the
        // LocalAccount authentication mode is on; the password hasher is supplied by the host.
        services.AddOptions<LocalAccountOptions>()
            .Bind(configuration.GetSection(LocalAccountOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ILocalCredentialStore, SqlLocalCredentialStore>();
        services.AddScoped<ILocalAccountLoginService, LocalAccountLoginService>();
        services.AddScoped<ILocalTwoFactorLoginV1, SqlLocalTwoFactorLoginV1>();
        services.AddScoped<ILocalTwoFactorServiceV1, LocalTwoFactorServiceV1>();
        services.AddScoped<ILocalTwoFactorOperatorRecoveryServiceV1, LocalTwoFactorOperatorRecoveryServiceV1>();
        services.AddScoped<ILocalAccountSessionValidator, LocalAccountSessionValidator>();
        // KIM-D30: logout renews the security stamp; every open session then fails the per-request check.
        services.AddScoped<ILocalAccountLogoutService, LocalAccountLogoutService>();
        // ID-2: the session's own password change (KIM-D12 policy) and its idempotency record.
        services.AddScoped<ILocalCredentialCommandStore, SqlLocalCredentialCommandStore>();
        services.AddScoped<ILocalPasswordContextTermSource, SqlLocalPasswordContextTermSource>();
        services.AddScoped<ILocalAccountPasswordChangeService, LocalAccountPasswordChangeService>();
        // ID-2 one-shot founder bootstrap (KIM-D5/KIM-D18); the Admin role code has no default.
        services.AddOptions<IdentityBootstrapOptions>()
            .Bind(configuration.GetSection(IdentityBootstrapOptions.SectionName));
        services.AddScoped<IFounderBootstrapService, FounderBootstrapService>();
        // ID-2r founder recovery (KIM-D22/D23/D43): KIM-D16 evaluator, company lock seam and the CLI service.
        services.AddOptions<FounderRecoveryOptions>()
            .Bind(configuration.GetSection(FounderRecoveryOptions.SectionName));
        services.AddScoped<IdentityUserManagementEvaluator>();
        services.AddScoped<IFounderRecoveryCompanyLock, SqlFounderRecoveryCompanyLock>();
        services.AddScoped<IFounderRecoveryAuditParticipant, FounderRecoveryAuditParticipant>();
        services.AddScoped<IFounderRecoveryService, FounderRecoveryService>();
        services.AddScoped<ISystemRolePermissionReconciler, SystemRolePermissionReconciler>();
        services.AddScoped<IdentityDataSeeder>();
        services.AddSingleton(rcsiBinding);
        services.AddSingleton<SqlServerRcsiReadinessProbe>();
        services.AddSingleton<IIdentityAuthorizationDatabaseReadiness,
            IdentityAuthorizationDatabaseReadiness>();
        if (!OpenApiDocumentGenerationMode.IsActive)
        {
            if (enableAuthorizationSnapshotDatabaseReadiness)
            {
                services.AddHostedService<AuthorizationSnapshotDatabaseReadinessService>();
            }
        }

        return services;
    }

    /// <summary>Host composition of the Organization lifecycle boundary; one wrapper for all three authority ports.</summary>
    public static IServiceCollection AddCompanyLifecycleAuthorizationGate(this IServiceCollection services)
    {
        services.AddScoped<CompanyLifecycleAuthorizationGate>();
        services.AddScoped<IErpAuthorizationService>(sp => sp.GetRequiredService<CompanyLifecycleAuthorizationGate>());
        services.AddScoped<IErpAuthorityRevalidationService>(sp => sp.GetRequiredService<CompanyLifecycleAuthorizationGate>());
        services.AddScoped<IActiveRoleCodeReader>(sp => sp.GetRequiredService<CompanyLifecycleAuthorizationGate>());
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

public interface IIdentityAuthorizationDatabaseReadiness
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}

internal sealed class IdentityAuthorizationDatabaseReadiness(
    IServiceScopeFactory scopeFactory,
    SqlServerRcsiReadinessProbe readinessProbe,
    SqlServerRcsiReadinessBinding readinessBinding)
    : IIdentityAuthorizationDatabaseReadiness
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
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
}

internal sealed class AuthorizationSnapshotDatabaseReadinessService(
    IIdentityAuthorizationDatabaseReadiness readiness)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        readiness.EnsureReadyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
