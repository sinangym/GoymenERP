using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Finance's persistence and service registration (FIN-T04b). The host calls
/// <see cref="AddFinancePersistence"/> (with Audit's interceptor through its
/// <c>configureDbContext</c>), <see cref="AddFinanceServices"/> and
/// <see cref="InitializeFinancePersistenceAsync"/>; that wiring is the central writer's.
/// </summary>
public static class FinanceModuleExtensions
{
    public static IServiceCollection AddFinancePersistence(
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

        services.AddDbContext<FinanceDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// FIN-T04b: the real Finance services behind the HTTP surface, replacing the host's unavailable
    /// permission port and bank reader. Every piece is per request (scoped):
    /// <list type="bullet">
    /// <item><see cref="FinancePermissionPort"/>: fresh #276 measurements of the Finance names;</item>
    /// <item><see cref="FinanceBankAccountService"/>: the persisted bank-account reader (FS0); its
    /// master writes are not exposed in this slice;</item>
    /// <item><see cref="ReceiptDraftService"/>: the draft service, bound to the Finance money operation
    /// key, audited with the roles that granted the save.</item>
    /// </list>
    /// Needs <see cref="AddFinancePersistence"/>, Audit's collector, the Settings provider and the
    /// Finance party-account port from the host. Without a Settings candidate reader the currency
    /// answer stays unavailable, never a default.
    /// </summary>
    public static IServiceCollection AddFinanceServices(this IServiceCollection services)
    {
        services.AddScoped<FinancePermissionPort>();
        services.AddScoped<IFinancePermissionPort>(provider => provider.GetRequiredService<FinancePermissionPort>());
        // The services are built by the factories below, so no registration statement of this module names
        // the company context: the host's scoped ICompanyContext is resolved, never registered, here.
        services.AddScoped(CreateBankAccountService);
        services.AddScoped<IFinanceBankAccountReader>(provider => provider.GetRequiredService<FinanceBankAccountService>());
        services.AddScoped<IManualReceiptService>(CreateReceiptDraftService);
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    private static FinanceBankAccountService CreateBankAccountService(IServiceProvider provider)
    {
        var companyContext = provider.GetRequiredService<ICompanyContext>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new FinanceBankAccountService(
            provider.GetRequiredService<FinanceDbContext>(),
            companyContext,
            currentUser,
            permissions,
            provider.GetService<ISettingsMoneyCandidateReader>()
                ?? new UnavailableSettingsPrerequisiteProvider(companyContext, currentUser),
            provider.GetRequiredService<IAuditEventCollector>(),
            // Registered as the reader only: the bank-account master's writes (propose, approve) get their
            // own per-command envelopes with their HTTP slice, not this one.
            Envelope(provider, ErpPermissions.FinanceBankAccountRead, permissions, FinanceAction.ReadBankAccounts),
            provider.GetRequiredService<TimeProvider>());
    }

    private static ReceiptDraftService CreateReceiptDraftService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        return new ReceiptDraftService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            permissions,
            provider.GetRequiredService<IFinanceBankAccountReader>(),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceReceiptDraftBinding(FinanceMoneyOperations.ManualBankReceiptV1),
            Envelope(provider, FinancePermissionNames.DraftSave, permissions, FinanceAction.SaveDraft),
            provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>The ADR-009 envelope of the request: the permission that authorizes the write and its roles.</summary>
    private static FinanceAuditEnvelope Envelope(IServiceProvider provider, string permissionCode,
        FinancePermissionPort permissions, FinanceAction action)
    {
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceAuditEnvelope(permissionCode, permissions.RoleCodesOf(action), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent);
    }

    /// <summary>Applies Finance's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeFinancePersistenceAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

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
