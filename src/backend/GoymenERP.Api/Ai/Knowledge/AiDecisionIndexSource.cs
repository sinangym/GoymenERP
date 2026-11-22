using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>Where the decision index comes from. Null means "no index": the lookup tool answers unavailable.</summary>
public interface IAiDecisionIndexSource
{
    AiDecisionIndex? Current { get; }
}

/// <summary>
/// Reads the decision index once from <c>Ai:Knowledge:DecisionIndexPath</c> (relative paths resolve against the content
/// root). No default path is configured: until an operator points it at the published
/// <c>contracts/ai/decision-index-v1.json</c>, the tool stays unavailable (fail-closed). The file is never written here.
/// </summary>
public sealed class ConfiguredAiDecisionIndexSource : IAiDecisionIndexSource
{
    public const string PathKey = "Ai:Knowledge:DecisionIndexPath";

    private readonly Lazy<AiDecisionIndex?> index;

    public ConfiguredAiDecisionIndexSource(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var configured = configuration[PathKey];
        var path = string.IsNullOrWhiteSpace(configured)
            ? null
            : Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        index = new Lazy<AiDecisionIndex?>(() => AiDecisionIndex.TryLoad(path), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public AiDecisionIndex? Current => index.Value;
}

/// <summary>A fixed index (tests and tools).</summary>
public sealed class FixedAiDecisionIndexSource(AiDecisionIndex? index) : IAiDecisionIndexSource
{
    public AiDecisionIndex? Current { get; } = index;
}
