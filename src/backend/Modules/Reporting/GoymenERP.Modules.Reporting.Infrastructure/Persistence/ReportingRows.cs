namespace GoymenERP.Modules.Reporting.Infrastructure.Persistence;

internal interface IReportingTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>Organization's company row, referenced for the tenant foreign key only (never written or migrated here).</summary>
internal sealed class ReportingCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// WP14 personal saved view (filters + columns) of one report. Own-only: nobody else lists, changes or passivates it. Never
/// deleted: passivation is one version step (AGENTS.md "silme yerine pasif").
/// </summary>
internal sealed class ReportingSavedViewRow : IReportingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string ReportId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
    public string ColumnsJson { get; set; } = "[]";
    public long Version { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// WP14 export record: one append-only row per file that left the system (a report page or an auditor package), with what was
/// asked, how many rows and the SHA-256 of the exact bytes. Written in the same transaction as the export's audit event.
/// </summary>
internal sealed class ReportingExportRow : IReportingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public int Kind { get; set; }
    public string ReportId { get; set; } = "";
    public string Format { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
    public string ColumnsJson { get; set; } = "[]";
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public int RowCount { get; set; }
    public int ByteLength { get; set; }
    public string ContentSha256 { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
}

/// <summary>Saved-view command attempts (idempotency per actor + key; refusals of state replay with their code).</summary>
internal sealed class ReportingAttemptRow : IReportingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ActorUserId { get; set; }
    public string IdempotencyKeySha256 { get; set; } = "";
    public string Command { get; set; } = "";
    public string RequestSha256 { get; set; } = "";
    public Guid TargetId { get; set; }
    public int Status { get; set; }
    public string? RefusalCode { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class ReportingExportKindsV1
{
    public const int ReportPage = 1;
    public const int AuditorPackage = 2;
    public const string AuditorPackageReportId = "auditor-package";
}
