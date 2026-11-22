using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.Ai.SecurityWatch;

/// <summary>
/// WP-C10 file contract for the deterministic dependency scan output of WP16
/// (<c>contracts/security/dependency-scan-report-v1.schema.json</c>, format
/// <see cref="Format"/>). The reader is strict and fail-closed: an unknown key, an unknown ecosystem or severity, a
/// malformed commit, a missing field or an oversized file makes the whole report <see cref="DependencyScanReadStatusV1.Invalid"/>
/// and releases no finding. Severity is the scanner's own label; no threshold or staleness verdict is computed here.
/// </summary>
public sealed partial record DependencyScanReportV1(
    DateTimeOffset GeneratedAtUtc,
    string SourceCommit,
    IReadOnlyList<DependencyScanScannerV1> Scanners,
    IReadOnlyList<DependencyScanFindingV1> Findings)
{
    public const string Format = "goymen.security.dependency-scan-report.v1";
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumFindings = 2000;
    public const int MaximumTextLength = 200;

    public static IReadOnlyList<string> Ecosystems { get; } = ["github-actions", "npm", "nuget", "pip"];

    /// <summary>Scanner labels, lowest first; the order is only used to sort the listing.</summary>
    public static IReadOnlyList<string> Severities { get; } = ["low", "moderate", "high", "critical"];

    public static DependencyScanReadResultV1 Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!HasExactly(root, "format", "generatedAtUtc", "sourceCommit", "scanners", "findings")
                || root.GetProperty("format").GetString() != Format
                || !TryUtc(root.GetProperty("generatedAtUtc"), out var generatedAtUtc)
                || root.GetProperty("sourceCommit").GetString() is not { } commit || !CommitPattern().IsMatch(commit)
                || root.GetProperty("scanners").ValueKind != JsonValueKind.Array
                || root.GetProperty("findings").ValueKind != JsonValueKind.Array
                || root.GetProperty("findings").GetArrayLength() > MaximumFindings)
            {
                return DependencyScanReadResultV1.Invalid;
            }

            var scanners = new List<DependencyScanScannerV1>();
            foreach (var scanner in root.GetProperty("scanners").EnumerateArray())
            {
                if (!HasExactly(scanner, "name", "version")
                    || !TryText(scanner.GetProperty("name"), out var name) || !TryText(scanner.GetProperty("version"), out var version))
                {
                    return DependencyScanReadResultV1.Invalid;
                }

                scanners.Add(new DependencyScanScannerV1(name, version));
            }

            if (scanners.Count == 0)
            {
                return DependencyScanReadResultV1.Invalid;
            }

            var findings = new List<DependencyScanFindingV1>();
            foreach (var finding in root.GetProperty("findings").EnumerateArray())
            {
                if (!HasExactly(finding, "ecosystem", "package", "installedVersion", "fixedVersion", "advisoryId", "severity", "title")
                    || finding.GetProperty("ecosystem").GetString() is not { } ecosystem || !Ecosystems.Contains(ecosystem)
                    || finding.GetProperty("severity").GetString() is not { } severity || !Severities.Contains(severity)
                    || !TryText(finding.GetProperty("package"), out var package)
                    || !TryText(finding.GetProperty("installedVersion"), out var installed)
                    || !TryText(finding.GetProperty("advisoryId"), out var advisory) || !AdvisoryPattern().IsMatch(advisory)
                    || !TryText(finding.GetProperty("title"), out var title))
                {
                    return DependencyScanReadResultV1.Invalid;
                }

                string? fixedVersion = null;
                var fixedElement = finding.GetProperty("fixedVersion");
                if (fixedElement.ValueKind != JsonValueKind.Null)
                {
                    if (!TryText(fixedElement, out var value))
                    {
                        return DependencyScanReadResultV1.Invalid;
                    }

                    fixedVersion = value;
                }

                findings.Add(new DependencyScanFindingV1(ecosystem, package, installed, fixedVersion, advisory, severity, title));
            }

            return DependencyScanReadResultV1.Ok(new DependencyScanReportV1(generatedAtUtc, commit, scanners, findings));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return DependencyScanReadResultV1.Invalid;
        }
    }

    private static bool HasExactly(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool TryText(JsonElement element, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } text
            || text.Trim().Length == 0 || text.Length > MaximumTextLength || text.Any(char.IsControl))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryUtc(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.String
               && element.GetString() is { } text && text.EndsWith('Z')
               && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value)
               && value.Offset == TimeSpan.Zero;
    }

    [GeneratedRegex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9:._-]{2,79}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AdvisoryPattern();
}

public sealed record DependencyScanScannerV1(string Name, string Version);

public sealed record DependencyScanFindingV1(
    string Ecosystem,
    string Package,
    string InstalledVersion,
    string? FixedVersion,
    string AdvisoryId,
    string Severity,
    string Title);

public enum DependencyScanReadStatusV1
{
    NotConfigured,
    Missing,
    Invalid,
    Ok,
}

public sealed record DependencyScanReadResultV1(DependencyScanReadStatusV1 Status, DependencyScanReportV1? Report)
{
    public static DependencyScanReadResultV1 NotConfigured { get; } = new(DependencyScanReadStatusV1.NotConfigured, null);

    public static DependencyScanReadResultV1 Missing { get; } = new(DependencyScanReadStatusV1.Missing, null);

    public static DependencyScanReadResultV1 Invalid { get; } = new(DependencyScanReadStatusV1.Invalid, null);

    public static DependencyScanReadResultV1 Ok(DependencyScanReportV1 report) => new(DependencyScanReadStatusV1.Ok, report);
}

/// <summary>Where the dependency scan report comes from.</summary>
public interface IDependencyScanReportSourceV1
{
    DependencyScanReadResultV1 Read();
}

/// <summary>
/// Reads the report from <c>Ai:SecurityWatch:DependencyScanReportPath</c> on every call (relative paths resolve against
/// the content root). No default path is configured: until an operator points it at the WP16 scan output, the summary
/// says "not configured" (fail-closed). The file is never written here.
/// </summary>
public sealed class ConfiguredDependencyScanReportSource : IDependencyScanReportSourceV1
{
    public const string PathKey = "Ai:SecurityWatch:DependencyScanReportPath";

    private readonly string? path;

    public ConfiguredDependencyScanReportSource(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var configured = configuration[PathKey];
        path = string.IsNullOrWhiteSpace(configured)
            ? null
            : Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
    }

    public DependencyScanReadResultV1 Read()
    {
        if (path is null)
        {
            return DependencyScanReadResultV1.NotConfigured;
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return DependencyScanReadResultV1.Missing;
            }

            return file.Length > DependencyScanReportV1.MaximumBytes
                ? DependencyScanReadResultV1.Invalid
                : DependencyScanReportV1.Parse(File.ReadAllText(file.FullName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DependencyScanReadResultV1.Missing;
        }
    }
}

/// <summary>A fixed report (tests).</summary>
public sealed class FixedDependencyScanReportSource(DependencyScanReadResultV1 result) : IDependencyScanReportSourceV1
{
    public DependencyScanReadResultV1 Read() => result;
}
