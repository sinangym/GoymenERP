using System.Text;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

return await SqlServerRcsiReadinessCli.RunAsync(
    args,
    Environment.GetEnvironmentVariable,
    Console.Out,
    Console.Error);

internal static class SqlServerRcsiReadinessCli
{
    private const string Usage =
        "Usage: GoymenERP.SqlServerRcsiReadiness "
        + "--connection-string-env <ENV_NAME> "
        + "--expected-database <DATABASE> "
        + "--target-label <LABEL> "
        + "--expected-data-source-sha256 <SHA256> [--output <JSON_PATH>]";

    public static async Task<int> RunAsync(
        string[] arguments,
        Func<string, string?> getEnvironmentVariable,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.Length == 1
            && string.Equals(arguments[0], "--help", StringComparison.Ordinal))
        {
            await standardOutput.WriteLineAsync(Usage);
            return 0;
        }

        if (!TryParse(arguments, out var options)
            || !SqlServerRcsiReadinessBinding.TryCreate(
                options.TargetLabel,
                options.ExpectedDatabase,
                options.ExpectedDataSourceSha256,
                out var binding)
            || !IsSafeEnvironmentVariableName(options.ConnectionStringEnvironmentVariable))
        {
            return await WriteEvidenceAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                outputPath: null,
                standardOutput,
                standardError);
        }

        var connectionString = getEnvironmentVariable(
            options.ConnectionStringEnvironmentVariable!);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return await WriteEvidenceAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                options.OutputPath,
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
                options.OutputPath,
                standardOutput,
                standardError);
        }

        await using (connection)
        {
            var evidence = await new SqlServerRcsiReadinessProbe()
                .ProbeAsync(connection, binding);
            return await WriteEvidenceAsync(
                evidence,
                options.OutputPath,
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
            if (!seen.Add(name) || string.IsNullOrWhiteSpace(value))
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
                case "--expected-data-source-sha256":
                    options.ExpectedDataSourceSha256 = value;
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
               && !string.IsNullOrWhiteSpace(options.ExpectedDataSourceSha256);
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

    private static async Task<int> WriteEvidenceAsync(
        SqlServerRcsiReadinessEvidence evidence,
        string? outputPath,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        var bytes = SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(evidence);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            try
            {
                var fullPath = Path.GetFullPath(outputPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(fullPath, bytes);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
            {
                await standardError.WriteLineAsync(
                    "RCSI readiness evidence could not be written.");
                evidence = SqlServerRcsiReadinessPolicy.EvidenceWriteFailed();
            }
        }

        var json = SqlServerRcsiReadinessEvidenceJson.Serialize(evidence);
        await standardOutput.WriteLineAsync(json);
        return (int)evidence.ExitCode;
    }

    private sealed class CliOptions
    {
        public string? ConnectionStringEnvironmentVariable { get; set; }

        public string? ExpectedDatabase { get; set; }

        public string? TargetLabel { get; set; }

        public string? ExpectedDataSourceSha256 { get; set; }

        public string? OutputPath { get; set; }
    }
}
