using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Ai;

namespace GoymenERP.Api.Ai.SecurityWatch;

/// <summary>
/// WP-C10 READ tool <c>security.tech-watch.summary.v1</c> (DALGA_PLANI_v2 C.10, AI-D18 "güvenlik ve teknoloji takip"):
/// <list type="number">
/// <item>dependency security alerts from the WP16 deterministic scan report (<see cref="IDependencyScanReportSourceV1"/>,
/// file contract <c>contracts/security/dependency-scan-report-v1.schema.json</c>), counted by the scanner's severity;</item>
/// <item>the company's high-severity security events (failed logins, authorization refusals, critical changes) read only
/// through the SecurityEvents owner digest, actors masked;</item>
/// <item>the emergency step list: schema only and always EMPTY. AI-D18 says the list is decided separately; this tool
/// runs no step, proposes no step and has no executor.</item>
/// </list>
/// Read only: no file is written, no change is opened, no setting is touched. Requires the existing
/// <see cref="ErpPermissions.PlatformSecurityEventsRead"/> company-wide; Confidential data class; registered but not
/// allowlisted.
/// </summary>
public sealed class SecurityTechWatchAiTool(
    IDependencyScanReportSourceV1 scanReports,
    SecurityEventDigestReader digestReader,
    TimeProvider timeProvider) : IAiOwnerReadToolV1
{
    public const string ToolId = "security.tech-watch.summary.v1";
    public const string ToolVersion = "1";
    public const string LimitField = "limit";
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 50;
    public const string TargetKind = "security-watch.company";
    public const string EmergencyStepsStatus = "separate-decision-pending";
    public const string EmergencyStepsDecision = "AI-D18";
    private const string InvalidArguments = "arguments";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AiToolDescriptorV1 Descriptor { get; } = new(
        ToolId,
        ToolVersion,
        ownerModule: "Ai",
        AiToolRiskClassV1.Read,
        AiDataClassV1.Confidential,
        requiredPermissions: [ErpPermissions.PlatformSecurityEventsRead],
        fieldAllowlist: [LimitField],
        ownerCommandName: null,
        new AiContractRevisionV1(
            InputSchema: "security.tech-watch.summary.v1/input@1",
            OutputSchema: "security.tech-watch.summary.v1/output@1",
            FieldPolicy: "security.tech-watch.summary.v1/fields@1",
            LimitPolicy: "security.tech-watch.summary.v1/limit-50@1",
            Catalogue: "contracts/ai/tool-catalog-v1.json",
            AuthorizationPolicy: "ai.panel.use+platform.security-events.read@1",
            SourceContract: "contracts/security/dependency-scan-report-v1.schema.json+ISecurityEventQueryServiceV1.GetEventsAsync@v1",
            RedactionPolicy: "security-events/actor-pseudonym-no-correlation-no-hash@1"));

    public async Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadArguments(input.ArgumentsJson, out var limit, out var invalidField))
        {
            return new AiReadResultV1(AiToolOutcomeV1.Invalid, invalidFields: new[] { invalidField });
        }

        SecurityEventDigestV1 digest;
        DependencyScanReadResultV1 scan;
        try
        {
            // Authority first: the owner digest measures the permission; nothing is read from the report before it.
            digest = await digestReader.ReadAsync(context.CompanyId, SecurityEventDigestReader.CategoryNames, cancellationToken);
            if (digest.Status != SecurityEventDigestStatusV1.Ok)
            {
                return new AiReadResultV1(digest.Status == SecurityEventDigestStatusV1.Denied
                    ? AiToolOutcomeV1.Denied
                    : AiToolOutcomeV1.Unavailable);
            }

            scan = scanReports.Read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AiReadResultV1(AiToolOutcomeV1.Unavailable);
        }

        var now = timeProvider.GetUtcNow().ToUniversalTime();
        var highSeverity = digest.Items.Where(item => item.Severity == SecurityEventSeveritiesV1.High).ToArray();
        var content = new SecurityTechWatchContentV1(
            DependencySection(scan, limit),
            new SecurityTechWatchEventsV1(
                digest.Complete,
                SecurityEventDigestReader.PageSize,
                highSeverity.Sum(item => item.OccurrenceCount),
                highSeverity.Length,
                [.. highSeverity.Take(limit)]),
            new SecurityEmergencyStepListV1(EmergencyStepsStatus, EmergencyStepsDecision, Executable: false, Steps: []));
        var source = new AiSourceV1(ToolId, ToolVersion, TargetKind, context.CompanyId, "Güvenlik ve teknoloji takibi",
            VersionOf(scan, digest), now, AiDataClassV1.Confidential);
        return new AiReadResultV1(AiToolOutcomeV1.Succeeded, JsonSerializer.Serialize(content, Json), source);
    }

    private static SecurityTechWatchDependenciesV1 DependencySection(DependencyScanReadResultV1 scan, int limit)
    {
        if (scan.Status != DependencyScanReadStatusV1.Ok || scan.Report is not { } report)
        {
            return new SecurityTechWatchDependenciesV1(StatusName(scan.Status), null, null, [], [], 0, 0, []);
        }

        var ordered = report.Findings
            .OrderByDescending(finding => IndexOf(DependencyScanReportV1.Severities, finding.Severity))
            .ThenBy(finding => finding.Ecosystem, StringComparer.Ordinal)
            .ThenBy(finding => finding.Package, StringComparer.Ordinal)
            .ThenBy(finding => finding.AdvisoryId, StringComparer.Ordinal)
            .ToArray();
        var counts = DependencyScanReportV1.Severities
            .Reverse()
            .Select(severity => new SecurityTechWatchSeverityCountV1(severity, report.Findings.Count(finding => finding.Severity == severity)))
            .ToArray();
        var listed = ordered.Take(limit).ToArray();
        return new SecurityTechWatchDependenciesV1(
            StatusName(scan.Status),
            report.GeneratedAtUtc,
            report.SourceCommit,
            report.Scanners,
            counts,
            report.Findings.Count,
            listed.Length,
            listed);
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string StatusName(DependencyScanReadStatusV1 status) => status switch
    {
        DependencyScanReadStatusV1.Ok => "ok",
        DependencyScanReadStatusV1.Missing => "missing",
        DependencyScanReadStatusV1.Invalid => "invalid",
        _ => "not-configured",
    };

    private static string VersionOf(DependencyScanReadResultV1 scan, SecurityEventDigestV1 digest)
    {
        var scanPart = scan.Report is { } report
            ? $"{report.SourceCommit}:{report.GeneratedAtUtc:O}:{report.Findings.Count}"
            : StatusName(scan.Status);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scanPart + "\n" + digest.Version)));
    }

    private static bool TryReadArguments(string? json, out int limit, out string invalidField)
    {
        limit = DefaultLimit;
        invalidField = InvalidArguments;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == LimitField && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var value) && value is >= 1 and <= MaximumLimit)
                {
                    limit = value;
                    continue;
                }

                invalidField = property.Name == LimitField ? LimitField : InvalidArguments;
                return false;
            }

            return true;
        }
    }
}

public sealed record SecurityTechWatchContentV1(
    SecurityTechWatchDependenciesV1 Dependencies,
    SecurityTechWatchEventsV1 SecurityEvents,
    SecurityEmergencyStepListV1 EmergencySteps);

/// <summary><c>Status</c>: ok, not-configured, missing or invalid; only "ok" carries scan values.</summary>
public sealed record SecurityTechWatchDependenciesV1(
    string Status,
    DateTimeOffset? GeneratedAtUtc,
    string? SourceCommit,
    IReadOnlyList<DependencyScanScannerV1> Scanners,
    IReadOnlyList<SecurityTechWatchSeverityCountV1> SeverityCounts,
    int FindingCount,
    int ListedCount,
    IReadOnlyList<DependencyScanFindingV1> Findings);

public sealed record SecurityTechWatchSeverityCountV1(string Severity, int Count);

/// <summary>
/// High-severity company security events among the newest rows read per type (see
/// <see cref="SecurityEventDigestReader"/>). The event channel has no open/closed state, so "open" is not claimed.
/// </summary>
public sealed record SecurityTechWatchEventsV1(
    bool Complete,
    int PageSizePerType,
    int HighSeverityOccurrenceCount,
    int HighSeverityRowCount,
    IReadOnlyList<SecurityEventDigestItemV1> Items);

/// <summary>
/// The emergency step list (AI-D18): its contents are a separate decision, so <c>Steps</c> is always empty and
/// <c>Executable</c> always false. The element schema exists so a later decision can fill it without a contract change.
/// </summary>
public sealed record SecurityEmergencyStepListV1(
    string Status,
    string DecisionRef,
    bool Executable,
    IReadOnlyList<SecurityEmergencyStepV1> Steps);

/// <summary>One pre-approved emergency step (schema only; no instance exists and nothing runs it).</summary>
public sealed record SecurityEmergencyStepV1(
    string StepId,
    string Title,
    string Trigger,
    string Effect,
    string ApprovalDecisionRef,
    bool NotifiesImmediately,
    bool ReviewedAfterwards);
