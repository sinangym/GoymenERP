using GoymenERP.Api.Contracts;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Observability;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Idempotency.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Manufacturing.Infrastructure;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var isOpenApiDocumentGeneration = OpenApiDocumentGenerationMode.IsActive;

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
builder.Services.AddHealthChecks();
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
builder.Services.AddFinanceServices();
builder.Services.AddScoped<IFinancePartyAccountPort, CounterpartyFinancePartyAccountPort>();
builder.Services.AddScoped<DraftReadContextReader>();
builder.Services.AddScoped<IFinanceDraftContextEndpointService, FinanceDraftContextEndpointService>();
builder.Services.AddSingleton<IFinanceDraftReadBinding, ManualReceiptDraftReadBinding>();
builder.Services.AddScoped<ISettingsPrerequisiteProvider, ApprovedSettingsPrerequisiteProvider>();
builder.Services.AddScoped<ISettingsMoneyCandidateReader, ApprovedSettingsPrerequisiteProvider>();
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
    services.GetRequiredService<SalesReferenceReadBindings>());
builder.Services.AddIdempotencyModule(builder.Configuration);
builder.Services.AddOrganizationModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddProductRuntime(builder.Configuration, builder.Environment);
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
builder.Services.AddManufacturingModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddSalesModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
// Registered before the shell authority is chosen: the module table maps the provider set.
builder.Services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
// FIN-UI-1a (#325): Finance's (staged) receipt and bank-account screens, before the authority is chosen.
builder.Services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();
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
app.MapInventoryEndpoints();
app.MapManufacturingEndpoints();
app.MapShellEndpoints();

if (!isOpenApiDocumentGeneration
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
}

await app.RunAsync();

public partial class Program;
