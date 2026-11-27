using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

internal sealed partial class LogisticsDispatchNoteWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        async Task Fence()
        {
            if (await fence.CheckAsync(context, ApproverPermission, token) is not null)
                throw new InvalidOperationException("Dispatch approval source unavailable.");
        }
        await Fence();
        if (itemId == Guid.Empty) return null;
        var companyId = context.CompanyId;
        var query = from documentRow in rows.DispatchNotes
                    join instructionRow in rows.Instructions on new { documentRow.CompanyId, Id = documentRow.InstructionId }
                        equals new { instructionRow.CompanyId, instructionRow.Id }
                    where documentRow.CompanyId == companyId && instructionRow.CompanyId == companyId && documentRow.DispatchNoteId == itemId
                    select new { Note = documentRow, Instruction = instructionRow };
        var found = await rows.ListAsync(query.Take(2), token);
        await Fence();
        if (found.Count == 0) return null;
        if (found.Count != 1) throw new InvalidOperationException("Ambiguous dispatch document.");
        var note = found[0].Note; var instruction = found[0].Instruction; var version = instruction.Version;
        var noteStatus = note.Status; var instructionStatus = instruction.Status;
        var pending = noteStatus == (int)DispatchNoteStatus.Draft && instructionStatus == (int)LoadingInstructionStatus.Issued;
        if (instruction.Id == Guid.Empty || instruction.CustomerPartyId == Guid.Empty || instruction.CompanyAccountId == Guid.Empty
            || version < 1 || pending && version == long.MaxValue || !Enum.IsDefined((DispatchNoteStatus)noteStatus)
            || !Enum.IsDefined((LoadingInstructionStatus)instructionStatus) || string.IsNullOrWhiteSpace(note.CreatedBy) || note.CreatedBy.Length > 128)
            throw new InvalidOperationException("Incomplete native dispatch document.");
        string? number = note.SeriesCode is null && note.SequenceNo is null ? null
            : DispatchNoteNumber.Of(note.SeriesCode!, note.SequenceNo ?? 0).Text;
        if (noteStatus == (int)DispatchNoteStatus.Draft && (number is not null || note.EDispatchNumber is not null)
            || noteStatus == (int)DispatchNoteStatus.Approved && number is null
            || note.EDispatchNumber is not null && (number is null || !LoadingInstruction.IsEDispatchNumber(note.EDispatchNumber)))
            throw new InvalidOperationException("Malformed native dispatch number group.");
        // The native REST caps a new draft at 500 shipments, but internal native callers have no separate cap.
        // Read the COMPLETE single-document line set. Never confuse Work's 12-reference bound with document capacity.
        var lines = await rows.ListAsync(rows.DispatchNoteLines.Where(line => line.CompanyId == companyId
                && line.InstructionId == instruction.Id && line.DispatchNoteId == itemId).OrderBy(line => line.LineNo), token);
        await Fence();
        if (lines.Count == 0 || lines.Where((line, index) => line.LineNo != index + 1 || line.ShipmentId == Guid.Empty
                || line.InstructionLineId == Guid.Empty || line.SourceOrderId == Guid.Empty || line.SourceOrderLineId == Guid.Empty
                || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty || line.Quantity <= 0 || line.QuantityKg <= 0).Any()
            || lines.Select(line => line.ShipmentId).Distinct().Count() != lines.Count)
            throw new InvalidOperationException("Incomplete native dispatch lines.");
        var final = await rows.ListAsync(query.Select(value => new { value.Instruction.Version, NoteStatus = value.Note.Status }).Take(2), token);
        await Fence();
        if (final.Count != 1 || final[0].Version != version || final[0].NoteStatus != noteStatus)
            throw new InvalidOperationException("Dispatch changed during purpose read.");
        var human = new WorkLogisticsDispatchNotePreviewV1(instruction.Id, itemId, instruction.CustomerPartyId, instruction.CompanyAccountId,
            instruction.EffectiveOn, ((DispatchNoteStatus)noteStatus).ToString(), number, note.EDispatchNumber, note.CreatedBy,
            note.CreatedAtUtc.ToUniversalTime(), lines.Select(line => new WorkLogisticsDispatchNoteLinePreviewV1(line.LineNo,
                line.ShipmentId, line.InstructionLineId, line.SourceOrderId, line.SourceOrderLineId, line.ProductId, line.UnitId,
                line.Quantity.ToString(CultureInfo.InvariantCulture), line.QuantityKg?.ToString(CultureInfo.InvariantCulture))).ToArray());
        var item = new WorkInboxItemV1(Module, Kind, itemId, LogisticsWorkInboxFence.Id(itemId), human.CreatedAtUtc,
            "/onaylarim/logistics.dispatch-note.v1/" + itemId.ToString("D"), note.CreatedBy,
            [new(DraftRule, null, null, null, null)], [LogisticsWorkInboxFence.InstructionReference(instruction.Id)]);
        var sourceVersion = version.ToString(CultureInfo.InvariantCulture);
        return new(companyId, item, sourceVersion, pending, LogisticsDispatchNoteProof: new(companyId, sourceVersion, human));
    }
}
