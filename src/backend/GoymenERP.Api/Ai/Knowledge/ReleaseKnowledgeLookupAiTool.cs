using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.Ai.Knowledge;

/// <summary>
/// WP-A4 READ tool <c>release-knowledge.lookup.v1</c> (DALGA_PLANI_v2 A.4 (3)): how the PUBLISHED software behaves — usage
/// notes bound to this build (the package is API content). One note by exact identity, or up to ten by keyword, each with its
/// source file and line. Rules: the package is used only when its exact bytes are approved for the company
/// (<c>ai.knowledge.approve</c>, AI-D32); a note is released only to an actor who holds every permission it names, checked
/// fresh in the server company (a note the actor may not see is not counted either, so its existence does not leak);
/// unimplemented features are not in the package at all; content labelled <c>yerel_kalir</c> (AI-D34 d) is withheld from this
/// Internal-class tool and only counted. No database row, SQL or network is read besides the approval and the permission check.
/// </summary>
public sealed partial class ReleaseKnowledgeLookupAiTool(
    IAiReleaseKnowledgeSource knowledgeSource,
    IAiKnowledgeApprovalReaderV1 approvals,
    IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "release-knowledge.lookup.v1";
    public const string ToolVersion = "1";
    public const string NoteIdField = "noteId";
    public const string KeywordField = "keyword";
    public const string LimitField = "limit";
    public const int DefaultLimit = 5;
    public const int MaximumLimit = 10;
    public const int MinimumKeywordLength = 2;
    public const int MaximumKeywordLength = 80;
    public const string TargetKind = "release-knowledge";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Ai",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [ErpPermissions.AiPanelUse],
        fieldAllowlist: [NoteIdField, KeywordField, LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "release-knowledge.lookup.v1/input@1",
            OutputSchema: "release-knowledge.lookup.v1/output@1",
            FieldPolicy: "release-knowledge.lookup.v1/fields@1",
            LimitPolicy: "release-knowledge.lookup.v1/limit-10@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+note-permissions+approved-package@1",
            SourceContract: "contracts/ai/release-knowledge-v1.json",
            RedactionPolicy: "ai-d36-mask-at-generation+ai-d34d-local-only-withheld@2"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadArguments(input.ArgumentsJson, out var query, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: new[] { invalidField });
        }

        if (knowledgeSource.Current is not { } package)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (await approvals.GetStateAsync(context.CompanyId, AiKnowledgeSourceKindV1.ReleaseKnowledge, package.PackageSha256,
                cancellationToken) != AiKnowledgeApprovalStateV1.Approved)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        IReadOnlyList<AiReleaseNote> candidates;
        if (query.NoteId is { } id)
        {
            var note = package.Find(id);
            candidates = note is null ? Array.Empty<AiReleaseNote>() : new[] { note };
        }
        else
        {
            candidates = package.Search(query.Keyword!);
        }

        // Fresh permission check per named permission, in the server company; an unregistered name is never granted.
        var granted = new Dictionary<string, bool>(StringComparer.Ordinal);
        var readable = new List<AiReleaseNote>();
        foreach (var note in candidates)
        {
            var allowed = true;
            foreach (var permission in note.RequiredPermissions)
            {
                if (!granted.TryGetValue(permission, out var holds))
                {
                    holds = ErpPermissions.IsKnown(permission)
                            && (await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken)).IsGranted;
                    granted[permission] = holds;
                }

                allowed &= holds;
            }

            if (allowed)
            {
                readable.Add(note);
            }
        }

        if (readable.Count == 0)
        {
            return new AiReadResultV1(AiToolOutcomeV1.NotFound);
        }

        var releasable = readable.Where(note => note.CloudDataClass <= Descriptor.DataClass).ToArray();
        var released = releasable.Take(query.Limit).ToArray();
        var content = new ReleaseKnowledgeLookupContentV1(
            package.SourceSetSha256,
            package.PackageSha256,
            releasable.Length,
            released.Length < releasable.Length,
            readable.Count - releasable.Length,
            [.. released.Select(note => new ReleaseKnowledgeNoteV1(note.Id, note.Title, note.Text,
                new ReleaseKnowledgeSourceV1(note.SourcePath, note.SourceLine), note.Masked, note.CloudEligibility))]);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, DecisionLookupAiTool.TargetIdOf(package.PackageSha256),
            "Yayındaki sürümün davranış bilgisi v1", package.PackageSha256, timeProvider.GetUtcNow().ToUniversalTime(),
            AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
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

            string? noteId = null;
            string? keyword = null;
            var limit = DefaultLimit;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case NoteIdField when property.Value.ValueKind == JsonValueKind.String
                                          && NoteIdPattern().IsMatch(property.Value.GetString()!):
                        noteId = property.Value.GetString();
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
                        invalidField = property.Name is NoteIdField or KeywordField or LimitField ? property.Name : AiInvalidFieldsV1.Arguments;
                        return false;
                }
            }

            // Exactly one of identity or keyword: a lookup never lists the whole package.
            if ((noteId is null) == (keyword is null))
            {
                return false;
            }

            query = new LookupQuery(noteId, keyword, limit);
            return true;
        }
    }

    [GeneratedRegex(@"\ARK-[A-Z0-9]+(?:-[A-Z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NoteIdPattern();

    private readonly record struct LookupQuery(string? NoteId, string? Keyword, int Limit);
}

public sealed record ReleaseKnowledgeLookupContentV1(
    string SourceSetVersion,
    string PackageVersion,
    int MatchCount,
    bool Truncated,
    int WithheldLocalOnlyCount,
    IReadOnlyList<ReleaseKnowledgeNoteV1> Notes);

public sealed record ReleaseKnowledgeNoteV1(string Id, string Title, string Text, ReleaseKnowledgeSourceV1 Source, bool Masked,
    string CloudEligibility);

public sealed record ReleaseKnowledgeSourceV1(string Path, int Line);
