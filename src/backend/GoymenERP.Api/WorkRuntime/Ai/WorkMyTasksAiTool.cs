using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.WorkRuntime.Ai;

/// <summary>
/// WP-A3c READ tool <c>work.my-tasks.v1</c>: the open tasks assigned to the caller (W1-C) — counted as overdue, due today
/// (Europe/Istanbul business day, SAT-D10 / P7) or later, and listed by due time — read ONLY through the Work owner's
/// <see cref="IWorkCollaborationServiceV1.ListMyTasksAsync"/>, which takes the actor and company from the server and
/// requires active company membership. Only the caller's own tasks exist in this read; another person's are never asked
/// for. The task details, cancel reason and people's display names are not released (AI-D36 masking: no person names in
/// tool output). When the owner holds more than one page, the counts are withheld and <c>complete</c> is false.
/// </summary>
internal sealed class WorkMyTasksAiTool(IWorkCollaborationServiceV1 collaboration, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "work.my-tasks.v1";
    public const string ToolVersion = "1";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const string Overdue = "Overdue";
    public const string DueToday = "DueToday";
    public const string Upcoming = "Upcoming";
    public const string TargetKind = "work.tasks.assigned-to-me";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly AiReadToolArgumentV1[] Arguments =
    [
        AiReadToolArgumentV1.Integer(LimitField, 1, MaximumLimit, DefaultLimit),
    ];

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Work",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        // W1-C: "my tasks" is read by every active member of the company; no extra grant exists or is invented here.
        requiredPermissions: [],
        fieldAllowlist: [LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "work.my-tasks.v1/input@1",
            OutputSchema: "work.my-tasks.v1/output@1",
            FieldPolicy: "work.my-tasks.v1/fields@1",
            LimitPolicy: "work.my-tasks.v1/limit-50-owner-page@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+work-company-membership@1",
            SourceContract: "IWorkCollaborationServiceV1.ListMyTasksAsync(open)@v1",
            RedactionPolicy: "work.my-tasks.v1/title-only-no-details-no-names@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, Arguments, out var arguments, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        if (AiReadToolSupportV1.BusinessDay(now) is not { } today || !Guid.TryParse(context.ActorId, out var actor))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        WorkTaskPageV1 page;
        try
        {
            page = await collaboration.ListMyTasksAsync(includeClosed: false, cancellationToken);
        }
        catch (WorkCollaborationException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        // Another company's page, another person's task or a closed task in an open-only read: release nothing.
        if (page is null || page.CompanyId != context.CompanyId
            || page.Items.Any(task => task is null || task.CompanyId != context.CompanyId || task.AssigneeUserId != actor
                                      || !string.Equals(task.Status, WorkTaskV1.Wire(WorkTaskStatusV1.Open), StringComparison.Ordinal))
            || page.Items.Select(task => task.Id).Distinct().Count() != page.Items.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var tasks = new List<(WorkTaskViewV1 Task, string State)>();
        foreach (var task in page.Items)
        {
            if (AiReadToolSupportV1.BusinessDay(task.DueAtUtc) is not { } dueDay)
            {
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
            }

            tasks.Add((task, task.DueAtUtc < now ? Overdue : dueDay == today ? DueToday : Upcoming));
        }

        var complete = !page.HasMore;
        var listed = tasks
            .OrderBy(entry => entry.Task.DueAtUtc)
            .ThenBy(entry => entry.Task.Id)
            .Take(arguments.Integer(LimitField))
            .Select(entry => new WorkMyTaskItemV1(entry.Task.Id, entry.Task.Title, entry.Task.RecordType, entry.Task.RecordId,
                entry.Task.DueAtUtc, entry.State, entry.Task.AssignerUserId == actor, entry.Task.Version))
            .ToArray();
        var content = new WorkMyTasksContentV1(
            today,
            complete,
            complete ? tasks.Count : null,
            complete ? tasks.Count(entry => entry.State == Overdue) : null,
            complete ? tasks.Count(entry => entry.State == DueToday) : null,
            complete ? tasks.Count(entry => entry.State == Upcoming) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Bana atanan açık görevler",
            AiReadToolSupportV1.Digest(page.Items.OrderBy(task => task.Id).Select(task => $"{task.Id:D}:{task.Version}")), now,
            AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>Released fields only; counts are null when <c>Complete</c> is false.</summary>
internal sealed record WorkMyTasksContentV1(
    DateOnly BusinessDate,
    bool Complete,
    int? OpenCount,
    int? OverdueCount,
    int? DueTodayCount,
    int? UpcomingCount,
    int ListedCount,
    IReadOnlyList<WorkMyTaskItemV1> Items);

/// <summary><c>AssignedBySelf</c> is true for a task the caller gave themself; no person name is released.</summary>
internal sealed record WorkMyTaskItemV1(
    Guid TaskId,
    string Title,
    string? RecordType,
    Guid? RecordId,
    DateTimeOffset DueAtUtc,
    string DueState,
    bool AssignedBySelf,
    string Version);
