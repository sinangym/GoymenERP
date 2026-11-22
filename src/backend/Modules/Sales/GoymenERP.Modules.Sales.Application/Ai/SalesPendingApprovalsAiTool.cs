using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application.Ai;

/// <summary>
/// WP-A3a READ tool <c>sales.pending-approvals.v1</c> (DALGA_PLANI_v2 A.3): the order revisions of the company truly waiting
/// for the internal approval decision — counted and listed — read ONLY through the Sales owner's own pending-approval read
/// (<see cref="ISalesOrderDraftPendingApprovalReadServiceV1.ListAsync"/>). That read re-measures <c>sales.drafts.read</c> and
/// <c>sales.orders.approve</c> (company-wide) for the server-side actor and company at every fence and keeps the Sales persona
/// rule (a revision the caller may not decide is left out by the owner, never by this tool). No SQL, no table, no other module.
/// <para>
/// Counts are exact integers over every page read; when the page cap is reached the count is withheld (null) and
/// <c>complete</c> is false — never an estimate. The submitter's identity and every free text are not released.
/// </para>
/// </summary>
public sealed class SalesPendingApprovalsAiTool(ISalesOrderDraftPendingApprovalReadServiceV1 approvals, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "sales.pending-approvals.v1";
    public const string ToolVersion = "1";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;

    /// <summary>The owner pages at most 50 revisions (each with its detail read); ten pages bound one answer.</summary>
    public const int PageLimit = 10;

    public const string Definition = "sales.internal-approval.pending@owner";
    public const string TargetKind = "sales.orders.pending-approval";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Sales",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove],
        fieldAllowlist: [LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "sales.pending-approvals.v1/input@1",
            OutputSchema: "sales.pending-approvals.v1/output@1",
            FieldPolicy: "sales.pending-approvals.v1/fields@1",
            LimitPolicy: "sales.pending-approvals.v1/limit-50-pages-10@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+sales.drafts.read+sales.orders.approve@1",
            SourceContract: "ISalesOrderDraftPendingApprovalReadServiceV1.ListAsync@v1",
            RedactionPolicy: "sales.pending-approvals.v1/no-actor-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = DefaultLimit;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [LimitField] = value => AiReadToolArgumentsV1.TryInt(value, 1, MaximumLimit, out limit),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        var ownerContext = new SalesOrderDraftPendingApprovalReadContextV1(context.CompanyId, context.ActorId);
        var rows = new List<SalesOrderDraftPendingApprovalItemV1>();
        var complete = true;
        string? cursor = null;
        var pages = 0;
        try
        {
            do
            {
                if (pages == PageLimit)
                {
                    complete = false;
                    break;
                }

                var result = await approvals.ListAsync(ownerContext, cursor, cancellationToken);
                pages++;
                switch (result?.Outcome)
                {
                    case SalesOrderDraftPendingApprovalReadOutcomeV1.Found when result.Resource is { Items: not null } page
                                                                              && page.CompanyId == context.CompanyId:
                        foreach (var item in page.Items)
                        {
                            // The owner answered for another company: release nothing (fail-closed).
                            if (item is null || item.CompanyId != context.CompanyId || item.DraftId == Guid.Empty
                                || string.IsNullOrWhiteSpace(item.DocumentNo))
                            {
                                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                            }

                            rows.Add(item);
                        }

                        cursor = page.NextCursor;
                        break;
                    case SalesOrderDraftPendingApprovalReadOutcomeV1.Denied:
                        return new AiReadResultV1(AiToolOutcomeV1.Denied);
                    default:
                        return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                }
            }
            while (cursor is not null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (rows.Select(row => row.DraftId).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows.OrderBy(row => row.SubmittedAtUtc).ThenBy(row => row.DraftId).ToArray();
        var listed = ordered.Take(limit).Select(row => new SalesPendingApprovalItemV1(
            row.DraftId, row.DocumentNo, row.Revision, row.Version, row.SubmittedAtUtc, row.IsOwnDraft)).ToArray();
        var content = new SalesPendingApprovalsContentV1(
            Definition,
            complete,
            complete ? ordered.Length : null,
            complete ? ordered.Count(row => row.IsOwnDraft) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Onay bekleyen satış siparişleri",
            AiReadToolArgumentsV1.SourceVersion(ordered.Select(row => $"{row.DraftId:D}:{row.Version}:{row.Revision}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>The released fields only: no submitter, no rejection reason, no free text. Counts are null when not complete.</summary>
public sealed record SalesPendingApprovalsContentV1(
    string Definition,
    bool Complete,
    int? PendingCount,
    int? OwnDraftCount,
    int ListedCount,
    IReadOnlyList<SalesPendingApprovalItemV1> Items);

public sealed record SalesPendingApprovalItemV1(
    Guid DraftId,
    string DocumentNo,
    string Revision,
    string Version,
    DateTimeOffset SubmittedAtUtc,
    bool IsOwnDraft);
