using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

/// <summary>
/// <c>identity bootstrap-founder</c> (spec ID-2, KS-5) and <c>identity recover-founder reactivate|reset-password</c>
/// (spec ID-2r): one-shot operator commands run from the deployed API binary. They never start the web host. Exit
/// codes: 0 completed, 1 refused, 2 usage. Only the exact argument lists are accepted, so no password, user name or
/// company can be passed on the command line. A missing or unknown recovery sub-command still reaches the service so
/// the refusal is recorded (ID-2r: every refusal writes one security event), then exits with the usage code.
/// </summary>
internal static class IdentityCommandLine
{
    internal const string CommandGroup = "identity";

    internal const string BootstrapFounderCommand = "bootstrap-founder";

    internal const string RecoverFounderCommand = "recover-founder";
    internal const string RecoverTwoFactorCommand = "recover-two-factor";

    internal const int ExitCompleted = 0;

    internal const int ExitRefused = 1;

    internal const int ExitUsage = 2;

    internal static bool IsIdentityCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], CommandGroup, StringComparison.Ordinal);

    /// <summary>The <c>bootstrap-founder</c>-only entry point; <c>recover-founder</c> needs a recovery console.</summary>
    internal static Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        IFounderBootstrapConsole console,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) =>
        RunAsync(services, args, console, recoveryConsole: null, output, error, cancellationToken);

    internal static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        IFounderBootstrapConsole console,
        IFounderRecoveryConsole? recoveryConsole,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken,
        ILocalTwoFactorOperatorRecoveryConsoleV1? operatorRecoveryConsole = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var isBootstrap = args.Count == 2
            && string.Equals(args[0], CommandGroup, StringComparison.Ordinal)
            && string.Equals(args[1], BootstrapFounderCommand, StringComparison.Ordinal);
        var isRecovery = recoveryConsole is not null
            && args.Count is 2 or 3
            && string.Equals(args[0], CommandGroup, StringComparison.Ordinal)
            && string.Equals(args[1], RecoverFounderCommand, StringComparison.Ordinal);
        var isOperatorRecovery = operatorRecoveryConsole is not null && args.Count >= 2
            && string.Equals(args[0], CommandGroup, StringComparison.Ordinal)
            && string.Equals(args[1], RecoverTwoFactorCommand, StringComparison.Ordinal);
        if (!isBootstrap && !isRecovery && !isOperatorRecovery)
        {
            await error.WriteLineAsync(FounderBootstrapTexts.Usage);
            await error.WriteLineAsync(FounderRecoveryTexts.Usage);
            await error.WriteLineAsync(OperatorTwoFactorRecoveryTexts.Usage);
            return ExitUsage;
        }

        // The marker exists only when the host resolved the LocalAccount mode (the same switch as the cookie scheme).
        if (services.GetService<LocalAccountAntiforgery.EnabledMarker>() is null)
        {
            await error.WriteLineAsync(FounderBootstrapTexts.LocalAccountModeRequired);
            return ExitRefused;
        }

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(
            Guid.NewGuid().ToString("N"),
            causationId: null,
            SourceChannels.System,
            clientIpAddress: null,
            userAgent: null);

        if (isOperatorRecovery)
        {
            var recovery = await scope.ServiceProvider.GetRequiredService<ILocalTwoFactorOperatorRecoveryServiceV1>()
                .RunAsync(args.Count == 2, operatorRecoveryConsole!, cancellationToken);
            if (recovery.Completed)
            {
                await output.WriteLineAsync(OperatorTwoFactorRecoveryTexts.Completed(recovery));
                return ExitCompleted;
            }
            await error.WriteLineAsync(OperatorTwoFactorRecoveryTexts.Refused(recovery));
            return recovery.Status == LocalTwoFactorOperatorRecoveryStatusV1.InvalidInvocation ? ExitUsage : ExitRefused;
        }

        if (isRecovery)
        {
            return await RunRecoveryAsync(
                scope.ServiceProvider,
                args.Count == 3 ? ParseSubcommand(args[2]) : null,
                recoveryConsole!,
                output,
                error,
                cancellationToken);
        }

        var result = await scope.ServiceProvider.GetRequiredService<IFounderBootstrapService>()
            .RunAsync(console, cancellationToken);
        if (result.Status == FounderBootstrapStatus.Completed && result.FounderUserId is { } founderUserId)
        {
            await output.WriteLineAsync(FounderBootstrapTexts.Completed(founderUserId, result.CompanyIds.Count));
            return ExitCompleted;
        }

        await error.WriteLineAsync(FounderBootstrapTexts.Refusal(result.Status));
        if (result.PolicyViolations.Count > 0)
        {
            await error.WriteLineAsync(FounderBootstrapTexts.PolicyViolationPrefix + string.Join(", ", result.PolicyViolations));
        }

        return ExitRefused;
    }

    /// <summary>Exact, case-sensitive sub-command names; anything else is "none" (the service refuses and records it).</summary>
    internal static FounderRecoverySubcommand? ParseSubcommand(string value) => value switch
    {
        FounderRecoveryTexts.ReactivateSubcommand => FounderRecoverySubcommand.Reactivate,
        FounderRecoveryTexts.ResetPasswordSubcommand => FounderRecoverySubcommand.ResetPassword,
        _ => null,
    };

    private static async Task<int> RunRecoveryAsync(
        IServiceProvider scopedServices,
        FounderRecoverySubcommand? subcommand,
        IFounderRecoveryConsole recoveryConsole,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await scopedServices.GetRequiredService<IFounderRecoveryService>()
            .RunAsync(subcommand, recoveryConsole, cancellationToken);
        if (result.Status == FounderRecoveryStatus.Completed && result.FounderUserId is { } founderUserId)
        {
            await output.WriteLineAsync(result.TemporaryPasswordExpiresAtUtc is { } expiresAtUtc
                ? FounderRecoveryTexts.TemporaryPasswordIssued(founderUserId, expiresAtUtc)
                : FounderRecoveryTexts.Reactivated(founderUserId, result.CompanyIds.Count));
            return ExitCompleted;
        }

        await error.WriteLineAsync(FounderRecoveryTexts.Refusal(result.Status));
        if (result.PolicyViolations.Count > 0)
        {
            await error.WriteLineAsync(FounderRecoveryTexts.PolicyViolationPrefix + string.Join(", ", result.PolicyViolations));
        }

        if (result.Status == FounderRecoveryStatus.SubcommandRequired)
        {
            await error.WriteLineAsync(FounderRecoveryTexts.Usage);
            return ExitUsage;
        }

        return ExitRefused;
    }
}
