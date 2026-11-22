using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using System.Text.Json;

namespace GoymenERP.Modules.Manufacturing.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>manufacturing.work-center-queue.v1</c>: the queue of the work center a given plan belongs to, in the
/// owner's order (URT-D97: termin, priority score, creation; or the stored manual order), with exact planned / processed /
/// remaining kg per plan and their totals. Read ONLY through the owner's queue read
/// (<see cref="IManufacturingAiReadSourceV1.GetPlanWorkCenterQueueAsync"/>, which measures <c>manufacturing.plans.read</c>
/// company-wide); the tool measures the same name for the server company before and after. Another company's plan is
/// not found. The queue order is the owner's, never recomputed here. Kilogram only.
/// </summary>
public sealed class ManufacturingWorkCenterQueueAiTool(IManufacturingAiReadSourceV1 source, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "manufacturing.work-center-queue.v1";
    public const string ToolVersion = "1";
    public const string PlanIdField = "planId";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const string TargetKind = "manufacturing.work-center-queue";
    private static readonly string[] Fields = [LimitField, PlanIdField];
    private static readonly string[] Permissions = [ErpPermissions.ManufacturingPlansRead];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Manufacturing",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: Permissions,
        fieldAllowlist: Fields,
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "manufacturing.work-center-queue.v1/input@1",
            OutputSchema: "manufacturing.work-center-queue.v1/output@1",
            FieldPolicy: "manufacturing.work-center-queue.v1/fields@1",
            LimitPolicy: "manufacturing.work-center-queue.v1/limit-50@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+manufacturing.plans.read@1",
            SourceContract: "ManufacturingCommandService.GetPlanWorkCenterQueueAsync@v1",
            RedactionPolicy: "manufacturing.work-center-queue.v1/no-cost-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryGuid(PlanIdField, out var planId) || planId is null) return Invalid(PlanIdField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        ManufacturingApiResult<PlanWorkCenterQueueViewV1> result;
        try
        {
            result = await source.GetPlanWorkCenterQueueAsync(planId.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } after)
        {
            return new AiReadResultV1(after);
        }

        switch (result.Status)
        {
            case ManufacturingApiStatus.Denied:
                return new AiReadResultV1(AiToolOutcomeV1.Denied);
            case ManufacturingApiStatus.NotFound:
                return new AiReadResultV1(AiToolOutcomeV1.NotFound);
            case ManufacturingApiStatus.Ok when result.Resource is { } queue:
                return Answer(queue, planId.Value, limit, context);
            default:
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }
    }

    private AiReadResultV1 Answer(PlanWorkCenterQueueViewV1 queue, Guid planId, int limit, AiServerContextV1 context)
    {
        // Another company's queue, another plan's queue or a repeated plan in it: release nothing (fail-closed).
        if (queue.CompanyId != context.CompanyId || queue.TargetPlanId != planId
            || queue.Items.Select(item => item.PlanId).Distinct().Count() != queue.Items.Count
            || !AiExactDecimalV1.TrySum(queue.Items.Select(item => item.PlannedKg), out var planned)
            || !AiExactDecimalV1.TrySum(queue.Items.Select(item => item.ProcessedKg), out var processed)
            || !AiExactDecimalV1.TrySum(queue.Items.Select(item => item.RemainingKg), out var remaining))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var listed = queue.Items.Take(limit).Select((item, index) => new ManufacturingQueueItemV1(
            index + 1, item.PlanId, item.DueDate, item.PriorityScore, Format(item.PlannedKg), Format(item.ProcessedKg),
            Format(item.RemainingKg), item.IsOverPlanned, item.IsQueued)).ToArray();
        var content = new ManufacturingWorkCenterQueueContentV1(
            ManufacturingAiUnitsV1.Kilogram,
            queue.WorkCenterId,
            planId,
            queue.HasManualOrder,
            true,
            queue.Items.Count,
            queue.Items.Count(item => item.IsQueued),
            queue.Items.Count(item => item.IsOverPlanned),
            AiExactDecimalV1.Format(planned),
            AiExactDecimalV1.Format(processed),
            AiExactDecimalV1.Format(remaining),
            listed.Length,
            listed);
        var sourceInfo = new AiSourceV1(ToolId, ToolVersion, TargetKind, queue.WorkCenterId, "İş merkezi kuyruğu",
            AiSourceVersionV1.Of(
                new[] { $"{queue.WorkCenterId:D}:{queue.QueueRevision}:{queue.TargetPlanVersion}" }
                    .Concat(queue.Items.Select(item => $"{item.PlanId:D}:{item.PriorityRevision}:{item.RemainingKg}"))),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), sourceInfo);
    }

    private static string Format(string text) =>
        AiExactDecimalV1.TryParse(text, out var value) ? AiExactDecimalV1.Format(value) : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>
/// The owner's whole queue is read in one answer, so totals are always given (<c>Complete</c> true); only the item list is
/// capped by <c>limit</c>. <c>Position</c> is the 1-based place in the owner's order.
/// </summary>
public sealed record ManufacturingWorkCenterQueueContentV1(
    string Unit,
    Guid WorkCenterId,
    Guid TargetPlanId,
    bool HasManualOrder,
    bool Complete,
    int PlanCount,
    int QueuedCount,
    int OverPlannedCount,
    string PlannedKgTotal,
    string ProcessedKgTotal,
    string RemainingKgTotal,
    int ListedCount,
    IReadOnlyList<ManufacturingQueueItemV1> Items);

public sealed record ManufacturingQueueItemV1(
    int Position,
    Guid PlanId,
    DateOnly? DueDate,
    int PriorityScore,
    string PlannedKg,
    string ProcessedKg,
    string RemainingKg,
    bool IsOverPlanned,
    bool IsQueued);
