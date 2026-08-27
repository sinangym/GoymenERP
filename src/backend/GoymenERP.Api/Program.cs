using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "Seed/organization-seed.json",
    optional: false,
    reloadOnChange: false);
builder.Configuration.AddJsonFile(
    "Seed/identity-seed.json",
    optional: false,
    reloadOnChange: false);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddOrganizationModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
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
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapOrganizationEndpoints();
app.MapIdentityEndpoints();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await app.Services.InitializeOrganizationModuleAsync();
    await app.Services.InitializeIdentityModuleAsync();
}

await app.RunAsync();

public partial class Program;
