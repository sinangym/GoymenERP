using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application.Ai;

/// <summary>
/// WP-A3a READ tool <c>logistics.instruction-shipment-status.v1</c> (DALGA_PLANI_v2 A.3, "sevk durumu"): ONE loading
/// instruction's status and, per line, the owner's exact instructed / loaded / shipped / delivered quantities and remainders,
/// with the dispatch notes prepared from it — read ONLY through the Logistics owner's own detail read
/// (<see cref="ILogisticsInstructionService.GetInstructionDetailAsync"/>), which measures
/// <c>logistics.loading-instructions.read</c> for the server-side actor and company and reads its own tenant-filtered tables.
/// <para>
/// No quantity is computed here: every figure is the owner's exact text in the line's unit (lines of different units are never
/// added). Whether the instruction is a "bekleyen sevkiyat" is AI-D35/AI-D37's owner rule, answered by
/// <c>logistics.pending-shipments.v1</c>, not re-derived. More than <see cref="LineLimit"/> lines are cut with
/// <c>linesComplete</c> false. Cancellation reason, canceller, weighing persons and every free text are not released.
/// </para>
/// </summary>
public sealed class LogisticsInstructionShipmentStatusAiTool(ILogisticsInstructionService instructions, TimeProvider timeProvider)
    : IAiOwnerReadToolV1
{
    public const string ToolId = "logistics.instruction-shipment-status.v1";
    public const string ToolVersion = "1";
    public const string InstructionIdField = "instructionId";
    public const int LineLimit = 100;
    public const string TargetKind = "logistics.loading-instruction.shipment-status";
    public const string PendingDefinitionTool = LogisticsPendingShipmentsAiTool.ToolId;

    public static readonly IReadOnlyList<string> InstructionStatuses = ["Draft", "Issued", "Cancelled"];
    public static readonly IReadOnlyList<string> DispatchNoteStatuses = ["Draft", "Approved", "Cancelled"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Logistics",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Internal,
        requiredPermissions: [LogisticsPermissionManifestV1.InstructionsRead],
        fieldAllowlist: [InstructionIdField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "logistics.instruction-shipment-status.v1/input@1",
            OutputSchema: "logistics.instruction-shipment-status.v1/output@1",
            FieldPolicy: "logistics.instruction-shipment-status.v1/fields@1",
            LimitPolicy: "logistics.instruction-shipment-status.v1/lines-100@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+logistics.loading-instructions.read@1",
            SourceContract: "ILogisticsInstructionService.GetInstructionDetailAsync@v1",
            RedactionPolicy: "logistics.instruction-shipment-status.v1/no-actor-no-free-text@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var instructionId = Guid.Empty;
        if (!AiReadToolArgumentsV1.TryRead(input.ArgumentsJson, new Dictionary<string, Func<JsonElement, bool>>(StringComparer.Ordinal)
            {
                [InstructionIdField] = value => AiReadToolArgumentsV1.TryId(value, out instructionId),
            }, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [invalidField]);
        }

        if (instructionId == Guid.Empty)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: [InstructionIdField]);
        }

        LoadingInstructionDetailView detail;
        try
        {
            detail = await instructions.GetInstructionDetailAsync(instructionId, cancellationToken);
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

        var instruction = detail?.Instruction;
        // Another company's or another instruction's answer, or a status outside the owner vocabulary: release nothing.
        if (instruction is null || detail!.Lines is null || detail.DispatchNotes is null
            || instruction.CompanyId != context.CompanyId || instruction.Id != instructionId
            || !InstructionStatuses.Contains(instruction.Status)
            || detail.Lines.Any(line => line is null || line.LineId == Guid.Empty)
            || detail.Lines.Select(line => line.LineId).Distinct().Count() != detail.Lines.Count
            || detail.DispatchNotes.Any(note => note is null || !DispatchNoteStatuses.Contains(note.Status)))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var lines = detail.Lines.OrderBy(line => line.LineNo).ThenBy(line => line.LineId).ToArray();
        var listed = lines.Take(LineLimit).Select(line => new LogisticsInstructionShipmentLineV1(
            line.LineId, line.LineNo, line.SourceOrderId, line.SourceOrderLineId, line.ProductId, line.UnitId,
            line.Instructed, line.Loaded, line.Shipped, line.Delivered, line.RemainingToLoad, line.LoadedNotShipped,
            line.ShippedNotDelivered, line.IsBlocked)).ToArray();
        var notes = detail.DispatchNotes.Select(note => new LogisticsInstructionDispatchNoteV1(
            note.DispatchNoteId, note.Status, note.Number, note.EDispatchNumber, note.CreatedAtUtc)).ToArray();
        var content = new LogisticsInstructionShipmentStatusContentV1(
            instruction.Id,
            instruction.Status,
            instruction.Version,
            instruction.EffectiveOn,
            instruction.CustomerPartyId,
            instruction.CompanyAccountId,
            PendingDefinitionTool,
            lines.Length,
            lines.Length <= LineLimit,
            listed,
            notes);
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, instruction.Id, "Yükleme talimatı sevk durumu",
            AiReadToolArgumentsV1.SourceVersion([$"{instruction.Id:D}:{instruction.Version}:{instruction.Status}",
                .. notes.Select(note => $"{note.DispatchNoteId:D}:{note.Status}:{note.Number}:{note.EDispatchNumber}")]),
            timeProvider.GetUtcNow().ToUniversalTime(), AiDataClassV1.Internal);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }
}

/// <summary>
/// The released fields only: no cancellation reason, no person, no weighing record. <c>LineCount</c> is the instruction's exact
/// line count; <c>LinesComplete</c> is false when more lines exist than were listed.
/// </summary>
public sealed record LogisticsInstructionShipmentStatusContentV1(
    Guid InstructionId,
    string Status,
    string Version,
    DateOnly EffectiveOn,
    Guid CustomerPartyId,
    Guid CompanyAccountId,
    string PendingShipmentDefinitionTool,
    int LineCount,
    bool LinesComplete,
    IReadOnlyList<LogisticsInstructionShipmentLineV1> Lines,
    IReadOnlyList<LogisticsInstructionDispatchNoteV1> DispatchNotes);

public sealed record LogisticsInstructionShipmentLineV1(
    Guid LineId,
    int LineNo,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid ProductId,
    Guid UnitId,
    string Instructed,
    string Loaded,
    string Shipped,
    string Delivered,
    string RemainingToLoad,
    string LoadedNotShipped,
    string ShippedNotDelivered,
    bool IsBlocked);

public sealed record LogisticsInstructionDispatchNoteV1(
    Guid DispatchNoteId,
    string Status,
    string? Number,
    string? EDispatchNumber,
    DateTimeOffset CreatedAtUtc);
