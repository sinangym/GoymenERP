using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Ai.Tools;

/// <summary>WP-B4 first bounded measure: the Sales owner's existing line margin and calculator trace.
/// No arithmetic, report engine, free-text source proof or client company is accepted here.</summary>
public sealed class SalesLineMarginExplanationAiToolV1(ISalesDraftService? sales, ICompanyContext? company,
    ICurrentUser? user, IErpAuthorityRevalidationService? authority, TimeProvider time) : IAiOwnerReadToolV1
{
    public const string ToolId = "sales.line-margin-explanation.v1";
    public const string ToolVersion = "1";
    public const string TargetKind = "sales.order.line-margin";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Permissions = [SalesPermissionManifestV1.DraftsRead,
        SalesPermissionManifestV1.OrdersPriceRead, SalesPermissionManifestV1.OrdersMarginRead];
    private static readonly string[] Fields = ["draftId", "lineId"];

    public AiToolDescriptorV1 Descriptor { get; } = new(ToolId, ToolVersion, "Sales", AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential, Permissions, ["draftId", "lineId"], null,
        new("sales.line-margin-explanation.v1/input@1", "sales.line-margin-explanation.v1/output@1",
            "sales.line-margin-explanation.v1/owner-trace-no-free-text@1", "sales.line-margin-explanation.v1/one-line@1",
            "contracts/ai/tool-catalog-v1.json", "ai.panel.use+sales.drafts.read+sales.orders.price.read+sales.orders.margin.read@1",
            "ISalesDraftService.ExplainDraftLineMarginAsync@v1", "sales.line-margin-explanation.v1/confidential-on-premises-only@1"),
        "Explains one sales order line margin using only the owner's existing amount, percentage and calculator steps. Requires draftId and lineId. Does not total, recalculate, compare periods or infer unavailable values.");

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AiReadArgumentsV1.TryParse(input.ArgumentsJson, Fields, out var arguments, out var invalid)) return Invalid(invalid);
        if (!arguments.TryGuid("draftId", out var draftId) || draftId is null) return Invalid("draftId");
        if (!arguments.TryGuid("lineId", out var lineId) || lineId is null) return Invalid("lineId");
        if (sales is null || company is null || user is null || authority is null) return new(AiToolOutcomeV1.Unavailable);
        if (await Fence() is { } refused) return new(refused);
        SalesDraftLineMarginExplanationV1 answer;
        try { answer = await sales.ExplainDraftLineMarginAsync(draftId.Value, lineId.Value, cancellationToken); }
        catch (SalesDraftServiceException exception)
        {
            return new(exception.Status switch { 403 => AiToolOutcomeV1.Denied, 404 => AiToolOutcomeV1.NotFound,
                409 => AiToolOutcomeV1.Conflict, _ => AiToolOutcomeV1.Unavailable });
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new(AiToolOutcomeV1.Unavailable); }
        if (await Fence() is { } late) return new(late);
        if (answer is null || answer.ContractVersion != SalesDraftLineMarginExplanationV1.CurrentContractVersion
            || answer.DraftId != draftId || answer.Margin is not { } margin || margin.LineId != lineId || margin.LineNo < 1
            || string.IsNullOrWhiteSpace(answer.Version) || answer.CalculatorVersion != SalesMarginTraceRulesV1.CalculatorVersion
            || answer.Steps is null || answer.Sources is null || answer.RuleReferences is null
            || answer.Steps.Any(step => step is null || string.IsNullOrWhiteSpace(step.Code)
                || !AiExactDecimalV1.TryParse(step.Value, out _) || step.Operands is null)
            || answer.Sources.Any(source => source is null)
            || answer.Steps.Select(step => step.Code).Distinct(StringComparer.Ordinal).Count() != answer.Steps.Count)
            return new(AiToolOutcomeV1.Unavailable);
        if (margin.State switch
            {
                "Computed" => !AiExactDecimalV1.TryParse(margin.Amount, out _) || margin.CurrencyId is null
                    || margin.CurrencyId == Guid.Empty || margin.UnavailableReason is not null
                    || margin.Percent is not null && !AiExactDecimalV1.TryParse(margin.Percent, out _),
                "Unavailable" => margin.Amount is not null || margin.CurrencyId is not null || margin.Percent is not null
                    || string.IsNullOrWhiteSpace(margin.UnavailableReason),
                _ => true,
            }) return new(AiToolOutcomeV1.Unavailable);
        // Values and operations are copied exactly; in particular unavailable and zero remain distinct.
        var content = new SalesLineMarginExplanationAiContentV1(answer.ContractVersion, answer.DraftId, answer.Version,
            answer.Kind, answer.CalculatorVersion, margin, answer.Steps,
            answer.Sources.Select(source => new SalesLineMarginAiSourceV1(source.Kind, source.RecordId, source.Revision,
                source.RecordedAtUtc)).ToArray(), answer.RuleReferences);
        var json = JsonSerializer.Serialize(content, Json);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, draftId.Value, "Satış satırı marj hesap izi",
            AiSourceVersionV1.Of([json]), time.GetUtcNow().ToUniversalTime(), AiDataClassV1.Confidential);
        return new(AiToolOutcomeV1.Succeeded, json, source);

        async Task<AiToolOutcomeV1?> Fence()
        {
            if (!Current()) return AiToolOutcomeV1.Denied;
            var refusal = authority is null ? AiToolOutcomeV1.Unavailable
                : await AiOwnerReadFenceV1.RequireCompanyWideAsync(authority, Permissions, context.CompanyId, cancellationToken);
            return !Current() ? AiToolOutcomeV1.Denied : refusal;
        }
        bool Current() => context.CompanyId != Guid.Empty && company is { IsResolved: true } && company.CompanyId == context.CompanyId
            && user is { IsAuthenticated: true } && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
    }

    private static AiReadResultV1 Invalid(string field) => new(AiToolOutcomeV1.Invalid, invalidFields: [field]);
}

/// <summary>Only defined owner facts; optional numeric text stays unchanged, including an unavailable null.</summary>
public sealed record SalesLineMarginExplanationAiContentV1(int ContractVersion, Guid DraftId, string DraftVersion,
    string Kind, string CalculatorVersion, SalesDraftLineMarginV1 Margin, IReadOnlyList<SalesMarginTraceStepV1> Steps,
    IReadOnlyList<SalesLineMarginAiSourceV1> Sources, IReadOnlyList<string> RuleReferences);
public sealed record SalesLineMarginAiSourceV1(string Kind, Guid? RecordId, int? Revision, DateTimeOffset? RecordedAtUtc);
