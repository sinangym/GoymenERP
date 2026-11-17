using System.Data;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>Internal server composition seam; no request-bound identities or authority claims.</summary>
public interface IFinanceDocumentNumberReservationV1
{
    Task<SettingsDocumentNumberReservationV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAuditEnvelope envelope, SettingsFinanceDocumentPurposeV1 purpose, Guid sourceId,
        DateOnly businessDate, CancellationToken cancellationToken);
}

internal sealed class SettingsFinanceDocumentNumberReservationV1(FinanceDbContext db,
    ISettingsDocumentNumberSeriesV1? owner) : IFinanceDocumentNumberReservationV1
{
    public Task<SettingsDocumentNumberReservationV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAuditEnvelope envelope, SettingsFinanceDocumentPurposeV1 purpose, Guid sourceId, DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        if (owner is null || !db.Database.IsRelational()
            || db.Database.CurrentTransaction?.GetDbTransaction() is not { IsolationLevel: IsolationLevel.Serializable } transaction)
            throw new SettingsPrerequisiteException(SettingsDocumentNumberSeriesProblemCodesV1.Unavailable);
        return owner.ReserveFinanceNumberAsync(transaction, context.CompanyId, purpose, sourceId, businessDate,
            new(context.CompanyId, context.ActorId, context.CorrelationId, context.CausationId,
                envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, metadata.IdempotencyKey), cancellationToken);
    }
}

/// <summary>KIM-D53 / Q7. A number is evidence of the actual record, never a draft,
/// bank reference or a second money event. No legacy source is retrospectively numbered.</summary>
internal static class FinanceDocumentNumbering
{
    internal sealed record Answer(SettingsDocumentNumberReservationV1? Number, bool Denied, string? Code);

    internal static FinanceDocumentNumberV1? View(IFinanceNumberedSource row) => row.DocumentNumber is null
        && row.DocumentNumberFormatVersion is null ? null
        : new(row.DocumentNumber ?? throw new InvalidOperationException("Belge numarası okunamadı."),
            row.DocumentNumberFormatVersion ?? throw new InvalidOperationException("Belge biçim sürümü okunamadı."));

    internal static async Task<Answer> ReserveAsync(FinanceDbContext db, IFinanceDocumentNumberReservationV1? owner,
        FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, SettingsFinanceDocumentPurposeV1 purpose,
        Guid sourceId, DateOnly businessDate, CancellationToken token)
    {
        FinanceTreasuryTransaction.MarkNumberReservation(db);
        if (owner is null)
            return new(null, false, SettingsDocumentNumberSeriesProblemCodesV1.Unavailable);
        try
        {
            var result = await owner.ReserveAsync(context, metadata, envelope, purpose, sourceId, businessDate, token);
            var expected = purpose is SettingsFinanceDocumentPurposeV1.ManualReceiptRecord or SettingsFinanceDocumentPurposeV1.CustomerInstrumentCollect
                ? SettingsDocumentSeriesV1.FinanceReceipt : SettingsDocumentSeriesV1.FinanceVoucher;
            if (result.CompanyId != context.CompanyId || result.Series != expected || result.DocumentId != sourceId
                || result.BusinessDate != businessDate || string.IsNullOrWhiteSpace(result.Number) || result.Number.Length > 64
                || !long.TryParse(result.FormatVersion, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var version) || version < 1 || result.Sequence < 1
                || result.FormatVersion != version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                return new(null, false, SettingsDocumentNumberSeriesProblemCodesV1.Unavailable);
            return new(result, false, null);
        }
        catch (SettingsPrerequisiteException error)
        { return new(null, error.Code == "settings_access_denied", error.Code); }
        catch (UnauthorizedAccessException)
        { return new(null, true, "settings_access_denied"); }
    }
}
