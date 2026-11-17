using GoymenERP.Api.Security;
using GoymenERP.Api.Startup;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LocalTwoFactorOperatorRecoveryCommandLineTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Only_exact_nonsecret_command_words_reach_the_operator_service_as_a_valid_invocation(bool extraArgument)
    {
        var recovery = new RecordingService();
        await using var provider = new ServiceCollection().AddScoped<ICorrelationContext, CorrelationContext>()
            .AddSingleton(new LocalAccountAntiforgery.EnabledMarker()).AddSingleton<ILocalTwoFactorOperatorRecoveryServiceV1>(recovery).BuildServiceProvider();
        using var output = new StringWriter(); using var error = new StringWriter();
        string[] args = extraArgument ? ["identity", "recover-two-factor", "SENTETIK-NOT-TO-BE-ECHOED"] : ["identity", "recover-two-factor"];
        var exit = await IdentityCommandLine.RunAsync(provider, args, new NeverBootstrap(), null, output, error, CancellationToken.None, new OperatorConsole());
        Assert.Equal([!extraArgument], recovery.Invocations);
        Assert.Equal(extraArgument ? IdentityCommandLine.ExitUsage : IdentityCommandLine.ExitCompleted, exit);
        Assert.DoesNotContain("SENTETIK-NOT-TO-BE-ECHOED", output.ToString() + error, StringComparison.Ordinal);
    }
    [Fact]
    public async Task The_server_command_requires_the_existing_local_account_mode_and_does_not_add_a_web_or_founder_path()
    {
        var recovery = new RecordingService();
        await using var provider = new ServiceCollection().AddSingleton<ILocalTwoFactorOperatorRecoveryServiceV1>(recovery).BuildServiceProvider();
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await IdentityCommandLine.RunAsync(provider, ["identity", "recover-two-factor"], new NeverBootstrap(), null,
            output, error, CancellationToken.None, new OperatorConsole());
        Assert.Equal(IdentityCommandLine.ExitRefused, exit); Assert.Empty(recovery.Invocations);
    }
    private sealed class RecordingService : ILocalTwoFactorOperatorRecoveryServiceV1
    {
        public List<bool> Invocations { get; } = [];
        public Task<LocalTwoFactorOperatorRecoveryResultV1> RunAsync(bool invocationValid, ILocalTwoFactorOperatorRecoveryConsoleV1 console, CancellationToken ct = default)
        { Invocations.Add(invocationValid); return Task.FromResult(new LocalTwoFactorOperatorRecoveryResultV1(invocationValid
            ? LocalTwoFactorOperatorRecoveryStatusV1.Recovered : LocalTwoFactorOperatorRecoveryStatusV1.InvalidInvocation, Guid.NewGuid(), 3)); }
    }
    private sealed class NeverBootstrap : IFounderBootstrapConsole
    {
        public bool IsInteractive => throw new InvalidOperationException();
        public string? ReadUserName() => throw new InvalidOperationException();
        public string? ReadDisplayName() => throw new InvalidOperationException();
        public string? ReadNewPassword() => throw new InvalidOperationException();
        public string? ReadNewPasswordConfirmation() => throw new InvalidOperationException();
    }
    private sealed class OperatorConsole : ILocalTwoFactorOperatorRecoveryConsoleV1
    {
        public bool IsInteractive => true; public string? ReadTargetUserName() => throw new InvalidOperationException();
        public string? ReadIdempotencyKey() => throw new InvalidOperationException(); public string? ReadReason() => throw new InvalidOperationException();
        public LocalTwoFactorRecoveryOperatorDeclarationV1? ReadOperatorDeclaration() => throw new InvalidOperationException();
        public bool Confirm(LocalTwoFactorOperatorRecoveryConfirmationV1 confirmation) => throw new InvalidOperationException();
    }
}
