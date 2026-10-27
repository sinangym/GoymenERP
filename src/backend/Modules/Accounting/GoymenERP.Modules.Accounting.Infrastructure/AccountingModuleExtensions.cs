using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Accounting.Infrastructure;

/// <summary>
/// Accounting's module registration. The API host does not call it yet: host wiring, permissions
/// and HTTP are a later slice, and the central writer's reviewed-inventory registration of this
/// DbContext and initializer comes with it. The audit interceptor is the host's to add through
/// <paramref name="configureDbContext"/>, as for every module.
/// </summary>
public static class AccountingModuleExtensions
{
    public static IServiceCollection AddAccountingModule(
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

        services.AddDbContext<AccountingDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        // Ö2 (ACC-V0): journal amounts are checked against the Settings owner's money profile; the host
        // registers ISettingsPrerequisiteProvider (the Settings module), never a default here.
        services.AddScoped<AccountingAmountScaleVerifier>();
        services.AddScoped<AccountingStore>();
        services.AddScoped<AccountingTrialBalanceReader>();
        services.AddScoped<IAccountingTrialBalanceReadStore>(serviceProvider =>
            serviceProvider.GetRequiredService<AccountingTrialBalanceReader>());
        services.AddScoped<AccountingTrialBalanceServiceV1>();
        services.AddScoped<IAccountingPeriodStore>(serviceProvider => serviceProvider.GetRequiredService<AccountingStore>());
        services.AddScoped<IAccountingPostingStore>(serviceProvider => serviceProvider.GetRequiredService<AccountingStore>());
        services.AddScoped<IAccountingCommandLog>(serviceProvider => serviceProvider.GetRequiredService<AccountingStore>());
        services.AddScoped<IAccountingNormalJournalDraftStore>(serviceProvider => serviceProvider.GetRequiredService<AccountingStore>());
        services.AddScoped<IAccountingJournalReversalProposalStoreV1>(serviceProvider =>
            serviceProvider.GetRequiredService<AccountingStore>());
        services.TryAddSingleton<IApprovedAccountingSourcePackageParserV1,
            ApprovedAccountingSourcePackageParserV1>();
        services.AddScoped<ApprovedAccountingSourceCatalogStoreV1>();
        services.AddScoped<IApprovedAccountingSourceCatalogStoreV1>(serviceProvider =>
            serviceProvider.GetRequiredService<ApprovedAccountingSourceCatalogStoreV1>());
        services.TryAddSingleton<NormalJournalPermissionBindingV1>();
        services.AddScoped<IApprovedNormalJournalSourceBindingCatalogV1>(serviceProvider =>
            serviceProvider.GetRequiredService<ApprovedAccountingSourceCatalogStoreV1>());
        services.TryAddScoped<INormalJournalDraftServerSourceProviderV1,
            FinanceManualReceiptNormalJournalSourceProviderV1>();
        services.AddScoped<NormalJournalDraftApprovalPolicyV1>();
        services.AddScoped<NormalJournalDraftServiceV1>();
        services.AddScoped<INormalJournalDraftLifecycleV1>(serviceProvider =>
            serviceProvider.GetRequiredService<NormalJournalDraftServiceV1>());
        services.AddScoped<NormalJournalDraftPublicServiceV1>();
        services.AddScoped<IAccountPlanMirrorReaderV1, ApprovedAccountPlanMirrorReaderV1>();
        services.AddScoped<NormalJournalDraftAccountReadServiceV1>();
        services.AddScoped<NormalJournalDraftSourceReadServiceV1>();
        services.AddScoped<AccountingJournalReversalServiceV1>();
        services.AddScoped<IAccountingJournalReversalLifecycleV1>(serviceProvider =>
            serviceProvider.GetRequiredService<AccountingJournalReversalServiceV1>());
        services.AddScoped<AccountingJournalReversalPublicServiceV1>();
        services.AddScoped<ApprovedAccountingSourceCatalogServiceV1>();
        services.AddScoped<AccountingApiService>();
        return services;
    }

    /// <summary>Applies Accounting's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializeAccountingModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

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
