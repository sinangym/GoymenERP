using GoymenERP.BuildingBlocks.Ai;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

/// <summary>
/// Default-deny READ tool catalog (ERP-000J §8 step 3). A tool is callable only when an owner module registered an
/// <see cref="IAiOwnerReadToolV1"/> with READ risk AND configuration allowlists its exact id and version. Draft,
/// simulate and write tools are never admitted here. A tool id registered more than once is excluded entirely.
/// AI-3 ships no module tool; concrete owner READ tools arrive with AI-6.
/// </summary>
public sealed class AiToolCatalog
{
    private readonly IReadOnlyDictionary<(string ToolId, string Version), IAiOwnerReadToolV1> tools;

    public AiToolCatalog(IEnumerable<IAiOwnerToolV1> ownerTools, IOptions<AiRuntimeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(ownerTools);
        ArgumentNullException.ThrowIfNull(options);
        // An unreadable section admits no tool (default-deny); the runtime state reports it as misconfigured.
        var configured = AiRuntimeOptionsReader.TryRead(options, out var value) ? value!.AllowedTools : null;
        var allowlist = (configured ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry?.ToolId) && !string.IsNullOrWhiteSpace(entry?.Version))
            .Select(entry => (entry.ToolId!, entry.Version!))
            .ToHashSet();

        var registered = ownerTools.Where(tool => tool is not null).ToArray();
        var duplicateIds = registered
            .GroupBy(tool => tool.Descriptor.ToolId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        tools = registered
            .OfType<IAiOwnerReadToolV1>()
            .Where(tool => tool.Descriptor.Risk == AiToolRiskClassV1.Read)
            .Where(tool => tool.Descriptor.OwnerCommandName is null)
            .Where(tool => !duplicateIds.Contains(tool.Descriptor.ToolId))
            .Where(tool => allowlist.Contains((tool.Descriptor.ToolId, tool.Descriptor.Version)))
            .ToDictionary(tool => (tool.Descriptor.ToolId, tool.Descriptor.Version));
    }

    /// <summary>Admitted READ tools in a stable order.</summary>
    public IReadOnlyList<AiToolDescriptorV1> Descriptors =>
    [
        .. tools.Values
            .Select(tool => tool.Descriptor)
            .OrderBy(descriptor => descriptor.ToolId, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Version, StringComparer.Ordinal),
    ];

    public IAiOwnerReadToolV1? Find(string? toolId, string? version) =>
        toolId is not null && version is not null && tools.TryGetValue((toolId, version), out var tool) ? tool : null;
}
