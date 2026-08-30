using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Observability;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Idempotency.Infrastructure;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "Seed/organization-seed.json",
    optional: false,
    reloadOnChange: false);
builder.Configuration.AddJsonFile(
    "Seed/identity-seed.json",
    optional: false,
    reloadOnChange: false);

builder.Services.AddErpProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddIdempotencyModule(builder.Configuration);
builder.Services.AddOrganizationModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCounterpartyModule(
    builder.Configuration,
    (services, options) => options.AddInterceptors(
        services.GetRequiredService<AuditSaveChangesInterceptor>()));
builder.Services.AddErpAuthentication(builder.Configuration, builder.Environment);

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationContextMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapOrganizationEndpoints();
app.MapIdentityEndpoints();
app.MapAuditEndpoints();
app.MapCounterpartyEndpoints();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await app.Services.InitializeOrganizationModuleAsync();
    await app.Services.InitializeIdentityModuleAsync();
    await app.Services.InitializeAuditModuleAsync();
    await app.Services.InitializeIdempotencyModuleAsync();
    await app.Services.InitializeCounterpartyModuleAsync();
}

await app.RunAsync();

public partial class Program;
