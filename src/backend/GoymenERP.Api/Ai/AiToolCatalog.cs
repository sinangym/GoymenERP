using GoymenERP.BuildingBlocks.Ai;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai;

/// <summary>
/// Default-deny READ tool catalog (ERP-000J §8 step 3). A tool is callable only when an owner module registered an
/// <see cref="IAiOwnerReadToolV1"/> with READ risk AND configuration allowlists its exact id and version. Draft,
/// simulate and write tools are never admitted here. A tool id registered more than once is excluded entirely.
/// AI-3 ships no module tool; concrete owner READ tools arrive with AI-6.
/// WP-A6: the allowlist entry also carries the approved pin of the tool's model card
/// (<see cref="AiToolDescriptionPinV1"/>). A registered card that no longer hashes to the approved pin — a reworded
/// description, a widened field list — is excluded and reported in <see cref="PinMismatchedTools"/>.
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
        var entries = (configured ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry?.ToolId) && !string.IsNullOrWhiteSpace(entry?.Version))
            .ToArray();
        var allowlist = entries.Select(entry => (entry.ToolId!, entry.Version!)).ToHashSet();
        // Two entries for one id+version with different pins are ambiguous: neither pin counts as approved.
        var pins = entries
            .GroupBy(entry => (entry.ToolId!, entry.Version!))
            .Where(group => group.Select(entry => entry.DescriptionSha256).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().DescriptionSha256);

        var registered = ownerTools.Where(tool => tool is not null).ToArray();
        var duplicateIds = registered
            .GroupBy(tool => tool.Descriptor.ToolId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = registered
            .OfType<IAiOwnerReadToolV1>()
            .Where(tool => tool.Descriptor.Risk == AiToolRiskClassV1.Read)
            .Where(tool => tool.Descriptor.OwnerCommandName is null)
            .Where(tool => !duplicateIds.Contains(tool.Descriptor.ToolId))
            .Where(tool => allowlist.Contains((tool.Descriptor.ToolId, tool.Descriptor.Version)))
            .ToArray();

        bool PinHolds(IAiOwnerReadToolV1 tool) =>
            pins.TryGetValue((tool.Descriptor.ToolId, tool.Descriptor.Version), out var pin)
            && AiToolDescriptionPinV1.Matches(tool.Descriptor, pin);

        tools = candidates.Where(PinHolds).ToDictionary(tool => (tool.Descriptor.ToolId, tool.Descriptor.Version));
        PinMismatchedTools =
        [
            .. candidates
                .Where(tool => !PinHolds(tool))
                .Select(tool => new AiToolPinMismatchV1(tool.Descriptor.ToolId, tool.Descriptor.Version))
                .OrderBy(entry => entry.ToolId, StringComparer.Ordinal)
                .ThenBy(entry => entry.Version, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Allowlisted tools kept out because the approved card pin is missing, ambiguous or stale. Identity only; the
    /// administrator compares the new card and approves a new pin (a human gate).
    /// </summary>
    public IReadOnlyList<AiToolPinMismatchV1> PinMismatchedTools { get; }

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

/// <summary>An allowlisted tool excluded because its model card no longer matches the approved pin.</summary>
public sealed record AiToolPinMismatchV1(string ToolId, string Version);
