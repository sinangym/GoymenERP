using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using System.Text.Json;

namespace GoymenERP.Modules.Manufacturing.Application.Ai;

/// <summary>
/// WP-A3b READ tool <c>manufacturing.plan-status.v1</c>: the company's production plans waiting for approval
/// (<c>Draft</c>) and approved (<c>Approved</c>) — or one asked status, cancelled included — counted per status and process
/// with the exact planned input kg total. Read ONLY through the owner's plan list
/// (<see cref="IManufacturingAiReadSourceV1.ListPlansAsync"/>, company-filtered stored rows); the tool measures
/// <c>manufacturing.plans.read</c> company-wide before and after reading. Over the scan cap the counts are withheld and
/// <c>complete</c> is false. Kilogram only.
/// </summary>
public sealed class ManufacturingPlanStatusAiTool(IManufacturingAiReadSourceV1 source, IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "manufacturing.plan-status.v1";
    public const string ToolVersion = "1";
    public const string StatusField = "status";
    public const string LimitField = "limit";
    public const string DraftStatus = "Draft";
    public const string ApprovedStatus = "Approved";
    public const string CancelledStatus = "Cancelled";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "manufacturing.plans";
    private static readonly string[] Fields = [LimitField, StatusField];
    private static readonly string[] Statuses = [DraftStatus, ApprovedStatus, CancelledStatus];
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
            InputSchema: "manufacturing.plan-status.v1/input@1",
            OutputSchema: "manufacturing.plan-status.v1/output@1",
            FieldPolicy: "manufacturing.plan-status.v1/fields@1",
            LimitPolicy: "manufacturing.plan-status.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+manufacturing.plans.read@1",
            SourceContract: "ManufacturingCommandService.ListPlansAsync@v1",
            RedactionPolicy: "manufacturing.plan-status.v1/no-cost-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid))
        {
            return Invalid(invalid);
        }

        if (!arguments.TryChoice(StatusField, Statuses, out var status)) return Invalid(StatusField);
        if (!arguments.TryInt(LimitField, 1, MaximumLimit, DefaultLimit, out var limit)) return Invalid(LimitField);
        var statuses = status is null ? new[] { DraftStatus, ApprovedStatus } : new[] { status };

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } refused)
        {
            return new AiReadResultV1(refused);
        }

        var rows = new List<PlanSummaryViewV1>();
        var complete = true;
        try
        {
            foreach (var wanted in statuses)
            {
                string? cursor = null;
                do
                {
                    var result = await source.ListPlansAsync(PageSize, cursor, wanted, cancellationToken);
                    if (result.Status == ManufacturingApiStatus.Denied)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Denied);
                    }

                    if (result.Status != ManufacturingApiStatus.Ok || result.Resource is not { } page)
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    foreach (var plan in page.Plans)
                    {
                        if (!string.Equals(plan.Status, wanted, StringComparison.Ordinal))
                        {
                            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                        }

                        rows.Add(plan);
                    }

                    cursor = page.NextCursor;
                    if (cursor is not null && rows.Count >= ScanLimit)
                    {
                        complete = false;
                        break;
                    }
                }
                while (cursor is not null);

                if (!complete)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken) is { } after)
        {
            return new AiReadResultV1(after);
        }

        if (rows.Select(row => row.PlanId).Distinct().Count() != rows.Count
            || !AiExactDecimalV1.TrySum(rows.Select(row => row.PlannedInputKg), out var plannedKg))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.PlanId).ToArray();
        var byStatus = statuses
            .Select(wanted => new ManufacturingStatusCountV1(wanted, ordered.Count(row => row.Status == wanted))).ToArray();
        var byProcess = ordered.GroupBy(row => row.Process, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ManufacturingProcessCountV1(group.Key, group.Count())).ToArray();
        var listed = ordered.Take(limit).Select(row => new ManufacturingPlanItemV1(
            row.PlanId, row.WorkCenterId, row.Process, row.Status, row.PlannedRollCount,
            AiExactDecimalV1.Format(Parse(row.PlannedInputKg)), row.CreatedAtUtc, row.ApprovedAtUtc)).ToArray();
        var content = new ManufacturingPlanStatusContentV1(
            ManufacturingAiUnitsV1.Kilogram,
            statuses,
            complete,
            complete ? ordered.Length : null,
            complete ? AiExactDecimalV1.Format(plannedKg) : null,
            complete ? byStatus : null,
            complete ? byProcess : null,
            listed.Length,
            listed);
        var sourceInfo = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Üretim planları",
            AiSourceVersionV1.Of(ordered.Select(row => $"{row.PlanId:D}:{row.Version}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), sourceInfo);
    }

    private static decimal Parse(string text) => AiExactDecimalV1.TryParse(text, out var value) ? value : throw new FormatException();

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

public sealed record ManufacturingStatusCountV1(string Status, int Count);

/// <summary>Counts and the total are null when <c>Complete</c> is false.</summary>
public sealed record ManufacturingPlanStatusContentV1(
    string Unit,
    IReadOnlyList<string> Statuses,
    bool Complete,
    int? PlanCount,
    string? PlannedInputKgTotal,
    IReadOnlyList<ManufacturingStatusCountV1>? ByStatus,
    IReadOnlyList<ManufacturingProcessCountV1>? ByProcess,
    int ListedCount,
    IReadOnlyList<ManufacturingPlanItemV1> Items);

public sealed record ManufacturingPlanItemV1(
    Guid PlanId,
    Guid WorkCenterId,
    string Process,
    string Status,
    int PlannedRollCount,
    string PlannedInputKg,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc);
