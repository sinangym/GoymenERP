using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Seeding;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Organization.Infrastructure;

public static class OrganizationModuleExtensions
{
    public static IServiceCollection AddOrganizationModule(
        this IServiceCollection services,
        IConfiguration configuration,
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

        services.Configure<OrganizationSeedOptions>(
            configuration.GetSection(OrganizationSeedOptions.SectionName));
        services.AddDbContext<OrganizationDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization"));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.AddScoped<OrganizationBootstrapWriteScope>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OrganizationCompanyLifecycleReader>();
        services.AddScoped<ICompanyLifecycleReadPortV1>(sp => sp.GetRequiredService<OrganizationCompanyLifecycleReader>());
        services.AddScoped<IOutboxCompanyDirectoryV1>(sp => sp.GetRequiredService<OrganizationCompanyLifecycleReader>());
        services.AddScoped<OrganizationCompanySetupInboxConsumer>();
        services.AddScoped<IOrganizationCompanyOpeningV1, OrganizationCompanyOpeningV1>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationMasterDataService, OrganizationMasterDataService>();
        // B-15 calendar master: the same command protocol and helpers as the master edit (one partial class).
        services.AddScoped<IOrganizationWorkCenterCalendarService, OrganizationMasterDataService>();
        services.AddScoped<IOrganizationCompanyTaxIdService, OrganizationMasterDataService>();
        services.TryAddScoped<IOrganizationLocationOperatorEligibilityPortV1, UnavailableOrganizationLocationOperatorEligibilityPortV1>();
        services.AddScoped<IOrganizationTransferDestinationSourceV1, OrganizationTransferDestinationSourceV1>();
        // The module read for other modules' commands: company-scoped, no user permission gate.
        services.AddScoped<IOrganizationLocationStatusReaderV1, OrganizationLocationStatusReader>();
        services.AddScoped<IOrganizationLocationCandidatesV1, OrganizationLocationCandidateReader>();
        services.AddScoped<IOrganizationLocationDisplayReaderV1, OrganizationLocationDisplayReader>();
        // KIM-D35: the company's "VKN girilmemiş" mark for modules that issue a VKN-bearing official document.
        services.AddScoped<IOrganizationCompanyTaxIdStatusReaderV1, OrganizationCompanyTaxIdStatusReader>();
        services.AddOrganizationWorkCenterPlanningReferences();
        services.AddScoped<OrganizationDataSeeder>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();

        return services;
    }

    public static async Task InitializeOrganizationModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        var seeder = scope.ServiceProvider.GetRequiredService<OrganizationDataSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
