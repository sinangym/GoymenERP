using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// AT-055 ("Aynı sevkiyat gerçekleşmesi iki kez irsaliye/faturaya bağlanamaz"), the dispatch-note (irsaliye) DRAFT — master
/// v3 §7.8 "fark kontrolü → irsaliye taslağı → onay/kesim", L-08 Satış İrsaliyesi Hazırlığı:
/// <list type="bullet">
/// <item><b>From definite shipments only</b>: the client names shipment ids of one issued instruction (one company, one customer);
/// each line is that shipment's recorded shipped quantity — nothing is typed, priced or recomputed.</item>
/// <item><b>Single link</b>: a shipment already on a dispatch note is a recorded terminal 409
/// (<see cref="LogisticsProblemCodesV1.ShipmentAlreadyDispatched"/>); SQL Server holds the same rule as a unique key.</item>
/// <item><b>Its own name</b> (<c>logistics.dispatch-notes.create</c>), measured before anything is read, after every read and right
/// before the single save; the instruction version is the precondition (stale → recorded 409) and moves by one.</item>
/// <item><b>Not decided, so absent</b>: a number, finalising/cancelling a draft, a printable template (OPEN-019 / DEC-DOC-010 assets
/// are not supplied) and any official e-İrsaliye submission (DEC-INT-001 / OPEN-016).</item>
/// </list>
/// </summary>
public sealed partial class LogisticsInstructionService
{
    private const int SalesOrderDispatchNoteCap = 100;

    public Task<LoadingInstructionResponse> CreateDispatchNoteAsync(Guid instructionId, CreateDispatchNoteRequest request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DispatchNoteId == Guid.Empty || request.ShipmentIds is null || request.ShipmentIds.Count == 0
            || request.ShipmentIds.Any(id => id == Guid.Empty) || request.ShipmentIds.Distinct().Count() != request.ShipmentIds.Count)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("shipmentIds", "validation_failed")]);
        }

        return MutateAsync(instructionId, request, request.ExpectedVersion, metadata, LogisticsCommands.CreateDispatchNoteV1,
            bindings.DispatchNotePermission,
            (instruction, expected, now, _, actorId) => instruction.CreateDispatchNoteDraft(expected, request.DispatchNoteId, request.ShipmentIds,
                actorId, now),
            new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-drafted.v1", "CreateDispatchNote", "İrsaliye taslağı hazırlandı"),
            prepare: null, cancellationToken);
    }

    public async Task<SalesOrderDispatchNotesView> GetSalesOrderDispatchNotesAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (salesOrderId == Guid.Empty)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("salesOrderId", "validation_failed")]);
        }

        IReadOnlyList<StoredDispatchNoteReference> stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListSalesOrderDispatchNotesAsync(salesOrderId, SalesOrderDispatchNoteCap + 1, cancellationToken), cancellationToken);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        var hasMore = stored.Count > SalesOrderDispatchNoteCap;
        return new SalesOrderDispatchNotesView(salesOrderId,
            [.. stored.Take(SalesOrderDispatchNoteCap).Select(note => new SalesOrderDispatchNoteView(note.DispatchNoteId, note.InstructionId,
                note.Status.ToString(), note.CreatedAtUtc,
                [.. note.Lines.Select(line => new SalesOrderDispatchNoteLineView(line.LineNo, line.SourceOrderLineId, line.ShipmentId, line.UnitId,
                    ExactText(line.Quantity)))]))],
            hasMore);
    }

    /// <summary>Plain invariant text with trailing zeros dropped (Ö2: equal quantities print equally).</summary>
    private static string ExactText(decimal value) => (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static DispatchNoteView DispatchNoteView(LoadingInstruction instruction, DispatchNoteDraft note) =>
        new(note.DispatchNoteId, note.Status.ToString(), note.CreatedBy, note.CreatedAtUtc,
            [.. note.Lines.Select(line =>
            {
                var source = instruction.Lines.Single(candidate => candidate.Id == line.InstructionLineId);
                return new DispatchNoteLineView(line.LineNo, line.ShipmentId, line.InstructionLineId, source.Source.OrderId, source.Source.LineId,
                    source.ProductId, source.UnitId, Text(line.Quantity));
            })]);
}
