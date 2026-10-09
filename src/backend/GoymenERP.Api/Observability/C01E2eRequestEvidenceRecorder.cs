using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Api.Observability;

/// <summary>
/// Writes the server half of the disposable C-01 activation request evidence.
/// The recorder is absent from every normal host and records no query, header, body or credential.
/// </summary>
public sealed class C01E2eRequestEvidenceRecorder
{
    internal const string EvidenceIssuer = "goymenerp-api-c01-testing-v1";
    internal const string EvidenceCorrelationPrefix = "c01a-evidence-";
    internal const string CapabilityRouteTemplate = "/api/v1/shell/capabilities";
    internal const string BusinessRouteTemplate = "/api/v1/counterparties";
    internal const string EvidencePathConfigurationKey =
        "GOYMEN_ERP_C01_E2E_REQUEST_EVIDENCE_PATH";
    internal const string DatabaseConfigurationKey =
        "GOYMEN_ERP_C01_E2E_DATABASE_NAME";
    internal const string EvidenceIssuerConfigurationKey =
        "GOYMEN_ERP_C01_E2E_EVIDENCE_ISSUER";
    internal const string ContainerIdentityConfigurationKey =
        "GOYMEN_ERP_C01_E2E_CONTAINER_IDENTITY";
    internal const string ProcessIdConfigurationKey =
        "GOYMEN_ERP_C01_E2E_PROCESS_ID";
    internal const string ProcessStartIdentityConfigurationKey =
        "GOYMEN_ERP_C01_E2E_PROCESS_START_IDENTITY";

    private const string DisposableConfirmationConfigurationKey =
        "GOYMEN_ERP_C01_E2E_DISPOSABLE";
    private const string TestedCommitConfigurationKey =
        "GOYMEN_ERP_C01_E2E_TESTED_COMMIT";
    private const string RunIdConfigurationKey =
        "GOYMEN_ERP_C01_E2E_RUN_ID";
    private const string ComposeProjectConfigurationKey =
        "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT";
    private const string EnableConfigurationKey =
        "C01E2e:EnableListCapability";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string path;
    private readonly string testedCommit;
    private readonly string runId;
    private readonly string runAttempt;
    private readonly string composeProject;
    private readonly string database;
    private readonly string issuer;
    private readonly string containerId;
    private readonly int processId;
    private readonly string processStartIdentity;
    private long ordinal;

    private C01E2eRequestEvidenceRecorder(
        string path,
        string testedCommit,
        string runId,
        string runAttempt,
        string composeProject,
        string database,
        string issuer,
        string containerId,
        int processId,
        string processStartIdentity)
    {
        this.path = path;
        this.testedCommit = testedCommit;
        this.runId = runId;
        this.runAttempt = runAttempt;
        this.composeProject = composeProject;
        this.database = database;
        this.issuer = issuer;
        this.containerId = containerId;
        this.processId = processId;
        this.processStartIdentity = processStartIdentity;

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("C-01 request evidence path requires a directory.");
        Directory.CreateDirectory(directory);
        using var evidence = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
    }

    internal static C01E2eRequestEvidenceRecorder? Create(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var evidencePath = configuration[EvidencePathConfigurationKey];
        if (evidencePath is null)
        {
            return null;
        }

        RequireExact(environment.EnvironmentName, "Testing", "environment");
        RequireExact(configuration[EnableConfigurationKey], "true", EnableConfigurationKey);
        RequireExact(
            configuration[DisposableConfirmationConfigurationKey],
            "CONFIRMED",
            DisposableConfirmationConfigurationKey);

        var testedCommit = Required(configuration, TestedCommitConfigurationKey);
        if (testedCommit.Length != 40 || testedCommit.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException("C-01 request evidence requires an exact lowercase Git SHA.");
        }

        var runId = RequiredDigits(configuration, RunIdConfigurationKey);
        var composeProject = Required(configuration, ComposeProjectConfigurationKey);
        var prefix = $"goymen-c01-{runId}-";
        var runAttempt = composeProject.StartsWith(prefix, StringComparison.Ordinal)
            ? composeProject[prefix.Length..]
            : string.Empty;
        if (!IsCanonicalPositiveInteger(runAttempt))
        {
            throw new InvalidOperationException(
                "C-01 request evidence requires a run-bound compose project and canonical attempt.");
        }

        var database = Required(configuration, DatabaseConfigurationKey);
        var expectedDatabase = $"GoymenErpC01_{runId}_{runAttempt}";
        RequireExact(database, expectedDatabase, DatabaseConfigurationKey);

        var issuer = Required(configuration, EvidenceIssuerConfigurationKey);
        RequireExact(issuer, EvidenceIssuer, EvidenceIssuerConfigurationKey);

        var containerId = Required(configuration, ContainerIdentityConfigurationKey);
        if (!IsSafeIdentity(containerId))
        {
            throw new InvalidOperationException("C-01 request evidence container identity is invalid.");
        }
        RequireExact(Environment.MachineName, containerId, ContainerIdentityConfigurationKey);

        var expectedProcessId = RequiredDigits(configuration, ProcessIdConfigurationKey);
        RequireExact(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            expectedProcessId,
            ProcessIdConfigurationKey);

        var processStartIdentity = Required(configuration, ProcessStartIdentityConfigurationKey);
        if (!IsSafeIdentity(processStartIdentity))
        {
            throw new InvalidOperationException("C-01 request evidence process start identity is invalid.");
        }

        if (!Path.IsPathFullyQualified(evidencePath))
        {
            throw new InvalidOperationException("C-01 request evidence path must be absolute.");
        }

        return new C01E2eRequestEvidenceRecorder(
            evidencePath,
            testedCommit,
            runId,
            runAttempt,
            composeProject,
            database,
            issuer,
            containerId,
            Environment.ProcessId,
            processStartIdentity);
    }

    internal async Task RecordAsync(HttpContext context, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        // The disposable host serves setup and health traffic before Playwright
        // begins. Only explicitly namespaced browser evidence belongs in the
        // strict browser/server bijection; all unrelated host traffic is ignored.
        if (!correlationId.StartsWith(EvidenceCorrelationPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var correlationSuffix = correlationId[EvidenceCorrelationPrefix.Length..];
        if (!Guid.TryParseExact(correlationSuffix, "D", out var correlationGuid)
            || !string.Equals(
                correlationSuffix,
                correlationGuid.ToString("D"),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "C-01 request evidence requires a canonical lowercase UUID correlation suffix.");
        }

        var routeTemplate = (context.Request.Method, context.Request.Path.Value) switch
        {
            ("GET", CapabilityRouteTemplate) => CapabilityRouteTemplate,
            ("GET", BusinessRouteTemplate) => BusinessRouteTemplate,
            _ => throw new InvalidOperationException(
                "C-01 request evidence request is outside the exact route-template allowlist."),
        };
        var correlationDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(correlationId)))
            .ToLowerInvariant();

        await writeLock.WaitAsync(context.RequestAborted);
        try
        {
            var record = new RequestEvidence(
                SchemaVersion: 1,
                CorrelationDigest: correlationDigest,
                Method: context.Request.Method,
                Path: routeTemplate,
                Status: context.Response.StatusCode,
                TestedCommit: testedCommit,
                RunId: runId,
                RunAttempt: runAttempt,
                ComposeProject: composeProject,
                Database: database,
                Issuer: issuer,
                ContainerId: containerId,
                ProcessId: processId,
                ProcessStartIdentity: processStartIdentity,
                Ordinal: ++ordinal);
            var line = JsonSerializer.Serialize(record, Json) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line, context.RequestAborted);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"C-01 request evidence requires {key}.");
        }

        return value;
    }

    private static string RequiredDigits(IConfiguration configuration, string key)
    {
        var value = Required(configuration, key);
        if (!IsCanonicalPositiveInteger(value))
        {
            throw new InvalidOperationException($"C-01 request evidence requires canonical numeric {key}.");
        }

        return value;
    }

    private static bool IsCanonicalPositiveInteger(string value) =>
        value.Length > 0
        && value[0] is >= '1' and <= '9'
        && value.All(character => character is >= '0' and <= '9');

    private static bool IsSafeIdentity(string value) =>
        value.Length is > 0 and <= 128
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static void RequireExact(string? actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"C-01 request evidence requires exact {name}.");
        }
    }

    private sealed record RequestEvidence(
        int SchemaVersion,
        string CorrelationDigest,
        string Method,
        string Path,
        int Status,
        string TestedCommit,
        string RunId,
        string RunAttempt,
        string ComposeProject,
        string Database,
        string Issuer,
        string ContainerId,
        int ProcessId,
        string ProcessStartIdentity,
        long Ordinal);
}
