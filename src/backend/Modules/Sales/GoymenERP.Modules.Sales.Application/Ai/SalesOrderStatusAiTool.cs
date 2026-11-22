using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application.Ai;

/// <summary>
/// WP-A3a READ tool <c>sales.order-status.v1</c> (DALGA_PLANI_v2 A.3): the company's sales orders counted on the owner's two
/// separate axes — lifecycle (<c>Draft</c>, <c>Cancelled</c>, <c>Closed</c>) and internal approval (<c>Preparing</c>,
/// <c>PendingApproval</c>, <c>Approved</c>, <c>Rejected</c>, <c>Withdrawn</c>) — and the latest ones listed, read ONLY through
/// the Sales owner's paged draft list (<see cref="ISalesDraftService.GetDraftListAsync"/>), which measures
/// <c>sales.drafts.read</c> for the server-side actor and company and filters by tenant. No SQL, no table, no other module.
/// <para>
/// Counts are exact integers over every page read. With an approval-state filter only that state is read: the other approval
/// counts are null (not read), never zero. When the scan cap is reached, or the owner answers a state outside its known
/// vocabulary, nothing is guessed: the counts are withheld (cap) or nothing is released (vocabulary).
/// </para>
/// </summary>
public sealed class SalesOrderStatusAiTool(ISalesDraftService drafts, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "sales.order-status.v1";
    public const string ToolVersion = "1";
    public const string ApprovalStateField = "approvalState";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string TargetKind = "sales.orders.status";

    public static readonly IReadOnlyList<string> LifecycleStates = ["Draft", "Cancelled", "Closed"];
    public static readonly IReadOnlyList<string> ApprovalStates = ["Preparing", "PendingApproval", "Approved", "Rejected", "Withdrawn"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Sales",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [SalesPermissionManifestV1.DraftsRead],
        fieldAllowlist: [ApprovalStateField, LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "sales.order-status.v1/input@1",
            OutputSchema: "sales.order-status.v1/output@1",
            FieldPolicy: "sales.order-status.v1/fields@1",
            LimitPolicy: "sales.order-status.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+sales.drafts.read@1",
            SourceContract: "ISalesDraftService.GetDraftListAsync@v1",
            RedactionPolicy: "sales.order-status.v1/no-price-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = DefaultLimit;
        string? approvalState = null;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [ApprovalStateField] = value => AiReadToolArgumentsV1.TryName(value, [.. ApprovalStates], out approvalState),
                [LimitField] = value => AiReadToolArgumentsV1.TryInt(value, 1, MaximumLimit, out limit),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var rows = new List<SalesOrderDraftSummaryView>();
        var complete = true;
        string? cursor = null;
        try
        {
            do
            {
                var page = await drafts.GetDraftListAsync(new SalesOrderDraftListQuery(PageSize, cursor, approvalState), cancellationToken);
                if (page?.Items is null || page.Items.Count > PageSize)
                {
                    return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }

                foreach (var item in page.Items)
                {
                    // Another company's row, a state outside the owner vocabulary or a row outside the asked filter: fail-closed.
                    if (item is null || item.CompanyId != context.CompanyId || item.Id == Guid.Empty
                        || string.IsNullOrWhiteSpace(item.DocumentNo)
                        || !LifecycleStates.Contains(item.State) || !ApprovalStates.Contains(item.ApprovalState)
                        || approvalState is not null && !string.Equals(item.ApprovalState, approvalState, StringComparison.Ordinal))
                    {
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                    }

                    rows.Add(item);
                }

                cursor = page.NextCursor;
                if (rows.Count > ScanLimit)
                {
                    complete = false;
                    break;
                }
            }
            while (cursor is not null);
        }
        catch (SalesDraftServiceException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        int? Approval(string name) => complete && (approvalState is null || approvalState == name)
            ? rows.Count(row => row.ApprovalState == name)
            : null;
        int? Lifecycle(string name) => complete ? rows.Count(row => row.State == name) : null;

        var ordered = rows.OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id).ToArray();
        var listed = ordered.Take(limit).Select(row => new SalesOrderStatusItemV1(
            row.Id, row.DocumentNo, row.Kind, row.State, row.ApprovalState, row.Revision, row.Version, row.PartyId,
            row.CompanyAccountId, row.BusinessDate, row.LineCount, row.CreatedAtUtc)).ToArray();
        var content = new SalesOrderStatusContentV1(
            complete,
            approvalState,
            complete ? ordered.Length : null,
            new SalesOrderLifecycleCountsV1(Lifecycle("Draft"), Lifecycle("Cancelled"), Lifecycle("Closed")),
            new SalesOrderApprovalCountsV1(Approval("Preparing"), Approval("PendingApproval"), Approval("Approved"),
                Approval("Rejected"), Approval("Withdrawn")),
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Satış siparişleri durumu",
            AiReadToolArgumentsV1.SourceVersion(ordered.Select(row => $"{row.Id:D}:{row.Version}:{row.State}:{row.ApprovalState}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>The released fields only: no price, no note, no actor, no free text. Counts are null when not read or not complete.</summary>
public sealed record SalesOrderStatusContentV1(
    bool Complete,
    string? ApprovalStateFilter,
    int? TotalCount,
    SalesOrderLifecycleCountsV1 Lifecycle,
    SalesOrderApprovalCountsV1 Approval,
    int ListedCount,
    IReadOnlyList<SalesOrderStatusItemV1> Items);

public sealed record SalesOrderLifecycleCountsV1(int? Draft, int? Cancelled, int? Closed);

public sealed record SalesOrderApprovalCountsV1(int? Preparing, int? PendingApproval, int? Approved, int? Rejected, int? Withdrawn);

public sealed record SalesOrderStatusItemV1(
    Guid OrderId,
    string DocumentNo,
    string Kind,
    string State,
    string ApprovalState,
    string Revision,
    string Version,
    Guid PartyId,
    Guid CompanyAccountId,
    DateOnly BusinessDate,
    int LineCount,
    DateTimeOffset CreatedAtUtc);
