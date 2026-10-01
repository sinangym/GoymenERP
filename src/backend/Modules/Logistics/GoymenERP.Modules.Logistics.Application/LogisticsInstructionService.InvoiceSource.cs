using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application;

public sealed partial class LogisticsInstructionService
{
    public async Task<ApprovedDispatchInvoiceSourceV1> GetApprovedDispatchInvoiceSourceAsync(
        Guid dispatchNoteId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (dispatchNoteId == Guid.Empty)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }

        StoredApprovedDispatchInvoiceSource? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindApprovedDispatchInvoiceSourceAsync(dispatchNoteId, cancellationToken), cancellationToken);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }

        if (stored is null)
        {
            throw new LogisticsInstructionServiceException(404, "not_found");
        }

        if (stored.Number is null || stored.ApprovedAtUtc is null || stored.Lines.Count == 0
            || stored.Lines.Any(line => line.LineNo < 1 || line.SourceOrderId == Guid.Empty || line.SourceOrderLineId == Guid.Empty
                || line.ShipmentId == Guid.Empty || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty || line.Quantity <= 0))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        return new ApprovedDispatchInvoiceSourceV1(
            stored.CompanyId,
            stored.DispatchNoteId,
            stored.InstructionId,
            stored.InstructionVersion,
            stored.CustomerPartyId,
            stored.CompanyAccountId,
            stored.Number.Text,
            stored.EDispatchNumber,
            stored.ApprovedAtUtc.Value,
            [.. stored.Lines.Select(line => new ApprovedDispatchInvoiceSourceLineV1(
                line.LineNo,
                line.SourceOrderId,
                line.SourceOrderLineId,
                line.ShipmentId,
                line.ProductId,
                line.UnitId,
                line.Quantity.ToString(CultureInfo.InvariantCulture))) ]);
    }
}
