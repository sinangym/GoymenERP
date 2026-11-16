using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-S13 — the invoice's weighing-difference prerequisite (EK1 SAT-D343; SAT-D319 (6); SAT-D320 (3) / SAT-D322 (1); EK15
/// SAT-D389; EK20 SAT-D405). The Logistics owner (STK-S24) records and decides the difference; Sales only reads it through
/// <see cref="ISalesDispatchWeighingDifferenceReadPortV1"/> (once per distinct note, the write name measured again after every
/// read) and waits — 409 <c>invoice_not_ready</c> with one reason per share of the note — when the note has no record, waits for
/// approval, or its scale kilograms cannot be carried to the lines yet. An absent, failing, foreign or inconsistent owner is a
/// wait too (<see cref="SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable"/>): a share is never invoiced unread.
/// </summary>
public sealed partial class SalesInvoiceService
{
    private async Task AddWeighingDifferenceWaitsAsync(Scope scope, IReadOnlyList<PreparedShare> prepared,
        List<SalesDraftFieldError> reasons, CancellationToken cancellationToken)
    {
        foreach (var note in prepared.GroupBy(item => item.Share.DispatchNoteId))
        {
            var reason = await WeighingDifferenceWaitAsync(scope, note.Key, cancellationToken);
            if (reason is not null)
            {
                reasons.AddRange(note.Select(item => new SalesDraftFieldError(item.Field, reason)));
            }
        }
    }

    private async Task<string?> WeighingDifferenceWaitAsync(Scope scope, Guid dispatchNoteId, CancellationToken cancellationToken)
    {
        if (weighingDifferences is null || dispatchNoteId == Guid.Empty)
        {
            return SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable;
        }

        SalesDispatchWeighingDifferenceReadV1? answer;
        try
        {
            answer = await weighingDifferences.ReadAsync(scope.CompanyId, dispatchNoteId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, WritePermission, cancellationToken);
            return SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable;
        }

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        var consistent = answer is not null && answer.CompanyId == scope.CompanyId && answer.DispatchNoteId == dispatchNoteId
            && (answer.Status == SalesDispatchWeighingDifferenceStatusV1.Recorded && answer.Value is not null
                || answer.Status == SalesDispatchWeighingDifferenceStatusV1.NotRecorded && answer.Value is null);
        if (!consistent)
        {
            return SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable;
        }

        return SalesDispatchWeighingInvoiceRule.Wait(answer!) switch
        {
            null => null,
            SalesDispatchWeighingInvoiceWaitView.NotRecorded => SalesInvoiceReasonCodesV1.WeighingDifferenceNotRecorded,
            SalesDispatchWeighingInvoiceWaitView.PendingApproval => SalesInvoiceReasonCodesV1.WeighingDifferencePendingApproval,
            SalesDispatchWeighingInvoiceWaitView.KgTransferPending => SalesInvoiceReasonCodesV1.WeighingKgTransferPending,
            _ => SalesInvoiceReasonCodesV1.WeighingDifferenceUnavailable,
        };
    }
}
