namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>WP14 problem codes. A code is a wire name; the screen holds the Turkish text.</summary>
public static class ReportingProblemCodesV1
{
    public const string ParameterUnknown = "reporting_parameter_unknown";
    public const string ParameterRequired = "reporting_parameter_required";
    public const string ParameterInvalid = "reporting_parameter_invalid";
    public const string PageInvalid = "reporting_page_invalid";
    public const string FormatInvalid = "reporting_format_invalid";
    public const string PermissionUnregistered = "reporting_permission_unregistered";
    public const string SourceUnavailable = "reporting_source_unavailable";
    public const string SourceNotConnected = "reporting_source_not_connected";
    public const string SourceNotFound = "reporting_source_not_found";
    public const string SourceWindowFull = "reporting_source_window_full";
    public const string TimeZoneUnavailable = "reporting_time_zone_unavailable";
    public const string CommandMetadataInvalid = "reporting_command_metadata_invalid";
    public const string SameKeyPayloadMismatch = "reporting_same_key_payload_mismatch";
    public const string ConcurrentChange = "reporting_concurrent_change";
    public const string VersionMismatch = "reporting_version_mismatch";
    public const string SavedViewInvalid = "reporting_saved_view_invalid";
    public const string SavedViewNameTaken = "reporting_saved_view_name_taken";
    public const string SavedViewPassive = "reporting_saved_view_passive";
    public const string SavedViewLimit = "reporting_saved_view_limit";
}

/// <summary>The closed failure channel of the report centre. Denials disclose no data or company facts.</summary>
public sealed class ReportingException(int statusCode, string code, string? field = null, bool isReplay = false,
    string? originalCorrelationId = null) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string? Field { get; } = field;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}
