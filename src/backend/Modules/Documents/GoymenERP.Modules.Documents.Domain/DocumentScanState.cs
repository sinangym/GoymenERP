namespace GoymenERP.Modules.Documents.Domain;

/// <summary>
/// DEC-DOC-009's malware-scan result of one stored version, kept apart from any business state. A version whose
/// content was not scanned — no scanner is configured, or the scanner could not answer — is stored but is NEVER
/// served (fail-closed); a scanner that finds malware refuses the upload, so an infected version is never stored.
/// A later rescan is not built: when a scanner arrives, a NotScanned version stays not downloadable until a
/// reviewed rescan path records its own result (append-only; the version row never changes).
/// </summary>
public enum DocumentScanState
{
    NotScanned = 1,
    Clean = 2,
}
