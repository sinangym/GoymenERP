using GoymenERP.Api.Security;
using GoymenERP.Api.Startup;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-2r <c>identity recover-founder reactivate|reset-password</c> without a host: the exact sub-command names, a missing
/// or unknown sub-command still reaching the service (so the refusal is recorded) and the exit codes. SENTETIK.
/// </summary>
public sealed class FounderRecoveryCommandLineTests
{
    [Theory]
    [InlineData("reactivate", FounderRecoverySubcommand.Reactivate)]
    [InlineData("reset-password", FounderRecoverySubcommand.ResetPassword)]
    [InlineData("Reactivate", null)]
    [InlineData("reset", null)]
    [InlineData("--yes", null)]
    public void Only_the_exact_subcommand_names_are_parsed(string value, FounderRecoverySubcommand? expected)
    {
        Assert.Equal(expected, IdentityCommandLine.ParseSubcommand(value));
    }

    [Fact]
    public async Task A_missing_or_unknown_subcommand_reaches_the_service_and_exits_with_the_usage_code()
    {
        var missing = await RunAsync(FounderRecoveryResult.Of(FounderRecoveryStatus.SubcommandRequired),
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand]);
        var unknown = await RunAsync(FounderRecoveryResult.Of(FounderRecoveryStatus.SubcommandRequired),
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "hepsi"]);

        foreach (var run in new[] { missing, unknown })
        {
            Assert.Equal(IdentityCommandLine.ExitUsage, run.ExitCode);
            Assert.Equal([(FounderRecoverySubcommand?)null], run.Service.Subcommands);
            Assert.Contains(FounderRecoveryTexts.Usage, run.Error, StringComparison.Ordinal);
            Assert.Contains(FounderRecoveryRefusalCodes.SubcommandRequired, run.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Extra_arguments_are_refused_with_the_usage_code_without_running_the_service()
    {
        var run = await RunAsync(
            FounderRecoveryResult.Of(FounderRecoveryStatus.Completed),
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "reset-password", "parola"]);

        Assert.Equal(IdentityCommandLine.ExitUsage, run.ExitCode);
        Assert.Empty(run.Service.Subcommands);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task Without_a_recovery_console_the_recovery_command_is_a_usage_error()
    {
        var services = Services(new RecordingRecoveryService(FounderRecoveryResult.Of(FounderRecoveryStatus.Completed)));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await IdentityCommandLine.RunAsync(
            services,
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "reactivate"],
            new NeverReadBootstrapConsole(),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(IdentityCommandLine.ExitUsage, exitCode);
    }

    [Fact]
    public async Task Completion_exits_0_with_the_matching_text_and_a_refusal_exits_1_with_its_stable_code()
    {
        var founderUserId = Guid.NewGuid();
        var expiresAtUtc = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
        var reset = await RunAsync(
            new FounderRecoveryResult(FounderRecoveryStatus.Completed, founderUserId, [], [], expiresAtUtc),
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "reset-password"]);
        var reactivated = await RunAsync(
            new FounderRecoveryResult(FounderRecoveryStatus.Completed, founderUserId, [Guid.NewGuid()], [], null),
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "reactivate"]);
        var refused = await RunAsync(
            FounderRecoveryResult.Of(FounderRecoveryStatus.NotNeeded),
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "reactivate"]);

        Assert.Equal(IdentityCommandLine.ExitCompleted, reset.ExitCode);
        Assert.Contains(FounderRecoveryTexts.TemporaryPasswordIssued(founderUserId, expiresAtUtc), reset.Output, StringComparison.Ordinal);
        Assert.Contains("2026-11-19 09:00 UTC", reset.Output, StringComparison.Ordinal);
        Assert.Equal([(FounderRecoverySubcommand?)FounderRecoverySubcommand.ResetPassword], reset.Service.Subcommands);

        Assert.Equal(IdentityCommandLine.ExitCompleted, reactivated.ExitCode);
        Assert.Contains(FounderRecoveryTexts.Reactivated(founderUserId, 1), reactivated.Output, StringComparison.Ordinal);

        Assert.Equal(IdentityCommandLine.ExitRefused, refused.ExitCode);
        Assert.Equal(string.Empty, refused.Output);
        Assert.Contains(FounderRecoveryTexts.Refusal(FounderRecoveryStatus.NotNeeded), refused.Error, StringComparison.Ordinal);
        Assert.Contains(FounderRecoveryRefusalCodes.NotNeeded, refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_recovery_command_refuses_outside_the_local_account_mode_without_running_the_service()
    {
        var service = new RecordingRecoveryService(FounderRecoveryResult.Of(FounderRecoveryStatus.Completed));
        var services = new ServiceCollection()
            .AddScoped<ICorrelationContext, CorrelationContext>()
            .AddSingleton<IFounderRecoveryService>(service)
            .BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await IdentityCommandLine.RunAsync(
            services,
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.RecoverFounderCommand, "reactivate"],
            new NeverReadBootstrapConsole(),
            new NeverReadRecoveryConsole(),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(IdentityCommandLine.ExitRefused, exitCode);
        Assert.Empty(service.Subcommands);
    }

    [Fact]
    public void Every_status_has_a_turkish_text_and_every_refusal_a_stable_code()
    {
        foreach (var status in Enum.GetValues<FounderRecoveryStatus>().Where(status => status != FounderRecoveryStatus.Completed))
        {
            var code = FounderRecoveryRefusalCodes.Of(status);
            Assert.NotNull(code);
            Assert.StartsWith("identity.founder_recovery.", code, StringComparison.Ordinal);
            Assert.EndsWith($"({code})", FounderRecoveryTexts.Refusal(status), StringComparison.Ordinal);
            Assert.NotEqual("Kurucu hesabı kurtarılamadı.", FounderRecoveryTexts.Refusal(status)[..^(code!.Length + 3)]);
        }

        Assert.Null(FounderRecoveryRefusalCodes.Of(FounderRecoveryStatus.Completed));
    }

    private static ServiceProvider Services(IFounderRecoveryService service) =>
        new ServiceCollection()
            .AddScoped<ICorrelationContext, CorrelationContext>()
            .AddSingleton(service)
            .AddSingleton(new LocalAccountAntiforgery.EnabledMarker())
            .BuildServiceProvider();

    private static async Task<(int ExitCode, string Output, string Error, RecordingRecoveryService Service)> RunAsync(
        FounderRecoveryResult result,
        string[] args)
    {
        var service = new RecordingRecoveryService(result);
        var services = Services(service);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await IdentityCommandLine.RunAsync(
            services,
            args,
            new NeverReadBootstrapConsole(),
            new NeverReadRecoveryConsole(),
            output,
            error,
            CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString(), service);
    }

    private sealed class RecordingRecoveryService(FounderRecoveryResult result) : IFounderRecoveryService
    {
        public List<FounderRecoverySubcommand?> Subcommands { get; } = [];

        public Task<FounderRecoveryResult> RunAsync(
            FounderRecoverySubcommand? subcommand,
            IFounderRecoveryConsole console,
            CancellationToken cancellationToken)
        {
            Subcommands.Add(subcommand);
            return Task.FromResult(result);
        }
    }

    private sealed class NeverReadBootstrapConsole : IFounderBootstrapConsole
    {
        public bool IsInteractive => true;

        public string? ReadUserName() => throw new InvalidOperationException();

        public string? ReadDisplayName() => throw new InvalidOperationException();

        public string? ReadNewPassword() => throw new InvalidOperationException();

        public string? ReadNewPasswordConfirmation() => throw new InvalidOperationException();
    }

    private sealed class NeverReadRecoveryConsole : IFounderRecoveryConsole
    {
        public bool IsInteractive => true;
        public FounderRecoveryOperatorDeclaration? ReadOperatorDeclaration() => throw new InvalidOperationException();

        public string? ReadNewPassword() => throw new InvalidOperationException();

        public string? ReadNewPasswordConfirmation() => throw new InvalidOperationException();

        public bool Confirm(FounderRecoveryConfirmation confirmation) => throw new InvalidOperationException();
    }
}
