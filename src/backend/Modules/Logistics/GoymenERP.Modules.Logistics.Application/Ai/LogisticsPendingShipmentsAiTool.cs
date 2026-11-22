using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application.Ai;

/// <summary>
/// Y-A1 READ tool <c>logistics.pending-shipments.v1</c> (AI-D32 "tek bir dar işlem sorgusu", DALGA_PLANI_v2 A.3): the
/// company's loading instructions still waiting — counted and listed — read ONLY through the Logistics owner's own
/// list service (<see cref="ILogisticsInstructionService.GetInstructionListAsync"/>), which measures
/// <c>logistics.loading-instructions.read</c> for the server-side actor and company at every fence. No SQL, no table,
/// no other module. Counts are exact integers over every page read; when the scan cap is reached the counts are
/// withheld (null) and <c>complete</c> is false — never an estimate.
/// <para>
/// ASSUMPTION-AI-PENDING-SHIPMENT-1 (reversible, fail-closed): "pending" = an instruction whose owner status is
/// <c>Draft</c> (waiting to be issued) or <c>Issued</c> (issued, not cancelled). Dispatch-note states are not counted in
/// this version (NEEDS-SINAN: which instruction/dispatch-note states count as "bekleyen sevkiyat").
/// </para>
/// </summary>
public sealed class LogisticsPendingShipmentsAiTool(ILogisticsInstructionService instructions, TimeProvider timeProvider)
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
    public const string Definition = "ASSUMPTION-AI-PENDING-SHIPMENT-1";
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
            LimitPolicy: "logistics.pending-shipments.v1/limit-50-scan-1000@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+logistics.loading-instructions.read@1",
            SourceContract: "ILogisticsInstructionService.GetInstructionListAsync@v1",
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
        var rows = new List<LoadingInstructionSummaryView>();
        var complete = true;
        try
        {
            foreach (var wanted in statuses)
            {
                string? cursor = null;
                do
                {
                    var page = await instructions.GetInstructionListAsync(
                        new LoadingInstructionListQuery(PageSize, cursor, wanted), cancellationToken);
                    foreach (var item in page.Items)
                    {
                        // The owner answered for another company or another status: release nothing (fail-closed).
                        if (item.CompanyId != context.CompanyId || !string.Equals(item.Status, wanted, StringComparison.Ordinal))
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

        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var ordered = rows
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .ToArray();
        var draft = ordered.Count(row => row.Status == DraftStatus);
        var issued = ordered.Count(row => row.Status == IssuedStatus);
        var listed = ordered.Take(limit).Select(row => new PendingShipmentItemV1(
            row.Id, row.Status, row.EffectiveOn, row.LineCount, row.CustomerPartyId, row.CreatedAtUtc, row.Version)).ToArray();
        var content = new PendingShipmentsContentV1(
            Definition,
            complete,
            complete ? draft + issued : null,
            complete ? draft : null,
            complete ? issued : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Bekleyen yükleme talimatları",
            VersionOf(ordered), timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static string VersionOf(IEnumerable<LoadingInstructionSummaryView> rows)
    {
        var text = string.Join('\n', rows.Select(row => $"{row.Id:D}:{row.Version}"));
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
/// The released fields only: no cancellation reason, actor or free text. Counts are null when <c>Complete</c> is false.
/// </summary>
public sealed record PendingShipmentsContentV1(
    string Definition,
    bool Complete,
    int? PendingCount,
    int? DraftCount,
    int? IssuedCount,
    int ListedCount,
    IReadOnlyList<PendingShipmentItemV1> Items);

public sealed record PendingShipmentItemV1(
    Guid InstructionId,
    string Status,
    DateOnly EffectiveOn,
    int LineCount,
    Guid CustomerPartyId,
    DateTimeOffset CreatedAtUtc,
    string Version);
