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
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Idempotency.Infrastructure;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure;
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
builder.Services.AddScoped<IFinancePermissionPort, UnavailableFinancePermissionPort>();
builder.Services.AddScoped<IFinanceBankAccountReader, UnavailableFinanceBankAccountReader>();
builder.Services.AddScoped<IFinancePartyAccountPort, CounterpartyFinancePartyAccountPort>();
builder.Services.AddScoped<ISettingsPrerequisiteProvider, UnavailableSettingsPrerequisiteProvider>();
builder.Services.AddScoped<DraftReadContextReader>();
builder.Services.AddScoped<IFinanceDraftContextEndpointService, FinanceDraftContextEndpointService>();
builder.Services.AddSingleton<IFinanceDraftReadBinding, UnavailableFinanceDraftReadBinding>();
builder.Services.AddAuditModule(builder.Configuration);
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
var shellCapabilityAuthority = C01E2eShellCapabilityActivation.Configure(
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
app.MapShellEndpoints();

if (!isOpenApiDocumentGeneration
    && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
{
    await app.Services.InitializeOrganizationModuleAsync();
    await app.Services.InitializeIdentityModuleAsync();
    await app.Services.InitializeAuditModuleAsync();
    await app.Services.InitializeIdempotencyModuleAsync();
    await app.Services.InitializeCounterpartyModuleAsync();
    await app.Services.InitializeProductRuntimeAsync();
}

await app.RunAsync();

public partial class Program;
