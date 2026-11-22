using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application.Ai;

/// <summary>
/// WP-A3a READ tool <c>logistics.order-dispatch-notes.v1</c> (DALGA_PLANI_v2 A.3, "irsaliye özeti"): the dispatch notes that
/// document shipments of ONE sales order — counted by owner status (<c>Draft</c>, <c>Approved</c>, <c>Cancelled</c>) and listed
/// with this order's lines — read ONLY through the Logistics owner's own read
/// (<see cref="ILogisticsInstructionService.GetSalesOrderDispatchNotesAsync"/>), which measures
/// <c>logistics.loading-instructions.read</c> for the server-side actor and company and reads its own tenant-filtered tables.
/// No SQL, no table, no other module, no price.
/// <para>
/// Quantities are the owner's exact texts in each line's unit (never added across units here). When the owner says more notes
/// exist than it returned, the counts are withheld (null) and <c>complete</c> is false. Dispatch-note states are not a
/// pending-shipment measure (AI-D35/AI-D37: that is <c>logistics.pending-shipments.v1</c>). An order with no dispatch note is
/// answered with zero counts: the owner's read does not tell an unknown order from one without notes.
/// </para>
/// </summary>
public sealed class LogisticsOrderDispatchNotesAiTool(ILogisticsInstructionService instructions, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "logistics.order-dispatch-notes.v1";
    public const string ToolVersion = "1";
    public const string SalesOrderIdField = "salesOrderId";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const string TargetKind = "logistics.dispatch-notes.sales-order";

    public static readonly IReadOnlyList<string> Statuses = ["Draft", "Approved", "Cancelled"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Logistics",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [LogisticsPermissionManifestV1.InstructionsRead],
        fieldAllowlist: [LimitField, SalesOrderIdField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "logistics.order-dispatch-notes.v1/input@1",
            OutputSchema: "logistics.order-dispatch-notes.v1/output@1",
            FieldPolicy: "logistics.order-dispatch-notes.v1/fields@1",
            LimitPolicy: "logistics.order-dispatch-notes.v1/limit-50-owner-cap@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+logistics.loading-instructions.read@1",
            SourceContract: "ILogisticsInstructionService.GetSalesOrderDispatchNotesAsync@v1",
            RedactionPolicy: "logistics.order-dispatch-notes.v1/no-actor-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = DefaultLimit;
        var salesOrderId = Guid.Empty;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [SalesOrderIdField] = value => AiReadToolArgumentsV1.TryId(value, out salesOrderId),
                [LimitField] = value => AiReadToolArgumentsV1.TryInt(value, 1, MaximumLimit, out limit),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        if (salesOrderId == Guid.Empty)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [SalesOrderIdField]);
        }

        SalesOrderDispatchNotesView view;
        try
        {
            view = await instructions.GetSalesOrderDispatchNotesAsync(salesOrderId, cancellationToken);
        }
        catch (LogisticsInstructionServiceException exception) when (exception.Status is 401 or 403)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Denied);
        }
        catch (LogisticsInstructionServiceException exception) when (exception.Status == 404)
        {
            return new AiReadResultV1(AiToolOutcomeV1.NotFound);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        // The owner answered another order, a status outside its vocabulary or a repeated note: release nothing (fail-closed).
        if (view?.Items is null || view.SalesOrderId != salesOrderId
            || view.Items.Any(note => note is null || note.DispatchNoteId == Guid.Empty || note.Lines is null
                                      || !Statuses.Contains(note.Status))
            || view.Items.Select(note => note.DispatchNoteId).Distinct().Count() != view.Items.Count)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var complete = !view.HasMore;
        int? CountOf(string status) => complete ? view.Items.Count(note => note.Status == status) : null;
        var ordered = view.Items.OrderBy(note => note.CreatedAtUtc).ThenBy(note => note.DispatchNoteId).ToArray();
        var listed = ordered.Take(limit).Select(note => new LogisticsOrderDispatchNoteItemV1(
            note.DispatchNoteId, note.InstructionId, note.Status, note.Number, note.EDispatchNumber, note.CreatedAtUtc,
            note.Lines.Count,
            [.. note.Lines.OrderBy(line => line.LineNo).Select(line => new LogisticsOrderDispatchNoteLineV1(
                line.LineNo, line.SourceOrderLineId, line.UnitId, line.Quantity))])).ToArray();
        var content = new LogisticsOrderDispatchNotesContentV1(
            salesOrderId,
            complete,
            CountOf("Draft"),
            CountOf("Approved"),
            CountOf("Cancelled"),
            complete ? view.Items.Count(note => note.Status == "Approved" && string.IsNullOrWhiteSpace(note.EDispatchNumber)) : null,
            listed.Length,
            listed);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, salesOrderId, "Satış siparişinin irsaliyeleri",
            AiReadToolArgumentsV1.SourceVersion(ordered.Select(note =>
                $"{note.DispatchNoteId:D}:{note.Status}:{note.Number}:{note.EDispatchNumber}:{note.Lines.Count}")),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>
/// The released fields only: no creator, approver or canceller, no cancellation reason. Counts are null when not complete;
/// <c>ApprovedWithoutEDispatchNumberCount</c> counts approved notes still waiting for their official e-İrsaliye number.
/// </summary>
public sealed record LogisticsOrderDispatchNotesContentV1(
    Guid SalesOrderId,
    bool Complete,
    int? DraftCount,
    int? ApprovedCount,
    int? CancelledCount,
    int? ApprovedWithoutEDispatchNumberCount,
    int ListedCount,
    IReadOnlyList<LogisticsOrderDispatchNoteItemV1> Items);

public sealed record LogisticsOrderDispatchNoteItemV1(
    Guid DispatchNoteId,
    Guid InstructionId,
    string Status,
    string? Number,
    string? EDispatchNumber,
    DateTimeOffset CreatedAtUtc,
    int LineCount,
    IReadOnlyList<LogisticsOrderDispatchNoteLineV1> Lines);

public sealed record LogisticsOrderDispatchNoteLineV1(int LineNo, Guid SourceOrderLineId, Guid UnitId, string Quantity);
