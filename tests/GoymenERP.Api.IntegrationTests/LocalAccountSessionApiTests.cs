using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-1 local account login over HTTP: the session cookie, the uniform 401 refusal, logout, the per-request stamp
/// check, the idle and absolute session limits, the login rate limit and the closed mode, plus KIM-S24: the shipped
/// KIM-D29 rate limit, the KIM-D30 logout that renews the stamp and the KIM-D31 antiforgery header. SENTETIK users only.
/// </summary>
public sealed class LocalAccountSessionApiTests
{
    private const string LoginPath = "/api/v1/identity/session/login";
    private const string LogoutPath = "/api/v1/identity/session/logout";
    private const string AntiforgeryPath = "/api/v1/identity/session/antiforgery";
    private const string MePath = "/api/v1/identity/me";
    private const string UserName = "sentetik.yerel";
    // Generated per run (spec "Varsayılan kimlik bilgisi yasağı" item 1).
    private static readonly string Password = NewPassword();
    private static readonly string WrongPassword = NewPassword();

    [Fact]
    public async Task The_right_password_answers_204_with_a_host_only_secure_cookie_and_me_opens()
    {
        await using var factory = new LocalAccountApiFactory();
        var user = await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();

        using var login = await LoginAsync(client, UserName, Password);

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(SetCookies(login));
        Assert.StartsWith(LocalAccountSession.CookieName + "=", cookie, StringComparison.Ordinal);
        var attributes = cookie.ToLowerInvariant();
        Assert.Contains("; secure", attributes, StringComparison.Ordinal);
        Assert.Contains("; httponly", attributes, StringComparison.Ordinal);
        Assert.Contains("; samesite=strict", attributes, StringComparison.Ordinal);
        Assert.Contains("; path=/", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("expires=", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, cookie, StringComparison.Ordinal);
        Assert.True(login.Headers.CacheControl?.NoStore);

        using var me = await client.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(user.ToString("D"), body.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Unknown_user_wrong_password_and_inactive_user_get_the_same_401_without_a_cookie()
    {
        await using var factory = new LocalAccountApiFactory();
        await factory.SeedLocalUserAsync(UserName, Password);
        await factory.SeedLocalUserAsync("sentetik.pasif", Password, isActive: false);
        using var client = factory.CreateClient();

        var refusals = new List<(string Code, string Title)>();
        foreach (var (name, password) in new[]
                 {
                     ("sentetik.olmayan", Password),
                     (UserName, WrongPassword),
                     ("sentetik.pasif", Password),
                     (UserName, string.Empty),
                 })
        {
            using var response = await LoginAsync(client, name, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(SetCookies(response));
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            using var body = JsonDocument.Parse(text);
            refusals.Add((body.RootElement.GetProperty("code").GetString()!, body.RootElement.GetProperty("title").GetString()!));
        }

        Assert.All(refusals, refusal => Assert.Equal(("authentication_required", "Oturum gerekli."), refusal));
    }

    [Fact]
    public async Task Logout_ends_the_session_and_me_answers_401()
    {
        await using var factory = new LocalAccountApiFactory();
        await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        using (var login = await LoginAsync(client, UserName, Password))
        {
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        using var logout = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(SetCookies(logout), cookie => cookie.StartsWith(LocalAccountSession.CookieName + "=;", StringComparison.Ordinal));

        using var me = await client.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        // Without a session the write is not cookie-authenticated: authorization answers 401, not the antiforgery 400.
        using var logoutAgain = await client.PostAsync(LogoutPath, content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, logoutAgain.StatusCode);
    }

    [Fact]
    public async Task A_renewed_security_stamp_ends_an_open_session_on_the_next_request()
    {
        await using var factory = new LocalAccountApiFactory();
        var userId = await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        using (await LoginAsync(client, UserName, Password))
        {
        }

        await factory.UpdateCredentialAsync(userId, credential =>
            typeof(LocalCredential).GetProperty(nameof(LocalCredential.SecurityStamp))!.SetValue(credential, Guid.NewGuid()));

        using var me = await client.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task A_user_deactivated_after_login_loses_the_session()
    {
        await using var factory = new LocalAccountApiFactory();
        var userId = await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        using (await LoginAsync(client, UserName, Password))
        {
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await dbContext.Users.SingleAsync(x => x.Id == userId);
            typeof(UserAccount).GetProperty(nameof(UserAccount.IsActive))!.SetValue(user, false);
            await dbContext.SaveChangesAsync();
        }

        using var me = await client.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task A_session_without_requests_for_longer_than_the_idle_timeout_ends()
    {
        await using var factory = new LocalAccountApiFactory();
        await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        using (await LoginAsync(client, UserName, Password))
        {
        }

        factory.Clock.Advance(LocalAccountApiFactory.IdleTimeout - TimeSpan.FromMinutes(1));
        using (var stillOpen = await client.GetAsync(MePath))
        {
            Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
        }

        factory.Clock.Advance(LocalAccountApiFactory.IdleTimeout + TimeSpan.FromMinutes(1));
        using var me = await client.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Activity_keeps_the_session_open_only_until_the_absolute_lifetime_counted_from_login()
    {
        await using var factory = new LocalAccountApiFactory();
        await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        using (await LoginAsync(client, UserName, Password))
        {
        }

        // Each step is longer than half the idle timeout, so every request renews the cookie.
        var step = TimeSpan.FromMinutes(20);
        var elapsed = TimeSpan.Zero;
        while (elapsed + step < LocalAccountApiFactory.AbsoluteLifetime)
        {
            factory.Clock.Advance(step);
            elapsed += step;
            using var open = await client.GetAsync(MePath);
            Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        }

        factory.Clock.Advance(LocalAccountApiFactory.AbsoluteLifetime - elapsed);
        using var me = await client.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Reaching_the_lockout_threshold_refuses_the_right_password_until_the_lock_ends()
    {
        await using var factory = new LocalAccountApiFactory();
        var userId = await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < LocalAccountApiFactory.MaxFailedAttempts; attempt++)
        {
            using var wrong = await LoginAsync(client, UserName, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var locked = await factory.FindCredentialAsync(userId);
        Assert.Equal(factory.Clock.GetUtcNow() + LocalAccountApiFactory.LockoutDuration, locked.LockoutEndUtc);
        Assert.Equal(1, locked.Version);

        using (var refused = await LoginAsync(client, UserName, Password))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Empty(SetCookies(refused));
        }

        factory.Clock.Advance(LocalAccountApiFactory.LockoutDuration);
        using var accepted = await LoginAsync(client, UserName, Password);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var cleared = await factory.FindCredentialAsync(userId);
        Assert.Null(cleared.LockoutEndUtc);
        Assert.Equal(0, cleared.FailedAccessCount);
        Assert.Equal(1, cleared.Version);
    }

    [Fact]
    public async Task The_rate_limit_answers_429_before_the_credential_is_read_and_keeps_other_names_open()
    {
        await using var factory = new LocalAccountApiFactory(permitLimit: 2);
        var userId = await factory.SeedLocalUserAsync(UserName, Password);
        await factory.SeedLocalUserAsync("sentetik.diger", Password);
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var wrong = await LoginAsync(client, UserName, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using (var limited = await LoginAsync(client, " SENTETIK.YEREL ", Password))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Empty(SetCookies(limited));
            using var body = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
            Assert.Equal(IdentitySessionProblemCodesV1.LoginRateLimited, body.RootElement.GetProperty("code").GetString());
        }

        // The refused attempt was not counted against the account.
        Assert.Equal(2, (await factory.FindCredentialAsync(userId)).FailedAccessCount);

        using var other = await LoginAsync(client, "sentetik.diger", Password);
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
    }

    [Theory]
    [InlineData("admin", "admin")]
    [InlineData("administrator", "password")]
    [InlineData("sa", "sa")]
    [InlineData("root", "123456")]
    [InlineData("goymen", "goymen")]
    public async Task A_fresh_host_has_no_local_credential_and_common_default_logins_are_refused(string userName, string password)
    {
        await using var factory = new LocalAccountApiFactory();
        using var client = factory.CreateClient();

        using var response = await LoginAsync(client, userName, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().LocalCredentials.AnyAsync());
    }

    [Fact]
    public async Task Without_the_local_account_mode_login_logout_and_the_antiforgery_token_answer_404()
    {
        await using var factory = new GoymenErpApiFactory();
        using var anonymous = factory.CreateClient();
        using var authenticated = factory.CreateClientFor(TestUsers.CompanyA);

        // No antiforgery header: the bearer modes never run the KIM-D31 check, so the route itself answers.
        using var login = await anonymous.PostAsJsonAsync(LoginPath, new { userName = UserName, password = Password });
        using var logout = await authenticated.PostAsync(LogoutPath, content: null);
        using var token = await anonymous.GetAsync(AntiforgeryPath);

        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, token.StatusCode);
        Assert.False(token.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task The_local_account_mode_does_not_start_with_an_invalid_rate_limit_setting()
    {
        await using var factory = new LocalAccountApiFactory(permitLimit: 0);

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("RateLimit:Login:PermitLimit", Flatten(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shipped_settings_carry_the_kim_d29_login_rate_limit()
    {
        // No test override: the values come from the shipped appsettings.json.
        await using var factory = new LocalAccountApiFactory(permitLimit: null);
        using var client = factory.CreateClient();

        var login = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAccountOptions>>()
            .Value.RateLimit.Login;

        Assert.Equal("SINAN-2026-11-15-KIM-D29", login.PolicyRef);
        Assert.Equal(10, login.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), login.Window);
        Assert.Equal(LocalAccountLoginRateLimitPartition.IpAddressAndUserName, login.PartitionBy);
    }

    [Fact]
    public async Task A_login_without_or_with_a_forged_antiforgery_header_answers_400_and_reads_no_credential()
    {
        await using var factory = new LocalAccountApiFactory();
        var userId = await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();

        using var missing = await client.PostAsJsonAsync(LoginPath, new { userName = UserName, password = WrongPassword });
        await AssertAntiforgeryRefusalAsync(missing);

        using var forged = await PostWithTokenAsync(
            client,
            LoginPath,
            "SENTETIK-forged-token",
            JsonContent.Create(new { userName = UserName, password = Password }));
        await AssertAntiforgeryRefusalAsync(forged);
        Assert.DoesNotContain(SetCookies(forged), cookie => cookie.StartsWith(LocalAccountSession.CookieName + "=", StringComparison.Ordinal));

        // Neither refusal reached the credential: no failed access was counted.
        var credential = await factory.FindCredentialAsync(userId);
        Assert.Equal(0, credential.FailedAccessCount);
        Assert.Equal(1, credential.Version);

        using var accepted = await LoginAsync(client, UserName, Password);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task The_antiforgery_token_route_sets_a_host_only_strict_cookie_and_is_never_cached()
    {
        await using var factory = new LocalAccountApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(AntiforgeryPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["requestToken"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("requestToken").GetString()));
        var cookie = Assert.Single(SetCookies(response));
        Assert.StartsWith(LocalAccountAntiforgery.CookieName + "=", cookie, StringComparison.Ordinal);
        var attributes = cookie.ToLowerInvariant();
        Assert.Contains("; secure", attributes, StringComparison.Ordinal);
        Assert.Contains("; httponly", attributes, StringComparison.Ordinal);
        Assert.Contains("; samesite=strict", attributes, StringComparison.Ordinal);
        Assert.Contains("; path=/", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", attributes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cookie_authenticated_write_needs_a_token_issued_to_the_current_session()
    {
        await using var factory = new LocalAccountApiFactory();
        await factory.SeedLocalUserAsync(UserName, Password);
        using var client = factory.CreateClient();
        var anonymousToken = await TokenAsync(client);
        using (var login = await PostWithTokenAsync(
                   client,
                   LoginPath,
                   anonymousToken,
                   JsonContent.Create(new { userName = UserName, password = Password })))
        {
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        using (var missing = await client.PostAsync(LogoutPath, content: null))
        {
            await AssertAntiforgeryRefusalAsync(missing);
        }

        // The token from before the login is bound to the anonymous user, not to this session.
        using (var stale = await PostWithTokenAsync(client, LogoutPath, anonymousToken, content: null))
        {
            await AssertAntiforgeryRefusalAsync(stale);
        }

        // The refused writes changed nothing: the session is still open.
        using (var me = await client.GetAsync(MePath))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using var logout = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Logout_renews_the_security_stamp_and_ends_every_other_session_of_the_user()
    {
        await using var factory = new LocalAccountApiFactory();
        var userId = await factory.SeedLocalUserAsync(UserName, Password);
        await factory.SeedLocalUserAsync("sentetik.diger", Password);
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        using var otherUser = factory.CreateClient();
        foreach (var (client, name) in new[] { (first, UserName), (second, UserName), (otherUser, "sentetik.diger") })
        {
            using var login = await LoginAsync(client, name, Password);
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        var before = await factory.FindCredentialAsync(userId);

        using (var logout = await LogoutAsync(first))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        var after = await factory.FindCredentialAsync(userId);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.NotEqual(Guid.Empty, after.SecurityStamp);
        Assert.Equal(before.Version + 1, after.Version);

        // The second browser of the same user loses its session on the next request with the generic 401.
        using (var secondMe = await second.GetAsync(MePath))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, secondMe.StatusCode);
            using var body = JsonDocument.Parse(await secondMe.Content.ReadAsStringAsync());
            Assert.Equal("authentication_required", body.RootElement.GetProperty("code").GetString());
        }

        // Another user's session is untouched, and the user can sign in again.
        using (var otherMe = await otherUser.GetAsync(MePath))
        {
            Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
        }

        using var again = await LoginAsync(second, UserName, Password);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        using var reopened = await second.GetAsync(MePath);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
    }

    private static string NewPassword() =>
        "S-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    /// <summary>The shared client's flow: a fresh token for the current user, then the write with the header.</summary>
    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password) =>
        await PostWithTokenAsync(client, LoginPath, await TokenAsync(client), JsonContent.Create(new { userName, password }));

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client) =>
        await PostWithTokenAsync(client, LogoutPath, await TokenAsync(client), content: null);

    private static async Task<string> TokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostWithTokenAsync(
        HttpClient client,
        string path,
        string token,
        HttpContent? content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add(LocalAccountAntiforgery.HeaderName, token);
        return await client.SendAsync(request);
    }

    private static async Task AssertAntiforgeryRefusalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(IdentitySessionProblemCodesV1.AntiforgeryTokenInvalid, body.RootElement.GetProperty("code").GetString());
    }

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(" | ", messages);
    }
}

/// <summary>
/// An in-process host in the <c>LocalAccount</c> mode over in-memory databases, with a manual clock that drives the
/// cookie expiry, the absolute session lifetime and the lockout. SENTETIK settings, not the policy values.
/// </summary>
internal sealed class LocalAccountApiFactory : WebApplicationFactory<Program>
{
    public const int MaxFailedAttempts = 3;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(2);

    private readonly string databaseName = $"goymen-erp-local-account-{Guid.NewGuid():N}";
    private readonly int? permitLimit;

    /// <param name="permitLimit">The test login rate limit; <see langword="null"/> keeps the shipped settings.</param>
    public LocalAccountApiFactory(int? permitLimit = 100)
    {
        this.permitLimit = permitLimit;
        // The cookie is Secure and __Host- prefixed; the client must talk HTTPS to send it back.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public ManualClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public async Task<Guid> SeedLocalUserAsync(string userName, string password, bool isActive = true)
    {
        var userId = Guid.NewGuid();
        var hasher = Services.GetRequiredService<ILocalPasswordHasher>();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = new UserAccount(userId, "Sentetik Yerel Kullanıcı", email: null);
        if (!isActive)
        {
            typeof(UserAccount).GetProperty(nameof(UserAccount.IsActive))!.SetValue(user, false);
        }

        dbContext.Users.Add(user);
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
            Clock.GetUtcNow(),
            Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
        return userId;
    }

    public async Task<LocalCredential> FindCredentialAsync(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().LocalCredentials
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);
    }

    public async Task UpdateCredentialAsync(Guid userId, Action<LocalCredential> change)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        change(await dbContext.LocalCredentials.SingleAsync(x => x.UserId == userId));
        await dbContext.SaveChangesAsync();
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
        // Host settings, not ConfigureAppConfiguration: Program reads the authentication mode before the factory's
        // app-configuration callbacks run.
        var settings = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
            ["Authentication:Mode"] = "LocalAccount",
            ["Identity:LocalAccount:Lockout:MaxFailedAttempts"] = MaxFailedAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Identity:LocalAccount:Lockout:Duration"] = LockoutDuration.ToString("c"),
            ["Identity:LocalAccount:Session:IdleTimeout"] = IdleTimeout.ToString("c"),
            ["Identity:LocalAccount:Session:AbsoluteLifetime"] = AbsoluteLifetime.ToString("c"),
            ["Identity:LocalAccount:PasswordHashing:Algorithm"] = LocalAccountPasswordHashingSettings.Pbkdf2Algorithm,
            ["Identity:LocalAccount:PasswordHashing:Pbkdf2Iterations"] =
                LocalAccountOptions.MinimumPbkdf2Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Identity:LocalAccount:PasswordPolicy:MinimumLength"] = "8",
            ["Identity:LocalAccount:PasswordPolicy:MaximumLength"] = "128",
            ["Identity:LocalAccount:PasswordPolicy:RequireCharacterClasses"] = "false",
            ["Identity:LocalAccount:PasswordPolicy:BlocklistEnabled"] = "true",
        };
        if (permitLimit is { } limit)
        {
            settings["Identity:LocalAccount:RateLimit:Login:PermitLimit"] =
                limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings["Identity:LocalAccount:RateLimit:Login:Window"] = "00:01:00";
            settings["Identity:LocalAccount:RateLimit:Login:PartitionBy"] =
                nameof(LocalAccountLoginRateLimitPartition.IpAddressAndUserName);
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            GoymenErpApiFactory.UseInMemoryDatabases(services, databaseName);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<ILocalCredentialStore>();
            services.AddScoped<ILocalCredentialStore, TrackingLocalCredentialStore>();
        });
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>
/// The in-memory provider has no set-based update, so the login writes are done through tracked entities with the
/// same conditions the relational store puts in its <c>WHERE</c> (the SQL facts cover the relational store itself).
/// </summary>
internal sealed class TrackingLocalCredentialStore(IdentityDbContext dbContext) : ILocalCredentialStore
{
    private readonly SqlLocalCredentialStore reads = new(dbContext);

    public Task<LocalCredentialLoginState?> FindForLoginAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        reads.FindForLoginAsync(normalizedUserName, cancellationToken);

    public Task<LocalCredentialSessionState?> FindForSessionAsync(Guid userId, CancellationToken cancellationToken) =>
        reads.FindForSessionAsync(userId, cancellationToken);

    public async Task<bool> TryRecordFailedAccessAsync(
        LocalCredentialLoginState read,
        LocalCredentialLockoutPolicy policy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var credential = await FindUnchangedAsync(read, cancellationToken);
        if (credential is null || LocalCredentialLockout.IsLockedOut(credential.LockoutEndUtc, nowUtc))
        {
            return false;
        }

        credential.ApplyFailedAccess(LocalCredentialLockout.AfterFailedAccess(
            credential.FailedAccessCount,
            credential.LockoutEndUtc,
            policy,
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryRecordSuccessfulAccessAsync(
        LocalCredentialLoginState read,
        string? rehashedPasswordHash,
        int? rehashedFormatVersion,
        CancellationToken cancellationToken)
    {
        var credential = await FindUnchangedAsync(read, cancellationToken);
        if (credential is null)
        {
            return false;
        }

        credential.ApplySuccessfulAccess(rehashedPasswordHash, rehashedFormatVersion);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryConsumeTemporaryPasswordAsync(
        LocalCredentialLoginState read,
        string? rehashedPasswordHash,
        int? rehashedFormatVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var credential = await FindUnchangedAsync(read, cancellationToken);
        if (credential is null || credential.TemporaryPasswordConsumedAtUtc is not null)
        {
            return false;
        }

        // The relational store sets only the consumption column; the domain exposes no setter for it.
        credential.ApplySuccessfulAccess(rehashedPasswordHash, rehashedFormatVersion);
        dbContext.Entry(credential).Property(nameof(LocalCredential.TemporaryPasswordConsumedAtUtc)).CurrentValue = nowUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryRenewSecurityStampAsync(
        Guid userId,
        Guid readSecurityStamp,
        Guid newSecurityStamp,
        CancellationToken cancellationToken)
    {
        var credential = await dbContext.LocalCredentials.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (credential is null || credential.SecurityStamp != readSecurityStamp)
        {
            return false;
        }

        credential.RenewSecurityStamp(newSecurityStamp);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<LocalCredential?> FindUnchangedAsync(LocalCredentialLoginState read, CancellationToken cancellationToken)
    {
        var credential = await dbContext.LocalCredentials.SingleOrDefaultAsync(x => x.UserId == read.UserId, cancellationToken);
        return credential is not null
               && string.Equals(credential.PasswordHash, read.PasswordHash, StringComparison.Ordinal)
               && credential.SecurityStamp == read.SecurityStamp
               && credential.TemporaryPasswordIssuedAtUtc == read.TemporaryPasswordIssuedAtUtc
            ? credential
            : null;
    }
}
