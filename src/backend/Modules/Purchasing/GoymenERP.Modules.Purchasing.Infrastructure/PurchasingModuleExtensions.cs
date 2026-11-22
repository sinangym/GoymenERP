using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Purchasing.Infrastructure;

/// <summary>
/// Purchasing's module registration (PUR-4a): the DbContext, the store, the unit verifier, the purchasing
/// service and the supply reference port. The host calls it (the central writer's step) with the audit
/// interceptor through <paramref name="configureDbContext"/>, as for every module, and supplies the Cari
/// supplier account port (<see cref="IPurchasingPartyAccountPortV1"/>), the Settings provider and the central
/// authority. The Product purchase-eligibility port (<see cref="IProductPurchaseEligibilityPortV1"/>) is
/// optional: until the Product owner publishes it the service answers 503 fail-closed. The shell capability
/// provider is registered by the host once the Purchasing names are in the catalogue.
/// </summary>
public static class PurchasingModuleExtensions
{
    public static IServiceCollection AddPurchasingModule(this IServiceCollection services, IConfiguration configuration,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureDbContext = null)
    {
        var provider = configuration["DB_PROVIDER"] ?? "SqlServer";
        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Desteklenmeyen veritabanı sağlayıcısı: {provider}");
        }

        var connectionString = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQL Server bağlantı dizesi DB_CONNECTION_STRING secret/env değeriyle tanımlanmalıdır.");
        }

        services.AddDbContext<PurchasingDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema));
            configureDbContext?.Invoke(serviceProvider, options);
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PurchasingStore>();
        services.AddScoped<PurchaseServiceOrderStoreV1>();
        services.AddScoped<IPurchaseServiceOrderStoreV1>(sp => sp.GetRequiredService<PurchaseServiceOrderStoreV1>());
        services.AddScoped<IPurchaseSubcontractOrderReadPortV1>(sp => sp.GetRequiredService<PurchaseServiceOrderStoreV1>());
        services.AddScoped<IPurchaseServiceAcceptanceReadPortV1>(sp => sp.GetRequiredService<PurchaseServiceOrderStoreV1>());
        services.AddScoped<IPurchaseServiceOrderServiceV1, PurchaseServiceOrderServiceV1>();
        services.AddScoped<IPurchaseAdditionalCostStoreV1, PurchaseAdditionalCostStoreV1>();
        services.AddScoped<IPurchaseAdditionalCostServiceV1, PurchaseAdditionalCostServiceV1>();
        // Direct ALC-S8 answer: the existing purchase-order reviser records expenses. Registration grants nothing.
        services.TryAddSingleton(new PurchaseAdditionalCostWriteAdmissionV1(PurchasingPermissionManifestV1.OrdersRevise,
            "Issue #1189 issuecomment-6946921285: alış siparişini revize etme yetkilisi"));
        services.AddScoped<IPurchasingStore>(serviceProvider => serviceProvider.GetRequiredService<PurchasingStore>());
        services.AddScoped<IPurchasingIncomingInvoiceStoreV1>(serviceProvider => serviceProvider.GetRequiredService<PurchasingStore>());
        services.AddScoped<IPurchaseIncomingInvoiceWorkflowV1, PurchaseIncomingInvoiceWorkflow>();
        // WP15 (1.16): read-only duplicate check; warns, never blocks (NEEDS-SINAN WP15-1).
        services.AddScoped<IPurchasingInvoiceDuplicateCandidateSourceV1>(serviceProvider => serviceProvider.GetRequiredService<PurchasingStore>());
        services.AddScoped<IPurchaseInvoiceDuplicateCheckV1, PurchaseInvoiceDuplicateCheck>();
        services.AddScoped<IPurchaseIncomingInvoiceMatchSourceV1, PurchaseIncomingInvoiceMatchSource>();
        services.AddScoped<IPurchaseIncomingInvoiceWorkspaceV1, PurchaseIncomingInvoiceWorkspace>();
        services.AddScoped<IPurchaseIncomingInvoiceDiagnosticsV1, PurchaseIncomingInvoiceDiagnostics>();
        services.AddScoped<IPurchaseInvoiceImportReservationStoreV1>(provider => provider.GetRequiredService<PurchasingStore>());
        services.AddScoped<IPurchaseInvoiceFileReaderV1, Imports.PurchaseInvoiceFileReader>();
        services.AddScoped<IPurchaseInvoiceImportV1, PurchaseInvoiceImport>();
        services.AddScoped<PurchaseInvoiceImportAdmission>();
        services.AddScoped<IPurchaseInvoiceImportAdmissionV1>(provider => provider.GetRequiredService<PurchaseInvoiceImportAdmission>());
        services.AddScoped<IPurchaseInvoiceImportMappingV1>(provider => provider.GetRequiredService<PurchaseInvoiceImportAdmission>());
        services.AddScoped<PurchaseUnitVerifier>();
        services.TryAddSingleton(new PurchasingServiceBindings());
        services.AddScoped<PurchasingService>(CreatePurchasingService);
        services.AddScoped<IPurchasingService>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchasingSupplierRevisionServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchasingOrderRecordReadSourceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchasingOrderReadServiceV1, PurchasingOrderReadServiceV1>();
        services.AddScoped<IPurchaseGoodsReceiptServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseGoodsReceiptQuantityComparisonServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        // ALC-S16 (F-027): the bulk goods receipt (KLN-D41/D43/D45).
        services.AddScoped<IPurchaseGoodsReceiptBatchStore, PurchaseGoodsReceiptBatchStore>();
        services.AddScoped<IPurchaseGoodsReceiptBatchServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseSupplierReturnStore, PurchaseSupplierReturnStore>();
        services.AddScoped<PurchaseSupplierReturnPurchaseSourceReader>();
        services.AddScoped<IPurchaseSupplierReturnServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseReceiptReversalServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseReceiptReversalBatchServiceV1>(provider => provider.GetRequiredService<PurchasingService>());
        services.AddScoped<IPurchaseAcceptanceSourceV1, PurchaseAcceptanceSource>();
        services.AddScoped<IPurchaseSupplyReferencePortV1, PurchaseSupplyReferencePort>();
        // AT-025: the purchase section of the operations work list reads the approved lines awaiting receipt through this port only.
        services.AddScoped<IPurchaseOpenLineReadPortV1, PurchaseOpenLineReadPort>();
        // A4 / MOD-03: the open purchase lines serving a Sales order (the host adapts it to Sales's linked-supply read contract).
        services.AddScoped<IPurchaseSalesLinkedSupplyReadPortV1, PurchaseSalesLinkedSupplyReadPort>();
        services.AddScoped<IPurchaseNotificationRecipientsV1, PurchaseNotificationRecipients>();
        // W1-A (AI-D31/AI-D32): the read-only Work inbox sources of Purchasing's pending approvals; they decide nothing.
        services.AddScoped<PurchasingWorkInboxFence>();
        services.AddScoped<IWorkInboxSourceV1, PurchaseOrderWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, PurchaseIncomingInvoiceVarianceWorkInboxSourceV1>();
        services.AddScoped<IWorkInboxSourceV1, PurchaseGoodsReceiptBatchWorkInboxSourceV1>();
        // F-017 F17-1 (ALS-D82): the allocatable amount of an approved general line, read by Inventory's future allocation only.
        services.AddScoped<IPurchaseFutureSupplyReadPortV1, PurchaseFutureSupplyReadPort>();
        // F17-1b / KLN-D30: current unreceived schedule slices, on the same scoped owner context/transaction.
        services.AddScoped<IPurchaseFutureSupplyScheduleReadPortV1, PurchaseFutureSupplyScheduleReadPort>();
        // BS-1 (F-011): the invoiced exchange rate of one goods receipt line, read by Inventory's average cost (Unavailable until invoiced).
        services.AddScoped<IPurchaseInvoiceCostRateReadPortV1, PurchaseInvoiceCostRateReadPort>();
        // SAT-D295 (BS-1): the recorded price of one purchase order line, read by Inventory's average cost (the rate is the invoice's).
        services.AddScoped<IPurchaseOrderLineCostReadPortV1, PurchaseOrderLineCostReadPort>();
        // FNS-D73: the exchange rate specified on one purchase order (none recorded yet: answered fail-closed, NEEDS-SINAN).
        services.AddScoped<IPurchaseOrderExchangeRateReadPortV1, PurchaseOrderExchangeRateReadPort>();
        return services;
    }

    // A named factory so that no registration statement of this module names the company context (architecture guard).
    private static PurchasingService CreatePurchasingService(IServiceProvider serviceProvider) => new PurchasingService(
        serviceProvider.GetRequiredService<ICompanyContext>(),
        serviceProvider.GetRequiredService<ICurrentUser>(),
        serviceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
        serviceProvider.GetRequiredService<ICorrelationContext>(),
        serviceProvider.GetRequiredService<IPurchasingPartyAccountPortV1>(),
        serviceProvider.GetService<IProductPurchaseEligibilityPortV1>(),
        serviceProvider.GetRequiredService<PurchaseUnitVerifier>(),
        serviceProvider.GetRequiredService<IPurchasingStore>(),
        serviceProvider.GetRequiredService<PurchasingServiceBindings>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptTransactionV1>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptReadsV1>(),
        // A2a-1 (TEK-01): the existing Settings money port and its operation-aware candidate read (ALS-D58: no new Settings port).
        serviceProvider.GetService<ISettingsPrerequisiteProvider>(),
        serviceProvider.GetService<ISettingsMoneyCandidateReaderV2>(),
        // A2a-2 (API-20, ALS-D74): the existing Settings line tax port for the item type's default VAT (no new Settings port).
        serviceProvider.GetService<ISettingsLineTaxResolverV1>(),
        // A4b (SAT-D220): Sales's approved line read, adapted by the host (not registered: Sales-sourced new lines stay closed).
        serviceProvider.GetService<IPurchaseRequestSalesSourcePortV1>(),
        // ALC-S16 (F-027): the bulk receipt's store and host transaction; the Ayarlar policy and Inventory's label register are optional
        // (unwired: no tolerance = every kg difference waits for approval, no generated code and no suffix suggestion — KLN-D41/D45).
        serviceProvider.GetRequiredService<IPurchaseGoodsReceiptBatchStore>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptBatchTransactionV1>(),
        serviceProvider.GetService<IPurchaseGoodsReceiptPolicyReadPortV1>(),
        serviceProvider.GetService<IPurchaseLabelCodeRegisterReadPortV1>(),
        // ALC-SC33 (FNS-D79): the existing Settings exchange-rate resolver for a foreign-currency order's default rate (no new port).
        serviceProvider.GetService<ISettingsExchangeRateResolverV1>(),
        // ALC-S3 (F-017 §3.5 F17-4): the host's bridge to the Inventory release port; unwired, the link list, the link drop and the
        // release-with-cancel/close are 503 and the plain cancel/close keep their earlier behavior (no allocation guard).
        serviceProvider.GetService<IPurchaseFutureAllocationLinkParticipantV1>(),
        // ALC-S5 (B-19; SAT-D306/D392/D393/D396): the supplier return store and host transaction. The invoice unit cost reader is
        // optional and unwired until supplier invoices exist (every return then takes the manual cost + exception path, SAT-D393);
        // the same-actor exception policy is not registered, so the default (a different approver) applies — OPEN, see SAT-D396.
        serviceProvider.GetRequiredService<IPurchaseSupplierReturnStore>(),
        serviceProvider.GetService<IPurchaseSupplierReturnTransactionV1>(),
        serviceProvider.GetService<IPurchaseSupplierReturnInvoiceCostReadPortV1>(),
        serviceProvider.GetService<PurchaseSupplierReturnPolicyV1>(),
        serviceProvider.GetService<IPurchaseReceiptReversalTransactionV1>(),
        serviceProvider.GetService<IPurchaseReceiptReversalBatchTransactionV1>(),
        serviceProvider.GetService<IPurchaseSupplierReturnCostSourceReadPortV1>(),
        // AK-3 (SAT-D437): the host's Product theoretical kg/piece adapter; unwired, an order with a piece line is not approved (503).
        serviceProvider.GetService<IPurchaseTheoreticalKilogramsPerUnitPortV1>(),
        // AK-8 (SAT-D433/SAT-D445): the host's adapter of Inventory's label lineage; unwired, a foreign-label return is 503.
        serviceProvider.GetService<IPurchaseSupplierReturnLineageReadPortV1>());

    /// <summary>Applies Purchasing's migrations. Its foreign keys need Organization's tables first.</summary>
    public static async Task InitializePurchasingModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
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
