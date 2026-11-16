using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.ExchangeRates;
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
        // F-029 S4b (SAT-D5/SAT-D6): the persistent delivery tolerance and approval amount threshold administration; the
        // policy port above reads the table once a family's compiled baseline is transferred (the catalog until then).
        services.AddScoped<ISettingsSalesPolicyAdministrationV1, SettingsSalesPolicyAdministration>();
        // F-029 S5/S6 (SAT-D25/D39, SAT-D102/D120): the persistent quote policy and e-Fatura scenario administration on the
        // same administration class, and the two read ports over the table; each port keeps answering from the reviewed
        // catalog until its family's compiled baseline is transferred (byte-identical answers, spec §4).
        services.AddScoped<ISettingsSalesQuotePolicyAdministrationV1, SettingsSalesPolicyAdministration>();
        services.AddScoped<ISettingsEInvoiceScenarioPolicyAdministrationV1, SettingsSalesPolicyAdministration>();
        services.Replace(ServiceDescriptor.Scoped<ISettingsSalesQuotePolicyProviderV1, SettingsSalesQuotePolicyProviderV1>());
        services.Replace(ServiceDescriptor.Scoped<ISettingsEInvoiceScenarioPolicyProviderV1,
            SettingsEInvoiceScenarioPolicyProviderV1>());
        // MOD-03 tax master (SAT-D83/D133/D135/D139/D140): the administration and the line tax port Sales consumes. The product's
        // item type comes from the host's Product adapter (ISettingsProductItemTypeSourceV1); until it is composed the
        // product-based read answers 503.
        services.AddScoped<ISettingsTaxMasterAdministrationV1, SettingsTaxMasterAdministration>();
        services.AddScoped<ISettingsLineTaxResolverV1, SettingsTaxMasterReader>();
        // SAT-D181 (EK26): the VAT rate / withholding ratio lists a Sales line's reasoned tax change picks from (read-only; empty
        // until entered, SAT-D148; maintenance is the SAT-D146 slice).
        services.AddScoped<ISettingsTaxRateListReaderV1, SettingsTaxRateListReader>();
        // F-004 (BS-9/BS-13/BS-16/BS-17, URN-D7…D9): the unit conversion policy record, its administration and the read port
        // Product / stock-logistics consume. No compiled value: the port refuses until a person records the first version.
        services.AddScoped<ISettingsUnitConversionPolicyAdministrationV1, SettingsUnitConversionPolicyAdministration>();
        services.AddScoped<ISettingsUnitConversionPolicyProviderV1, SettingsUnitConversionPolicyProviderV1>();
        // F-029 S1/S2 (URN-D2…D5): the persistent unit profile and its administration. The prerequisite port keeps its
        // signature (spec P2) and answers from the reviewed catalog, byte-identical, until the company's one-time baseline
        // transfer; from then on the Settings table is its only unit profile source. Money and period reads are unchanged.
        services.Replace(ServiceDescriptor.Scoped<ISettingsPrerequisiteProvider, SettingsUnitProfilePrerequisiteProvider>());
        services.AddScoped<ISettingsUnitProfileAdministrationV1, SettingsUnitProfileAdministration>();
        // F-024 (NS-01): the segregation of duties setting (may the preparer also approve?), its administration and the read
        // port approving modules consume. No row answers the decided default: the preparer may not approve.
        services.AddScoped<ISettingsSegregationOfDutiesPolicyAdministrationV1, SettingsSegregationOfDutiesPolicyAdministration>();
        services.AddScoped<ISettingsSegregationOfDutiesPolicyProviderV1, SettingsSegregationOfDutiesPolicyProviderV1>();
        // KIM-D34: the group-wide login attempt limit record, its administration and the read port the host's login rate
        // limiter refreshes from. Nothing is seeded: until the first version the host keeps its configured starting value.
        services.AddScoped<ISettingsLoginRateLimitAdministrationV1, SettingsLoginRateLimitAdministration>();
        services.AddScoped<ISettingsLoginRateLimitProviderV1, SettingsLoginRateLimitProviderV1>();
        // URT-D89 (URT-D87 the unit, URT-D91/URT-D92 the buffer): the per-work-center capacity record, its administration and
        // the read port the Manufacturing due-date suggestion consumes. Nothing is seeded: no version is no capacity. The work
        // center reference port is composed by the API host over the Organization owner's read (none composed: fail closed).
        services.AddScoped<ISettingsWorkCenterCapacityAdministrationV1, SettingsWorkCenterCapacityAdministration>();
        services.AddScoped<ISettingsWorkCenterCapacityProviderV1, SettingsWorkCenterCapacityProviderV1>();
        // API-98/B-14 (KIM-D49, KIM-D51): the company's reference lists (expense/service types, paint colors, scrap kinds),
        // their administration and the read port the purchasing, stock/logistics, production and finance modules consume.
        // Nothing is seeded: every list starts empty; the operation type list is not opened.
        services.AddScoped<ISettingsReferenceListAdministrationV1, SettingsReferenceListAdministration>();
        services.AddScoped<ISettingsReferenceListReaderV1, SettingsReferenceListReaderV1>();
        // B-13 (ADR-SET-002): the company policy registry. One provider answers every read port of the registry; the number
        // series port (ISettingsNumberSeriesProviderV1) stays unregistered until its policy is decided (NEEDS-SINAN N-12).
        services.AddScoped<ISettingsCompanyPolicyAdministrationV1, SettingsCompanyPolicyAdministration>();
        services.AddScoped<SettingsCompanyPolicyProviderV1>();
        services.AddScoped<ISettingsFinancePolicyProviderV1>(provider =>
            provider.GetRequiredService<SettingsCompanyPolicyProviderV1>());
        services.AddScoped<ISettingsManufacturingDueDateBufferProviderV1>(provider =>
            provider.GetRequiredService<SettingsCompanyPolicyProviderV1>());
        services.AddScoped<ISettingsSubcontractTaxDefaultProviderV1>(provider =>
            provider.GetRequiredService<SettingsCompanyPolicyProviderV1>());
        services.AddScoped<ISettingsFinanceCreditAccountMappingProviderV1>(provider =>
            provider.GetRequiredService<SettingsCompanyPolicyProviderV1>());
        services.AddScoped<ISettingsBankCsvMappingAdministrationV1, SettingsBankCsvMappingAdministration>();
        services.AddScoped<ISettingsBankCsvMappingProviderV1, SettingsBankCsvMappingProviderV1>();
        // DEC-FIN-010 / SAT-D54/D55/D169…D172: the exchange-rate record, its administration and the rate-day read port. Both
        // official sources stay disabled unless configuration enables them explicitly (an https base address is required);
        // enabling the outbound connection in an environment is a human gate.
        services.AddScoped<ISettingsExchangeRateAdministrationV1, SettingsExchangeRateAdministration>();
        services.AddScoped<ISettingsExchangeRateResolverV1, SettingsExchangeRateResolverV1>();
        var exchangeRateSources = ExchangeRateSourceOptions.Read(configuration);
        if (exchangeRateSources.TcmbEnabled)
        {
            services.TryAddSingleton<IExchangeRateBulletinSourceV1>(_ => new TcmbExchangeRateBulletinSource(new HttpClient
            {
                BaseAddress = exchangeRateSources.TcmbBaseAddress, Timeout = exchangeRateSources.TcmbTimeout,
            }));
        }

        services.TryAddSingleton<IExchangeRateBulletinSourceV1, DisabledExchangeRateBulletinSource>();
        services.TryAddSingleton<IExchangeRateHalfDaySourceV1, DisabledExchangeRateHalfDaySource>();
        // Üretim U1-a (API-01/02, MOD-01; URT-D6/D7/D9/D10): the Manufacturing parameter port over published Settings
        // versions and the reviewed version-0 catalog, and the parameter / tolerance profile lifecycle administration.
        services.AddScoped<ISettingsManufacturingParameterProviderV1, SettingsManufacturingParameterProviderV1>();
        services.AddScoped<SettingsManufacturingProfileAdministration>();
        services.AddScoped<ISettingsManufacturingParameterProfileAdministrationV1>(provider =>
            provider.GetRequiredService<SettingsManufacturingProfileAdministration>());
        services.AddScoped<ISettingsManufacturingToleranceProfileAdministrationV1>(provider =>
            provider.GetRequiredService<SettingsManufacturingProfileAdministration>());
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
