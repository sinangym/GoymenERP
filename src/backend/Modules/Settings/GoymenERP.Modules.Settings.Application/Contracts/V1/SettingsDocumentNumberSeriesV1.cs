using System.Data;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

public enum SettingsDocumentSeriesV1 { SalesOrder = 1, SalesInvoice = 2, DispatchNote = 3, PurchaseOrder = 4 }
public static class SettingsDocumentNumberSeriesPermissionsV1
{
    public const string Manage = "settings.document-number-series.manage"; // Registration only; no grants or Critical classification.
}
public sealed record SettingsDocumentNumberFormatV1(Guid CompanyId, SettingsDocumentSeriesV1 Series, string Prefix, int SequenceDigits, string Version);
public sealed record SettingsDocumentNumberReservationV1(Guid CompanyId, SettingsDocumentSeriesV1 Series, Guid DocumentId,
    DateOnly BusinessDate, string Number, string FormatVersion, long Sequence, bool Replayed);
public sealed record SettingsDocumentNumberGapV1(SettingsDocumentSeriesV1 Series, int Year, string Sequence, string Number,
    Guid DocumentId, DateTimeOffset CancelledAtUtc, string Reason);
public sealed record RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1 Series, string? ExpectedVersion,
    string? Prefix, int? SequenceDigits, string? Reason);
public sealed record SettingsDocumentNumberFormatVersionV1(string Version, string Prefix, int SequenceDigits, string RecordedBy,
    DateTimeOffset RecordedAtUtc, string Reason);
public sealed record SettingsDocumentNumberFormatViewV1(Guid CompanyId, SettingsDocumentSeriesV1 Series,
    IReadOnlyList<SettingsDocumentNumberFormatVersionV1> Versions);
/// <summary>N-12/KIM-D53: reservations join the enclosing owner's open Serializable SQL transaction; this port never commits it.</summary>
public interface ISettingsDocumentNumberSeriesV1
{
    Task<SettingsDocumentNumberFormatV1> GetPolicyAsync(Guid companyId, SettingsDocumentSeriesV1 series, CancellationToken cancellationToken = default);
    Task<SettingsDocumentNumberReservationV1> ReserveAsync(IDbTransaction transaction, Guid companyId,
        SettingsDocumentSeriesV1 series, Guid documentId, DateOnly businessDate, string expectedFormatVersion,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsDocumentNumberReservationV1> ReservePurchaseOrderNumberAsync(IDbTransaction transaction, Guid companyId,
        Guid documentId, DateOnly businessDate, string expectedFormatVersion, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
    Task CancelAsync(IDbTransaction transaction, Guid companyId, SettingsDocumentSeriesV1 series, Guid documentId,
        string reason, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
public interface ISettingsDocumentNumberSeriesAdministrationV1
{
    Task<SettingsDocumentNumberFormatViewV1> GetFormatHistoryAsync(SettingsDocumentSeriesV1 series, CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordFormatAsync(RecordSettingsDocumentNumberFormatV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SettingsDocumentNumberGapV1>> GetGapsAsync(SettingsDocumentSeriesV1 series, int year, CancellationToken cancellationToken = default);
}
public static class SettingsDocumentNumberSeriesProblemCodesV1
{
    public const string FormatMissing = "settings_number_series_format_missing";
    public const string VersionConflict = "settings_number_series_version_conflict";
    public const string IdempotencyConflict = "settings_number_series_idempotency_conflict";
    public const string DocumentAlreadyNumbered = "settings_document_already_numbered";
    public const string Unavailable = "settings_number_series_unavailable";
}
