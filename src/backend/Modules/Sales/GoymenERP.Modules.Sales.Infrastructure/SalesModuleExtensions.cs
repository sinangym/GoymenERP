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
        // API-20 / API-21: the sales request store is the same Sales store (one context, one attempt table).
        services.AddScoped<ISalesRequestStore>(serviceProvider => serviceProvider.GetRequiredService<SalesStore>());
        // API-22…24: the sales quote store is the same Sales store (the conversion writes the order in the quote's one save).
        services.AddScoped<ISalesQuoteStore>(serviceProvider => serviceProvider.GetRequiredService<SalesStore>());
        // API-41 / API-42: the read-only report queries are the same Sales store (one company-filtered context).
        services.AddScoped<ISalesReportReadStore>(serviceProvider => serviceProvider.GetRequiredService<SalesStore>());
        // The draft use cases (Sales API slice). The host supplies the Cari account port, the Settings
        // provider, the optional Product eligibility port and the central authority; the shell
        // capability provider is registered by the host once the Sales names are in the catalogue.
        services.TryAddSingleton(new SalesDraftServiceBindings());
        services.AddScoped(CreateDraftService);
        services.AddScoped<ISalesDraftService>(serviceProvider => serviceProvider.GetRequiredService<SalesDraftService>());
        // API-20 / API-21: the request use cases share the draft service's fence and owner reads (one scoped instance).
        services.AddScoped<ISalesRequestService>(serviceProvider => serviceProvider.GetRequiredService<SalesDraftService>());
        services.AddScoped<ISalesQuoteService>(serviceProvider => serviceProvider.GetRequiredService<SalesDraftService>());
        // API-41 / API-42: the Sales reports share the draft service's fence, measurement basis and price-read decision.
        services.AddScoped<ISalesReportService>(serviceProvider => serviceProvider.GetRequiredService<SalesDraftService>());
        // MOD-07: the sales order PDF's price facts share the draft service's fence and its price-read decision.
        services.AddScoped<ISalesOrderPrintReadServiceV1>(serviceProvider => serviceProvider.GetRequiredService<SalesDraftService>());
        services.AddScoped<ISalesOrderDraftPendingApprovalReadServiceV1,
            SalesOrderDraftPendingApprovalReadServiceV1>();
        // SALES-A1: the Sales-owned line reference port (Business 5737989917 = B) for the allocation
        // and loading consumers, over the same company-filtered store. Registered here, in the module
        // (no host file), so that a consumer module reads Sales only through this port.
        services.AddScoped<ISalesOrderLineReferencePortV1, SalesOrderLineReferencePort>();
        // API-30 / API-31: the read-only dispatch list and invoice candidates over the Logistics dispatch owner's ports
        // (registered by that module), the Sales store's own order references and the Cari party display (host adapter).
        services.TryAddSingleton(new SalesDispatchReadBindings());
        services.AddScoped<ISalesOrderDispatchReferenceReader, SalesOrderDispatchReferenceReader>();
        services.AddScoped<ISalesDispatchReadService>(CreateDispatchReadService);
        return services;
    }

    // Named for the same architecture guard as the draft service factory below: the company context is resolved, never registered.
    // A missing owner port (Logistics or Cari not composed) stays null and the read answers 503, never an empty list.
    private static SalesDispatchReadService CreateDispatchReadService(IServiceProvider serviceProvider) => new SalesDispatchReadService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<SalesDispatchReadBindings>(),
        serviceProvider.GetRequiredService<ISalesOrderDispatchReferenceReader>(),
        serviceProvider.GetService<ISalesPartyDisplaySourceV1>(),
        serviceProvider.GetService<ISalesDispatchNoteReadPortV1>(),
        serviceProvider.GetService<ISalesInvoiceableShipmentReadPortV1>());

    // Built in a named factory so that no registration statement of this module names the company
    // context: the host's scoped ICompanyContext is resolved here, never registered (architecture guard).
    private static SalesDraftService CreateDraftService(IServiceProvider serviceProvider) => new SalesDraftService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<IActiveRoleCodeReader>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetRequiredService<ISalesPartyAccountPortV1>(),
        serviceProvider.GetRequiredService<ISettingsPrerequisiteProvider>(),
        serviceProvider.GetService<IProductSalesEligibilityV1>(),
        serviceProvider.GetRequiredService<ISalesOrderDraftStore>(),
        serviceProvider.GetRequiredService<SalesDraftServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        // SALES-A2a: the Cari contact-information port is the host's registration (central); until it is
        // registered a draft naming a delivery address or contact is 503, never stored unvalidated.
        serviceProvider.GetService<ISalesPartyContactInformationPortV1>(),
        // AT-024: the delivery owner's read port, registered by that owner's module (Logistics); until it is, the
        // delivered / remaining read is 503, never an assumed zero.
        serviceProvider.GetService<ISalesOrderDeliveryReadPortV1>(),
        // DEC-SAL-005: the impact owners' read ports, registered by those owners' modules (Inventory: active allocations;
        // Logistics: loading-instruction lines); until one is, its category of the cancellation preview is unreadable and a
        // cancellation is refused, never decided on an assumed zero.
        serviceProvider.GetService<ISalesOrderAllocationReadPortV1>(),
        serviceProvider.GetService<ISalesOrderLoadingReadPortV1>(),
        // AT-018 / AT-021 / MOD-02: the Product price tier chain (SAT-D9), registered by the Product owner's
        // AddProductPriceLists; until it is, a list-price reference is 503, never an assumed "no price".
        serviceProvider.GetService<IProductQuantityPriceReferenceReaderV2>(),
        // SAT-D8 / API-10: the customer-return owner's read port, registered by that owner's module (Logistics); until it
        // is, the order's approved-returns read is 503, never an assumed "nothing returned".
        serviceProvider.GetService<ISalesOrderCustomerReturnReadPortV1>(),
        // API-20 / API-21: the sales request store (this module's own).
        serviceProvider.GetRequiredService<ISalesRequestStore>(),
        // API-22…24: the sales quote store (this module's own).
        serviceProvider.GetRequiredService<ISalesQuoteStore>(),
        // DEC-SAL-001 / API-08 (SAT-D5): the Settings Sales policy port, registered by the host from the Settings owner;
        // until it is, the realization axis is "PolicyUnavailable", never a default tolerance. SAT-D2 / API-12 reads the
        // discount limits through the same port.
        serviceProvider.GetService<ISettingsSalesPolicyProviderV1>(),
        // SAT-D2 / SAT-D34 / SAT-D63: the actor's discount limit groups (the person → group bindings, the Settings owner's
        // record, composed by the host). Without groups no limit is defined: every line below its list price goes to approval;
        // with groups but no Settings source, such a line is 503, never checked against a guessed limit.
        serviceProvider.GetService<ISalesDiscountLimitGroupSourceV1>(),
        // MOD-01 / API-06: the customer-finance owner's risk-input port, registered by that owner's module (Finance); until it
        // is, open receivables, the credit limit and collateral are "Unavailable" and the risk check is never "Within".
        serviceProvider.GetService<ISalesPartyRiskReadPortV1>(),
        // SAT-D25 / SAT-D39: the Settings quote policy port (loss reason list, default validity), registered by the host from the
        // Settings owner; until it is, a loss is 503 (never a guessed reason) and no validity is suggested (503 on the read).
        serviceProvider.GetService<ISettingsSalesQuotePolicyProviderV1>(),
        // API-41 / API-42: the report queries (this module's own store).
        serviceProvider.GetRequiredService<ISalesReportReadStore>(),
        // API-41 / API-42: the Cari party display (host adapter, registered centrally); until it is, a report naming a customer is
        // 503, never a guessed name.
        serviceProvider.GetService<ISalesPartyDisplaySourceV1>());

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
