using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool DispatchVersion(string? value, bool increment = false) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var version) && version > 0 && (!increment || version < long.MaxValue)
        && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool NativeDispatchQuantity(string? text) => text is not null
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value < 1_000_000_000_000_000m && decimal.Round(value, 3) == value
        && value.ToString(CultureInfo.InvariantCulture) == text;

    private static bool ValidLogisticsDispatchNoteProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != LogisticsDispatchNote) return snapshot.LogisticsDispatchNoteProof is null;
        if (snapshot.LogisticsDispatchNoteProof is not { } proof || proof.CompanyId != snapshot.CompanyId
            || proof.InstructionVersion != snapshot.SourceVersion || !DispatchVersion(snapshot.SourceVersion, snapshot.Pending)
            || proof.Note is not { } note || note.InstructionId == Guid.Empty || note.DispatchNoteId != snapshot.Item.ItemId
            || note.CustomerPartyId == Guid.Empty || note.CompanyAccountId == Guid.Empty
            || note.CreatedBy != snapshot.Item.PreparedBy || string.IsNullOrWhiteSpace(note.CreatedBy) || note.CreatedBy.Length > 128
            || note.CreatedAtUtc != snapshot.Item.WaitingSinceUtc || note.CreatedAtUtc.Offset != TimeSpan.Zero
            || note.Status is not ("Draft" or "Approved" or "Cancelled") || snapshot.Pending && note.Status != "Draft"
            || note.Number is { Length: > 28 } || note.Number is not null && string.IsNullOrWhiteSpace(note.Number)
            || note.Status == "Draft" && (note.Number is not null || note.EDispatchNumber is not null)
            || note.Status == "Approved" && note.Number is null
            || note.EDispatchNumber is not null && (note.Number is null || !LoadingInstruction.IsEDispatchNumber(note.EDispatchNumber))
            || note.Lines is not { Count: > 0 }
            || note.Lines.Where((line, index) => line.LineNo != index + 1 || line.ShipmentId == Guid.Empty
                || line.InstructionLineId == Guid.Empty || line.SourceOrderId == Guid.Empty || line.SourceOrderLineId == Guid.Empty
                || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty || !NativeDispatchQuantity(line.Quantity)
                || line.QuantityKg is not null && !NativeDispatchQuantity(line.QuantityKg)).Any()
            || note.Lines.Select(line => line.ShipmentId).Distinct().Count() != note.Lines.Count)
            return false;
        return snapshot.Item.References.Count == 1 && snapshot.Item.References[0].Role == "loading-instruction"
            && snapshot.Item.References[0].RecordId == note.InstructionId;
    }

    private async Task<WorkDecisionResultV1> DecideLogisticsDispatchNoteAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (logisticsInstructions is null || snapshot.LogisticsDispatchNoteProof is not { Note: { } proof })
            throw new WorkDecisionException(503);
        var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        var nativeRequest = new ApproveDispatchNoteRequest(request.ExpectedVersion);
        LoadingInstructionResponse? answer;
        try
        {
            answer = await logisticsInstructions.ApproveDispatchNoteAsync(proof.InstructionId, id, nativeRequest,
                new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), token);
        }
        catch (LogisticsInstructionServiceException error)
        { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        var expectedHash = LogisticsRequestHash.Compute(LogisticsCommands.ApproveDispatchNoteV1, proof.InstructionId,
            new { dispatchNoteId = id, request = nativeRequest });
        // The mutable current view can be cancelled/e-numbered/version+3. Only the immutable original receipt proves approval.
        if (answer is null || !answer.CommandReceiptProven || answer.DispatchNoteApprovalReceipt is not { } receipt
            || receipt.CompanyId != snapshot.CompanyId || receipt.CommandName != LogisticsCommands.ApproveDispatchNoteV1
            || receipt.InstructionId != proof.InstructionId || receipt.DispatchNoteId != id || receipt.ExpectedVersion != expected
            || receipt.ApprovedVersion != expected + 1 || receipt.ApprovedBy != actor || receipt.RequestHash != expectedHash
            || receipt.ApprovedAtUtc == default || receipt.ApprovedAtUtc.Offset != TimeSpan.Zero
            || receipt.OriginalCorrelationId != answer.OriginalCorrelationId
            || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId
            || answer.OriginalCorrelationId.Length > 128 || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId
            || !answer.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)
            || answer.Detail?.Instruction is not { } instruction || instruction.CompanyId != snapshot.CompanyId
            || instruction.Id != proof.InstructionId || instruction.CustomerPartyId != proof.CustomerPartyId || instruction.CompanyAccountId != proof.CompanyAccountId
            || !DispatchVersion(instruction.Version) || long.Parse(instruction.Version, CultureInfo.InvariantCulture) < receipt.ApprovedVersion
            || !answer.IsReplay && instruction.Version != receipt.ApprovedVersion.ToString(CultureInfo.InvariantCulture)
            || instruction.Status is not ("Draft" or "Issued" or "Cancelled") || answer.Detail.DispatchNotes is null)
            throw new WorkDecisionException(503);
        var notes = answer.Detail.DispatchNotes.Where(note => note.DispatchNoteId == id).ToArray();
        if (notes.Length != 1 || notes[0].CreatedBy != proof.CreatedBy || notes[0].CreatedAtUtc != proof.CreatedAtUtc
            || notes[0].Status is not ("Approved" or "Cancelled") || !answer.IsReplay && notes[0].Status != "Approved"
            || notes[0].ApprovedBy != receipt.ApprovedBy || notes[0].ApprovedAtUtc != receipt.ApprovedAtUtc
            || string.IsNullOrWhiteSpace(notes[0].Number) || notes[0].Number!.Length > 28
            || notes[0].Lines is null || !notes[0].Lines.Select(line => new WorkLogisticsDispatchNoteLinePreviewV1(line.LineNo,
                line.ShipmentId, line.InstructionLineId, line.SourceOrderId, line.SourceOrderLineId, line.ProductId, line.UnitId,
                line.Quantity, line.QuantityKg)).SequenceEqual(proof.Lines))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, LogisticsDispatchNote, id, "Approved", answer.IsReplay, answer.OriginalCorrelationId);
    }
}
