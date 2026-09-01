using System.Globalization;
using System.Text;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

return await SqlServerRcsiReadinessCli.RunAsync(
    args,
    Environment.GetEnvironmentVariable,
    Console.Out,
    Console.Error,
    TimeProvider.System,
    static (connection, binding, cancellationToken) =>
        new SqlServerRcsiReadinessProbe().ProbeAsync(
            connection,
            binding,
            cancellationToken));

internal static class SqlServerRcsiReadinessCli
{
    private const string Usage =
        "Usage: GoymenERP.SqlServerRcsiReadiness "
        + "--connection-string-env <ENV_NAME> "
        + "--expected-database <DATABASE> "
        + "--target-label <LABEL> "
        + "--expected-logical-endpoint-sha256 <SHA256> "
        + "[--expected-logical-endpoint-sha256 <SHA256> ...] "
        + "--run-id <UUID> --tested-commit <40HEX> "
        + "--timeout-seconds <POSITIVE_TECHNICAL_SECONDS> "
        + "--output <NEW_JSON_PATH>";

    public static async Task<int> RunAsync(
        string[] arguments,
        Func<string, string?> getEnvironmentVariable,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider timeProvider,
        Func<
            SqlConnection,
            SqlServerRcsiReadinessBinding,
            CancellationToken,
            Task<SqlServerRcsiReadinessEvidence>> probeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(probeAsync);

        if (arguments.Length == 1
            && string.Equals(arguments[0], "--help", StringComparison.Ordinal))
        {
            await standardOutput.WriteLineAsync(Usage);
            return 0;
        }

        var observedAtUtc = timeProvider.GetUtcNow();
        if (!TryParse(arguments, out var options)
            || !SqlServerRcsiReadinessRunContext.TryCreate(
                options.RunId,
                options.TestedCommit,
                observedAtUtc,
                out var runContext)
            || !SqlServerRcsiReadinessBinding.TryCreate(
                options.TargetLabel,
                options.ExpectedDatabase,
                options.ApprovedLogicalEndpointSha256,
                out var binding)
            || !IsSafeEnvironmentVariableName(options.ConnectionStringEnvironmentVariable))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext: null,
                standardOutput);
        }

        if (!TryGetFreshOutputPath(options.OutputPath, out var fullOutputPath))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.EvidenceWriteFailed(),
                runContext,
                standardOutput);
        }

        var connectionString = getEnvironmentVariable(
            options.ConnectionStringEnvironmentVariable!);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return await WriteEvidenceAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext,
                fullOutputPath,
                standardOutput,
                standardError);
        }

        SqlConnection connection;
        try
        {
            connection = new SqlConnection(connectionString);
        }
        catch (ArgumentException)
        {
            return await WriteEvidenceAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext,
                fullOutputPath,
                standardOutput,
                standardError);
        }

        await using (connection)
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken))
        {
            timeoutSource.CancelAfter(options.Timeout);
            SqlServerRcsiReadinessEvidence evidence;
            try
            {
                evidence = await probeAsync(
                    connection,
                    binding,
                    timeoutSource.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested
                && timeoutSource.IsCancellationRequested)
            {
                evidence = SqlServerRcsiReadinessPolicy.ProbeFailed(
                    binding,
                    TryReadConfiguredDatabase(connection));
            }

            return await WriteEvidenceAsync(
                evidence,
                runContext,
                fullOutputPath,
                standardOutput,
                standardError);
        }
    }

    private static bool TryParse(string[] arguments, out CliOptions options)
    {
        options = new CliOptions();
        if (arguments.Length == 0 || arguments.Length % 2 != 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index += 2)
        {
            var name = arguments[index];
            var value = arguments[index + 1];
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(
                    name,
                    "--expected-logical-endpoint-sha256",
                    StringComparison.Ordinal))
            {
                options.ApprovedLogicalEndpointSha256.Add(value);
                continue;
            }

            if (!seen.Add(name))
            {
                return false;
            }

            switch (name)
            {
                case "--connection-string-env":
                    options.ConnectionStringEnvironmentVariable = value;
                    break;
                case "--expected-database":
                    options.ExpectedDatabase = value;
                    break;
                case "--target-label":
                    options.TargetLabel = value;
                    break;
                case "--run-id":
                    options.RunId = value;
                    break;
                case "--tested-commit":
                    options.TestedCommit = value;
                    break;
                case "--timeout-seconds":
                    if (!TryParseTimeout(value, out var timeout))
                    {
                        return false;
                    }

                    options.Timeout = timeout;
                    break;
                case "--output":
                    options.OutputPath = value;
                    break;
                default:
                    return false;
            }
        }

        return !string.IsNullOrWhiteSpace(options.ConnectionStringEnvironmentVariable)
               && !string.IsNullOrWhiteSpace(options.ExpectedDatabase)
               && !string.IsNullOrWhiteSpace(options.TargetLabel)
               && options.ApprovedLogicalEndpointSha256.Count > 0
               && !string.IsNullOrWhiteSpace(options.RunId)
               && !string.IsNullOrWhiteSpace(options.TestedCommit)
               && options.Timeout > TimeSpan.Zero
               && !string.IsNullOrWhiteSpace(options.OutputPath);
    }

    private static bool TryParseTimeout(string value, out TimeSpan timeout)
    {
        timeout = TimeSpan.Zero;
        if (!long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds)
            || seconds <= 0)
        {
            return false;
        }

        try
        {
            timeout = TimeSpan.FromSeconds(seconds);
            return timeout.TotalMilliseconds <= uint.MaxValue - 1;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsSafeEnvironmentVariableName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static bool TryGetFreshOutputPath(
        string? outputPath,
        out string fullOutputPath)
    {
        fullOutputPath = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return false;
            }

            fullOutputPath = Path.GetFullPath(outputPath);
            return !File.Exists(fullOutputPath)
                   && !Directory.Exists(fullOutputPath)
                   && !string.IsNullOrWhiteSpace(Path.GetFileName(fullOutputPath));
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }

    private static string? TryReadConfiguredDatabase(SqlConnection connection)
    {
        try
        {
            return connection.Database;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<int> WriteEvidenceAsync(
        SqlServerRcsiReadinessEvidence evidence,
        SqlServerRcsiReadinessRunContext runContext,
        string fullOutputPath,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        var bytes = SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(
            evidence,
            runContext);
        if (!await TryWriteNewEvidenceFileAsync(
                fullOutputPath,
                runContext.RunId,
                bytes))
        {
            await standardError.WriteLineAsync(
                "RCSI readiness evidence could not be written to a new path.");
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.EvidenceWriteFailed(),
                runContext,
                standardOutput);
        }

        await standardOutput.WriteAsync(Encoding.UTF8.GetString(bytes));
        return (int)evidence.ExitCode;
    }

    private static async Task<bool> TryWriteNewEvidenceFileAsync(
        string fullOutputPath,
        string runId,
        byte[] bytes)
    {
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullOutputPath)}.{runId}.tmp");
        var temporaryFileCreated = false;
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(fullOutputPath) || Directory.Exists(fullOutputPath))
            {
                return false;
            }

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                temporaryFileCreated = true;
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: false);
            temporaryFileCreated = false;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (temporaryFileCreated)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
                {
                    // The tool never deletes any path except its own successfully-created temp.
                }
            }
        }
    }

    private static async Task<int> WriteStandardOutputOnlyAsync(
        SqlServerRcsiReadinessEvidence evidence,
        SqlServerRcsiReadinessRunContext? runContext,
        TextWriter standardOutput)
    {
        if (runContext is null)
        {
            return (int)evidence.ExitCode;
        }

        await standardOutput.WriteAsync(
            SqlServerRcsiReadinessEvidenceJson.Serialize(evidence, runContext));
        return (int)evidence.ExitCode;
    }

    private sealed class CliOptions
    {
        public string? ConnectionStringEnvironmentVariable { get; set; }

        public string? ExpectedDatabase { get; set; }

        public string? TargetLabel { get; set; }

        public List<string> ApprovedLogicalEndpointSha256 { get; } = [];

        public string? RunId { get; set; }

        public string? TestedCommit { get; set; }

        public TimeSpan Timeout { get; set; }

        public string? OutputPath { get; set; }
    }
}
