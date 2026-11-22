using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.WorkRuntime.Ai;

/// <summary>
/// WP-A3c READ tool <c>work.inbox-awaiting-me.v1</c>: the approvals waiting for the caller's decision ("benden karar
/// bekleyen") — counted by module and kind, with the overdue count, and listed — read ONLY through the Work inbox read
/// query (<see cref="WorkInboxReadQueryV1.ListAsync"/>). The query is used as it is: each source is asked only when the
/// caller holds that owner's approve permission (checked by Work and again by the owner), a record never waits on its own
/// maker (maker ≠ checker), and the company is the server's. The tool therefore names no permission of its own; without
/// any decidable source the answer is refused. The owner's explanation card (rule values) is not released.
/// </summary>
internal sealed class WorkInboxAwaitingMeAiTool(WorkInboxReadQueryV1 inbox) : IAiOwnerReadToolV1
{
    public const string ToolId = "work.inbox-awaiting-me.v1";
    public const string ToolVersion = "1";

    public AiToolDescriptorV1 Descriptor { get; } = WorkInboxAiRead.Descriptor(ToolId, ToolVersion,
        "work.inbox-awaiting-me.v1", "work.inbox-awaiting-me.v1/no-explanation-values@1");

    public Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default) =>
        WorkInboxAiRead.ReadAsync(inbox, input, context, ToolId, ToolVersion, overdueOnly: false,
            "work.inbox.awaiting-my-decision", "Benden karar bekleyen onaylar", cancellationToken);
}

/// <summary>
/// WP-A3c READ tool <c>work.overdue-approvals.v1</c>: of the approvals waiting for the caller's decision, those past the
/// Settings duration (YOL-D18, computed by the inbox itself) — counted and listed — through the same Work inbox read with
/// the same maker ≠ checker and permission scope. An item whose duration is not defined is neither overdue nor dropped
/// silently: it is counted as <c>durationUndefinedCount</c>.
/// </summary>
internal sealed class WorkOverdueApprovalsAiTool(WorkInboxReadQueryV1 inbox) : IAiOwnerReadToolV1
{
    public const string ToolId = "work.overdue-approvals.v1";
    public const string ToolVersion = "1";

    public AiToolDescriptorV1 Descriptor { get; } = WorkInboxAiRead.Descriptor(ToolId, ToolVersion,
        "work.overdue-approvals.v1", "work.overdue-approvals.v1/no-explanation-values@1");

    public Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default) =>
        WorkInboxAiRead.ReadAsync(inbox, input, context, ToolId, ToolVersion, overdueOnly: true,
            "work.inbox.overdue", "Geciken onaylar", cancellationToken);
}

internal static class WorkInboxAiRead
{
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public static AiToolDescriptorV1 Descriptor(string toolId, string version, string schema, string redaction) => new(
        toolId,
        version,
        ownerModule: "Work",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        // The inbox measures each source's own approve permission; a tool-level grant would widen or duplicate that scope.
        requiredPermissions: [],
        fieldAllowlist: [LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: schema + "/input@1",
            OutputSchema: schema + "/output@1",
            FieldPolicy: schema + "/fields@1",
            LimitPolicy: schema + "/limit-50-per-source-owner-cap@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+work-inbox-source-approver-permissions@1",
            SourceContract: "WorkInboxReadQueryV1.ListAsync(awaiting-my-decision)@v1",
            RedactionPolicy: redaction));

    public static async Task<AiReadResultV1> ReadAsync(WorkInboxReadQueryV1 inbox, AiToolInputV1 input, AiServerContextV1 context,
        string toolId, string version, bool overdueOnly, string targetKind, string title, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, Arguments, out var arguments, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        WorkInboxReadResultV1<WorkInboxPageV1> result;
        try
        {
            result = await inbox.ListAsync(WorkInboxViewsV1.AwaitingMyDecision, null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (result is not { Outcome: WorkInboxReadOutcomeV1.Found, Resource: { } page })
        {
            return new AiReadResultV1(result?.Outcome == WorkInboxReadOutcomeV1.Denied ? AiToolOutcomeV1.Denied : AiToolOutcomeV1.Unavailable);
        }

        if (page.CompanyId != context.CompanyId || page.Items.Any(item => item is null || item.Duration is null)
            || page.Items.Select(item => (item.Kind, item.ItemId)).Distinct().Count() != page.Items.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        // A source that could not answer, or that holds more than it showed, makes the counts incomplete: withhold them.
        var complete = page.Sources.All(source => source.State == WorkInboxReadQueryV1.SourceReady && !source.HasMore);
        var selected = overdueOnly
            ? page.Items.Where(item => item.Duration.IsOverdue == true).ToArray()
            : page.Items.ToArray();
        var byKind = selected
            .GroupBy(item => (item.Module, item.Kind))
            .Select(group => new WorkInboxKindCountV1(group.Key.Module, group.Key.Kind, group.Count()))
            .OrderBy(group => group.Module, StringComparer.Ordinal)
            .ThenBy(group => group.Kind, StringComparer.Ordinal)
            .ToArray();
        var listed = selected
            .OrderBy(item => item.WaitingSinceUtc)
            .ThenBy(item => item.Module, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.ItemId)
            .Take(arguments.Integer(LimitField))
            .Select(item => new WorkInboxAiItemV1(item.Module, item.Kind, item.ItemId, item.DocumentLabel, item.Link,
                item.WaitingSinceUtc, item.AwaitingPermission, item.Duration.State, item.Duration.DueAtUtc, item.Duration.IsOverdue))
            .ToArray();
        var content = new WorkInboxAiContentV1(
            page.View,
            overdueOnly,
            complete,
            complete ? selected.Length : null,
            complete ? page.Items.Count(item => item.Duration.IsOverdue == true) : null,
            complete ? page.Items.Count(item => item.Duration.IsOverdue is null) : null,
            complete ? byKind : [],
            page.Sources.Where(source => source.State != WorkInboxReadQueryV1.SourceReady || source.HasMore)
                .Select(source => new WorkInboxAiSourceGapV1(source.Module, source.Kind, source.State, source.HasMore))
                .ToArray(),
            listed.Length,
            listed);
        var source = new AiSourceV1(toolId, version, targetKind, context.CompanyId, title,
            AiReadToolSupportV1.Digest(page.Items.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.ItemId)
                .Select(item => $"{item.Kind}:{item.ItemId:D}:{item.WaitingSinceUtc:O}")),
            page.GeneratedAtUtc.ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>
/// Released fields only. Counts are null (and <c>ByKind</c> empty) when <c>Complete</c> is false; <c>SourceGaps</c> names
/// each source that could not answer or holds more than it showed. <c>OverdueCount</c> and <c>DurationUndefinedCount</c>
/// are over every item waiting for the caller.
/// </summary>
internal sealed record WorkInboxAiContentV1(
    string View,
    bool OverdueOnly,
    bool Complete,
    int? Count,
    int? OverdueCount,
    int? DurationUndefinedCount,
    IReadOnlyList<WorkInboxKindCountV1> ByKind,
    IReadOnlyList<WorkInboxAiSourceGapV1> SourceGaps,
    int ListedCount,
    IReadOnlyList<WorkInboxAiItemV1> Items);

internal sealed record WorkInboxKindCountV1(string Module, string Kind, int Count);

internal sealed record WorkInboxAiSourceGapV1(string Module, string Kind, string State, bool HasMore);

internal sealed record WorkInboxAiItemV1(
    string Module,
    string Kind,
    Guid ItemId,
    string DocumentLabel,
    string Link,
    DateTimeOffset WaitingSinceUtc,
    string AwaitingPermission,
    string DurationState,
    DateTimeOffset? DueAtUtc,
    bool? IsOverdue);
