using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.Api.FinanceRuntime;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Api.Search;
using GoymenERP.Api.Observability;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.OrganizationRuntime;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.Api.Startup;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Api.ReportingRuntime;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Documents.Infrastructure;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Infrastructure.Shell;
using GoymenERP.Modules.Idempotency.Infrastructure;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using GoymenERP.Modules.SecurityEvents.Infrastructure;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure;
using GoymenERP.Modules.Settings.Infrastructure.Shell;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Shell;
using GoymenERP.Modules.Reporting.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);
var isOpenApiDocumentGeneration = OpenApiDocumentGenerationMode.IsActive;
var databaseStartupMode = DatabaseStartupModeResolver.Resolve(
    builder.Configuration,
    builder.Environment.EnvironmentName);

if (isOpenApiDocumentGeneration)
{
    var buildTimeDefaults = new Dictionary<string, string?>
    {
        ["DB_PROVIDER"] = "SqlServer",
        ["Authentication:Mode"] = AuthenticationModes.OidcJwtBearer,
        ["Authentication:Oidc:RequireHttpsMetadata"] = "true",
    };
    if (string.IsNullOrWhiteSpace(builder.Configuration["DB_CONNECTION_STRING"]))
    {
        buildTimeDefaults["DB_CONNECTION_STRING"] =
            "Server=openapi.invalid;Database=OpenApi;User Id=OpenApi;Password=OpenApi;"
            + "Encrypt=true;TrustServerCertificate=false;Connect Timeout=1;";
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Oidc:Authority"]))
    {
        buildTimeDefaults["Authentication:Oidc:Authority"] = "https://openapi.invalid";
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Oidc:Audience"]))
    {
        buildTimeDefaults["Authentication:Oidc:Audience"] = "goymen-erp-api";
    }

    builder.Configuration.AddInMemoryCollection(buildTimeDefaults);
}

builder.Configuration.AddJsonFile(
    "Seed/organization-seed.json",
    optional: false,
    reloadOnChange: false);
builder.Configuration.AddJsonFile(
    "Seed/identity-seed.json",
    optional: false,
    reloadOnChange: false);

// F-032: diagnostic file log + OpenTelemetry; both off unless the Observability section turns them on.
builder.AddErpObservability(isOpenApiDocumentGeneration);

builder.Services.AddErpProblemDetails();
builder.Services.AddErpOpenApi();
builder.Services.AddOpenApi(ErpOpenApiContract.DocumentName, options =>
{
    options.AddOperationTransformer(SalesEndpoints.TransformDraftReadContractOpenApiAsync);
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(NativeApprovalReadDetailV1))
        {
            // Detail dispatch is owned by the route kind. The wire intentionally has only item + payload.
            schema.Discriminator = null;
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseSchemaCompatibilityHealthCheck>(
        DatabaseSchemaCompatibilityHealthCheck.Name,
        tags: [DatabaseSchemaCompatibilityHealthCheck.ReadinessTag]);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
var c01RequestEvidenceRecorder = C01E2eRequestEvidenceRecorder.Create(
    builder.Environment,
    builder.Configuration);
if (c01RequestEvidenceRecorder is not null)
{
    builder.Services.AddSingleton(c01RequestEvidenceRecorder);
}
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<ShellCapabilityGenerationProtector>();
builder.Services.AddSingleton<ShellCapabilityRegistry>();
builder.Services.AddScoped<IActorSessionBindingResolver, ActorSessionBindingResolver>();
builder.Services.AddScoped<IAuthenticationSessionBindingResolver, AuthenticationSessionBindingResolver>();
builder.Services.AddScoped<ShellCapabilityService>();
// FIN-T04b (#320): Finance persistence (with the audit interceptor) and its services: the real
// permission port, the persisted bank-account reader and the draft service.
builder.Services.AddFinancePersistence(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// FIN-T05 (Sinan's K1, #66 5748258299, closing 5745134466): a definitive manual bank receipt's period
// is decided by its occurredOn. The rule that maps the date needs Accounting's period port, which the
// Finance module deliberately cannot see, so the HOST composes the two — and the mapping stays in one
// host class rather than becoming a rule every accounting source inherits.
//
// BEFORE AddFinanceServices() on purpose: that method ends with
// TryAddSingleton<IFinanceRecordPeriodRule, UndecidedFinanceRecordPeriodRule>, and TryAdd tests the
// SERVICE TYPE only — so registering first makes it a no-op and EXACTLY ONE descriptor for this
// service ever enters the container. The undecided fallback is never registered at all.
//
// Why one descriptor is worth insisting on, which is a property of the container and needs no
// prediction: with two, the next reader cannot see which registration is dead, and a later consumer
// that ENUMERATES instead of resolving would get both. (Nothing injects
// IEnumerable<IFinanceRecordPeriodRule> today — checked — so that second risk is future, not present.)
//
// What the REVERSE order actually does is measured, not described here: the FIN-T05 host composition
// cases pin the container's behaviour under both orders. Find them by searching the API integration
// tests for IFinanceRecordPeriodRule, and believe them over any sentence in this file. The first
// version of this comment predicted that outcome, confidently and wrongly, which is why this one
// points at the test instead: a comment that names a pin cannot go stale, a comment that predicts a
// result can.
//
// Scoped, not singleton: both depend on ICompanyContext, ICurrentUser and IAccountingPeriodStore, which
// are per request. A singleton would capture one request's context — the defect #366 closed in two
// other host adapters this morning. Verified safe: the rule is resolved inside the SCOPED
// IManualReceiptService factory, so nothing longer-lived captures it.
//
// This grants nothing. The port reads under the CALLER's own accounting.periods.read; an actor without
// that name is refused with finance_period_rule_unavailable, and granting it is a human decision.
builder.Services.AddScoped<IAccountingPeriodStatusPortV1, AccountingPeriodStatusPort>();
builder.Services.AddScoped<IFinanceRecordPeriodRule, TransactionalOccurredOnFinanceRecordPeriodRule>();
builder.Services.AddScoped<IAccountingPeriodCarryForwardPortV1, FinancePeriodCarryForwardPortV1>();
builder.Services.AddFinanceServices();
builder.Services.AddFinanceOfficialInvoiceImportHost();
builder.Services.AddFinanceOfficialSalesInvoiceHost();
builder.Services.AddSalesFinanceAdmissionHost();
builder.Services.AddSalesFinanceCancellationHost();
builder.Services.AddFinanceInvoiceAcceptanceHost();
builder.Services.AddScoped<INativeApprovalReadAdapterV1, FinanceInvoiceImportPendingApprovalAdapterV1>();
builder.Services.AddScoped<INativeApprovalReadAdapterV1, InventoryStockCountPendingApprovalAdapterV1>();
builder.Services.AddScoped<INativeApprovalReadAdapterV1, InventoryStockScrapPendingApprovalAdapterV1>();
builder.Services.AddScoped<INativeApprovalReadAdapterV1, SalesOrderDraftPendingApprovalAdapterV1>();
builder.Services.AddScoped<NativeApprovalReadQueryV1>();
builder.Services.AddScoped<NativeApprovalQueueReadQueryV1>();
// W1-A (AI-D31/AI-D32, YOL-D18): the Work inbox reads the owners' IWorkInboxSourceV1 ports (registered by each module)
// and records only Work's own measurement events; it decides nothing.
builder.Services.AddWorkApprovalInboxMeasurementsV1();
// WP1 (D5): the four native D-05 kinds are inbox sources too — one box; the duration class comes from Work's versioned rule
// table (D3, YOL-D18 defaults) and business days are counted in the P7 / SAT-D10 business time zone (D6).
builder.Services.AddWorkApprovalRulesV1();
builder.Services.AddScoped<IWorkInboxSourceV1>(provider => new NativeApprovalWorkInboxSourceV1(
    ActivatorUtilities.CreateInstance<FinanceInvoiceImportPendingApprovalAdapterV1>(provider)));
builder.Services.AddScoped<IWorkInboxSourceV1>(provider => new NativeApprovalWorkInboxSourceV1(
    ActivatorUtilities.CreateInstance<InventoryStockCountPendingApprovalAdapterV1>(provider)));
builder.Services.AddScoped<IWorkInboxSourceV1>(provider => new NativeApprovalWorkInboxSourceV1(
    ActivatorUtilities.CreateInstance<InventoryStockScrapPendingApprovalAdapterV1>(provider)));
builder.Services.AddScoped<IWorkInboxSourceV1>(provider => new NativeApprovalWorkInboxSourceV1(
    ActivatorUtilities.CreateInstance<SalesOrderDraftPendingApprovalAdapterV1>(provider)));
builder.Services.AddScoped<WorkInboxReadQueryV1>();
// WP-A3c: Work READ tools for the AI catalog over the same inbox query (maker ≠ checker, owner permissions) and the
// W1-C "my tasks" read. Registration only makes them candidates of the default-deny catalog; Ai:AllowedTools stays empty.
builder.Services.AddScoped<GoymenERP.BuildingBlocks.Ai.IAiOwnerToolV1, GoymenERP.Api.WorkRuntime.Ai.WorkInboxAwaitingMeAiTool>();
builder.Services.AddScoped<GoymenERP.BuildingBlocks.Ai.IAiOwnerToolV1, GoymenERP.Api.WorkRuntime.Ai.WorkOverdueApprovalsAiTool>();
builder.Services.AddScoped<GoymenERP.BuildingBlocks.Ai.IAiOwnerToolV1, GoymenERP.Api.WorkRuntime.Ai.WorkMyTasksAiTool>();
builder.Services.AddScoped<IFinancePartyAccountPort, CounterpartyFinancePartyAccountPort>();
// B-17: the version bump with the account's roles (dual role) and supplier IBAN view; V1 stays for its consumers.
builder.Services.AddScoped<IFinancePartyAccountPortV2, CounterpartyFinancePartyAccountPortV2>();
builder.Services.AddScoped<DraftReadContextReader>();
builder.Services.AddScoped<IFinanceDraftContextEndpointService, FinanceDraftContextEndpointService>();
builder.Services.AddSingleton<IFinanceDraftReadBinding, ManualReceiptDraftReadBinding>();
builder.Services.AddScoped<ISettingsPrerequisiteProvider, ApprovedSettingsPrerequisiteProvider>();
builder.Services.AddScoped<ISettingsMoneyCandidateReader, ApprovedSettingsPrerequisiteProvider>();
// SAT-D18: operation-aware currency candidates, so a Sales-only profile (GSP/GIN TRY) is listed for Sales only.
builder.Services.AddScoped<ISettingsMoneyCandidateReaderV2, ApprovedSettingsPrerequisiteProvider>();
// MOD-03 slice 1: the Settings-owned Sales policy registry (SAT-D2/D5/D6). AddSettingsModule replaces it with the
// SettingsSalesPolicyProviderV1 that adds the company's discount limit groups (SAT-D61…D64) to this catalog.
builder.Services.AddScoped<ISettingsSalesPolicyProviderV1, ApprovedSettingsPrerequisiteProvider>();
// SAT-D102: the Settings-owned e-Fatura scenario code list and default (SAT-D120 seeds the code list; the default
// starts empty); read by Cari. AddSettingsModule replaces it with SettingsEInvoiceScenarioPolicyProviderV1 (F-029 S6), which
// answers from this catalog until the company's baseline is transferred to the Settings table.
builder.Services.AddScoped<ISettingsEInvoiceScenarioPolicyProviderV1, ApprovedSettingsPrerequisiteProvider>();
// SAT-D25 / SAT-D39: the Settings-owned Sales quote policies (loss reason list, default validity); the reviewed catalog is
// empty, so a loss is refused and no validity is suggested until Sinan enters the records. AddSettingsModule replaces it
// with SettingsSalesQuotePolicyProviderV1 (F-029 S5), which answers from this catalog until the baseline is transferred.
builder.Services.AddScoped<ISettingsSalesQuotePolicyProviderV1, ApprovedSettingsSalesQuotePolicyProvider>();
// PRD-UNIT-1: the company-independent unit identities of the approved catalog (immutable source data).
builder.Services.AddSingleton<ISettingsUnitIdentityCatalogV1, ApprovedSettingsUnitIdentityCatalog>();
builder.Services.AddScoped<ISalesAccountSelectionSourceV1, CounterpartySalesAccountSelectionSourceV1>();
// SALES-API (#321): the Sales reference reads are bound to the draft form's permission (the Sales
// owner's decision), and the Cari account port serves the draft use case.
// SAT-D27 (2): a holder of the request or quote write name reads the same customer/product/currency lists; SAT-D27 (1):
// the sales-owner picker (opened by the order create name alone) lists the company's active holders of that name.
builder.Services.AddSingleton(new SalesReferenceReadBindings(
    AccountsPermission: SalesPermissionManifestV1.DraftsCreate,
    CurrencyPermission: SalesPermissionManifestV1.DraftsCreate,
    ProductPermission: SalesPermissionManifestV1.DraftsCreate,
    MoneyOperation: SalesMoneyOperations.OrderDraftV1,
    AlternativePermissions: [SalesPermissionManifestV1.RequestsWrite, SalesPermissionManifestV1.QuotesWrite],
    OwnerEligibilityPermission: SalesPermissionManifestV1.DraftsCreate));
builder.Services.AddScoped<ISalesPartyAccountPortV1, CounterpartySalesPartyAccountPortV1>();
// SALES-A2a: the same Cari adapter answers the contact-information port — one authorized owner read,
// not a second copy of the party's addresses and contacts in Sales.
builder.Services.AddScoped<ISalesPartyContactInformationPortV1, CounterpartySalesPartyAccountPortV1>();
builder.Services.AddScoped<ISalesReferenceReadService>(CreateSalesReferenceReadService);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddSecurityEventsModule(builder.Configuration);
// E8 and the AI provider key: secret store reader only; no secret value is configured here.
builder.Services.AddErpSecretStore(builder.Configuration);
// AI-3: READ-only AI runtime, off unless configured (Ai:Enabled); no owner tool is registered here.
builder.Services.AddAiRuntime(builder.Configuration);
builder.Services.AddAiSecurityWatchTools();
builder.Services.AddSettingsModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// KIM-S19 (B-13): Finance's opening period reader answers from the Settings company policy registry. Finance registers its
// fail-closed default with TryAddScoped, so the host replaces it here; any failure still answers "not available".
builder.Services.Replace(ServiceDescriptor.Scoped<IFinanceOpeningPeriodReader, SettingsFinanceOpeningPeriodReader>());
builder.Services.Replace(ServiceDescriptor.Scoped<IFinanceCollateralTypeCatalogReader, SettingsFinanceCollateralTypeCatalogReaderV1>());

static ISalesReferenceReadService CreateSalesReferenceReadService(IServiceProvider services) =>
    new SalesReferenceReadHandler(
    services.GetRequiredService<ICompanyContext>(),
    services.GetRequiredService<ICurrentUser>(),
    services.GetRequiredService<IErpAuthorizationService>(),
    services.GetRequiredService<ISalesAccountSelectionSourceV1>(),
    services.GetRequiredService<ISettingsPrerequisiteProvider>(),
    services.GetService<IProductSalesEligibilityV1>(),
    services.GetRequiredService<SalesReferenceReadBindings>(),
    // SALES-A2a: both optional, and both answer 503 while absent — the currencies list is Settings'
    // approved candidates, the delivery address and contact the Cari owner's contact information.
    services.GetService<ISettingsMoneyCandidateReader>(),
    services.GetService<ISalesPartyContactInformationPortV1>(),
    services.GetService<ISettingsMoneyCandidateReaderV2>(),
    // SAT-D27 (1): the sales-owner picker's Identity adapter.
    services.GetService<ISalesOwnerCandidateSourceV1>());
builder.Services.AddIdempotencyModule(builder.Configuration);
builder.Services.AddOrganizationModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddIdentityModule(
    builder.Configuration,
    enableAuthorizationSnapshotDatabaseReadiness:
        databaseStartupMode == DatabaseStartupMode.Initialize);
builder.Services.AddProductRuntime(builder.Configuration, builder.Environment);
// AT-018: company-scoped price-list persistence, its two Sales reference readers and owner service.
builder.Services.AddProductPriceListHost(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddProductPurchaseSelection(p => p.GetRequiredService<ProductRuntimeOptions>().IsEnabled);
builder.Services.AddProductLogisticsDocumentDisplay(p => p.GetRequiredService<ProductRuntimeOptions>().IsEnabled);
builder.Services.AddCounterpartyModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()),
    allowEphemeralCursorKey:
        builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Testing")
        || isOpenApiDocumentGeneration);
builder.Services.AddScoped<
    IOrganizationLocationOperatorEligibilityPortV1,
    CounterpartyOrganizationLocationOperatorEligibilityPortV1>();
builder.Services.AddInventoryModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddInventoryStockPositionCursor(
    builder.Configuration["Inventory:StockPositionCursorSigningKeyBase64"]);
builder.Services.AddScoped<IInventoryStockProductFilterV1, InventoryStockProductFilterHost>();
// STK-S30 (KLN-D39/D70): the Product owner's similarity facts behind label suggestions and the substitute check.
builder.Services.AddScoped<IInventoryProductSimilarityFactsV1, InventoryProductSimilarityFactsHost>();
builder.Services.AddScoped<IInventoryStockOwnerDisplaySourceV1, InventoryStockOwnerDisplayHost>();
// STK-S25 (BS-1, SAT-D295): the Purchasing owner's supplier invoice rate behind the foreign-currency average cost.
builder.Services.AddScoped<IInventoryPurchaseInvoiceCostRateSourceV1, InventoryPurchaseInvoiceCostRateHost>();
builder.Services.AddInventoryMovementTimelineCursor(
    builder.Configuration["Inventory:StockPositionCursorSigningKeyBase64"]);
builder.Services.AddInventoryParentRollReferences();
builder.Services.AddInventoryPhysicalAllocationReads();
// R19: the label genealogy writer/reader and the fenced split/merge command behind the published route.
builder.Services.AddInventoryLabelGenealogy();
// STK-S33 (KLN-D44/KLN-D41): the cross-module label trace under the label-detail permissions (no new permission). The host
// adapters of the production, party and supplier-search owners replace the trace's not-answered defaults, so they are added
// before the trace's TryAdd registrations; Logistics answers a shipment line's customer through its own read port.
builder.Services.AddScoped<IInventoryLabelTraceProductionSourceV1, InventoryLabelTraceProductionHostSource>();
builder.Services.AddScoped<IInventoryLabelTracePartySourceV1, InventoryLabelTracePartyHostSource>();
builder.Services.AddScoped<IInventoryLabelTraceSupplierSearchSourceV1, InventoryLabelTraceSupplierSearchHostSource>();
builder.Services.AddLogisticsShipmentCustomerRead();
builder.Services.AddInventoryLabelTrace();
// Label classification is an Inventory-owned, versioned command. Central registers only the reviewed
// permission identity and binds the existing scoped store/command; this grants no actor access.
builder.Services.AddScoped(_ => new InventoryLabelClassificationAuthorityV1(
    ErpPermissions.InventoryLabelClassificationWrite));
builder.Services.AddScoped<IInventoryLabelClassificationWriterV1>(services =>
    services.GetRequiredService<global::GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryStore>());
builder.Services.AddScoped<IInventoryLabelClassificationCommandV1, InventoryLabelClassificationCommandService>();
// AT-037: the stock count's fenced record/decide commands and the label count read behind the published routes.
builder.Services.AddInventoryStockCounts();
// ST-02 scrap write-off: the fenced record/decide commands and the label scrap read behind the published routes.
builder.Services.AddInventoryStockScraps();
// SAT-D304 / SAT-D372: the opening-stock cost submit/decide commands (giren ≠ onaylayan) behind the published routes.
builder.Services.AddInventoryOpeningCosts();
// D-05: read-only Inventory pending approvals composed into the existing Work inbox.
builder.Services.AddInventoryPendingApprovalReads();
// B-22a: the stock transfer plan/dispatch commands and the transfer read behind the published routes, with the
// destination custody check over Organization's company-scoped location read (DEC-ORG-004). Dispatch moves stock
// only after the Logistics outgoing-document proof answers CurrentEffective. Accept is not published here.
builder.Services.AddInventoryTransfers();
builder.Services.AddScoped<IInventoryTransferDestinationPortV1, OrganizationInventoryTransferDestinationPort>();
// INV-OPEN-1 part 2: the opening command's host ports — Organization's company-scoped location module read
// and Product's stock eligibility (the owners' contracts; no user read, no copy of either owner's rows).
builder.Services.AddScoped<IInventoryLocationStatusPort, OrganizationInventoryLocationStatusPort>();
builder.Services.AddScoped<IInventoryProductStockPort, ProductInventoryStockPort>();
builder.Services.AddScoped<IInventoryOpeningOwnerAccountPortV1, CounterpartyInventoryOpeningOwnerAccountPortV1>();
builder.Services.AddManufacturingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// MFG realization v1: the host composes the approved Manufacturing ports with their API-owned adapters.
builder.Services.AddManufacturingCostRuntimeComposition();
builder.Services.AddScoped<IManufacturingInventoryOutputAdmission, ManufacturingInventoryOutputAdmission>();
builder.Services.AddScoped<IManufacturingInputOrderRateReadPortV1, ManufacturingSalesOrderInputRateReadPortV1>();
builder.Services.AddScoped<IManufacturingInventoryInputIssuePortV1, ManufacturingInventoryInputIssueHost>();
// F-009 D3 (URT-S2): the output correction shares the return/output host and its one transaction per command.
builder.Services.AddScoped<ManufacturingInventoryReturnOutputHost>();
builder.Services.AddScoped<IManufacturingInventoryReturnOutputPortV1>(services =>
    services.GetRequiredService<ManufacturingInventoryReturnOutputHost>());
builder.Services.AddScoped<IManufacturingInventoryOutputCorrectionPortV1>(services =>
    services.GetRequiredService<ManufacturingInventoryReturnOutputHost>());
// Üretim U3-a (URT-D21, B-01): the plan approval / cancellation and the order closure commit with Inventory's
// plan-input allocation or its release in one transaction.
builder.Services.AddScoped<IManufacturingPlanAllocationPortV1, ManufacturingPlanAllocationHost>();
builder.Services.AddScoped<IManufacturingOutputSelectionV1, ManufacturingOutputSelectionReader>();
builder.Services.AddSalesModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// A Sales draft read may disclose only the code/name of the Cari record already linked to that
// authorized Sales result. The adapter grants no generic Cari read and has no public lookup route.
builder.Services.AddScoped<ISalesPartyDisplaySourceV1, SalesPartyDisplayHost>();
// F2 (SAT-D309, LOJ-D9): the subcontract service invoice reads the delivery weighing's net kg only through the Logistics
// owner's F1 read; the adapter has no route, carries no authority and maps facts only.
builder.Services.AddScoped<ISalesSubcontractDeliveryWeighingReadPortV1, SalesSubcontractDeliveryWeighingHost>();
builder.Services.AddScoped<GoymenERP.Modules.Sales.Infrastructure.Persistence.ISalesSubcontractWeighingCommitReadPortV1, SalesSubcontractWeighingCommitReadHost>();
// SAT-S13 (SAT-D343, SAT-D389): the dispatch screen and the invoice gate read a note's weighing difference only through the
// Logistics owner's STK-S24 read; the adapter has no route, carries no authority and maps facts only.
builder.Services.AddScoped<ISalesDispatchWeighingDifferenceReadPortV1, SalesDispatchWeighingDifferenceHost>();
// MOD-08: the same read may name the people its approval and acceptance already identify by id, through Identity's
// company-fenced user directory. The adapter has no route and passes only ids from the authorized record.
builder.Services.AddScoped<ISalesUserDisplaySourceV1, SalesUserDisplayHost>();
// SAT-D27 (1): the sales-owner picker's adapter to the same Identity directory (scoped, in-process).
builder.Services.AddScoped<ISalesOwnerCandidateSourceV1, SalesOwnerCandidateHost>();
// SAT-D63/D66: the Sales limit check reads the actor's limit groups from the Settings owner's person → group bindings, and
// the Settings limit administration reads people and their company memberships from the Identity owner, both through
// central adapters (no module reads another's tables).
builder.Services.AddScoped<ISalesDiscountLimitGroupSourceV1, SalesDiscountLimitGroupHost>();
builder.Services.AddScoped<ISalesContractDiscountLimitSourceV1, SettingsSalesContractDiscountLimitHost>();
builder.Services.AddScoped<ISettingsPersonDirectoryV1, SettingsPersonDirectoryHost>();
// KIM-D39: the Settings segregation of duties owner reads "exactly one active user in the company" from the Identity owner
// through this central adapter (Settings reads no Identity table).
builder.Services.AddScoped<ISettingsCompanyActiveUsersV1, SettingsCompanyActiveUsersHost>();
// SAT-D134/D139: the Settings line tax port reads the product's item type from the Product owner's company-scoped field
// through this central adapter (Settings reads no Product table).
builder.Services.AddScoped<ISettingsProductItemTypeSourceV1, SettingsProductItemTypeHost>();
// URT-D89: the Settings work center capacity administration asks whether a work center exists in the server company through
// this central adapter over the Organization owner's planning reference read (Settings reads no Organization table).
builder.Services.AddScoped<ISettingsWorkCenterReferenceV1, SettingsWorkCenterReferenceHost>();
builder.Services.AddScoped<ISalesDraftReadServiceV1, SalesDraftReadServiceV1>();
// STK/T11: the host-owned allocation boundary consumes Sales only through its approved-line port and
// hands the derived command to Inventory's writer after fresh company-wide authority fences.
builder.Services.AddScoped<InventoryPhysicalAllocationHostService>();
// LOG-4a: the loading instruction's customer is the Cari owner's answer (the Sales party port's shape) and its
// order lines are Sales's approved-line port (registered by the Sales module); nothing of either is copied.
builder.Services.AddScoped<ILogisticsPartyAccountPortV1, CounterpartyLogisticsPartyAccountPortV1>();
builder.Services.AddScoped<ILogisticsRealizationTransactionRunnerV1, LogisticsRealizationTransactionRunner>();
builder.Services.AddScoped<ILogisticsCustomerReturnScopeV1, LogisticsCustomerReturnScope>();
builder.Services.AddScoped<ILogisticsCustomerReturnTransactionRunnerV1, LogisticsCustomerReturnTransactionRunner>();
builder.Services.AddScoped<ILogisticsTransferOutgoingDocumentScopeV1, LogisticsTransferOutgoingDocumentScope>();
builder.Services.AddScoped<IInventoryTransferOutgoingDocumentProofPortV1, InventoryTransferOutgoingDocumentProofAdapter>();
// STK-S24: the weighing-difference fence and the read-only physical stock seam over Inventory. The Settings rate/band port
// keeps Logistics's fail-closed default until Settings publishes those values.
builder.Services.AddScoped<ILogisticsWeighingDifferenceScopeV1, LogisticsWeighingDifferenceScope>();
builder.Services.AddScoped<IDispatchKilogramDocumentReadPortV1, DispatchKilogramDocumentReadPort>();
builder.Services.AddScoped<ILogisticsWeighingDifferenceStockReadPortV1, InventoryWeighingDifferenceStockAdapter>();
builder.Services.AddLogisticsModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// PUR-4a: the order's supplier is the Cari owner's answer (the Sales party port's shape, plus the account's stored
// Supplier role flag). The purchase-eligibility port adapts Product's published IProductPurchaseEligibilityV1
// (AddProductPurchaseSelection above); while Product's runtime is off or its answer fails, the service answers
// 503 product_eligibility_unavailable, fail-closed.
builder.Services.AddScoped<IPurchasingPartyAccountPortV1, CounterpartyPurchasingPartyAccountPortV1>();
builder.Services.AddScoped<IProductPurchaseEligibilityPortV1, ProductPurchaseEligibilityPort>();
// AK-3 (SAT-D437): a piece line's theoretical kg/piece is read from the Product owner at order approval and recorded on the line.
builder.Services.AddScoped<IPurchaseTheoreticalKilogramsPerUnitPortV1, GoymenERP.Api.PurchasingRuntime.ProductOwnerPurchaseTheoreticalKilogramsPerUnitPort>();
// A4 / MOD-03 (SAT-D219/D220): Purchasing's Sales-facing adapters live in the host (Purchasing does not reference Sales): the
// linked-supply read (SupplyKind Purchasing) with its recorder declaration, and the Sales-sourced request line reader.
builder.Services.AddScoped<ISalesOrderLinkedSupplyReadPortV1, PurchasingSalesOrderLinkedSupplyReadPort>();
builder.Services.AddSingleton<ISalesOrderLinkedSupplyRecorderV1, PurchasingSalesOrderLinkedSupplyRecorder>();
builder.Services.AddScoped<IPurchaseRequestSalesSourcePortV1, PurchasingRequestSalesSourcePort>();
// Üretim U2-b / API-16 (SAT-D219): Manufacturing's Sales-facing adapters live in the host too (Manufacturing does not reference
// Sales): the linked-supply read (SupplyKind Production) with its recorder declaration, and the approved-line reader the plan's
// sales order link uses.
builder.Services.AddScoped<ISalesOrderLinkedSupplyReadPortV1, ManufacturingSalesOrderLinkedSupplyReadPort>();
builder.Services.AddSingleton<ISalesOrderLinkedSupplyRecorderV1, ManufacturingSalesOrderLinkedSupplyRecorder>();
builder.Services.AddScoped<IManufacturingSalesOrderLineReferencePortV1, ManufacturingSalesOrderLineReferencePort>();
// Üretim U5-b / API-36 (URT-D16, URT-D22, SAT-D175): the subcontract service order read a customer-goods plan's link checks,
// and Sales's binding question answered from the Manufacturing owner (Bound / NotBound by the company's plans that are not
// cancelled; see the adapter).
builder.Services.AddScoped<IManufacturingSubcontractServiceOrderPortV1, ManufacturingSubcontractServiceOrderPort>();
builder.Services.AddScoped<ISalesSubcontractOrderBindingReadPortV1, ManufacturingSalesSubcontractOrderBindingReadPort>();
builder.Services.AddSingleton(new PurchasingServiceBindings(
    GoodsReceiptPermission: ErpPermissions.PurchasingGoodsReceiptsRecord));
builder.Services.AddScoped<PurchaseGoodsReceiptHost>();
builder.Services.AddScoped<IPurchaseGoodsReceiptTransactionV1>(provider => provider.GetRequiredService<PurchaseGoodsReceiptHost>());
builder.Services.AddScoped<IPurchaseGoodsReceiptReadsV1>(provider => provider.GetRequiredService<PurchaseGoodsReceiptHost>());
builder.Services.AddScoped<IPurchaseGoodsReceiptBatchTransactionV1, PurchaseGoodsReceiptBatchHost>();
// ALC-S5 (B-19; SAT-D306): one transaction for the supplier return row and Inventory's exit at the original line cost.
builder.Services.AddScoped<IPurchaseSupplierReturnTransactionV1, PurchaseSupplierReturnHost>();
builder.Services.AddScoped<IPurchaseSupplierReturnCostSourceReadPortV1, PurchaseSupplierReturnCostSources>();
// AK-8 (SAT-D433/SAT-D445): Purchasing reads a label's lineage only through Inventory's read port.
builder.Services.AddScoped<IPurchaseSupplierReturnLineageReadPortV1, PurchaseSupplierReturnLineage>();
// F-017 F17-1 (§3.4): Inventory's future allocation takes the purchase general line's lock through this binder (no module reference).
builder.Services.AddScoped<IPurchaseLineSerializationLockBinderV1, PurchaseLineSerializationLockHostBinder>();
// F-017 STOK F017-b: the sales order line lock (taken before the purchase line lock) and the Purchasing adapter of Inventory's
// future supply source; both replace Inventory's fail-closed defaults. No route or permission (F017-b2).
builder.Services.AddScoped<ISalesOrderLineSerializationLockBinderV1, SalesOrderLineSerializationLockHostBinder>();
builder.Services.AddScoped<IInventoryFutureSupplySourceV1, InventoryFutureSupplyHostSource>();
builder.Services.AddScoped<IInventoryFutureSupplyScheduleSourceV1, PurchasingOwnerInventoryFutureSupplyScheduleSource>();
// X1 (KLN-D28 (9)): the approved sales order cancellation releases its lines' future supply allocations through Inventory's
// release port in the cancellation's own transaction (sales line locks, then purchase line locks). No route or permission.
builder.Services.AddScoped<ISalesOrderFutureAllocationReleaseParticipantV1, SalesFutureAllocationReleaseHostParticipant>();
// SAT-S8 / KLN-D105: only the accepted reduced revision releases future demand, in the Sales command transaction.
builder.Services.AddScoped<ISalesOrderFutureDemandReductionParticipantV1, SalesFutureDemandReductionHostParticipant>();
builder.Services.AddScoped<ILogisticsSubcontractOrderReadPortV1, ManufacturingLogisticsSubcontractOrderReadPort>();
// F-017 §3.5 F17-4 (ALS-D83/ALS-D85): Purchasing's link drop, drop-and-cancel and drop-and-close release through Inventory's
// release port in Purchasing's own transaction (the order's purchase line locks are already held); the plain cancel/closure are
// refused while an active allocation remains.
builder.Services.AddScoped<IPurchaseFutureAllocationLinkParticipantV1, PurchasingFutureAllocationLinkHostParticipant>();
// KLN-D42 (STK-S31): the Purchasing adapter of Inventory's shortage warning supply source (replaces the fail-closed default).
builder.Services.AddScoped<IInventoryShortageSupplySourceV1, InventoryShortageSupplyHostSource>();
builder.Services.AddPurchasingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// Purchasing list/detail may disclose only the current Cari presentation of the supplier already
// bound to the authorized order. This adapter grants no generic Cari read capability or route.
builder.Services.AddScoped<IPurchasingSupplierDisplaySourceV1, PurchasingSupplierDisplayHost>();
// ACC-V0a: Accounting's periods, journals and posting queues (its verifier reads Settings' amount profile through
// the Settings prerequisite provider the host already registers; no shell screen until ACC-V0b).
builder.Services.AddAccountingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// FIN-S13 (B-04): Accounting's sales invoice journal consumer (scoped, not with the dispatcher until Sales registers its outbox
// source) and its host admission authority, fail-closed unless Accounting:InvoiceJournal:InboxAdmissionEnabled is true.
builder.Services.AddAccountingInvoiceJournal(builder.Configuration);
// B-24b / F-021: Work's module (desired-state store, outbox/inbox tables) is registered in the host. The ERP-000F
// outbox dispatcher stays OFF unless Work:OutboxDispatcher:Enabled is true (production activation is a human gate);
// event sources and consumers are registered by their owners.
builder.Services.AddWorkModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddWorkOutboxDispatcher(builder.Configuration);
// W1-C: notes, mentions, tasks and the collaboration inbox; the scheduler stays off unless Work:CollaborationScheduler:Enabled.
builder.Services.AddWorkCollaborationHostV1(builder.Configuration);
// WP-C6 (AI-D30): personal assistant memory; writing stays closed until Work:AssistantMemory is opened by a human decision.
builder.Services.AddWorkAssistantMemoryV1(builder.Configuration);
// WP14 (DALGA_PLANI_v2 1.15, YOL-D13, ROL-D13/D15): the report centre. Its numbers come from owner read ports through host
// adapters; Reporting stores only saved views, export records and command attempts. No permission is granted.
builder.Services.AddReportingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddInventoryStockAgingReadPort();
builder.Services.AddReportingRuntimeV1();
// STK-S27 (LOJ-D19): Logistics's definite-shipment outbox is the dispatcher source "logistics" (registered, not enabled).
builder.Services.AddLogisticsDefiniteShipmentOutbox();
// SAT-S10 (LOJ-D19, SAT-D285): the Sales usage mirror is the inbox consumer of that source (registered after it, not enabled).
builder.Services.AddSalesRiskExceptionUsageInbox();
builder.Services.AddCompanySetupMessaging();
builder.Services.AddTrainCompletionOwnerBindings();
builder.Services.AddInventorySubcontractAllocationRelease();
builder.Services.AddPurchaseInvoiceImportHost();
builder.Services.AddPurchaseInvoiceImportProductSearch();
builder.Services.AddCompanyLifecycleAuthorizationGate();
// SAT-D406 / SAT-D367: the Sales risk remainder's plan quantity, answered by the Logistics owner's planned-quantity read.
builder.Services.AddSalesOrderPlannedQuantityReadAdapter();
// ERP-000G / AT-065: the document vault (audit interceptor, the Cari party owner link; the development object store and
// EICAR-only scanner only in Development/Testing; elsewhere the own-server store and Windows Defender scanner when both
// are configured, otherwise uploads and downloads answer 503, fail-closed).
builder.Services.AddDocumentsRuntime(builder.Configuration, builder.Environment);
builder.Services.AddDocumentBrandingPdfRuntime();
builder.Services.AddLogisticsSubcontractDeliveryRuntime();
// AT-002 / ERP-000I: the global search composer and its module-owned sources (Cari, Product, Sales), after the modules.
builder.Services.AddGlobalSearch();
// Registered before the shell authority is chosen: the module table maps the provider set.
builder.Services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
// FIN-UI-1a (#325): Finance's (staged) receipt and bank-account screens, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();
// MFG-UI (#323): Manufacturing's (staged) order detail and facts control, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, ManufacturingShellCapabilityProvider>();
// LOG-4a: Logistics's (staged) loading-instruction screens, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, LogisticsShellCapabilityProvider>();
// PUR-4a: Purchasing's request and order screens (mounted in the shell router), before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, PurchasingShellCapabilityProvider>();
// ACC-V0b: Accounting's (staged) period, journal and posting-queue screens, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, AccountingShellCapabilityProvider>();
// SY-01/SY-02: Identity's read-only users-and-permissions view (identity.assignments.read), before the authority
// is chosen. No control: assigning and revoking stay blocked (NEEDS-SINAN #66 5738113623, OPEN-013).
builder.Services.AddSingleton<IShellCapabilityProvider, IdentityShellCapabilityProvider>();
// AT-018: Product's company-scoped U-05 price-list screens and edit/publish controls.
builder.Services.AddSingleton<IShellCapabilityProvider, ProductPriceListShellCapabilityProvider>();
builder.Services.AddSingleton<IShellCapabilityProvider, ProductPaintMembershipShellCapabilityProvider>();
// SAT-D61…D70: Settings' discount limit screen (settings.sales-discount-limits.manage, granted to no role).
builder.Services.AddSingleton<IShellCapabilityProvider, SettingsShellCapabilityProvider>();
// WP9: Work's own "Bana atanan" route and assign control (work.tasks.complete / work.tasks.assign, granted to no role).
builder.Services.AddSingleton<IShellCapabilityProvider, WorkShellCapabilityProvider>();
var shellCapabilityAuthority = C01E2eShellCapabilityActivation.Configure(
    builder.Services,
    builder.Environment,
    builder.Configuration);
// BROWSER-CI a2 part 2: the gated fixture writers exist only inside the browser-suite fixture runner's
// in-process host (Testing + its exact run binding); nowhere else are they registered.
C01E2eFixtureWriterActivation.Configure(
    builder.Services,
    builder.Environment,
    builder.Configuration);
// MFG browser acceptance: the compose-bound disposable API may replace only the read-only Settings
// tolerance provider. Fixture-writer keys and writer services remain confined to the in-process runner.
BrowserSuiteApiReadFixtureActivation.Configure(
    builder.Services,
    builder.Environment,
    builder.Configuration);
builder.Services.AddSingleton(new ShellCapabilityAuthorityCatalog(
    [shellCapabilityAuthority]));
builder.Services.AddErpAuthentication(builder.Configuration, builder.Environment);

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .WithExposedHeaders(CounterpartyClientContract.HeaderName)
            .AllowAnyMethod()));

var app = builder.Build();

_ = app.Services.GetRequiredService<ShellCapabilityRegistry>().Revision;

app.UseExceptionHandler();
app.UseMiddleware<ShellCapabilityTransportMiddleware>();
app.UseMiddleware<CorrelationContextMiddleware>();
app.UseCors();
if (databaseStartupMode == DatabaseStartupMode.ValidateOnly)
{
    app.UseMiddleware<DatabaseReadinessGateMiddleware>();
}
app.UseAuthentication();
app.UseLocalAccountAntiforgery();
// ID-2: a session whose credential still requires a password change reaches only the allow-listed endpoints.
app.UseLocalAccountPasswordChangeFence();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();
// F023-2 / KIM-D10: an endpoint's own 401/403 (service fence) is recorded once as authz.denied.
app.UseMiddleware<AuthorizationDenialResponseMiddleware>();

app.MapErpHealthEndpoints();
app.MapOrganizationEndpoints();
app.MapIdentityEndpoints();
app.MapAuditEndpoints();
app.MapSecurityEventsEndpoints();
app.MapAiEndpoints();
app.MapCounterpartyEndpoints();
app.MapProductEndpoints();
app.MapProductPriceListEndpoints();
app.MapProductItemTypeEndpoints();
app.MapProductDimensionEndpoints();
app.MapProductProfileWeightTableEndpoints();
app.MapProductPackageStandardEndpoints();
app.MapProductPaintMembershipEndpoints();
app.MapProductCogsClassificationEndpoints();
app.MapFinanceEndpoints();
app.MapFinancePartyBalanceTransfersV1();
app.MapFinanceBankStatementImportsV1();
app.MapFinanceCashExchangeBookValuesV1();
app.MapFinanceOpenItemReadEndpoints();
app.MapWorkApprovalEndpoints();
app.MapWorkQueueEndpoints();
app.MapWorkInboxEndpoints();
app.MapWorkApprovalRuleEndpoints();
app.MapWorkNotificationEndpoints();
app.MapWorkCollaborationEndpoints();
app.MapWorkAssistantMemoryEndpoints();
app.MapReportingEndpoints();
app.MapSalesEndpoints();
app.MapSalesProductCandidateEndpoints();
app.MapInventoryEndpoints();
app.MapInventorySubcontractAllocationReleaseEndpoints();
app.MapInventoryTransferPlanCandidateEndpoints();
app.MapInventoryTransferReceiptDocumentEndpoints();
app.MapManufacturingEndpoints();
app.MapLogisticsEndpoints();
app.MapLogisticsSubcontractDeliveryEndpoints();
app.MapLogisticsPlanExcessEndpoints();
app.MapLogisticsTransferDepartureEndpoints();
app.MapLogisticsTransferOutgoingDocumentEndpoints();
app.MapLogisticsWeighingDifferenceEndpoints();
app.MapLogisticsDispatchPdfEndpoints();
app.MapLogisticsCustomerReturnEndpoints();
app.MapPurchasingEndpoints();
app.MapPurchaseServiceOrderEndpointsV1();
app.MapPurchaseAdditionalCostEndpointsV1();
app.MapPurchasingProductCandidateEndpoints();
app.MapAccountingEndpoints();
app.MapAccountingSourceVouchersV1();
app.MapAccountingInvoiceLineCogsEndpoints();
app.MapAccountingPostingMappingWorkspaceV1();
app.MapAccountingInventoryCostsV1();
app.MapGlobalSearchEndpoints();
app.MapDocumentEndpoints();
app.MapDocumentBrandingEndpoints();
app.MapSettingsEndpoints();
app.MapSettingsTaxMasterEndpoints();
app.MapSettingsSalesPolicyEndpoints();
app.MapSettingsUnitConversionPolicyEndpoints();
app.MapSettingsUnitProfileEndpoints();
app.MapSettingsMoneyProfileEndpoints();
app.MapSettingsSegregationOfDutiesPolicyEndpoints();
app.MapSettingsCompanyPolicyEndpoints();
app.MapSettingsFinancePolicyEndpoints();
app.MapSettingsLoginRateLimitEndpoints();
app.MapSettingsWorkCenterCapacityEndpoints();
app.MapSettingsReferenceListEndpoints();
app.MapSettingsPurchaseInvoiceVariancePolicyEndpoints();
app.MapSettingsWarehouseTransferScaleEndpoints();
app.MapSettingsCollateralTypeEndpoints();
app.MapSettingsBankCsvMappingEndpoints();
app.MapSettingsSalesQuoteAndEInvoicePolicyEndpoints();
app.MapSettingsExchangeRateEndpoints();
app.MapSettingsManufacturingProfileEndpoints();
app.MapShellEndpoints();

if (!isOpenApiDocumentGeneration
    && databaseStartupMode == DatabaseStartupMode.Initialize
    && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
{
    await app.Services.InitializeOrganizationModuleAsync();
    await app.Services.InitializeSettingsModuleAsync();
    await app.Services.InitializeIdentityModuleAsync();
    await app.Services.InitializeAuditModuleAsync();
    await app.Services.InitializeSecurityEventsModuleAsync();
    await app.Services.InitializeIdempotencyModuleAsync();
    await app.Services.InitializeCounterpartyModuleAsync();
    // After Organization: Inventory's composite foreign keys reference its tables.
    await app.Services.InitializeInventoryModuleAsync();
    await app.Services.InitializeProductRuntimeAsync();
    // After Organization and Product: price-list rows reference both owner tables.
    if (app.Services.GetRequiredService<ProductRuntimeOptions>().IsEnabled)
    {
        await app.Services.InitializeProductPriceListsAsync();
    }
    // After Organization: Finance's foreign keys reference its tables.
    await app.Services.InitializeFinancePersistenceAsync();
    // After Organization: Sales's foreign keys reference its tables.
    await app.Services.InitializeSalesModuleAsync();
    // After Organization: Manufacturing's foreign keys reference its tables.
    await app.Services.InitializeManufacturingModuleAsync();
    // After Organization: Logistics's foreign keys reference its tables.
    await app.Services.InitializeLogisticsModuleAsync();
    // After Organization: Purchasing's foreign keys reference its tables.
    await app.Services.InitializePurchasingModuleAsync();
    // After Organization: Accounting's foreign keys reference its tables.
    await app.Services.InitializeAccountingModuleAsync();
    // After Organization: Work's tables are tenant-scoped by CompanyId.
    await app.Services.InitializeWorkModuleAsync();
    // After Organization: the report centre's tables are tenant-scoped by CompanyId.
    await app.Services.InitializeReportingModuleAsync();
    // After Organization: the document vault's foreign keys reference its tables.
    await app.Services.InitializeDocumentsModuleAsync();
}

// ID-2/ID-2r operator commands (identity bootstrap-founder, identity recover-founder): run once and exit; the web host
// is never started.
if (IdentityCommandLine.IsIdentityCommand(args))
{
    return await IdentityCommandLine.RunAsync(
        app.Services,
        args,
        new ConsoleFounderBootstrapConsole(Console.Out),
        new ConsoleFounderRecoveryConsole(Console.Out, app.Environment.EnvironmentName),
        Console.Out,
        Console.Error,
        CancellationToken.None,
        operatorRecoveryConsole: new ConsoleLocalTwoFactorOperatorRecoveryConsoleV1(Console.Out, app.Environment.EnvironmentName));
}

await app.RunAsync();
return IdentityCommandLine.ExitCompleted;

public partial class Program;
