using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>Reads only the Logistics owner's existing scoped store. Both reads must describe the same durable version;
/// a concurrent cancellation or transition is unavailable, never a fabricated invoice-to-stock link.</summary>
public sealed class LogisticsInvoicedShipmentProofReadPortV1(ICompanyContext company, ILogisticsInstructionStore store)
    : ILogisticsInvoicedShipmentProofReadPortV1
{
    public async Task<LogisticsInvoicedShipmentProofReadV1> GetAsync(Guid companyId, Guid dispatchNoteId,
        int dispatchNoteLineNo, Guid shipmentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameCompany(companyId)) return Unavailable();
        if (dispatchNoteId == Guid.Empty || dispatchNoteLineNo < 1 || shipmentId == Guid.Empty) return NotFound();
        try
        {
            var source = await store.FindApprovedDispatchInvoiceSourceAsync(dispatchNoteId, cancellationToken);
            if (!SameCompany(companyId)) return Unavailable();
            if (source is null) return NotFound();
            if (source.CompanyId != companyId || source.DispatchNoteId != dispatchNoteId || source.InstructionId == Guid.Empty
                || source.InstructionVersion < 1 || source.Number is null || source.ApprovedAtUtc is null) return Unavailable();
            var sourceLine = source.Lines.SingleOrDefault(line => line.LineNo == dispatchNoteLineNo);
            if (sourceLine is null || sourceLine.ShipmentId != shipmentId) return NotFound();
            var stored = await store.FindInstructionAsync(source.InstructionId, cancellationToken);
            if (!SameCompany(companyId)) return Unavailable();
            var instruction = stored?.Instruction;
            if (instruction is null || instruction.CompanyId != companyId || instruction.Id != source.InstructionId
                || instruction.Version != source.InstructionVersion || instruction.ShipmentKind != LoadingInstructionShipmentKind.Sale
                || instruction.Status != LoadingInstructionStatus.Issued) return Unavailable();
            var note = instruction.DispatchNotes.SingleOrDefault(item => item.DispatchNoteId == dispatchNoteId);
            if (note is null || note.Status != DispatchNoteStatus.Approved || note.Number != source.Number
                || note.ApprovedAtUtc != source.ApprovedAtUtc) return Unavailable();
            var noteLine = note.Lines.SingleOrDefault(item => item.LineNo == dispatchNoteLineNo);
            var shipment = instruction.Shares.SingleOrDefault(item => item.ShareId == shipmentId);
            if (noteLine is null || shipment is null || shipment.Kind != LoadingShareKind.Shipped
                || noteLine.ShipmentId != shipmentId || noteLine.InstructionLineId != shipment.LineId
                || noteLine.Quantity != shipment.Quantity || sourceLine.Quantity != shipment.Quantity.Value
                || sourceLine.Quantity <= 0m) return Unavailable();
            var line = instruction.Lines.SingleOrDefault(item => item.Id == shipment.LineId);
            if (line is null || line.Id == Guid.Empty || line.Source.OrderId == Guid.Empty || line.Source.LineId == Guid.Empty
                || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty || line.Source.Revision < 0
                || line.Source.OrderId != sourceLine.SourceOrderId || line.Source.LineId != sourceLine.SourceOrderLineId
                || line.Source.Revision != sourceLine.SourceOrderRevision || line.ProductId != sourceLine.ProductId
                || line.UnitId != sourceLine.UnitId) return Unavailable();
            return new(LogisticsInvoicedShipmentProofOutcomeV1.Found,
                new(companyId, dispatchNoteId, dispatchNoteLineNo, shipmentId, instruction.Id, line.Id,
                    line.Source.OrderId, line.Source.LineId, line.Source.Revision, line.ProductId, line.UnitId,
                    sourceLine.Quantity, instruction.Version.ToString(CultureInfo.InvariantCulture)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    private bool SameCompany(Guid companyId) => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId;
    private static LogisticsInvoicedShipmentProofReadV1 NotFound() => new(LogisticsInvoicedShipmentProofOutcomeV1.NotFound);
    private static LogisticsInvoicedShipmentProofReadV1 Unavailable() => new(LogisticsInvoicedShipmentProofOutcomeV1.Unavailable);
}
