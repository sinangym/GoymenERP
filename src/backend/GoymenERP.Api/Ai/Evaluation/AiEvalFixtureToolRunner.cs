using System.Text.Json;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Ai;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>
/// WP-A2 tool runner over the committed SENTETIK fixtures. <c>decisions.lookup.v1</c> is the real
/// <see cref="DecisionLookupAiTool"/> over the decision index (masking included). <c>logistics.pending-shipments.v1</c>
/// returns the item's recorded owner result: the tool itself is run against the same 20 fixtures in
/// <c>LogisticsPendingShipmentsAiToolTests</c>, so here only the model's use of the result is measured. A shipment call
/// whose canonical arguments differ from the recorded call, or a call without a recorded result, is unavailable (the
/// fixture does not cover it) and never a guess. No database, no network, no other company.
/// </summary>
public sealed class AiEvalFixtureToolRunner : IAiEvalToolRunnerV1
{
    public const string ShipmentFixtureTitle = "Bekleyen sevkiyatlar (SENTETIK fikstür)";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly DecisionLookupAiTool decisions;
    private readonly AiServerContextV1 context;
    private readonly TimeProvider timeProvider;

    public AiEvalFixtureToolRunner(AiDecisionIndex? decisionIndex, Guid companyId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        this.timeProvider = timeProvider;
        decisions = new DecisionLookupAiTool(new FixedDecisionIndexSource(decisionIndex), timeProvider);
        context = new AiServerContextV1(companyId, "ai-eval-runner", "ai-eval-session", "ai-eval-scope@1",
            "ai-eval-authority@1", "ai-eval", null, null, null);
        CompanyId = companyId;
        Catalog =
        [
            new AiModelToolV1(DecisionLookupAiTool.ToolId, DecisionLookupAiTool.ToolVersion, decisions.Descriptor.DataClass,
                decisions.Descriptor.FieldAllowlist),
            new AiModelToolV1(LogisticsPendingShipmentsAiTool.ToolId, LogisticsPendingShipmentsAiTool.ToolVersion,
                AiDataClassV1.Internal, [LogisticsPendingShipmentsAiTool.LimitField, LogisticsPendingShipmentsAiTool.StatusField]),
        ];
    }

    public Guid CompanyId { get; }

    public IReadOnlyList<AiModelToolV1> Catalog { get; }

    public Task<AiReadResultV1> ReadAsync(AiEvalItemV1 item, AiPlannedToolCallV1 call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();

        if (Is(call, DecisionLookupAiTool.ToolId, DecisionLookupAiTool.ToolVersion))
            return decisions.ReadAsync(new AiToolInputV1(call.ArgumentsJson), context, cancellationToken);

        if (Is(call, LogisticsPendingShipmentsAiTool.ToolId, LogisticsPendingShipmentsAiTool.ToolVersion))
            return Task.FromResult(ShipmentFixture(item, call));

        return Task.FromResult(new AiReadResultV1(AiToolOutcomeV1.Unavailable));
    }

    private AiReadResultV1 ShipmentFixture(AiEvalItemV1 item, AiPlannedToolCallV1 call)
    {
        if (item.ExpectedToolResult is not { } expected || item.ExpectedToolCall is not { } recorded
            || !SameArguments(recorded.ArgumentsJson, call.ArgumentsJson))
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);

        if (item.FixturePermission == AiEvalSetV1.PermissionServiceFailure)
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);

        switch (expected.Outcome)
        {
            case "denied":
                return new AiReadResultV1(AiToolOutcomeV1.Denied);
            case "invalid":
                return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [AiInvalidFieldsV1.Arguments]);
            case "succeeded":
                break;
            default:
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var content = JsonSerializer.Serialize(new
        {
            definition = LogisticsPendingShipmentsAiTool.Definition,
            pendingCount = expected.PendingCount,
            draftCount = expected.DraftCount,
            issuedCount = expected.IssuedCount,
            fullyShippedExcludedCount = expected.FullyShippedExcludedCount,
            listedCount = expected.ListedCount ?? 0,
            complete = expected.Complete ?? false,
        }, Json);
        var source = new AiSourceV1(LogisticsPendingShipmentsAiTool.ToolId, LogisticsPendingShipmentsAiTool.ToolVersion,
            LogisticsPendingShipmentsAiTool.TargetKind, CompanyId, ShipmentFixtureTitle, $"fikstur:{item.Id}",
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, content, source);
    }

    private static bool Is(AiPlannedToolCallV1 call, string toolId, string version) =>
        string.Equals(call.ToolId, toolId, StringComparison.Ordinal) && string.Equals(call.ToolVersion, version, StringComparison.Ordinal);

    private static bool SameArguments(string recorded, string planned)
    {
        try
        {
            return string.Equals(AiCanonicalJson.Canonicalize(recorded), AiCanonicalJson.Canonicalize(planned), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class FixedDecisionIndexSource(AiDecisionIndex? index) : IAiDecisionIndexSource
    {
        public AiDecisionIndex? Current { get; } = index;
    }
}
