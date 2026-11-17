using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

/// <summary>Explicit SYNTHETIC number answer for existing isolated service/store tests.
/// No Settings reservation, counter, authority or SQL numbering acceptance is claimed.</summary>
internal sealed class SyntheticFinanceRecordNumbers : IFinanceDocumentNumberReservationV1
{
    private static long sequence;
    public Task<SettingsDocumentNumberReservationV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAuditEnvelope envelope, SettingsFinanceDocumentPurposeV1 purpose, Guid sourceId, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var next = Interlocked.Increment(ref sequence);
        var series = purpose is SettingsFinanceDocumentPurposeV1.ManualReceiptRecord or SettingsFinanceDocumentPurposeV1.CustomerInstrumentCollect
            ? SettingsDocumentSeriesV1.FinanceReceipt : SettingsDocumentSeriesV1.FinanceVoucher;
        return Task.FromResult(new SettingsDocumentNumberReservationV1(context.CompanyId, series, sourceId,
            businessDate, $"SENTETIK-{businessDate.Year:D4}-{next:D6}", "1", next, false));
    }
}
