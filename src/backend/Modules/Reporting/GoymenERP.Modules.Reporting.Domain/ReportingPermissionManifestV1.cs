namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// WP14 (DALGA_PLANI_v2 1.15, YOL-D13, ROL-D13, ROL-D15): the report centre's own permission names, spelled exactly as
/// <c>ErpPermissions</c> registers them. Registered, not granted: no seeded role holds any of them; giving one to a role is a
/// human gate. Reading a report needs the owner's own read permissions (the catalogue declares them); these names cover only
/// what the centre adds: taking data out (export), the auditor package and the intercompany dump.
/// </summary>
public static class ReportingPermissionManifestV1
{
    public const int Version = 1;

    /// <summary>Download a report page as CSV/XLSX (DEC-DOC-009: export is a separate authority). Every export is recorded.</summary>
    public const string Export = "reporting.export";

    /// <summary>YOL-D13 / ROL-D13: build the company's auditor package (read-only export; the auditor gets no system access).</summary>
    public const string AuditorPackageExport = "reporting.auditor-package.export";

    /// <summary>ROL-D15 / YOL-D6: the intercompany transaction dump. Recommended holders are named by ROL-D15; the grant is a human gate.</summary>
    public const string IntercompanyRead = "reporting.intercompany.read";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(
        [Export, AuditorPackageExport, IntercompanyRead], StringComparer.Ordinal);
}
