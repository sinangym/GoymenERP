using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Infrastructure;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "Seed/organization-seed.json",
    optional: false,
    reloadOnChange: false);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.Configure<BootstrapIdentityOptions>(
    builder.Configuration.GetSection(BootstrapIdentityOptions.SectionName));
builder.Services
    .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddOrganizationModule(builder.Configuration);

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
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapOrganizationEndpoints();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await app.Services.InitializeOrganizationModuleAsync();
}

await app.RunAsync();

public partial class Program;
