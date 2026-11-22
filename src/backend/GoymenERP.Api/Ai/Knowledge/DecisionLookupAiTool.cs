using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>
/// Y-A1 READ tool <c>decisions.lookup.v1</c> (AI-D12, AI-D32 "kaynaklı karar açıklaması", DALGA_PLANI_v2 A.4 (1)):
/// one decision by exact identity, or up to ten by keyword, each with its source file and line, status, effective
/// date and the decisions that update it. It reads only the decision index file (no database, no SQL, no network);
/// the runtime has already checked the actor, the server company context, <c>ai.panel.use</c> and
/// <see cref="ErpPermissions.AiDecisionsRead"/>. Personal data candidates are masked (AI-D34 d). Registered here,
/// callable only when <c>Ai:AllowedTools</c> admits it (default deny).
/// </summary>
public sealed partial class DecisionLookupAiTool(IAiDecisionIndexSource indexSource, TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "decisions.lookup.v1";
    public const string ToolVersion = "1";
    public const string DecisionIdField = "decisionId";
    public const string KeywordField = "keyword";
    public const string LimitField = "limit";
    public const int DefaultLimit = 5;
    public const int MaximumLimit = 10;
    public const int MinimumKeywordLength = 2;
    public const int MaximumKeywordLength = 80;
    public const string TargetKind = "decision-index";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Ai",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [ErpPermissions.AiDecisionsRead],
        fieldAllowlist: [DecisionIdField, KeywordField, LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "decisions.lookup.v1/input@1",
            OutputSchema: "decisions.lookup.v1/output@1",
            FieldPolicy: "decisions.lookup.v1/fields@1",
            LimitPolicy: "decisions.lookup.v1/limit-10@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+ai.decisions.read@1",
            SourceContract: "contracts/ai/decision-index-v1.json",
            RedactionPolicy: "ai-d34d-personal-name-and-pattern-mask@1"));

    public Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadArguments(input.ArgumentsJson, out var query, out var invalidField))
        {
            return Task.FromResult(new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: new[] { invalidField }));
        }

        if (indexSource.Current is not { } index)
        {
            return Task.FromResult(new AiReadResultV1(AiToolOutcomeV1.Unavailable));
        }

        IReadOnlyList<AiDecisionRecord> matches;
        if (query.DecisionId is { } id)
        {
            var record = index.Find(id);
            matches = record is null ? Array.Empty<AiDecisionRecord>() : new[] { record };
        }
        else
        {
            matches = index.Search(query.Keyword!);
        }

        if (matches.Count == 0)
        {
            return Task.FromResult(new AiReadResultV1(AiToolOutcomeV1.NotFound));
        }

        var released = matches.Take(query.Limit).ToArray();
        var content = new DecisionLookupContentV1(
            index.SourceSetSha256,
            matches.Count,
            released.Length < matches.Count,
            [.. released.Select(record => new DecisionLookupRecordV1(
                record.Id, record.Kind, record.Title, record.Text, record.Status, record.EffectiveDate,
                new DecisionLookupSourceV1(record.SourcePath, record.SourceLine),
                new DecisionLookupScopeV1(record.ScopeArea, record.ScopeAffected),
                [.. record.SupersededBy.Select(link => new DecisionLookupLinkV1(link.By, link.Type, link.Confidence, link.Extent))],
                record.DataClassCandidate,
                record.Masked))]);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, TargetIdOf(index.SourceSetSha256), "Karar dizini v1",
            index.SourceSetSha256, timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return Task.FromResult(new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source));
    }

    /// <summary>A stable non-empty identity for the index version (the first 16 bytes of its source digest).</summary>
    public static Guid TargetIdOf(string sourceSetSha256)
    {
        var bytes = sourceSetSha256.Length == 64 && HexPattern().IsMatch(sourceSetSha256)
            ? Convert.FromHexString(sourceSetSha256)
            : SHA256.HashData(Encoding.UTF8.GetBytes(sourceSetSha256));
        var id = new Guid(bytes.AsSpan(0, 16));
        return id == Guid.Empty ? new Guid(SHA256.HashData(bytes).AsSpan(0, 16)) : id;
    }

    private static bool TryReadArguments(string? json, out LookupQuery query, out string invalidField)
    {
        query = default;
        invalidField = AiInvalidFieldsV1.Arguments;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? decisionId = null;
            string? keyword = null;
            var limit = DefaultLimit;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case DecisionIdField when property.Value.ValueKind == JsonValueKind.String
                                              && DecisionIdPattern().IsMatch(property.Value.GetString()!):
                        decisionId = property.Value.GetString();
                        break;
                    case KeywordField when property.Value.ValueKind == JsonValueKind.String
                                           && property.Value.GetString()!.Trim() is { Length: >= MinimumKeywordLength and <= MaximumKeywordLength } trimmed:
                        keyword = trimmed;
                        break;
                    case LimitField when property.Value.ValueKind == JsonValueKind.Number
                                         && property.Value.TryGetInt32(out var value) && value is >= 1 and <= MaximumLimit:
                        limit = value;
                        break;
                    default:
                        invalidField = property.Name is DecisionIdField or KeywordField or LimitField ? property.Name : AiInvalidFieldsV1.Arguments;
                        return false;
                }
            }

            // Exactly one of identity or keyword: a lookup never lists the whole index.
            if ((decisionId is null) == (keyword is null))
            {
                return false;
            }

            query = new LookupQuery(decisionId, keyword, limit);
            return true;
        }
    }

    [GeneratedRegex(@"\A[A-Z][A-Z0-9-]{1,79}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionIdPattern();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    private readonly record struct LookupQuery(string? DecisionId, string? Keyword, int Limit);
}

public sealed record DecisionLookupContentV1(string IndexVersion, int MatchCount, bool Truncated, IReadOnlyList<DecisionLookupRecordV1> Records);

public sealed record DecisionLookupRecordV1(
    string Id,
    string Kind,
    string Title,
    string Text,
    string Status,
    string? EffectiveDate,
    DecisionLookupSourceV1 Source,
    DecisionLookupScopeV1 Scope,
    IReadOnlyList<DecisionLookupLinkV1> SupersededBy,
    string DataClassCandidate,
    bool Masked);

public sealed record DecisionLookupSourceV1(string Path, int? Line);

public sealed record DecisionLookupScopeV1(string? Area, string? Affected);

public sealed record DecisionLookupLinkV1(string By, string Type, string Confidence, string Extent);
