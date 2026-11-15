using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Startup;

/// <summary>
/// <c>identity bootstrap-founder</c> (spec ID-2, KS-5): a one-shot operator command run from the deployed API binary.
/// It never starts the web host. Exit codes: 0 completed, 1 refused, 2 usage. Only the exact two arguments are
/// accepted, so no password, user name or company can be passed on the command line.
/// </summary>
internal static class IdentityCommandLine
{
    internal const string CommandGroup = "identity";

    internal const string BootstrapFounderCommand = "bootstrap-founder";

    internal const int ExitCompleted = 0;

    internal const int ExitRefused = 1;

    internal const int ExitUsage = 2;

    internal static bool IsIdentityCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], CommandGroup, StringComparison.Ordinal);

    internal static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        IFounderBootstrapConsole console,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count != 2
            || !string.Equals(args[0], CommandGroup, StringComparison.Ordinal)
            || !string.Equals(args[1], BootstrapFounderCommand, StringComparison.Ordinal))
        {
            await error.WriteLineAsync(FounderBootstrapTexts.Usage);
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
}
