using System.Text;
using System.Text.Json;
using GoymenERP.Api.Ai.Tools;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Counterparty.Infrastructure.Ai;
using GoymenERP.Modules.Logistics.Application.Ai;

namespace GoymenERP.Api.Ai;

/// <summary>B1's exact-record READ families. The context constrains both the untrusted hint/plan
/// and the owner's returned source; it never authorizes a read, changes the catalogue or creates grounding.</summary>
internal sealed record AiOpenRecordContextBindingV1(AiOpenRecordContextV1 Context, string ToolId, string ToolVersion, string IdField)
{
    public static bool TryBind(AiOpenRecordContextV1 context, out AiOpenRecordContextBindingV1? binding)
    {
        binding = null;
        if (context.TargetId == Guid.Empty || context.ExpectedSourceVersion is { } version && !IsSourceVersion(version)) return false;
        binding = context.TargetKind switch
        {
            SalesDraftSummaryAiToolV1.TargetKind => new(context, SalesDraftSummaryAiToolV1.ToolId,
                SalesDraftSummaryAiToolV1.ToolVersion, SalesDraftSummaryAiToolV1.DraftIdField),
            CounterpartyPartySummaryAiTool.TargetKind => new(context, CounterpartyPartySummaryAiTool.ToolId,
                CounterpartyPartySummaryAiTool.ToolVersion, CounterpartyPartySummaryAiTool.PartyIdField),
            LogisticsInstructionShipmentStatusAiTool.TargetKind => new(context, LogisticsInstructionShipmentStatusAiTool.ToolId,
                LogisticsInstructionShipmentStatusAiTool.ToolVersion, LogisticsInstructionShipmentStatusAiTool.InstructionIdField),
            _ => null,
        };
        return binding is not null;
    }

    // Owner source versions are SHA256 digests of their released facts, never UI row versions.
    private static bool IsSourceVersion(string? value) => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public AiToolHintV1 Hint() => new(ToolId, ToolVersion,
        JsonSerializer.Serialize(new Dictionary<string, string> { [IdField] = Context.TargetId.ToString("D") }));

    public bool Matches(string? tool, string? version, string? arguments, int maxArgumentBytes)
    {
        if (tool != ToolId || version != ToolVersion || arguments is null
            || Encoding.UTF8.GetByteCount(arguments) > maxArgumentBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var fields = document.RootElement.EnumerateObject().ToArray();
            return fields.Length == 1 && fields[0].Name == IdField
                && AiReadToolArgumentsV1.TryId(fields[0].Value, out var id) && id == Context.TargetId;
        }
        catch (JsonException) { return false; }
    }

    public bool MatchesSource(AiSourceV1? source) => source is not null && source.ToolId == ToolId && source.ToolVersion == ToolVersion
        && source.TargetKind == Context.TargetKind && source.TargetId == Context.TargetId && IsSourceVersion(source.SourceVersion)
        && (Context.ExpectedSourceVersion is null || source.SourceVersion == Context.ExpectedSourceVersion);
}
