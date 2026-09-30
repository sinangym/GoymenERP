using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// AT-055 ("Aynı sevkiyat gerçekleşmesi iki kez irsaliye/faturaya bağlanamaz"), the dispatch note (irsaliye) — master v3 §7.8
/// "fark kontrolü → irsaliye taslağı → onay/kesim", L-08 Satış İrsaliyesi Hazırlığı, and the user's decisions of 2026-09-30:
/// <list type="bullet">
/// <item><b>Draft from definite shipments only</b>: the client names shipment ids of one issued instruction (one company, one
/// customer); each line is that shipment's recorded shipped quantity — nothing is typed, priced or recomputed.</item>
/// <item><b>Single link</b>: a shipment on a non-cancelled note is a recorded terminal 409
/// (<see cref="LogisticsProblemCodesV1.ShipmentAlreadyDispatched"/>); SQL Server holds the same rule as a filtered unique key
/// over the lines that are not released.</item>
/// <item><b>Approve</b> (<c>logistics.dispatch-notes.approve</c>): Draft → Approved, and the company's next SYSTEM number
/// (series + gap-free sequence) is assigned in the same save — a concurrent approval loses with a retryable 409
/// <c>concurrent_change</c>, never a gap or a duplicate.</item>
/// <item><b>Cancel</b> (<c>logistics.dispatch-notes.cancel</c>): a draft or an approved note is cancelled with a reason, never
/// deleted; its shipments are released for a new note (correction = cancel + new draft).</item>
/// <item><b>e-İrsaliye number</b> (<c>logistics.dispatch-notes.approve</c>): entered on an approved note, once, unique per
/// company, kept apart from the system number.</item>
/// <item>Every command: its own name measured before anything is read, after every read and right before the single save; the
/// instruction version is the precondition (stale → recorded 409) and moves by one; one Idempotency-Key per command; one audit
/// event each.</item>
/// <item><b>Not here</b>: a printable template (OPEN-019 / DEC-DOC-010), official e-İrsaliye submission (DEC-INT-001 / OPEN-016)
/// and notes spanning several instructions (grouping is a follow-up).</item>
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

    public Task<LoadingInstructionResponse> ApproveDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, ApproveDispatchNoteRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireNoteId(dispatchNoteId);
        // The number is the store's (the company series, in the same save); the domain only moves the state.
        return MutateAsync(instructionId, new { dispatchNoteId, request }, request.ExpectedVersion, metadata, LogisticsCommands.ApproveDispatchNoteV1,
            bindings.DispatchNoteApprovePermission,
            (instruction, expected, now, _, actorId) => instruction.ApproveDispatchNote(expected, dispatchNoteId, actorId, now),
            new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-approved.v1", "ApproveDispatchNote", "İrsaliye onaylandı ve numaralandı"),
            prepare: null, cancellationToken);
    }

    public Task<LoadingInstructionResponse> CancelDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, CancelDispatchNoteRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireNoteId(dispatchNoteId);
        if (string.IsNullOrWhiteSpace(request.Reason) || !string.Equals(request.Reason, request.Reason.Trim(), StringComparison.Ordinal)
            || request.Reason.Length > LoadingInstruction.DispatchNoteCancelReasonMaximumLength)
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("reason", "validation_failed")]);
        }

        return MutateAsync(instructionId, new { dispatchNoteId, request }, request.ExpectedVersion, metadata, LogisticsCommands.CancelDispatchNoteV1,
            bindings.DispatchNoteCancelPermission,
            (instruction, expected, now, _, actorId) => instruction.CancelDispatchNote(expected, dispatchNoteId, actorId, request.Reason, now),
            // The audit reason is the command's; the user's reason is on the note and in the after-image.
            new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-cancelled.v1", "CancelDispatchNote", "İrsaliye iptal edildi"),
            prepare: null, cancellationToken);
    }

    public Task<LoadingInstructionResponse> SetEDispatchNumberAsync(Guid instructionId, Guid dispatchNoteId, SetEDispatchNumberRequest request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireNoteId(dispatchNoteId);
        if (!LoadingInstruction.IsEDispatchNumber(request.EDispatchNumber))
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed", [new("eDispatchNumber", "validation_failed")]);
        }

        return MutateAsync(instructionId, new { dispatchNoteId, request }, request.ExpectedVersion, metadata, LogisticsCommands.SetEDispatchNumberV1,
            bindings.DispatchNoteApprovePermission,
            (instruction, expected, now, _, actorId) => instruction.SetEDispatchNumber(expected, dispatchNoteId, request.EDispatchNumber, actorId, now),
            new LogisticsMutationAudit("logistics.loading-instruction.e-dispatch-number-recorded.v1", "SetEDispatchNumber", "e-İrsaliye numarası kaydedildi"),
            prepare: null, cancellationToken);
    }

    private static void RequireNoteId(Guid dispatchNoteId)
    {
        if (dispatchNoteId == Guid.Empty)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }
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
                note.Status.ToString(), note.Number?.Text, note.EDispatchNumber, note.CreatedAtUtc,
                [.. note.Lines.Select(line => new SalesOrderDispatchNoteLineView(line.LineNo, line.SourceOrderLineId, line.ShipmentId, line.UnitId,
                    ExactText(line.Quantity)))]))],
            hasMore);
    }

    /// <summary>Plain invariant text with trailing zeros dropped (Ö2: equal quantities print equally).</summary>
    private static string ExactText(decimal value) => (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static DispatchNoteView DispatchNoteView(LoadingInstruction instruction, DispatchNoteDraft note) =>
        new(note.DispatchNoteId, note.Status.ToString(), note.Number?.Text, note.CreatedBy, note.CreatedAtUtc, note.ApprovedBy, note.ApprovedAtUtc,
            note.CancelledBy, note.CancelledAtUtc, note.CancelReason, note.EDispatchNumber,
            [.. note.Lines.Select(line =>
            {
                var source = instruction.Lines.Single(candidate => candidate.Id == line.InstructionLineId);
                return new DispatchNoteLineView(line.LineNo, line.ShipmentId, line.InstructionLineId, source.Source.OrderId, source.Source.LineId,
                    source.ProductId, source.UnitId, Text(line.Quantity));
            })]);
}
