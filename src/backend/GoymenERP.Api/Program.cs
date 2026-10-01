using GoymenERP.Api.Contracts;
using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Api.Search;
using GoymenERP.Api.Observability;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.Api.Startup;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure;
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
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddErpProblemDetails();
builder.Services.AddErpOpenApi();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseSchemaCompatibilityHealthCheck>(
        DatabaseSchemaCompatibilityHealthCheck.Name,
        tags: [DatabaseSchemaCompatibilityHealthCheck.ReadinessTag]);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<ShellCapabilityGenerationProtector>();
builder.Services.AddSingleton<ShellCapabilityRegistry>();
builder.Services.AddScoped<IActorSessionBindingResolver, ActorSessionBindingResolver>();
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
builder.Services.AddScoped<IFinanceRecordPeriodRule, OccurredOnFinanceRecordPeriodRule>();
builder.Services.AddFinanceServices();
builder.Services.AddScoped<IFinancePartyAccountPort, CounterpartyFinancePartyAccountPort>();
builder.Services.AddScoped<DraftReadContextReader>();
builder.Services.AddScoped<IFinanceDraftContextEndpointService, FinanceDraftContextEndpointService>();
builder.Services.AddSingleton<IFinanceDraftReadBinding, ManualReceiptDraftReadBinding>();
builder.Services.AddScoped<ISettingsPrerequisiteProvider, ApprovedSettingsPrerequisiteProvider>();
builder.Services.AddScoped<ISettingsMoneyCandidateReader, ApprovedSettingsPrerequisiteProvider>();
builder.Services.AddScoped<ISettingsManufacturingToleranceProviderV1, ApprovedSettingsPrerequisiteProvider>();
// PRD-UNIT-1: the company-independent unit identities of the approved catalog (immutable source data).
builder.Services.AddSingleton<ISettingsUnitIdentityCatalogV1, ApprovedSettingsUnitIdentityCatalog>();
builder.Services.AddScoped<ISalesAccountSelectionSourceV1, CounterpartySalesAccountSelectionSourceV1>();
// SALES-API (#321): the Sales reference reads are bound to the draft form's permission (the Sales
// owner's decision), and the Cari account port serves the draft use case.
builder.Services.AddSingleton(new SalesReferenceReadBindings(
    AccountsPermission: SalesPermissionManifestV1.DraftsCreate,
    CurrencyPermission: SalesPermissionManifestV1.DraftsCreate,
    ProductPermission: SalesPermissionManifestV1.DraftsCreate,
    MoneyOperation: SalesMoneyOperations.OrderDraftV1));
builder.Services.AddScoped<ISalesPartyAccountPortV1, CounterpartySalesPartyAccountPortV1>();
// SALES-A2a: the same Cari adapter answers the contact-information port — one authorized owner read,
// not a second copy of the party's addresses and contacts in Sales.
builder.Services.AddScoped<ISalesPartyContactInformationPortV1, CounterpartySalesPartyAccountPortV1>();
builder.Services.AddScoped<ISalesReferenceReadService>(CreateSalesReferenceReadService);
builder.Services.AddAuditModule(builder.Configuration);

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
    services.GetService<ISalesPartyContactInformationPortV1>());
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
builder.Services.AddProductPurchaseSelection(p => p.GetRequiredService<ProductRuntimeOptions>().IsEnabled);
builder.Services.AddCounterpartyModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()),
    allowEphemeralCursorKey:
        builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Testing")
        || isOpenApiDocumentGeneration);
builder.Services.AddInventoryModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddInventoryParentRollReferences();
builder.Services.AddInventoryPhysicalAllocationReads();
// R19: the label genealogy writer/reader and the fenced split/merge command behind the published route.
builder.Services.AddInventoryLabelGenealogy();
// AT-037: the stock count's fenced record/decide commands and the label count read behind the published routes.
builder.Services.AddInventoryStockCounts();
// ST-02 scrap write-off: the fenced record/decide commands and the label scrap read behind the published routes.
builder.Services.AddInventoryStockScraps();
// INV-OPEN-1 part 2: the opening command's host ports — Organization's company-scoped location module read
// and Product's stock eligibility (the owners' contracts; no user read, no copy of either owner's rows).
builder.Services.AddScoped<IInventoryLocationStatusPort, OrganizationInventoryLocationStatusPort>();
builder.Services.AddScoped<IInventoryProductStockPort, ProductInventoryStockPort>();
builder.Services.AddManufacturingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// MFG realization v1: the host composes the approved Manufacturing ports with their API-owned adapters.
builder.Services.AddScoped<IManufacturingInventoryOutputAdmission, ManufacturingInventoryOutputAdmission>();
builder.Services.AddScoped<IManufacturingInventoryInputIssuePortV1, ManufacturingInventoryInputIssueHost>();
builder.Services.AddScoped<IManufacturingInventoryReturnOutputPortV1, ManufacturingInventoryReturnOutputHost>();
builder.Services.AddScoped<IManufacturingOutputSelectionV1, ManufacturingOutputSelectionReader>();
builder.Services.AddSalesModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// STK/T11: the host-owned allocation boundary consumes Sales only through its approved-line port and
// hands the derived command to Inventory's writer after fresh company-wide authority fences.
builder.Services.AddScoped<InventoryPhysicalAllocationHostService>();
// LOG-4a: the loading instruction's customer is the Cari owner's answer (the Sales party port's shape) and its
// order lines are Sales's approved-line port (registered by the Sales module); nothing of either is copied.
builder.Services.AddScoped<ILogisticsPartyAccountPortV1, CounterpartyLogisticsPartyAccountPortV1>();
builder.Services.AddScoped<ILogisticsRealizationTransactionRunnerV1, LogisticsRealizationTransactionRunner>();
builder.Services.AddLogisticsModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// PUR-4a: the order's supplier is the Cari owner's answer (the Sales party port's shape, plus the account's stored
// Supplier role flag). No purchase-eligibility port is registered: Product has not published one (ÜRÜN #288 P3-3),
// so the service answers 503 product_eligibility_unavailable fail-closed until the owner registers it.
builder.Services.AddScoped<IPurchasingPartyAccountPortV1, CounterpartyPurchasingPartyAccountPortV1>();
builder.Services.AddScoped<IProductPurchaseEligibilityPortV1, ProductPurchaseEligibilityPort>();
builder.Services.AddSingleton(new PurchasingServiceBindings(
    GoodsReceiptPermission: ErpPermissions.PurchasingGoodsReceiptsRecord));
builder.Services.AddScoped<PurchaseGoodsReceiptHost>();
builder.Services.AddScoped<IPurchaseGoodsReceiptTransactionV1>(provider => provider.GetRequiredService<PurchaseGoodsReceiptHost>());
builder.Services.AddScoped<IPurchaseGoodsReceiptReadsV1>(provider => provider.GetRequiredService<PurchaseGoodsReceiptHost>());
builder.Services.AddPurchasingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// ACC-V0a: Accounting's periods, journals and posting queues (its verifier reads Settings' amount profile through
// the Settings prerequisite provider the host already registers; no shell screen until ACC-V0b).
builder.Services.AddAccountingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// ERP-000G / AT-065: the document vault (audit interceptor, the Cari party owner link; the development object store and
// EICAR-only scanner only in Development/Testing; elsewhere the own-server store and Windows Defender scanner when both
// are configured, otherwise uploads and downloads answer 503, fail-closed).
builder.Services.AddDocumentsRuntime(builder.Configuration, builder.Environment);
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
// PUR-4a: Purchasing's (staged) order screens, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, PurchasingShellCapabilityProvider>();
// ACC-V0b: Accounting's (staged) period, journal and posting-queue screens, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, AccountingShellCapabilityProvider>();
// SY-01/SY-02: Identity's read-only users-and-permissions view (identity.assignments.read), before the authority
// is chosen. No control: assigning and revoking stay blocked (NEEDS-SINAN #66 5738113623, OPEN-013).
builder.Services.AddSingleton<IShellCapabilityProvider, IdentityShellCapabilityProvider>();
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
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();

app.MapErpHealthEndpoints();
app.MapOrganizationEndpoints();
app.MapIdentityEndpoints();
app.MapAuditEndpoints();
app.MapCounterpartyEndpoints();
app.MapProductEndpoints();
app.MapFinanceEndpoints();
app.MapSalesEndpoints();
app.MapSalesProductCandidateEndpoints();
app.MapInventoryEndpoints();
app.MapManufacturingEndpoints();
app.MapLogisticsEndpoints();
app.MapPurchasingEndpoints();
app.MapPurchasingProductCandidateEndpoints();
app.MapAccountingEndpoints();
app.MapGlobalSearchEndpoints();
app.MapDocumentEndpoints();
app.MapShellEndpoints();

if (!isOpenApiDocumentGeneration
    && databaseStartupMode == DatabaseStartupMode.Initialize
    && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
{
    await app.Services.InitializeOrganizationModuleAsync();
    await app.Services.InitializeIdentityModuleAsync();
    await app.Services.InitializeAuditModuleAsync();
    await app.Services.InitializeIdempotencyModuleAsync();
    await app.Services.InitializeCounterpartyModuleAsync();
    // After Organization: Inventory's composite foreign keys reference its tables.
    await app.Services.InitializeInventoryModuleAsync();
    await app.Services.InitializeProductRuntimeAsync();
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
    // After Organization: the document vault's foreign keys reference its tables.
    await app.Services.InitializeDocumentsModuleAsync();
}

await app.RunAsync();

public partial class Program;
