using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application.Ai;

/// <summary>
/// Y-A1 READ tool <c>logistics.pending-shipments.v1</c> (AI-D32 "tek bir dar işlem sorgusu", DALGA_PLANI_v2 A.3): the
/// company's loading instructions still waiting — counted and listed — read ONLY through the Logistics owner's own
/// paged read (<see cref="ILogisticsPendingShipmentReadV1.GetPendingShipmentPageAsync"/>), which measures
/// <c>logistics.loading-instructions.read</c> for the server-side actor and company at every fence and gives each
/// instruction's shipment state from its own lines (no detail read per instruction). No SQL, no table, no other module.
/// Counts are exact integers over every page read; when the scan cap is reached, or any instruction's shipment state is
/// unknown, the counts are withheld (null) and <c>complete</c> is false — never an estimate.
/// <para>
/// AI-D35 (SINAN_KARARLARI_2026-11-22_EK_SORULAR.md; closes ASSUMPTION-AI-PENDING-SHIPMENT-1): "bekleyen sevkiyat" = a
/// loading instruction that is draft or issued and not turned into a definite shipment; dispatch-note states are not
/// counted. Read here as the owner statuses <c>Draft</c> and <c>Issued</c> whose shipment state is not
/// <see cref="LogisticsShipmentStatesV1.FullyShipped"/>: a partly shipped instruction counts, a fully shipped or
/// cancelled one does not.
/// </para>
/// </summary>
public sealed class LogisticsPendingShipmentsAiTool(ILogisticsPendingShipmentReadV1 instructions, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "logistics.pending-shipments.v1";
    public const string ToolVersion = "1";
    public const string StatusField = "status";
    public const string LimitField = "limit";
    public const string DraftStatus = "Draft";
    public const string IssuedStatus = "Issued";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const int PageSize = 100;
    public const int ScanLimit = 1000;
    public const string Definition = "AI-D35";
    public const string TargetKind = "logistics.loading-instructions.pending";
    private const string InvalidArguments = "arguments";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Logistics",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [LogisticsPermissionManifestV1.InstructionsRead],
        fieldAllowlist: [LimitField, StatusField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "logistics.pending-shipments.v1/input@1",
            OutputSchema: "logistics.pending-shipments.v1/output@1",
            FieldPolicy: "logistics.pending-shipments.v1/fields@1",
            LimitPolicy: "logistics.pending-shipments.v1/limit-50-scan-1000@2",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+logistics.loading-instructions.read@1",
            SourceContract: "ILogisticsPendingShipmentReadV1.GetPendingShipmentPageAsync@v1",
            RedactionPolicy: "logistics.pending-shipments.v1/no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadArguments(input.ArgumentsJson, out var status, out var limit, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: new[] { invalidField });
        }

        var statuses = status is null ? new[] { DraftStatus, IssuedStatus } : new[] { status };
        var rows = new List<LogisticsPendingShipmentRowV1>();
        var complete = true;
        try
        {
            foreach (var wanted in statuses)
            {
                string? cursor = null;
                do
                {
                    var page = await instructions.GetPendingShipmentPageAsync(
                        new LogisticsPendingShipmentPageQueryV1(wanted, PageSize, cursor), cancellationToken);
                    foreach (var item in page.Items)
                    {
                        // The owner answered for another company or another status: release nothing (fail-closed).
                        if (item.CompanyId != context.CompanyId || !string.Equals(item.Status, wanted, StringComparison.Ordinal)
                            || !KnownStates.Contains(item.ShipmentState))
                        {
                            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
                        }

                        rows.Add(item);
                    }

                    cursor = page.NextCursor;
                    // More than the cap was seen: stop, and withhold the counts rather than estimate them.
                    if (rows.Count > ScanLimit)
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
        catch (LogisticsInstructionServiceException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        if (rows.Select(row => row.InstructionId).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.InstructionId)
            .ToArray();
        // An instruction whose shipment state cannot be told is neither counted nor dropped silently: fail-closed.
        if (ordered.Any(row => row.ShipmentState == LogisticsShipmentStatesV1.Unknown))
        {
            complete = false;
        }

        var pending = ordered
            .Where(row => row.ShipmentState is LogisticsShipmentStatesV1.NotShipped or LogisticsShipmentStatesV1.PartiallyShipped)
            .ToArray();
        var fullyShipped = ordered.Count(row => row.ShipmentState == LogisticsShipmentStatesV1.FullyShipped);
        var draft = pending.Count(row => row.Status == DraftStatus);
        var issued = pending.Count(row => row.Status == IssuedStatus);
        var listed = pending.Take(limit).Select(row => new PendingShipmentItemV1(
            row.InstructionId, row.Status, row.EffectiveOn, row.LineCount, row.CustomerPartyId, row.CreatedAtUtc, row.Version,
            row.ShipmentState)).ToArray();
        var content = new PendingShipmentsContentV1(
            Definition,
            complete,
            complete ? draft + issued : null,
            complete ? draft : null,
            complete ? issued : null,
            complete ? fullyShipped : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Bekleyen yükleme talimatları",
            VersionOf(ordered), timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static readonly HashSet<string> KnownStates = new(StringComparer.Ordinal)
    {
        LogisticsShipmentStatesV1.NotShipped,
        LogisticsShipmentStatesV1.PartiallyShipped,
        LogisticsShipmentStatesV1.FullyShipped,
        LogisticsShipmentStatesV1.Unknown,
    };

    private static string VersionOf(IEnumerable<LogisticsPendingShipmentRowV1> rows)
    {
        var text = string.Join('\n', rows.Select(row => $"{row.InstructionId:D}:{row.Version}:{row.ShipmentState}"));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool TryReadArguments(string? json, out string? status, out int limit, out string invalidField)
    {
        status = null;
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
                    case StatusField when property.Value.ValueKind == JsonValueKind.String
                                          && property.Value.GetString() is DraftStatus or IssuedStatus:
                        status = property.Value.GetString();
                        break;
                    case LimitField when property.Value.ValueKind == JsonValueKind.Number
                                         && property.Value.TryGetInt32(out var value) && value is >= 1 and <= MaximumLimit:
                        limit = value;
                        break;
                    default:
                        invalidField = property.Name is StatusField or LimitField ? property.Name : InvalidArguments;
                        return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// The released fields only: no cancellation reason, actor or free text. Counts are null when <c>Complete</c> is false;
/// <c>FullyShippedExcludedCount</c> says how many draft/issued instructions were left out as fully shipped (AI-D35).
/// </summary>
public sealed record PendingShipmentsContentV1(
    string Definition,
    bool Complete,
    int? PendingCount,
    int? DraftCount,
    int? IssuedCount,
    int? FullyShippedExcludedCount,
    int ListedCount,
    IReadOnlyList<PendingShipmentItemV1> Items);

public sealed record PendingShipmentItemV1(
    Guid InstructionId,
    string Status,
    DateOnly EffectiveOn,
    int LineCount,
    Guid CustomerPartyId,
    DateTimeOffset CreatedAtUtc,
    string Version,
    string ShipmentState);
