using GoymenERP.BuildingBlocks.Ai;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.Commands;

/// <summary>
/// Default-deny WRITE tool catalog for the command pipeline. A command tool is admitted only when an owner module
/// registered an <see cref="IAiOwnerCommandToolV1"/> with WRITE risk and an owner command name, declared its effects,
/// is not on the AI-D25 closed list, and <c>Ai:Commands:AllowedTools</c> lists its exact id and version. A tool id
/// registered more than once is excluded entirely. This package registers no command tool and allowlists none.
/// WP-A6: the allowlist entry must also carry the approved pin of the tool's model card (<see cref="AiToolDescriptionPinV1"/>);
/// a changed card (description, fields, permissions) is excluded until a human approves the new pin.
/// </summary>
public sealed class AiCommandToolCatalog
{
    private readonly IReadOnlyDictionary<(string ToolId, string Version), IAiOwnerCommandToolV1> tools;
    private readonly HashSet<string> closedToolIds;

    public AiCommandToolCatalog(IEnumerable<IAiOwnerToolV1> ownerTools, IOptions<AiCommandOptions> options)
    {
        ArgumentNullException.ThrowIfNull(ownerTools);
        ArgumentNullException.ThrowIfNull(options);
        List<AiAllowedToolOptions>? configured;
        try
        {
            configured = options.Value?.AllowedTools;
        }
        catch (InvalidOperationException)
        {
            configured = null;
        }

        var allowlist = (configured ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry?.ToolId) && !string.IsNullOrWhiteSpace(entry?.Version))
            .Select(entry => (entry.ToolId!, entry.Version!))
            .ToHashSet();
        var pins = AiToolPinAllowlistV1.ApprovedPins(configured ?? []);

        var registered = ownerTools.Where(tool => tool is not null).ToArray();
        var duplicateIds = registered
            .GroupBy(tool => tool.Descriptor.ToolId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var commands = registered
            .OfType<IAiOwnerCommandToolV1>()
            .Where(tool => tool.Descriptor.Risk == AiToolRiskClassV1.Write && tool.Descriptor.OwnerCommandName is not null)
            .Where(tool => !duplicateIds.Contains(tool.Descriptor.ToolId))
            .ToArray();
        closedToolIds = commands
            .Where(AiCommandClosedListV1.IsClosed)
            .Select(tool => tool.Descriptor.ToolId)
            .ToHashSet(StringComparer.Ordinal);
        tools = commands
            .Where(tool => !closedToolIds.Contains(tool.Descriptor.ToolId))
            .Where(tool => allowlist.Contains((tool.Descriptor.ToolId, tool.Descriptor.Version)))
            .Where(tool => AiToolPinAllowlistV1.Holds(pins, tool.Descriptor))
            .ToDictionary(tool => (tool.Descriptor.ToolId, tool.Descriptor.Version));
    }

    public IReadOnlyList<AiToolDescriptorV1> Descriptors =>
    [
        .. tools.Values
            .Select(tool => tool.Descriptor)
            .OrderBy(descriptor => descriptor.ToolId, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Version, StringComparer.Ordinal),
    ];

    public IAiOwnerCommandToolV1? Resolve(string? toolId, string? version) =>
        toolId is not null && version is not null && tools.TryGetValue((toolId, version), out var tool) ? tool : null;

    /// <summary>True when a registered command tool with this id is on the closed list (refused with its own code).</summary>
    public bool IsClosed(string? toolId) => toolId is not null && closedToolIds.Contains(toolId);
}
