using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Ai;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Ai.Tools;

/// <summary>One exact Sales detail, with only closed status vocabulary and recorded line quantities.
/// No list scan, prices, actor names, free text, totals or derived quantities are released.</summary>
public sealed class SalesDraftSummaryAiToolV1(ISalesDraftService? sales, ICompanyContext? company,
    ICurrentUser? user, IErpAuthorityRevalidationService? authority, TimeProvider time) : IAiOwnerReadToolV1
{
    public const string ToolId = "sales.draft-summary.v1";
    public const string ToolVersion = "1";
    public const string TargetKind = "sales.order.draft-summary";
    public const string DraftIdField = "draftId";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Permissions = [SalesPermissionManifestV1.DraftsRead];

    public AiToolDescriptorV1 Descriptor { get; } = new(ToolId, ToolVersion, "Sales", AiToolRiskClassV1.Read,
        AiDataClassV1.Internal, Permissions, [DraftIdField], null,
        new("sales.draft-summary.v1/input@1", "sales.draft-summary.v1/output@1",
            "sales.draft-summary.v1/no-price-no-people-no-free-text@1", "sales.draft-summary.v1/one-draft@1",
            "contracts/ai/tool-catalog-v1.json", "ai.panel.use+sales.drafts.read@1",
            "ISalesDraftService.GetDraftDetailAsync@v1", "sales.draft-summary.v1/recorded-status-and-quantities@1"),
        "Reads one sales draft by draftId. Returns only its identity, version, revision, recorded lifecycle and approval states and line identities, units and quantity text. Does not calculate totals or disclose prices, notes or people.");

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, [DraftIdField], out var args, out var invalid))
            return new(AiToolOutcomeV1.Invalid, invalidFields: [invalid]);
        if (!args.TryGuid(DraftIdField, out var id) || id is null)
            return new(AiToolOutcomeV1.Invalid, invalidFields: [DraftIdField]);
        if (sales is null || company is null || user is null || authority is null) return new(AiToolOutcomeV1.Unavailable);
        if (await Fence() is { } refusal) return new(refusal);
        SalesOrderDraftDetailView detail;
        try { detail = await sales.GetDraftDetailAsync(id.Value, cancellationToken); }
        catch (SalesDraftServiceException error)
        {
            return new(error.Status switch { 401 or 403 => AiToolOutcomeV1.Denied, 404 => AiToolOutcomeV1.NotFound,
                409 => AiToolOutcomeV1.Conflict, _ => AiToolOutcomeV1.Unavailable });
        }
        catch (Exception error) when (error is not OperationCanceledException) { return new(AiToolOutcomeV1.Unavailable); }
        if (await Fence() is { } late) return new(late);
        if (detail?.Draft is not { } draft || draft.Id != id || draft.CompanyId != context.CompanyId
            || string.IsNullOrWhiteSpace(draft.Version) || string.IsNullOrWhiteSpace(draft.Revision)
            || !SalesOrderStatusAiTool.LifecycleStates.Contains(draft.State)
            || !SalesOrderStatusAiTool.ApprovalStates.Contains(draft.ApprovalState)
            || detail.Lines is null || detail.Lines.Count != draft.LineCount
            || detail.Lines.Any(line => line is null || line.LineId == Guid.Empty || line.LineNo < 1
                || line.ProductId == Guid.Empty || line.OrderUnitId == Guid.Empty
                || !AiExactDecimalV1.TryParse(line.Quantity, out var quantity) || quantity <= 0)
            || detail.Lines.Select(line => line.LineId).Distinct().Count() != detail.Lines.Count
            || detail.Lines.Select(line => line.LineNo).Distinct().Count() != detail.Lines.Count)
            return new(AiToolOutcomeV1.Unavailable);
        var content = new SalesDraftSummaryAiContentV1(draft.Id, draft.Version, draft.Revision, draft.State, draft.ApprovalState,
            detail.Lines.Select(line => new SalesDraftSummaryAiLineV1(line.LineId, line.LineNo, line.ProductId,
                line.OrderUnitId, line.Quantity)).ToArray());
        var json = JsonSerializer.Serialize(content, Json);
        return new(AiToolOutcomeV1.Succeeded, json, new(ToolId, ToolVersion, TargetKind, draft.Id, "Satış taslağı özeti",
            AiSourceVersionV1.Of([json]), time.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal));

        bool Current() => context.CompanyId != Guid.Empty && company is { IsResolved: true } && company.CompanyId == context.CompanyId
            && user is { IsAuthenticated: true } && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
        async Task<AiToolOutcomeV1?> Fence()
        {
            if (!Current()) return AiToolOutcomeV1.Denied;
            var denied = authority is null ? AiToolOutcomeV1.Unavailable
                : await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken);
            return !Current() ? AiToolOutcomeV1.Denied : denied;
        }
    }
}

public sealed record SalesDraftSummaryAiContentV1(Guid DraftId, string Version, string Revision, string State,
    string ApprovalState, IReadOnlyList<SalesDraftSummaryAiLineV1> Lines);
public sealed record SalesDraftSummaryAiLineV1(Guid LineId, int LineNo, Guid ProductId, Guid OrderUnitId, string Quantity);
