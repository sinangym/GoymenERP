namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>WP14 wire constants of the report centre.</summary>
public static class ReportingApiV1
{
    public const string ReportsPath = "/api/v1/reporting/reports";
    public const string ReportPath = "/api/v1/reporting/reports/{reportId}";
    public const string ExportPath = "/api/v1/reporting/reports/{reportId}/exports";
    public const string SavedViewsPath = "/api/v1/reporting/saved-views";
    public const string SavedViewPath = "/api/v1/reporting/saved-views/{viewId:guid}";
    public const string SavedViewPassivatePath = "/api/v1/reporting/saved-views/{viewId:guid}/passivate";
    public const string AuditorPackagePath = "/api/v1/reporting/auditor-packages";
    public const string FormatCsv = "csv";
    public const string FormatXlsx = "xlsx";
}

public sealed record ReportingParameterViewV1(string Id, string Kind, bool Required, IReadOnlyList<string> Choices, string? Minimum,
    string? Maximum);

public sealed record ReportingColumnViewV1(string Id, string Kind);

/// <summary>One report the actor may open now: every required owner permission is held company-wide. <c>CanExport</c> is the export authority.</summary>
public sealed record ReportingReportViewV1(string Id, string OwnerModule, IReadOnlyList<ReportingParameterViewV1> Parameters,
    IReadOnlyList<ReportingColumnViewV1> Columns, bool CanExport);

public sealed record ReportingCatalogResponseV1(Guid CompanyId, IReadOnlyList<ReportingReportViewV1> Reports, bool CanBuildAuditorPackage);

public sealed record ReportingSummaryItemV1(string Id, string Value);

/// <summary>
/// One page of a report as a table: column ids in order, cells as invariant text (null = unknown, never a guessed 0). The sums in
/// <c>Summary</c> are the backend's over the rows of this page only (a page is never presented as the whole).
/// </summary>
public sealed record ReportingTableV1(
    Guid CompanyId,
    string ReportId,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<ReportingSummaryItemV1> Summary,
    IReadOnlyList<ReportingSummaryItemV1> SourceStatuses,
    DateTimeOffset GeneratedAtUtc);

public sealed record ReportingExportRequestV1(string? Format, IReadOnlyDictionary<string, string?>? Parameters, int? Page, int? PageSize,
    IReadOnlyList<string>? Columns);

/// <summary>The produced file and the id of the export record written with it (one record per download, YOL-D13 / DEC-DOC-009).</summary>
public sealed record ReportingFileV1(Guid ExportId, string FileName, string ContentType, byte[] Content, string ContentSha256, int RowCount);

public sealed record ReportingAuditorPackageRequestV1(string? From, string? To);

public sealed record ReportingCommandMetadataV1(string IdempotencyKey, string CorrelationId, string? CausationId);

public sealed record ReportingSavedViewRequestV1(string? ReportId, string? Name, IReadOnlyDictionary<string, string?>? Parameters,
    IReadOnlyList<string>? Columns, string? ExpectedVersion);

public sealed record ReportingSavedViewPassivateRequestV1(string? ExpectedVersion);

public sealed record ReportingSavedViewV1(Guid Id, Guid CompanyId, string ReportId, string Name, IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> Columns, string Version, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record ReportingSavedViewPageV1(Guid CompanyId, string ReportId, IReadOnlyList<ReportingSavedViewV1> Items);

public sealed record ReportingSavedViewResponseV1(ReportingSavedViewV1 View, bool IsReplay, string CorrelationId);

/// <summary>Saved-view limits (technical, per person and report).</summary>
public static class ReportingSavedViewLimitsV1
{
    public const int NameMaximumLength = 80;
    public const int MaximumActivePerReport = 50;
}

/// <summary>The report centre service the host endpoints call.</summary>
public interface IReportingServiceV1
{
    Task<ReportingCatalogResponseV1> ListReportsAsync(CancellationToken cancellationToken = default);

    Task<ReportingTableV1> RunAsync(string reportId, IReadOnlyDictionary<string, string?> parameters, int? page, int? pageSize,
        CancellationToken cancellationToken = default);

    Task<ReportingFileV1> ExportAsync(string reportId, ReportingExportRequestV1 request, CancellationToken cancellationToken = default);

    Task<ReportingFileV1> BuildAuditorPackageAsync(ReportingAuditorPackageRequestV1 request, CancellationToken cancellationToken = default);

    Task<ReportingSavedViewPageV1> ListSavedViewsAsync(string reportId, CancellationToken cancellationToken = default);

    Task<ReportingSavedViewResponseV1> CreateSavedViewAsync(ReportingSavedViewRequestV1 request, ReportingCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);

    Task<ReportingSavedViewResponseV1> UpdateSavedViewAsync(Guid viewId, ReportingSavedViewRequestV1 request,
        ReportingCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ReportingSavedViewResponseV1> PassivateSavedViewAsync(Guid viewId, ReportingSavedViewPassivateRequestV1 request,
        ReportingCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
