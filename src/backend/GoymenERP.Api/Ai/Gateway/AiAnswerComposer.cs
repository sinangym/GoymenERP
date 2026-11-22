using System.Globalization;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.Gateway;

/// <summary>
/// What a model sees when it composes text: the routed deployment, the task, the instruction and the numbered sources.
/// No company id, actor, session, authority or policy travels to the model.
/// </summary>
public sealed record AiComposeRequestV1(
    string ModelDeployment,
    AiTaskKindV1 TaskKind,
    string Instruction,
    IReadOnlyList<AiComposeSourceV1> Sources);

/// <summary>One numbered source (1-based <see cref="Number"/>) the model may cite.</summary>
public sealed record AiComposeSourceV1(int Number, string Title, string SourceVersion, DateTimeOffset AsOfUtc, string ContentJson);

/// <summary>Untrusted model output: text, the source numbers it claims to rest on, and the provider-reported units.</summary>
public sealed record AiComposedTextV1(string Text, IReadOnlyList<int> CitedSources, int UsageUnits);

/// <summary>
/// A model endpoint able to compose text. Selected by the runtime's provider kind AND the route's placement; a composer
/// for a placement that is not registered keeps every route of that placement unavailable (a local model waits for
/// OPEN-014). External composers are added only together with the provider decision (OPEN-020, AI-D5..AI-D7).
/// </summary>
public interface IAiAnswerComposerV1
{
    string ProviderKind { get; }

    AiModelPlacementV1 Placement { get; }

    Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic in-process composer for development and tests. It never reaches the network and never invents a value:
/// it restates the top-level scalar fields of every source with the source's title, version and as-of date, cites every
/// source, and reports one usage unit per started 4 characters of output (a stand-in for a provider's token count). It is
/// registered once per placement so routing can be exercised; the runtime never admits the Fake kind in Production.
/// </summary>
public sealed class FakeAiAnswerComposer(AiModelPlacementV1 placement) : IAiAnswerComposerV1
{
    private const int CharactersPerUsageUnit = 4;

    public string ProviderKind => AiProviderKindsV1.Fake;

    public AiModelPlacementV1 Placement { get; } = placement;

    public Task<AiComposedTextV1> ComposeAsync(AiComposeRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var text = new StringBuilder();
        foreach (var source in request.Sources)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(CultureInfo.InvariantCulture,
                $"[{source.Number}] {source.Title} ({source.SourceVersion}, {source.AsOfUtc:yyyy-MM-dd}):");
            foreach (var (name, value) in ScalarFields(source.ContentJson))
                text.Append(CultureInfo.InvariantCulture, $" {name}: {value};");
        }

        var composed = text.ToString();
        var units = Math.Max(1, (composed.Length + CharactersPerUsageUnit - 1) / CharactersPerUsageUnit);
        return Task.FromResult(new AiComposedTextV1(composed, request.Sources.Select(source => source.Number).ToArray(), units));
    }

    private static IEnumerable<(string Name, string Value)> ScalarFields(string contentJson)
    {
        using var document = JsonDocument.Parse(contentJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    yield return (property.Name, property.Value.GetString()!);
                    break;
                case JsonValueKind.Number:
                    yield return (property.Name, property.Value.GetRawText());
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    yield return (property.Name, property.Value.GetRawText());
                    break;
            }
        }
    }
}
