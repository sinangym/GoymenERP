using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Ai;

/// <summary>
/// WP-C10 READ tool <c>security.events.summary.v1</c> (DALGA_PLANI_v2 C.10, AI-D18 "sistemin kendi güvenlik
/// olayları"): the company's failed logins, authorization refusals and critical changes, counted and listed from the
/// newest rows, read only through <see cref="SecurityEventDigestReader"/> (the module's own company read; no SQL, no
/// table, no other module). Requires the existing <see cref="ErpPermissions.PlatformSecurityEventsRead"/> company-wide;
/// no new permission and no grant. The data class is <see cref="AiDataClassV1.Confidential"/>, so the runtime refuses
/// it unless <c>Ai:MaxDataClass</c> admits confidential data; it is registered but not allowlisted
/// (<c>Ai:AllowedTools</c> stays empty). Actor identities are masked per response.
/// </summary>
public sealed class SecurityEventsSummaryAiTool(SecurityEventDigestReader digestReader, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "security.events.summary.v1";
    public const string ToolVersion = "1";
    public const string CategoryField = "category";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const string TargetKind = "security-events.company";
    private const string InvalidArguments = "arguments";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "SecurityEvents",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: [ErpPermissions.PlatformSecurityEventsRead],
        fieldAllowlist: [CategoryField, LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "security.events.summary.v1/input@1",
            OutputSchema: "security.events.summary.v1/output@1",
            FieldPolicy: "security.events.summary.v1/fields@1",
            LimitPolicy: "security.events.summary.v1/limit-50-page-100-per-type@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+platform.security-events.read@1",
            SourceContract: "ISecurityEventQueryServiceV1.GetEventsAsync@v1",
            RedactionPolicy: "security-events/actor-pseudonym-no-correlation-no-hash@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadArguments(input.ArgumentsJson, out var category, out var limit, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: new[] { invalidField });
        }

        SecurityEventDigestV1 digest;
        try
        {
            digest = await digestReader.ReadAsync(
                context.CompanyId,
                category is null ? SecurityEventDigestReader.CategoryNames : new[] { category },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        switch (digest.Status)
        {
            case SecurityEventDigestStatusV1.Ok:
                break;
            case SecurityEventDigestStatusV1.Denied:
                return new AiReadResultV1(AiToolOutcomeV1.Denied);
            default:
                return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var counts = SecurityEventDigestReader.CategoryNames
            .Where(name => category is null || name == category)
            .Select(name => new SecurityEventCategoryCountV1(
                name,
                digest.Items.Where(item => item.Category == name).Sum(item => item.OccurrenceCount),
                digest.Items.Count(item => item.Category == name),
                SecurityEventDigestReader.Categories[name].All(type => !digest.IncompleteEventTypes.Contains(type))))
            .ToArray();
        var listed = digest.Items.Take(limit).ToArray();
        var content = new SecurityEventsSummaryContentV1(
            digest.Complete,
            SecurityEventDigestReader.PageSize,
            counts,
            digest.IncompleteEventTypes,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Şirket güvenlik olayları",
            digest.Version, timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static bool TryReadArguments(string? json, out string? category, out int limit, out string invalidField)
    {
        category = null;
        limit = DefaultLimit;
        invalidField = InvalidArguments;
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case CategoryField when property.Value.ValueKind == JsonValueKind.String
                                            && SecurityEventDigestReader.Categories.ContainsKey(property.Value.GetString()!):
                        category = property.Value.GetString();
                        break;
                    case LimitField when property.Value.ValueKind == JsonValueKind.Number
                                         && property.Value.TryGetInt32(out var value) && value is >= 1 and <= MaximumLimit:
                        limit = value;
                        break;
                    default:
                        invalidField = property.Name is CategoryField or LimitField ? property.Name : InvalidArguments;
                        return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// <c>Complete</c> is false when any read type had more than <c>PageSizePerType</c> rows; the counts then cover only the
/// newest rows read and say so per category (never an estimate of the rest).
/// </summary>
public sealed record SecurityEventsSummaryContentV1(
    bool Complete,
    int PageSizePerType,
    IReadOnlyList<SecurityEventCategoryCountV1> Categories,
    IReadOnlyList<string> IncompleteEventTypes,
    int ListedCount,
    IReadOnlyList<SecurityEventDigestItemV1> Items);

/// <summary><c>OccurrenceCount</c> sums aggregated denials; <c>RowCount</c> is the number of stored rows read.</summary>
public sealed record SecurityEventCategoryCountV1(string Category, int OccurrenceCount, int RowCount, bool Complete);
