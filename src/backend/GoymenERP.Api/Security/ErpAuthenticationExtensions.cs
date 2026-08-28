using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace GoymenERP.Api.Security;

public static class ErpAuthenticationExtensions
{
    public static IServiceCollection AddErpAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<BootstrapIdentityOptions>(
            configuration.GetSection(BootstrapIdentityOptions.SectionName));

        var settings = configuration
            .GetSection(ErpAuthenticationOptions.SectionName)
            .Get<ErpAuthenticationOptions>() ?? new ErpAuthenticationOptions();
        var mode = environment.IsEnvironment("Testing")
            ? AuthenticationModes.DevelopmentBootstrap
            : string.IsNullOrWhiteSpace(settings.Mode)
                ? AuthenticationModes.OidcJwtBearer
                : settings.Mode.Trim();

        if (string.Equals(
                mode,
                AuthenticationModes.DevelopmentBootstrap,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "DevelopmentBootstrap yalnız Development ve Testing ortamlarında kullanılabilir.");
            }

            services
                .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                    DevelopmentAuthenticationHandler.SchemeName,
                    _ => { });
        }
        else if (string.Equals(
                     mode,
                     AuthenticationModes.OidcJwtBearer,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.Oidc.Authority)
                || string.IsNullOrWhiteSpace(settings.Oidc.Audience))
            {
                throw new InvalidOperationException(
                    "OIDC/JWT için Authentication:Oidc:Authority ve Audience zorunludur.");
            }

            var authority = settings.Oidc.Authority.Trim();
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
                    || !string.Equals(
                        authorityUri.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(authorityUri.Host)
                    || !string.IsNullOrEmpty(authorityUri.UserInfo)
                    || !string.IsNullOrEmpty(authorityUri.Query)
                    || !string.IsNullOrEmpty(authorityUri.Fragment))
                {
                    throw new InvalidOperationException(
                        "OIDC/JWT Authority, Development ve Testing dışındaki ortamlarda kullanıcı bilgisi, sorgu veya parça içermeyen mutlak bir HTTPS adresi olmalıdır.");
                }

                if (!settings.Oidc.RequireHttpsMetadata)
                {
                    throw new InvalidOperationException(
                        "OIDC/JWT için RequireHttpsMetadata, Development ve Testing dışındaki ortamlarda true olmalıdır.");
                }
            }

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.Audience = settings.Oidc.Audience;
                    options.RequireHttpsMetadata = settings.Oidc.RequireHttpsMetadata;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = "name",
                        RoleClaimType = "roles",
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                    };
                });
        }
        else
        {
            throw new InvalidOperationException($"Desteklenmeyen kimlik modu: {mode}");
        }

        services.AddScoped<IClaimsTransformation, ErpClaimsTransformation>();
        services.AddScoped<IAuthorizationHandler, ErpPermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            var authenticatedErpUser = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(GoymenClaimTypes.UserId)
                .Build();
            options.DefaultPolicy = authenticatedErpUser;
            options.FallbackPolicy = authenticatedErpUser;

            foreach (var permission in ErpPermissions.All)
            {
                options.AddPolicy(
                    permission,
                    policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(GoymenClaimTypes.UserId)
                        .AddRequirements(new ErpPermissionRequirement(permission)));
            }
        });

        return services;
    }
}
