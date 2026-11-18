using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-6 <c>Authentication:Providers</c>: the list validation at startup, the registered schemes, and KS-12 over HTTP
/// (an OIDC token is refused while only <c>LocalAccount</c> is on, and resolves through <c>ExternalIdentityLink</c>
/// once OIDC is enabled). SENTETIK issuer, subject and users only; no remote discovery.
/// </summary>
public sealed class AuthenticationProvidersTests
{
    private const string Authority = "https://identity.example.test";
    private const string Audience = "goymen-erp-api";
    private const string LoginPath = "/api/v1/identity/session/login";
    private const string AntiforgeryPath = "/api/v1/identity/session/antiforgery";
    private const string MePath = "/api/v1/identity/me";

    [Fact]
    public void A_scalar_providers_value_is_refused_instead_of_falling_back_to_the_mode()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["Authentication:Providers"] = "LocalAccount,OidcJwtBearer",
            });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddErpAuthentication(configuration, new FakeHostEnvironment("Production")));

        Assert.Contains("bir liste olmalıdır", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" ", null, "boş bir öğe içeremez")]
    [InlineData("DevelopmentBootstrap", null, "Providers listesinde kullanılamaz")]
    [InlineData("LocalAccount", "developmentbootstrap", "Providers listesinde kullanılamaz")]
    [InlineData("Saml", null, "Desteklenmeyen kimlik modu: Saml")]
    [InlineData("LocalAccount", "localaccount", "birden çok kez içeremez")]
    public void An_invalid_providers_list_is_refused_at_startup(string first, string? second, string expected)
    {
        var values = LocalAccountSettings();
        values["Authentication:Providers:0"] = first;
        if (second is not null)
        {
            values["Authentication:Providers:1"] = second;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddErpAuthentication(
                CreateConfiguration(values),
                new FakeHostEnvironment("Testing")));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_local_account_only_list_registers_no_bearer_scheme_even_when_the_mode_names_oidc()
    {
        var values = LocalAccountSettings();
        values["Authentication:Mode"] = AuthenticationModes.OidcJwtBearer;
        values["Authentication:Oidc:Authority"] = Authority;
        values["Authentication:Oidc:Audience"] = Audience;
        values["Authentication:Providers:0"] = AuthenticationModes.LocalAccount;
        var services = new ServiceCollection();

        services.AddErpAuthentication(CreateConfiguration(values), new FakeHostEnvironment("Production"));

        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal("LocalAccount", (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Null(await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));
        Assert.Null(await schemes.GetSchemeAsync(ErpAuthenticationExtensions.ProviderSelectorScheme));
    }

    [Fact]
    public async Task Both_providers_register_both_schemes_behind_the_selector_default()
    {
        var values = LocalAccountSettings();
        values["Authentication:Oidc:Authority"] = Authority;
        values["Authentication:Oidc:Audience"] = Audience;
        values["Authentication:Providers:0"] = "localaccount";
        values["Authentication:Providers:1"] = " OidcJwtBearer ";
        var services = new ServiceCollection();

        services.AddErpAuthentication(CreateConfiguration(values), new FakeHostEnvironment("Production"));

        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal(
            ErpAuthenticationExtensions.ProviderSelectorScheme,
            (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Equal(
            ErpAuthenticationExtensions.ProviderSelectorScheme,
            (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
        Assert.NotNull(await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));
        Assert.NotNull(await schemes.GetSchemeAsync("LocalAccount"));
    }

    [Fact]
    public void An_enabled_oidc_provider_still_needs_authority_and_audience()
    {
        var values = LocalAccountSettings();
        values["Authentication:Providers:0"] = AuthenticationModes.LocalAccount;
        values["Authentication:Providers:1"] = AuthenticationModes.OidcJwtBearer;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddErpAuthentication(
                CreateConfiguration(values),
                new FakeHostEnvironment("Production")));

        Assert.Contains("Authority ve Audience zorunludur", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("urn:goymen:local-account")]
    [InlineData("URN:GOYMEN:LOCAL-ACCOUNT/")]
    public void An_oidc_authority_equal_to_the_local_issuer_is_refused(string authority)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Providers:0"] = AuthenticationModes.OidcJwtBearer,
            ["Authentication:Oidc:Authority"] = authority,
            ["Authentication:Oidc:Audience"] = Audience,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddErpAuthentication(
                CreateConfiguration(values),
                new FakeHostEnvironment("Production")));

        Assert.Contains("yerel hesap issuer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KS12_an_oidc_token_gets_401_while_only_the_local_account_provider_is_on()
    {
        await using var factory = new AuthenticationProvidersApiFactory(AuthenticationModes.LocalAccount);
        var subject = $"sentetik-dis-kimlik-{Guid.NewGuid():N}";
        await factory.SeedExternalUserAsync(Authority, subject);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.IssueToken(subject));

        using var me = await client.GetAsync(MePath);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task KS12_the_same_oidc_token_resolves_the_linked_user_once_oidc_is_enabled()
    {
        await using var factory = new AuthenticationProvidersApiFactory(
            AuthenticationModes.LocalAccount,
            AuthenticationModes.OidcJwtBearer);
        var subject = $"sentetik-dis-kimlik-{Guid.NewGuid():N}";
        var userId = await factory.SeedExternalUserAsync(Authority, subject);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.IssueToken(subject));

        using var me = await client.GetAsync(MePath);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(userId.ToString("D"), body.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task An_unlinked_oidc_subject_stays_without_a_local_user()
    {
        await using var factory = new AuthenticationProvidersApiFactory(
            AuthenticationModes.LocalAccount,
            AuthenticationModes.OidcJwtBearer);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.IssueToken($"sentetik-bagsiz-{Guid.NewGuid():N}"));

        using var me = await client.GetAsync(MePath);

        Assert.True(
            me.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Beklenmeyen durum: {(int)me.StatusCode}");
    }

    [Fact]
    public async Task With_both_providers_local_login_works_and_a_refused_bearer_never_falls_back_to_the_cookie()
    {
        await using var factory = new AuthenticationProvidersApiFactory(
            AuthenticationModes.LocalAccount,
            AuthenticationModes.OidcJwtBearer);
        var password = "S-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var userId = await factory.SeedLocalUserAsync("sentetik.coklu", password);
        using var client = factory.CreateClient();

        using var antiforgery = await client.GetAsync(AntiforgeryPath);
        Assert.Equal(HttpStatusCode.OK, antiforgery.StatusCode);
        using var tokenBody = JsonDocument.Parse(await antiforgery.Content.ReadAsStringAsync());
        var token = Assert.IsType<string>(tokenBody.RootElement.GetProperty("requestToken").GetString());
        Assert.NotEmpty(token);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = JsonContent.Create(new { userName = "sentetik.coklu", password }),
        };
        loginRequest.Headers.Add(LocalAccountAntiforgery.HeaderName, token);

        using (var login = await client.SendAsync(loginRequest))
        {
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        using (var me = await client.GetAsync(MePath))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
            Assert.Equal(userId.ToString("D"), body.RootElement.GetProperty("userId").GetString());
        }

        using var withInvalidBearer = new HttpRequestMessage(HttpMethod.Get, MePath);
        withInvalidBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sentetik-gecersiz-token");
        using var refused = await client.SendAsync(withInvalidBearer);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static Dictionary<string, string?> LocalAccountSettings() =>
        new(AuthenticationProvidersApiFactory.LocalAccountSettings());

    private static IConfiguration CreateConfiguration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = nameof(AuthenticationProvidersTests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// An in-process host with the given provider list over in-memory databases. The OIDC metadata is a static
    /// SENTETIK configuration with a per-run symmetric key, so no discovery request leaves the process.
    /// </summary>
    private sealed class AuthenticationProvidersApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"goymen-erp-auth-providers-{Guid.NewGuid():N}";
        private readonly string[] providers;
        private readonly SymmetricSecurityKey signingKey = new(RandomNumberGenerator.GetBytes(32));

        public AuthenticationProvidersApiFactory(params string[] providers)
        {
            this.providers = providers;
            // The local session cookie is Secure and __Host- prefixed; the client must talk HTTPS to send it back.
            ClientOptions.BaseAddress = new Uri("https://localhost");
        }

        public static IReadOnlyDictionary<string, string?> LocalAccountSettings() =>
            new Dictionary<string, string?>
            {
                ["Identity:LocalAccount:Lockout:MaxFailedAttempts"] = "3",
                ["Identity:LocalAccount:Lockout:Duration"] = TimeSpan.FromMinutes(15).ToString("c"),
                ["Identity:LocalAccount:Session:IdleTimeout"] = TimeSpan.FromMinutes(30).ToString("c"),
                ["Identity:LocalAccount:Session:AbsoluteLifetime"] = TimeSpan.FromHours(2).ToString("c"),
                ["Identity:LocalAccount:PasswordHashing:Algorithm"] = LocalAccountPasswordHashingSettings.Pbkdf2Algorithm,
                ["Identity:LocalAccount:PasswordHashing:Pbkdf2Iterations"] =
                    LocalAccountOptions.MinimumPbkdf2Iterations.ToString(CultureInfo.InvariantCulture),
                ["Identity:LocalAccount:PasswordPolicy:MinimumLength"] = "8",
                ["Identity:LocalAccount:PasswordPolicy:MaximumLength"] = "128",
                ["Identity:LocalAccount:PasswordPolicy:RequireCharacterClasses"] = "false",
                ["Identity:LocalAccount:PasswordPolicy:BlocklistEnabled"] = "true",
                ["Identity:LocalAccount:RateLimit:Login:PermitLimit"] = "100",
                ["Identity:LocalAccount:RateLimit:Login:Window"] = "00:01:00",
                ["Identity:LocalAccount:RateLimit:Login:PartitionBy"] =
                    nameof(LocalAccountLoginRateLimitPartition.IpAddressAndUserName),
            };

        public string IssueToken(string subject)
        {
            var now = DateTime.UtcNow;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Authority,
                Audience = Audience,
                Subject = new ClaimsIdentity([new Claim("sub", subject)]),
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(10),
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            });
        }

        public async Task<Guid> SeedExternalUserAsync(string issuer, string subject)
        {
            var userId = Guid.NewGuid();
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            dbContext.Users.Add(new UserAccount(userId, "Sentetik Dış Kimlik Kullanıcısı", email: null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), userId, issuer, subject));
            await dbContext.SaveChangesAsync();
            return userId;
        }

        public async Task<Guid> SeedLocalUserAsync(string userName, string password)
        {
            var userId = Guid.NewGuid();
            var hasher = Services.GetRequiredService<ILocalPasswordHasher>();
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            dbContext.Users.Add(new UserAccount(userId, "Sentetik Yerel Kullanıcı", email: null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
                Guid.NewGuid(),
                userId,
                LocalAccountSession.Issuer,
                userId.ToString("D")));
            dbContext.LocalCredentials.Add(new LocalCredential(
                userId,
                userName,
                hasher.Hash(password),
                hasher.FormatVersion,
                mustChangePassword: false,
                DateTimeOffset.UtcNow,
                Guid.NewGuid()));
            await dbContext.SaveChangesAsync();
            return userId;
        }

        protected override void ConfigureClient(HttpClient client)
        {
            base.ConfigureClient(client);
            client.DefaultRequestHeaders.Add(
                GoymenERP.Api.Contracts.CounterpartyClientContract.HeaderName,
                GoymenERP.Api.Contracts.CounterpartyClientContract.Version);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "DB_CONNECTION_STRING",
                "Server=testing;Database=testing;User Id=testing;Password=testing;");
            // Host settings, not ConfigureAppConfiguration: Program reads the authentication settings before the
            // factory's app-configuration callbacks run.
            builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command", "Warning");
            builder.UseSetting("Authentication:Oidc:Authority", Authority);
            builder.UseSetting("Authentication:Oidc:Audience", Audience);
            for (var index = 0; index < providers.Length; index++)
            {
                builder.UseSetting(
                    $"Authentication:Providers:{index.ToString(CultureInfo.InvariantCulture)}",
                    providers[index]);
            }

            foreach (var (key, value) in LocalAccountSettings())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                GoymenErpApiFactory.UseInMemoryDatabases(services, databaseName);
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
                        configuration.SigningKeys.Add(signingKey);
                        options.Configuration = configuration;
                        options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                        options.TokenValidationParameters.ValidIssuer = Authority;
                        options.TokenValidationParameters.IssuerSigningKey = signingKey;
                    });
            });
        }
    }
}
