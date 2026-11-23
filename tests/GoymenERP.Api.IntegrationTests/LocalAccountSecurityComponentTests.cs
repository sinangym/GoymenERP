using System.Net;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-1 security building blocks without a host: the framework password hasher (KIM-D12 option A), the login rate
/// limit partitions and the startup guard of the development weak-password exception. SENTETIK values only.
/// </summary>
public sealed class LocalAccountSecurityComponentTests
{
    // Generated per run (spec "Varsayılan kimlik bilgisi yasağı" item 1).
    private static readonly string Password = NewPassword();

    [Fact]
    public void A_framework_v3_hash_verifies_the_right_password_only_and_never_contains_it()
    {
        var hasher = Hasher(LocalAccountOptions.MinimumPbkdf2Iterations);

        var hash = hasher.Hash(Password);

        Assert.Equal(LocalAccountPasswordHasher.FrameworkV3FormatVersion, hasher.FormatVersion);
        Assert.DoesNotContain(Password, hash, StringComparison.Ordinal);
        Assert.NotEqual(hash, hasher.Hash(Password));
        Assert.Equal(LocalPasswordVerification.Success, hasher.Verify(hash, Password));
        Assert.Equal(LocalPasswordVerification.Failed, hasher.Verify(hash, NewPassword()));
        Assert.Equal(LocalPasswordVerification.Failed, hasher.Verify(hash, string.Empty));
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_verifies_and_asks_for_a_rehash()
    {
        var oldHash = Hasher(LocalAccountOptions.MinimumPbkdf2Iterations).Hash(Password);

        var raised = Hasher(LocalAccountOptions.MinimumPbkdf2Iterations + 1);

        Assert.Equal(LocalPasswordVerification.SuccessRehashNeeded, raised.Verify(oldHash, Password));
        Assert.Equal(LocalPasswordVerification.Success, raised.Verify(raised.Hash(Password), Password));
    }

    [Theory]
    [InlineData("bm90LWEtaGFzaA==")]
    [InlineData("%%%")]
    public void A_damaged_stored_hash_fails_instead_of_throwing(string damagedHash)
    {
        Assert.Equal(LocalPasswordVerification.Failed, Hasher(LocalAccountOptions.MinimumPbkdf2Iterations).Verify(damagedHash, Password));
    }

    [Theory]
    [InlineData("Pbkdf2", null)]
    [InlineData("Pbkdf2", LocalAccountOptions.MinimumPbkdf2Iterations - 1)]
    [InlineData("Argon2id", LocalAccountOptions.MinimumPbkdf2Iterations)]
    [InlineData(null, LocalAccountOptions.MinimumPbkdf2Iterations)]
    public void The_hasher_does_not_start_with_missing_or_weak_settings(string? algorithm, int? iterations)
    {
        var options = new LocalAccountOptions();
        options.PasswordHashing.Algorithm = algorithm;
        options.PasswordHashing.Pbkdf2Iterations = iterations;

        Assert.Throws<InvalidOperationException>(() => new LocalAccountPasswordHasher(Options.Create(options)));
    }

    [Fact]
    public void The_dummy_verification_accepts_any_input_without_an_answer()
    {
        var hasher = Hasher(LocalAccountOptions.MinimumPbkdf2Iterations);

        hasher.VerifyAgainstDummy(Password);
        hasher.VerifyAgainstDummy(string.Empty);
    }

    [Fact]
    public void Ip_and_user_name_partitions_share_a_window_per_normalized_name_and_address()
    {
        using var limiter = Limiter(permitLimit: 1, LocalAccountLoginRateLimitPartition.IpAddressAndUserName);

        Assert.True(limiter.TryAcquire(Request("192.0.2.10"), "Ali"));
        Assert.False(limiter.TryAcquire(Request("192.0.2.10"), " ALI "));
        Assert.True(limiter.TryAcquire(Request("192.0.2.10"), "veli"));
        Assert.True(limiter.TryAcquire(Request("192.0.2.11"), "ali"));
    }

    [Fact]
    public void Ip_partitions_share_one_window_across_user_names()
    {
        using var limiter = Limiter(permitLimit: 2, LocalAccountLoginRateLimitPartition.IpAddress);

        Assert.True(limiter.TryAcquire(Request("192.0.2.20"), "ali"));
        Assert.True(limiter.TryAcquire(Request("192.0.2.20"), "veli"));
        Assert.False(limiter.TryAcquire(Request("192.0.2.20"), "ayse"));
        Assert.True(limiter.TryAcquire(Request("192.0.2.21"), "ali"));
    }

    [Theory]
    [InlineData(null, "00:01:00", LocalAccountLoginRateLimitPartition.IpAddress)]
    [InlineData(0, "00:01:00", LocalAccountLoginRateLimitPartition.IpAddress)]
    [InlineData(5, null, LocalAccountLoginRateLimitPartition.IpAddress)]
    [InlineData(5, "00:00:00", LocalAccountLoginRateLimitPartition.IpAddress)]
    [InlineData(5, "00:01:00", null)]
    [InlineData(5, "00:01:00", (LocalAccountLoginRateLimitPartition)99)]
    public void The_rate_limiter_has_no_code_default_and_does_not_start_without_settings(
        int? permitLimit,
        string? window,
        LocalAccountLoginRateLimitPartition? partition)
    {
        var options = new LocalAccountOptions();
        options.RateLimit.Login.PermitLimit = permitLimit;
        options.RateLimit.Login.Window = window is null ? null : TimeSpan.Parse(window, System.Globalization.CultureInfo.InvariantCulture);
        options.RateLimit.Login.PartitionBy = partition;

        Assert.Throws<InvalidOperationException>(() => new LocalAccountLoginRateLimiter(Options.Create(options)));
    }

    [Fact]
    public void The_weak_password_key_in_an_ordinary_configuration_source_stops_the_host()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [LocalAccountStartupGuard.DevelopmentAllowWeakPasswordKey] = "false",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            LocalAccountStartupGuard.EnsureDevelopmentWeakPasswordSource(configuration, new HostEnvironment(Environments.Development)));
    }

    [Theory]
    [InlineData("true", "Development", true)]
    [InlineData("true", "Production", false)]
    [InlineData("true", "Testing", false)]
    [InlineData("false", "Production", true)]
    [InlineData("evet", "Development", false)]
    public void The_weak_password_key_is_read_only_from_user_secrets_and_true_only_in_development(
        string value,
        string environmentName,
        bool accepted)
    {
        var root = Directory.CreateTempSubdirectory("goymen-local-account-guard-");
        try
        {
            var secretsDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "usersecrets", "sentetik-id"));
            File.WriteAllText(
                Path.Combine(secretsDirectory.FullName, "secrets.json"),
                $$"""{ "Identity": { "LocalAccount": { "DevelopmentAllowWeakPassword": "{{value}}" } } }""");
            using var fileProvider = new PhysicalFileProvider(secretsDirectory.FullName);
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(fileProvider, "secrets.json", optional: false, reloadOnChange: false)
                .Build();

            var check = () => LocalAccountStartupGuard.EnsureDevelopmentWeakPasswordSource(
                configuration,
                new HostEnvironment(environmentName));

            if (accepted)
            {
                check();
            }
            else
            {
                Assert.Throws<InvalidOperationException>(check);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_json_file_named_secrets_outside_a_user_secrets_folder_is_not_trusted()
    {
        var root = Directory.CreateTempSubdirectory("goymen-local-account-guard-");
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "secrets.json"),
                """{ "Identity": { "LocalAccount": { "DevelopmentAllowWeakPassword": "true" } } }""");
            using var fileProvider = new PhysicalFileProvider(root.FullName);
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(fileProvider, "secrets.json", optional: false, reloadOnChange: false)
                .Build();

            Assert.Throws<InvalidOperationException>(() =>
                LocalAccountStartupGuard.EnsureDevelopmentWeakPasswordSource(configuration, new HostEnvironment(Environments.Development)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Without_the_weak_password_key_the_guard_passes_in_every_environment()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        LocalAccountStartupGuard.EnsureDevelopmentWeakPasswordSource(configuration, new HostEnvironment(Environments.Production));
        LocalAccountStartupGuard.EnsureDevelopmentWeakPasswordSource(configuration, new HostEnvironment(Environments.Development));
    }

    private static string NewPassword() =>
        "S-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    private static LocalAccountPasswordHasher Hasher(int iterations)
    {
        var options = new LocalAccountOptions();
        options.PasswordHashing.Algorithm = LocalAccountPasswordHashingSettings.Pbkdf2Algorithm;
        options.PasswordHashing.Pbkdf2Iterations = iterations;
        return new LocalAccountPasswordHasher(Options.Create(options));
    }

    private static LocalAccountLoginRateLimiter Limiter(int permitLimit, LocalAccountLoginRateLimitPartition partition)
    {
        var options = new LocalAccountOptions();
        options.RateLimit.Login.PermitLimit = permitLimit;
        options.RateLimit.Login.Window = TimeSpan.FromHours(1);
        options.RateLimit.Login.PartitionBy = partition;
        return new LocalAccountLoginRateLimiter(Options.Create(options));
    }

    private static DefaultHttpContext Request(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }

    private sealed class HostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "GoymenERP.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
