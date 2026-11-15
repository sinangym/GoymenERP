using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Security;

public static class ErpAuthenticationExtensions
{
    /// <summary>The policy scheme that forwards to one of the enabled providers when more than one is on (ID-6).</summary>
    public const string ProviderSelectorScheme = "GoymenErpProviderSelector";

    public static IServiceCollection AddErpAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<BootstrapIdentityOptions>(
            configuration.GetSection(BootstrapIdentityOptions.SectionName));
        LocalAccountStartupGuard.EnsureDevelopmentWeakPasswordSource(configuration, environment);

        // The local account parts are registered in every mode so the service graph stays complete; they are built
        // only when a login or session request reaches them, and those requests exist only while the LocalAccount scheme
        // is enabled.
        services.AddSingleton<ILocalPasswordHasher, LocalAccountPasswordHasher>();
        services.AddSingleton<LocalAccountLoginRateLimiter>();

        // A scalar value (for example an environment variable holding "LocalAccount,OidcJwtBearer") would bind to an
        // empty list and silently fall back to Mode, so it is refused before binding.
        var providersSection = configuration.GetSection(
            $"{ErpAuthenticationOptions.SectionName}:{ErpAuthenticationOptions.ProvidersKey}");
        if (!string.IsNullOrWhiteSpace(providersSection.Value))
        {
            throw new InvalidOperationException(
                "Authentication:Providers bir liste olmalıdır (örn. Authentication:Providers:0, Authentication:Providers:1).");
        }

        var settings = configuration
            .GetSection(ErpAuthenticationOptions.SectionName)
            .Get<ErpAuthenticationOptions>() ?? new ErpAuthenticationOptions();
        var providers = ResolveProviders(settings, configuration, environment);

        if (providers is [AuthenticationModes.DevelopmentBootstrap])
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException(
                    "DevelopmentBootstrap yalnız Development ve Testing ortamlarında kullanılabilir.");
            }

            var bootstrapIdentityOptions = configuration
                .GetSection(BootstrapIdentityOptions.SectionName)
                .Get<BootstrapIdentityOptions>() ?? new BootstrapIdentityOptions();
            ValidateDevelopmentBootstrapHeaders(bootstrapIdentityOptions);

            services
                .AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                    DevelopmentAuthenticationHandler.SchemeName,
                    _ => { });
        }
        else
        {
            // ID-6: one provider is the default scheme itself; two providers sit behind a policy scheme that picks
            // the bearer handler when the request carries an Authorization header and the local session cookie
            // otherwise. A request with both never falls back to the cookie when its bearer token is refused.
            var defaultScheme = providers.Count == 1
                ? SchemeNameFor(providers[0])
                : ProviderSelectorScheme;
            var authentication = services.AddAuthentication(defaultScheme);
            foreach (var provider in providers)
            {
                if (string.Equals(provider, AuthenticationModes.OidcJwtBearer, StringComparison.Ordinal))
                {
                    AddOidcJwtBearer(authentication, settings.Oidc, environment);
                }
                else
                {
                    AddLocalAccount(authentication, configuration);
                }
            }

            if (providers.Count > 1)
            {
                authentication.AddPolicyScheme(
                    ProviderSelectorScheme,
                    displayName: null,
                    options => options.ForwardDefaultSelector = SelectProviderScheme);
            }
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

    /// <summary>
    /// Returns the canonical, de-duplicated provider list. A non-empty <c>Authentication:Providers</c> list wins over
    /// <c>Authentication:Mode</c> in every environment; it never names DevelopmentBootstrap, which stays a
    /// Development/Testing-only <c>Mode</c>. Without a list the single <c>Mode</c> rule is unchanged.
    /// </summary>
    private static IReadOnlyList<string> ResolveProviders(
        ErpAuthenticationOptions settings,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (settings.Providers.Length == 0)
        {
            var mode = environment.IsEnvironment("Testing")
                ? IsExplicitLocalAccountMode(configuration)
                    ? AuthenticationModes.LocalAccount
                    : AuthenticationModes.DevelopmentBootstrap
                : string.IsNullOrWhiteSpace(settings.Mode)
                    ? AuthenticationModes.OidcJwtBearer
                    : settings.Mode.Trim();
            return [CanonicalSingleMode(mode)];
        }

        var providers = new List<string>(settings.Providers.Length);
        foreach (var entry in settings.Providers)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new InvalidOperationException(
                    "Authentication:Providers boş bir öğe içeremez.");
            }

            var candidate = entry.Trim();
            if (string.Equals(
                    candidate,
                    AuthenticationModes.DevelopmentBootstrap,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "DevelopmentBootstrap Authentication:Providers listesinde kullanılamaz; yalnız Authentication:Mode olarak Development ve Testing ortamlarında seçilebilir.");
            }

            var canonical = CanonicalProvider(candidate)
                ?? throw new InvalidOperationException($"Desteklenmeyen kimlik modu: {candidate}");
            if (providers.Contains(canonical, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Authentication:Providers aynı kimlik modunu birden çok kez içeremez: {canonical}");
            }

            providers.Add(canonical);
        }

        return providers;
    }

    private static string CanonicalSingleMode(string mode)
    {
        if (string.Equals(
                mode,
                AuthenticationModes.DevelopmentBootstrap,
                StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationModes.DevelopmentBootstrap;
        }

        return CanonicalProvider(mode)
            ?? throw new InvalidOperationException($"Desteklenmeyen kimlik modu: {mode}");
    }

    private static string? CanonicalProvider(string value)
    {
        if (string.Equals(value, AuthenticationModes.OidcJwtBearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationModes.OidcJwtBearer;
        }

        if (string.Equals(value, AuthenticationModes.LocalAccount, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationModes.LocalAccount;
        }

        return null;
    }

    private static string SchemeNameFor(string provider) =>
        string.Equals(provider, AuthenticationModes.OidcJwtBearer, StringComparison.Ordinal)
            ? JwtBearerDefaults.AuthenticationScheme
            : LocalAccountSession.SchemeName;

    private static string SelectProviderScheme(HttpContext context) =>
        context.Request.Headers.ContainsKey(HeaderNames.Authorization)
            ? JwtBearerDefaults.AuthenticationScheme
            : LocalAccountSession.SchemeName;

    private static void AddOidcJwtBearer(
        AuthenticationBuilder authentication,
        OidcJwtBearerOptions oidc,
        IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(oidc.Authority)
            || string.IsNullOrWhiteSpace(oidc.Audience))
        {
            throw new InvalidOperationException(
                "OIDC/JWT için Authentication:Oidc:Authority ve Audience zorunludur.");
        }

        var authority = oidc.Authority.Trim();
        if (string.Equals(
                authority.TrimEnd('/'),
                LocalAccountSession.Issuer,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "OIDC/JWT Authority yerel hesap issuer değeriyle aynı olamaz.");
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            if (!IsSafeHttpsAuthority(authority))
            {
                throw new InvalidOperationException(
                    "OIDC/JWT Authority, Development ve Testing dışındaki ortamlarda kullanıcı bilgisi, sorgu veya parça içermeyen mutlak ve iyi biçimlendirilmiş bir HTTPS adresi olmalıdır.");
            }

            if (!oidc.RequireHttpsMetadata)
            {
                throw new InvalidOperationException(
                    "OIDC/JWT için RequireHttpsMetadata, Development ve Testing dışındaki ortamlarda true olmalıdır.");
            }
        }

        authentication.AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = oidc.Audience;
            options.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
            options.IncludeErrorDetails = false;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                // The identity carries the scheme name so the claims transformation can tell a bearer identity
                // from the local session identity (ID-6).
                AuthenticationType = JwtBearerDefaults.AuthenticationScheme,
                NameClaimType = "name",
                RoleClaimType = "roles",
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
            };
        });
    }

    private static void AddLocalAccount(
        AuthenticationBuilder authentication,
        IConfiguration configuration)
    {
        var localAccountOptions = configuration
            .GetSection(LocalAccountOptions.SectionName)
            .Get<LocalAccountOptions>() ?? new LocalAccountOptions();
        localAccountOptions.EnsureValid();

        authentication.AddCookie(
            LocalAccountSession.SchemeName,
            options => LocalAccountSession.Configure(options, localAccountOptions.Session));
        // KIM-D31: the antiforgery header on the login and on every cookie-authenticated write.
        authentication.Services.AddLocalAccountAntiforgery();
    }

    /// <summary>
    /// Without a provider list, Testing hosts use DevelopmentBootstrap unless a test asks for the local account mode by
    /// name; no other <c>Mode</c> can be chosen there. A test that needs OIDC names it in the provider list.
    /// </summary>
    private static bool IsExplicitLocalAccountMode(IConfiguration configuration) =>
        string.Equals(
            configuration[$"{ErpAuthenticationOptions.SectionName}:Mode"]?.Trim(),
            AuthenticationModes.LocalAccount,
            StringComparison.Ordinal);

    private static void ValidateDevelopmentBootstrapHeaders(BootstrapIdentityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            throw new InvalidOperationException(
                "DevelopmentBootstrap için BootstrapIdentity:HeaderName zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(options.SessionHeaderName))
        {
            throw new InvalidOperationException(
                "DevelopmentBootstrap için BootstrapIdentity:SessionHeaderName zorunludur.");
        }

        if (string.Equals(
                options.HeaderName,
                options.SessionHeaderName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DevelopmentBootstrap kullanıcı ve oturum header adları farklı olmalıdır.");
        }
    }

    private static bool IsSafeHttpsAuthority(string authority)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
            || !authorityUri.IsWellFormedOriginalString()
            || !string.Equals(
                authorityUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorityUri.Host)
            || !string.IsNullOrEmpty(authorityUri.UserInfo)
            || !string.IsNullOrEmpty(authorityUri.Query)
            || !string.IsNullOrEmpty(authorityUri.Fragment))
        {
            return false;
        }

        var authoritySeparatorIndex = authority.IndexOf("://", StringComparison.Ordinal);
        if (authoritySeparatorIndex < 0)
        {
            return false;
        }

        var authorityComponentStart = authoritySeparatorIndex + 3;
        var authorityComponentEnd = authority.IndexOfAny(
            ['/', '?', '#'],
            authorityComponentStart);
        var authorityComponentLength = authorityComponentEnd < 0
            ? authority.Length - authorityComponentStart
            : authorityComponentEnd - authorityComponentStart;

        return !authority.AsSpan(authorityComponentStart, authorityComponentLength).Contains('@');
    }
}
