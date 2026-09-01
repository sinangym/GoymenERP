using System.Data;
using System.Data.Common;
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
    ExpectedTargetMismatch = 6,
}

public sealed record SqlServerRcsiReadinessBinding
{
    private SqlServerRcsiReadinessBinding(
        string targetLabel,
        string expectedDatabase,
        string expectedDataSourceSha256)
    {
        TargetLabel = targetLabel;
        ExpectedDatabase = expectedDatabase;
        ExpectedDataSourceSha256 = expectedDataSourceSha256;
    }

    public string TargetLabel { get; }

    public string ExpectedDatabase { get; }

    public string ExpectedDataSourceSha256 { get; }

    public static SqlServerRcsiReadinessBinding Create(
        string targetLabel,
        string expectedDatabase,
        string expectedDataSourceSha256)
    {
        if (!TryCreate(
                targetLabel,
                expectedDatabase,
                expectedDataSourceSha256,
                out var binding))
        {
            throw new ArgumentException(
                "SQL Server RCSI readiness target binding is invalid.");
        }

        return binding;
    }

    public static SqlServerRcsiReadinessBinding CreateFromDataSource(
        string targetLabel,
        string expectedDatabase,
        string expectedDataSource) =>
        Create(
            targetLabel,
            expectedDatabase,
            SqlServerDataSourceFingerprint.ComputeSha256(expectedDataSource));

    public static bool TryCreate(
        string? targetLabel,
        string? expectedDatabase,
        string? expectedDataSourceSha256,
        out SqlServerRcsiReadinessBinding binding)
    {
        binding = null!;
        var normalizedLabel = targetLabel?.Trim();
        var normalizedDatabase = expectedDatabase?.Trim();
        if (!IsSafeLabel(normalizedLabel)
            || string.IsNullOrWhiteSpace(normalizedDatabase)
            || normalizedDatabase.Length > 128
            || normalizedDatabase.Any(char.IsControl)
            || !SqlServerDataSourceFingerprint.TryNormalizeSha256(
                expectedDataSourceSha256,
                out var normalizedFingerprint))
        {
            return false;
        }

        binding = new SqlServerRcsiReadinessBinding(
            normalizedLabel!,
            normalizedDatabase!,
            normalizedFingerprint);
        return true;
    }

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

public static class SqlServerDataSourceFingerprint
{
    public static string ComputeSha256(string dataSource)
    {
        if (!TryComputeSha256(dataSource, out var fingerprint))
        {
            throw new ArgumentException(
                "SQL Server data source is invalid.",
                nameof(dataSource));
        }

        return fingerprint;
    }

    public static bool TryComputeSha256(
        string? dataSource,
        out string fingerprint)
    {
        fingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Length > 1024
            || dataSource.Any(char.IsControl))
        {
            return false;
        }

        var normalized = dataSource
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

public sealed record SqlServerRcsiReadinessEvidence(
    string ContractVersion,
    string? TargetLabel,
    string? ExpectedDataSourceSha256,
    string? ObservedDataSourceSha256,
    bool? DataSourceMatches,
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
            observedDataSourceSha256: null,
            dataSourceMatches: null,
            configuredDatabase: null,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "BINDING_INVALID",
            exitCode: SqlServerRcsiReadinessExitCode.InvalidBinding);

    public static SqlServerRcsiReadinessEvidence ExpectedDatabaseMismatch(
        SqlServerRcsiReadinessBinding binding,
        string? configuredDatabase,
        string? observedDataSourceSha256,
        bool? dataSourceMatches,
        string? observedDatabase) =>
        CreateEvidence(
            binding,
            observedDataSourceSha256,
            dataSourceMatches,
            configuredDatabase,
            observedDatabase,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "EXPECTED_DATABASE_MISMATCH",
            exitCode: SqlServerRcsiReadinessExitCode.ExpectedDatabaseMismatch);

    public static SqlServerRcsiReadinessEvidence ExpectedTargetMismatch(
        SqlServerRcsiReadinessBinding binding,
        string configuredDatabase,
        string observedDataSourceSha256) =>
        CreateEvidence(
            binding,
            observedDataSourceSha256,
            dataSourceMatches: false,
            configuredDatabase,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "EXPECTED_TARGET_MISMATCH",
            exitCode: SqlServerRcsiReadinessExitCode.ExpectedTargetMismatch);

    public static SqlServerRcsiReadinessEvidence Evaluate(
        SqlServerRcsiReadinessBinding binding,
        string configuredDatabase,
        string observedDatabase,
        string observedDataSourceSha256,
        int readCommittedSnapshotSetting)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!string.Equals(
                binding.ExpectedDataSourceSha256,
                observedDataSourceSha256,
                StringComparison.Ordinal))
        {
            return ExpectedTargetMismatch(
                binding,
                configuredDatabase,
                observedDataSourceSha256);
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
                observedDataSourceSha256,
                dataSourceMatches: true,
                observedDatabase);
        }

        if (readCommittedSnapshotSetting != 1)
        {
            return CreateEvidence(
                binding,
                observedDataSourceSha256,
                dataSourceMatches: true,
                configuredDatabase,
                observedDatabase,
                readCommittedSnapshotEnabled: false,
                status: "NOT_READY",
                reasonCode: "RCSI_DISABLED",
                exitCode: SqlServerRcsiReadinessExitCode.RcsiDisabled);
        }

        return CreateEvidence(
            binding,
            observedDataSourceSha256,
            dataSourceMatches: true,
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
        string? observedDataSourceSha256 = null,
        bool? dataSourceMatches = null) =>
        CreateEvidence(
            binding,
            observedDataSourceSha256,
            dataSourceMatches,
            configuredDatabase,
            observedDatabase: null,
            readCommittedSnapshotEnabled: null,
            status: "NOT_READY",
            reasonCode: "PROBE_FAILED",
            exitCode: SqlServerRcsiReadinessExitCode.ProbeFailed);

    public static SqlServerRcsiReadinessEvidence EvidenceWriteFailed() =>
        CreateEvidence(
            binding: null,
            observedDataSourceSha256: null,
            dataSourceMatches: null,
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
            SqlServerRcsiReadinessExitCode.ExpectedTargetMismatch =>
                "Identity authorization snapshot SQL Server target identity does not match the expected target.",
            SqlServerRcsiReadinessExitCode.InvalidBinding =>
                "Identity authorization snapshot SQL Server readiness binding is invalid.",
            _ => "Identity authorization snapshot SQL Server RCSI readiness probe failed closed.",
        };
    }

    private static SqlServerRcsiReadinessEvidence CreateEvidence(
        SqlServerRcsiReadinessBinding? binding,
        string? observedDataSourceSha256,
        bool? dataSourceMatches,
        string? configuredDatabase,
        string? observedDatabase,
        bool? readCommittedSnapshotEnabled,
        string status,
        string reasonCode,
        SqlServerRcsiReadinessExitCode exitCode) =>
        new(
            ContractVersion,
            binding?.TargetLabel,
            binding?.ExpectedDataSourceSha256,
            observedDataSourceSha256,
            dataSourceMatches,
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
                observedDataSourceSha256: null,
                dataSourceMatches: null,
                observedDatabase: null);
        }

        var openedByProbe = connection.State == ConnectionState.Closed;
        string? observedDataSourceSha256 = null;
        bool? dataSourceMatches = null;
        var evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
            binding,
            configuredDatabase);
        try
        {
            if (openedByProbe)
            {
                await connection.OpenAsync(cancellationToken);
            }

            if (!SqlServerDataSourceFingerprint.TryComputeSha256(
                    connection.DataSource,
                    out observedDataSourceSha256))
            {
                evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                    binding,
                    configuredDatabase);
            }
            else if (!(dataSourceMatches = string.Equals(
                         binding.ExpectedDataSourceSha256,
                         observedDataSourceSha256,
                         StringComparison.Ordinal)).Value)
            {
                evidence = SqlServerRcsiReadinessPolicy.ExpectedTargetMismatch(
                    binding,
                    configuredDatabase,
                    observedDataSourceSha256);
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
                        observedDataSourceSha256,
                        dataSourceMatches);
                }
                else
                {
                    var observedDatabase = reader.GetString(0);
                    var rcsiSetting = reader.GetInt32(1);

                    evidence = SqlServerRcsiReadinessPolicy.Evaluate(
                        binding,
                        configuredDatabase,
                        observedDatabase,
                        observedDataSourceSha256,
                        rcsiSetting);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                binding,
                configuredDatabase,
                observedDataSourceSha256,
                dataSourceMatches);
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
                        observedDataSourceSha256,
                        dataSourceMatches);
                }
            }
        }

        return evidence;
    }
}

public static class SqlServerRcsiReadinessEvidenceJson
{
    public static byte[] SerializeUtf8(SqlServerRcsiReadinessEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

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
            WriteNullableString(writer, "targetLabel", evidence.TargetLabel);
            WriteNullableString(
                writer,
                "expectedDataSourceSha256",
                evidence.ExpectedDataSourceSha256);
            WriteNullableString(
                writer,
                "observedDataSourceSha256",
                evidence.ObservedDataSourceSha256);
            WriteNullableBoolean(writer, "dataSourceMatches", evidence.DataSourceMatches);
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

    public static string Serialize(SqlServerRcsiReadinessEvidence evidence) =>
        Encoding.UTF8.GetString(SerializeUtf8(evidence));

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
