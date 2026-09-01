using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.BuildingBlocks.SqlServer;

public enum SqlServerRcsiReadinessExitCode
{
    Ready = 0,
    InvalidBinding = 2,
    ExpectedDatabaseMismatch = 3,
    RcsiDisabled = 4,
    ProbeFailed = 5,
    ExpectedLogicalEndpointMismatch = 6,
}

public sealed class SqlServerRcsiReadinessBinding
{
    private SqlServerRcsiReadinessBinding(
        string targetLabel,
        string expectedDatabase,
        ReadOnlyCollection<string> approvedLogicalEndpointSha256)
    {
        TargetLabel = targetLabel;
        ExpectedDatabase = expectedDatabase;
        ApprovedLogicalEndpointSha256 = approvedLogicalEndpointSha256;
    }

    public string TargetLabel { get; }

    public string ExpectedDatabase { get; }

    public IReadOnlyList<string> ApprovedLogicalEndpointSha256 { get; }

    public static SqlServerRcsiReadinessBinding Create(
        string targetLabel,
        string expectedDatabase,
        IEnumerable<string> approvedLogicalEndpointSha256)
    {
        if (!TryCreate(
                targetLabel,
                expectedDatabase,
                approvedLogicalEndpointSha256,
                out var binding))
        {
            throw new ArgumentException(
                "SQL Server RCSI readiness target binding is invalid.");
        }

        return binding;
    }

    public static SqlServerRcsiReadinessBinding CreateFromLogicalEndpoints(
        string targetLabel,
        string expectedDatabase,
        IEnumerable<string> approvedLogicalEndpoints)
    {
        ArgumentNullException.ThrowIfNull(approvedLogicalEndpoints);
        return Create(
            targetLabel,
            expectedDatabase,
            approvedLogicalEndpoints.Select(
                SqlServerLogicalEndpointFingerprint.ComputeSha256));
    }

    public static bool TryCreate(
        string? targetLabel,
        string? expectedDatabase,
        IEnumerable<string>? approvedLogicalEndpointSha256,
        out SqlServerRcsiReadinessBinding binding)
    {
        binding = null!;
        var normalizedLabel = targetLabel?.Trim();
        var normalizedDatabase = expectedDatabase?.Trim();
        if (!IsSafeLabel(normalizedLabel)
            || string.IsNullOrWhiteSpace(normalizedDatabase)
            || normalizedDatabase.Length > 128
            || normalizedDatabase.Any(char.IsControl)
            || approvedLogicalEndpointSha256 is null)
        {
            return false;
        }

        var normalizedFingerprints = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var fingerprint in approvedLogicalEndpointSha256)
        {
            if (!SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                    fingerprint,
                    out var normalizedFingerprint))
            {
                return false;
            }

            normalizedFingerprints.Add(normalizedFingerprint);
        }

        if (normalizedFingerprints.Count == 0)
        {
            return false;
        }

        binding = new SqlServerRcsiReadinessBinding(
            normalizedLabel!,
            normalizedDatabase!,
            Array.AsReadOnly(normalizedFingerprints.ToArray()));
        return true;
    }

    public bool IsApprovedLogicalEndpoint(string observedLogicalEndpointSha256) =>
        ApprovedLogicalEndpointSha256.Contains(
            observedLogicalEndpointSha256,
            StringComparer.Ordinal);

    private static bool IsSafeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-' or '/' or ':');
    }
}

public static class SqlServerLogicalEndpointFingerprint
{
    public static string ComputeSha256(string logicalEndpoint)
    {
        if (!TryComputeSha256(logicalEndpoint, out var fingerprint))
        {
            throw new ArgumentException(
                "SQL Server logical endpoint is invalid.",
                nameof(logicalEndpoint));
        }

        return fingerprint;
    }

    public static bool TryComputeSha256(
        string? logicalEndpoint,
        out string fingerprint)
    {
        fingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(logicalEndpoint)
            || logicalEndpoint.Length > 1024
            || logicalEndpoint.Any(char.IsControl))
        {
            return false;
        }

        var normalized = logicalEndpoint
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
        return true;
    }

    public static bool TryNormalizeSha256(
        string? value,
        out string fingerprint)
    {
        fingerprint = string.Empty;
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            return false;
        }

        fingerprint = value.ToLowerInvariant();
        return true;
    }
}

public sealed class SqlServerRcsiReadinessRunContext
{
    private SqlServerRcsiReadinessRunContext(
        string runId,
        string testedCommit,
        string expectedToolAssemblySha256,
        string observedToolAssemblySha256,
        string runtimePayloadManifestSha256,
        DateTimeOffset observedAtUtc)
    {
        RunId = runId;
        TestedCommit = testedCommit;
        ExpectedToolAssemblySha256 = expectedToolAssemblySha256;
        ObservedToolAssemblySha256 = observedToolAssemblySha256;
        RuntimePayloadManifestSha256 = runtimePayloadManifestSha256;
        ObservedAtUtc = observedAtUtc;
    }

    public string RunId { get; }

    public string TestedCommit { get; }

    public string ExpectedToolAssemblySha256 { get; }

    public string ObservedToolAssemblySha256 { get; }

    public string RuntimePayloadManifestSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static bool TryCreate(
        string? runId,
        string? testedCommit,
        string? expectedToolAssemblySha256,
        string? observedToolAssemblySha256,
        string? runtimePayloadManifestSha256,
        DateTimeOffset observedAtUtc,
        out SqlServerRcsiReadinessRunContext context)
    {
        context = null!;
        if (!Guid.TryParseExact(runId, "D", out var parsedRunId)
            || parsedRunId == Guid.Empty
            || testedCommit is null
            || testedCommit.Length != 40
            || !testedCommit.All(Uri.IsHexDigit)
            || !SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                expectedToolAssemblySha256,
                out var normalizedExpectedToolAssemblySha256)
            || !SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                observedToolAssemblySha256,
                out var normalizedObservedToolAssemblySha256)
            || !SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                runtimePayloadManifestSha256,
                out var normalizedRuntimePayloadManifestSha256)
            || observedAtUtc.Offset != TimeSpan.Zero)
        {
            return false;
        }

        context = new SqlServerRcsiReadinessRunContext(
            parsedRunId.ToString("D", CultureInfo.InvariantCulture),
            testedCommit.ToLowerInvariant(),
            normalizedExpectedToolAssemblySha256,
            normalizedObservedToolAssemblySha256,
            normalizedRuntimePayloadManifestSha256,
            observedAtUtc);
        return true;
    }
}

public sealed record SqlServerRcsiReadinessEvidence(
    string ContractVersion,
    string? TargetLabel,
    IReadOnlyList<string>? ApprovedLogicalEndpointSha256,
    string? ObservedLogicalEndpointSha256,
    bool? LogicalEndpointMatches,
    string? ExpectedDatabase,
    string? ConfiguredDatabase,
    string? ObservedDatabase,
    bool? ReadCommittedSnapshotEnabled,
    string Status,
    string ReasonCode,
    SqlServerRcsiReadinessExitCode ExitCode,
    string QueryMode)
{
    public bool IsReady => ExitCode == SqlServerRcsiReadinessExitCode.Ready;
}

public static class SqlServerRcsiReadinessPolicy
{
    public const string ContractVersion = "GOYMEN-ERP-SQL-RCSI-READINESS-V1";
    public const string ReadOnlyQueryMode = "READ_ONLY_SYS_DATABASES_CURRENT_V1";
    public const string StartupRcsiDisabledMessage =
        "Identity authorization snapshot requires SQL Server READ_COMMITTED_SNAPSHOT ON.";

    public static SqlServerRcsiReadinessEvidence InvalidBinding() =>
        CreateEvidence(
            binding: null,
            observedLogicalEndpointSha256: null,
            logicalEndpointMatches: null,
            configuredDatabase: null,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "BINDING_INVALID",
            exitCode: SqlServerRcsiReadinessExitCode.InvalidBinding);

    public static SqlServerRcsiReadinessEvidence ExpectedDatabaseMismatch(
        SqlServerRcsiReadinessBinding binding,
        string? configuredDatabase,
        string? observedLogicalEndpointSha256,
        bool? logicalEndpointMatches,
        string? observedDatabase) =>
        CreateEvidence(
            binding,
            observedLogicalEndpointSha256,
            logicalEndpointMatches,
            configuredDatabase,
            observedDatabase,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "EXPECTED_DATABASE_MISMATCH",
            exitCode: SqlServerRcsiReadinessExitCode.ExpectedDatabaseMismatch);

    public static SqlServerRcsiReadinessEvidence ExpectedLogicalEndpointMismatch(
        SqlServerRcsiReadinessBinding binding,
        string configuredDatabase,
        string observedLogicalEndpointSha256) =>
        CreateEvidence(
            binding,
            observedLogicalEndpointSha256,
            logicalEndpointMatches: false,
            configuredDatabase,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "EXPECTED_LOGICAL_ENDPOINT_MISMATCH",
            exitCode: SqlServerRcsiReadinessExitCode.ExpectedLogicalEndpointMismatch);

    public static SqlServerRcsiReadinessEvidence Evaluate(
        SqlServerRcsiReadinessBinding binding,
        string configuredDatabase,
        string observedDatabase,
        string observedLogicalEndpointSha256,
        int readCommittedSnapshotSetting)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!binding.IsApprovedLogicalEndpoint(observedLogicalEndpointSha256))
        {
            return ExpectedLogicalEndpointMismatch(
                binding,
                configuredDatabase,
                observedLogicalEndpointSha256);
        }

        if (!string.Equals(
                binding.ExpectedDatabase,
                configuredDatabase,
                StringComparison.Ordinal)
            || !string.Equals(
                binding.ExpectedDatabase,
                observedDatabase,
                StringComparison.Ordinal))
        {
            return ExpectedDatabaseMismatch(
                binding,
                configuredDatabase,
                observedLogicalEndpointSha256,
                logicalEndpointMatches: true,
                observedDatabase);
        }

        if (readCommittedSnapshotSetting != 1)
        {
            return CreateEvidence(
                binding,
                observedLogicalEndpointSha256,
                logicalEndpointMatches: true,
                configuredDatabase,
                observedDatabase,
                readCommittedSnapshotEnabled: false,
                status: "NOT_READY",
                reasonCode: "RCSI_DISABLED",
                exitCode: SqlServerRcsiReadinessExitCode.RcsiDisabled);
        }

        return CreateEvidence(
            binding,
            observedLogicalEndpointSha256,
            logicalEndpointMatches: true,
            configuredDatabase,
            observedDatabase,
            readCommittedSnapshotEnabled: true,
            status: "READY",
            reasonCode: "RCSI_READY",
            exitCode: SqlServerRcsiReadinessExitCode.Ready);
    }

    public static SqlServerRcsiReadinessEvidence ProbeFailed(
        SqlServerRcsiReadinessBinding binding,
        string? configuredDatabase,
        string? observedLogicalEndpointSha256 = null,
        bool? logicalEndpointMatches = null) =>
        CreateEvidence(
            binding,
            observedLogicalEndpointSha256,
            logicalEndpointMatches,
            configuredDatabase,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "PROBE_FAILED",
            exitCode: SqlServerRcsiReadinessExitCode.ProbeFailed);

    public static SqlServerRcsiReadinessEvidence EvidenceWriteFailed() =>
        CreateEvidence(
            binding: null,
            observedLogicalEndpointSha256: null,
            logicalEndpointMatches: null,
            configuredDatabase: null,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "EVIDENCE_WRITE_FAILED",
            exitCode: SqlServerRcsiReadinessExitCode.ProbeFailed);

    public static string GetStartupFailureMessage(SqlServerRcsiReadinessEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence.ExitCode switch
        {
            SqlServerRcsiReadinessExitCode.RcsiDisabled => StartupRcsiDisabledMessage,
            SqlServerRcsiReadinessExitCode.ExpectedDatabaseMismatch =>
                "Identity authorization snapshot SQL Server target does not match the expected database.",
            SqlServerRcsiReadinessExitCode.ExpectedLogicalEndpointMismatch =>
                "Identity authorization snapshot SQL Server logical endpoint is not approved.",
            SqlServerRcsiReadinessExitCode.InvalidBinding =>
                "Identity authorization snapshot SQL Server readiness binding is invalid.",
            _ => "Identity authorization snapshot SQL Server RCSI readiness probe failed closed.",
        };
    }

    private static SqlServerRcsiReadinessEvidence CreateEvidence(
        SqlServerRcsiReadinessBinding? binding,
        string? observedLogicalEndpointSha256,
        bool? logicalEndpointMatches,
        string? configuredDatabase,
        string? observedDatabase,
        bool? readCommittedSnapshotEnabled,
        string status,
        string reasonCode,
        SqlServerRcsiReadinessExitCode exitCode) =>
        new(
            ContractVersion,
            binding?.TargetLabel,
            binding?.ApprovedLogicalEndpointSha256,
            observedLogicalEndpointSha256,
            logicalEndpointMatches,
            binding?.ExpectedDatabase,
            configuredDatabase,
            observedDatabase,
            readCommittedSnapshotEnabled,
            status,
            reasonCode,
            exitCode,
            ReadOnlyQueryMode);
}

public sealed class SqlServerRcsiReadinessProbe
{
    private const string ProbeSql =
        """
        SELECT
            CONVERT(nvarchar(128), DB_NAME()) AS [DatabaseName],
            CONVERT(int, [is_read_committed_snapshot_on]) AS [RcsiEnabled]
        FROM [sys].[databases]
        WHERE [database_id] = DB_ID()
        """;

    public async Task<SqlServerRcsiReadinessEvidence> ProbeAsync(
        DbConnection connection,
        SqlServerRcsiReadinessBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(binding);

        string? configuredDatabase;
        try
        {
            configuredDatabase = connection.Database;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SqlServerRcsiReadinessPolicy.ProbeFailed(binding, configuredDatabase: null);
        }

        if (!string.Equals(
                binding.ExpectedDatabase,
                configuredDatabase,
                StringComparison.Ordinal))
        {
            return SqlServerRcsiReadinessPolicy.ExpectedDatabaseMismatch(
                binding,
                configuredDatabase,
                observedLogicalEndpointSha256: null,
                logicalEndpointMatches: null,
                observedDatabase: null);
        }

        var openedByProbe = connection.State == ConnectionState.Closed;
        string? observedLogicalEndpointSha256 = null;
        bool? logicalEndpointMatches = null;
        var evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
            binding,
            configuredDatabase);
        try
        {
            if (!SqlServerLogicalEndpointFingerprint.TryComputeSha256(
                    connection.DataSource,
                    out observedLogicalEndpointSha256))
            {
                return evidence;
            }

            logicalEndpointMatches = binding.IsApprovedLogicalEndpoint(
                observedLogicalEndpointSha256);
            if (!logicalEndpointMatches.Value)
            {
                return SqlServerRcsiReadinessPolicy.ExpectedLogicalEndpointMismatch(
                    binding,
                    configuredDatabase,
                    observedLogicalEndpointSha256);
            }

            if (openedByProbe)
            {
                await connection.OpenAsync(cancellationToken);
            }

            if (!SqlServerLogicalEndpointFingerprint.TryComputeSha256(
                    connection.DataSource,
                    out observedLogicalEndpointSha256))
            {
                evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                    binding,
                    configuredDatabase);
            }
            else if (!(logicalEndpointMatches = binding.IsApprovedLogicalEndpoint(
                         observedLogicalEndpointSha256)).Value)
            {
                evidence = SqlServerRcsiReadinessPolicy.ExpectedLogicalEndpointMismatch(
                    binding,
                    configuredDatabase,
                    observedLogicalEndpointSha256);
            }
            else
            {
                await using var command = connection.CreateCommand();
                command.CommandText = ProbeSql;
                command.CommandType = CommandType.Text;
                await using var reader = await command.ExecuteReaderAsync(
                    CommandBehavior.SingleRow,
                    cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)
                    || reader.IsDBNull(0)
                    || reader.IsDBNull(1))
                {
                    evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                        binding,
                        configuredDatabase,
                        observedLogicalEndpointSha256,
                        logicalEndpointMatches);
                }
                else
                {
                    var observedDatabase = reader.GetString(0);
                    var rcsiSetting = reader.GetInt32(1);

                    evidence = SqlServerRcsiReadinessPolicy.Evaluate(
                        binding,
                        configuredDatabase,
                        observedDatabase,
                        observedLogicalEndpointSha256,
                        rcsiSetting);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                binding,
                configuredDatabase,
                observedLogicalEndpointSha256,
                logicalEndpointMatches);
        }
        finally
        {
            if (openedByProbe && connection.State != ConnectionState.Closed)
            {
                try
                {
                    await connection.CloseAsync();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                        binding,
                        configuredDatabase,
                        observedLogicalEndpointSha256,
                        logicalEndpointMatches);
                }
            }
        }

        return evidence;
    }
}

public static class SqlServerRcsiReadinessEvidenceJson
{
    public static byte[] SerializeUtf8(
        SqlServerRcsiReadinessEvidence evidence,
        SqlServerRcsiReadinessRunContext runContext)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(runContext);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions
                   {
                       Indented = false,
                       SkipValidation = false,
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", evidence.ContractVersion);
            writer.WriteString("runId", runContext.RunId);
            writer.WriteString("testedCommit", runContext.TestedCommit);
            writer.WriteString(
                "expectedToolAssemblySha256",
                runContext.ExpectedToolAssemblySha256);
            writer.WriteString(
                "observedToolAssemblySha256",
                runContext.ObservedToolAssemblySha256);
            writer.WriteString(
                "runtimePayloadManifestSha256",
                runContext.RuntimePayloadManifestSha256);
            writer.WriteString(
                "observedAtUtc",
                runContext.ObservedAtUtc.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    CultureInfo.InvariantCulture));
            WriteNullableString(writer, "targetLabel", evidence.TargetLabel);
            WriteNullableStringArray(
                writer,
                "approvedLogicalEndpointSha256",
                evidence.ApprovedLogicalEndpointSha256);
            WriteNullableString(
                writer,
                "observedLogicalEndpointSha256",
                evidence.ObservedLogicalEndpointSha256);
            WriteNullableBoolean(
                writer,
                "logicalEndpointMatches",
                evidence.LogicalEndpointMatches);
            WriteNullableString(writer, "expectedDatabase", evidence.ExpectedDatabase);
            WriteNullableString(writer, "configuredDatabase", evidence.ConfiguredDatabase);
            WriteNullableString(writer, "observedDatabase", evidence.ObservedDatabase);
            WriteNullableBoolean(
                writer,
                "readCommittedSnapshotEnabled",
                evidence.ReadCommittedSnapshotEnabled);
            writer.WriteString("status", evidence.Status);
            writer.WriteString("reasonCode", evidence.ReasonCode);
            writer.WriteNumber("exitCode", (int)evidence.ExitCode);
            writer.WriteString("queryMode", evidence.QueryMode);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static string Serialize(
        SqlServerRcsiReadinessEvidence evidence,
        SqlServerRcsiReadinessRunContext runContext) =>
        Encoding.UTF8.GetString(SerializeUtf8(evidence, runContext));

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableBoolean(
        Utf8JsonWriter writer,
        string propertyName,
        bool? value)
    {
        if (value.HasValue)
        {
            writer.WriteBoolean(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }
}
