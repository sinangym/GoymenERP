using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

return await SqlServerRcsiReadinessCli.RunAsync(
    args,
    Environment.GetEnvironmentVariable,
    Console.OpenStandardOutput(),
    Console.Error,
    TimeProvider.System,
    static (connection, binding, cancellationToken) =>
        new SqlServerRcsiReadinessProbe().ProbeAsync(
            connection,
            binding,
            cancellationToken));

internal static class SqlServerRcsiReadinessCli
{
    private static readonly string[] ForbiddenRuntimeInjectionEnvironmentVariables =
    [
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "CORECLR_ENABLE_PROFILING",
        "CORECLR_PROFILER",
        "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_32",
        "CORECLR_PROFILER_PATH_64",
        "COR_ENABLE_PROFILING",
        "COR_PROFILER",
        "COR_PROFILER_PATH",
    ];
    private const string Usage =
        "Usage: GoymenERP.SqlServerRcsiReadiness "
        + "--connection-string-env <ENV_NAME> "
        + "--expected-database <DATABASE> "
        + "--target-label <LABEL> "
        + "--expected-logical-endpoint-sha256 <SHA256> "
        + "[--expected-logical-endpoint-sha256 <SHA256> ...] "
        + "--run-id <UUID> --tested-commit <40HEX> "
        + "--expected-tool-assembly-sha256 <SHA256> "
        + "--runtime-payload-root <ABSOLUTE_DIRECTORY> "
        + "--runtime-payload-manifest <ABSOLUTE_JSON_PATH> "
        + "--expected-runtime-payload-manifest-sha256 <SHA256> "
        + "--timeout-seconds <POSITIVE_TECHNICAL_SECONDS> "
        + "--output <NEW_JSON_PATH>";

    public static async Task<int> RunAsync(
        string[] arguments,
        Func<string, string?> getEnvironmentVariable,
        Stream standardOutput,
        TextWriter standardError,
        TimeProvider timeProvider,
        Func<
            SqlConnection,
            SqlServerRcsiReadinessBinding,
            CancellationToken,
            Task<SqlServerRcsiReadinessEvidence>> probeAsync,
        CancellationToken cancellationToken = default,
        SqlServerRcsiReadinessRuntimePayloadVerifierDelegate? verifyRuntimePayload = null)
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
            await WriteUtf8Async(
                standardOutput,
                Encoding.UTF8.GetBytes(Usage + Environment.NewLine));
            return 0;
        }

        var observedAtUtc = timeProvider.GetUtcNow();
        if (!TryParse(arguments, out var options)
            || !SqlServerRcsiReadinessBinding.TryCreate(
                options.TargetLabel,
                options.ExpectedDatabase,
                options.ApprovedLogicalEndpointSha256,
                out var binding)
            || !IsSafeEnvironmentVariableName(options.ConnectionStringEnvironmentVariable)
            || !HasValidInvocationMetadata(options, observedAtUtc))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext: null,
                standardOutput);
        }

        if (ForbiddenRuntimeInjectionEnvironmentVariables.Any(variable =>
                !string.IsNullOrEmpty(getEnvironmentVariable(variable))))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext: null,
                standardOutput);
        }

        verifyRuntimePayload ??= SqlServerRcsiReadinessRuntimePayloadVerifier.TryVerify;
        if (!verifyRuntimePayload(
                options.RuntimePayloadRoot,
                options.RuntimePayloadManifestPath,
                options.ExpectedRuntimePayloadManifestSha256,
                out var toolArtifactIdentity)
            || !SqlServerRcsiReadinessRunContext.TryCreate(
                options.RunId,
                options.TestedCommit,
                options.ExpectedToolAssemblySha256,
                toolArtifactIdentity.AssemblySha256,
                toolArtifactIdentity.RuntimePayloadManifestSha256,
                observedAtUtc,
                out var runContext))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext: null,
                standardOutput);
        }

        if (!string.Equals(
                runContext.TestedCommit,
                toolArtifactIdentity.SourceRevisionId,
                StringComparison.Ordinal)
            || !string.Equals(
                runContext.ExpectedToolAssemblySha256,
                runContext.ObservedToolAssemblySha256,
                StringComparison.Ordinal))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
                runContext,
                standardOutput);
        }

        if (!TryGetFreshOutputPath(options.OutputPath, out var fullOutputPath))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.EvidenceWriteFailed(),
                runContext,
                standardOutput);
        }

        if (SqlServerRcsiReadinessRuntimePayloadVerifier.IsUnderPayloadRoot(
                options.RuntimePayloadRoot!,
                fullOutputPath)
            || SqlServerRcsiReadinessRuntimePayloadVerifier
                .HasReparsePointInExistingAncestry(fullOutputPath))
        {
            return await WriteStandardOutputOnlyAsync(
                SqlServerRcsiReadinessPolicy.InvalidBinding(),
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

            if (!verifyRuntimePayload(
                    options.RuntimePayloadRoot,
                    options.RuntimePayloadManifestPath,
                    options.ExpectedRuntimePayloadManifestSha256,
                    out var postProbeIdentity)
                || postProbeIdentity != toolArtifactIdentity)
            {
                return (int)SqlServerRcsiReadinessExitCode.InvalidBinding;
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
                case "--expected-tool-assembly-sha256":
                    options.ExpectedToolAssemblySha256 = value;
                    break;
                case "--runtime-payload-root":
                    options.RuntimePayloadRoot = value;
                    break;
                case "--runtime-payload-manifest":
                    options.RuntimePayloadManifestPath = value;
                    break;
                case "--expected-runtime-payload-manifest-sha256":
                    options.ExpectedRuntimePayloadManifestSha256 = value;
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
               && !string.IsNullOrWhiteSpace(options.ExpectedToolAssemblySha256)
               && !string.IsNullOrWhiteSpace(options.RuntimePayloadRoot)
               && !string.IsNullOrWhiteSpace(options.RuntimePayloadManifestPath)
               && !string.IsNullOrWhiteSpace(
                   options.ExpectedRuntimePayloadManifestSha256)
               && options.Timeout > TimeSpan.Zero
               && IsSyntacticallyValidOutputPath(options.OutputPath);
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
        catch (Exception exception) when (
            exception is OverflowException or ArgumentOutOfRangeException)
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

    private static bool HasValidInvocationMetadata(
        CliOptions options,
        DateTimeOffset observedAtUtc)
    {
        return Guid.TryParseExact(options.RunId, "D", out var runId)
               && runId != Guid.Empty
               && options.TestedCommit is { Length: 40 }
               && options.TestedCommit.All(Uri.IsHexDigit)
               && SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                   options.ExpectedToolAssemblySha256,
                   out _)
               && SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                   options.ExpectedRuntimePayloadManifestSha256,
                   out _)
               && !string.IsNullOrWhiteSpace(options.RuntimePayloadRoot)
               && Path.IsPathFullyQualified(options.RuntimePayloadRoot)
               && !string.IsNullOrWhiteSpace(options.RuntimePayloadManifestPath)
               && Path.IsPathFullyQualified(options.RuntimePayloadManifestPath)
               && observedAtUtc.Offset == TimeSpan.Zero;
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

    private static bool IsSyntacticallyValidOutputPath(string? outputPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(outputPath);
            return !string.IsNullOrWhiteSpace(Path.GetFileName(fullPath));
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
        Stream standardOutput,
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

        await WriteUtf8Async(standardOutput, bytes);
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
        Stream standardOutput)
    {
        if (runContext is null)
        {
            return (int)evidence.ExitCode;
        }

        await WriteUtf8Async(
            standardOutput,
            SqlServerRcsiReadinessEvidenceJson.SerializeUtf8(evidence, runContext));
        return (int)evidence.ExitCode;
    }

    private static async Task WriteUtf8Async(Stream output, byte[] bytes)
    {
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    }

    private sealed class CliOptions
    {
        public string? ConnectionStringEnvironmentVariable { get; set; }

        public string? ExpectedDatabase { get; set; }

        public string? TargetLabel { get; set; }

        public List<string> ApprovedLogicalEndpointSha256 { get; } = [];

        public string? RunId { get; set; }

        public string? TestedCommit { get; set; }

        public string? ExpectedToolAssemblySha256 { get; set; }

        public string? RuntimePayloadRoot { get; set; }

        public string? RuntimePayloadManifestPath { get; set; }

        public string? ExpectedRuntimePayloadManifestSha256 { get; set; }

        public TimeSpan Timeout { get; set; }

        public string? OutputPath { get; set; }
    }
}

internal delegate bool SqlServerRcsiReadinessRuntimePayloadVerifierDelegate(
    string? payloadRoot,
    string? manifestPath,
    string? expectedManifestSha256,
    out SqlServerRcsiReadinessToolArtifactIdentity identity);

internal sealed record SqlServerRcsiReadinessToolArtifactIdentity(
    string SourceRevisionId,
    string AssemblySha256,
    string RuntimePayloadManifestSha256)
{
    public static bool TryReadExecutingAssembly(
        out string sourceRevisionId,
        out string assemblySha256,
        out string assemblyFileName)
    {
        sourceRevisionId = string.Empty;
        assemblySha256 = string.Empty;
        assemblyFileName = string.Empty;
        try
        {
            var assembly = typeof(SqlServerRcsiReadinessCli).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            var separatorIndex = informationalVersion?.LastIndexOf('+') ?? -1;
            if (separatorIndex < 0)
            {
                return false;
            }

            sourceRevisionId = informationalVersion![(separatorIndex + 1)..];
            if (sourceRevisionId.Length != 40 || !sourceRevisionId.All(Uri.IsHexDigit))
            {
                return false;
            }

            var assemblyPath = assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return false;
            }

            using var stream = new FileStream(
                assemblyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            assemblySha256 = Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
            sourceRevisionId = sourceRevisionId.ToLowerInvariant();
            assemblyFileName = Path.GetFileName(assemblyPath);
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
    }
}

internal static class SqlServerRcsiReadinessRuntimePayloadVerifier
{
    private const string ManifestContractVersion =
        "GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V1";
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool TryVerify(
        string? payloadRoot,
        string? manifestPath,
        string? expectedManifestSha256,
        out SqlServerRcsiReadinessToolArtifactIdentity identity)
    {
        identity = null!;
        try
        {
            if (string.IsNullOrWhiteSpace(payloadRoot)
                || string.IsNullOrWhiteSpace(manifestPath)
                || !Path.IsPathFullyQualified(payloadRoot)
                || !Path.IsPathFullyQualified(manifestPath)
                || !SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                    expectedManifestSha256,
                    out var normalizedExpectedManifestSha256)
                || !SqlServerRcsiReadinessToolArtifactIdentity.TryReadExecutingAssembly(
                    out var sourceRevisionId,
                    out var executingAssemblySha256,
                    out var executingAssemblyFileName))
            {
                return false;
            }

            var fullPayloadRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(payloadRoot));
            var fullManifestPath = Path.GetFullPath(manifestPath);
            if (!Directory.Exists(fullPayloadRoot)
                || !File.Exists(fullManifestPath)
                || HasReparsePointInExistingAncestry(fullPayloadRoot)
                || HasReparsePointInExistingAncestry(fullManifestPath)
                || IsUnderPayloadRoot(fullPayloadRoot, fullManifestPath))
            {
                return false;
            }

            var manifestBytes = File.ReadAllBytes(fullManifestPath);
            var manifestSha256 = ComputeSha256(manifestBytes);
            if (!string.Equals(
                    manifestSha256,
                    normalizedExpectedManifestSha256,
                    StringComparison.Ordinal)
                || !TryParseCanonicalManifest(
                    manifestBytes,
                    out var manifestSourceRevisionId,
                    out var manifestFiles)
                || !string.Equals(
                    manifestSourceRevisionId,
                    sourceRevisionId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var actualFiles = ReadActualPayloadFiles(fullPayloadRoot);
            if (actualFiles is null
                || manifestFiles.Count != actualFiles.Count
                || !manifestFiles.Keys.SequenceEqual(
                    actualFiles.Keys,
                    StringComparer.Ordinal))
            {
                return false;
            }

            foreach (var (path, expectedFile) in manifestFiles)
            {
                var actualFile = actualFiles[path];
                if (expectedFile.Length != actualFile.Length
                    || !string.Equals(
                        expectedFile.Sha256,
                        actualFile.Sha256,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            var requiredFiles = new[]
            {
                executingAssemblyFileName,
                "GoymenERP.BuildingBlocks.SqlServer.dll",
                "GoymenERP.SqlServerRcsiReadiness.deps.json",
                "GoymenERP.SqlServerRcsiReadiness.runtimeconfig.json",
            };
            if (requiredFiles.Any(requiredFile => !manifestFiles.ContainsKey(requiredFile))
                || !string.Equals(
                    manifestFiles[executingAssemblyFileName].Sha256,
                    executingAssemblySha256,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var assembly = typeof(SqlServerRcsiReadinessCli).Assembly;
            if (ReferenceEquals(Assembly.GetEntryAssembly(), assembly)
                && !string.Equals(
                    Path.GetFullPath(assembly.Location),
                    Path.Combine(fullPayloadRoot, executingAssemblyFileName),
                    PathComparison))
            {
                return false;
            }

            identity = new SqlServerRcsiReadinessToolArtifactIdentity(
                sourceRevisionId,
                executingAssemblySha256,
                manifestSha256);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or JsonException)
        {
            return false;
        }
    }

    public static bool IsUnderPayloadRoot(string payloadRoot, string candidatePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(payloadRoot));
        var candidate = Path.GetFullPath(candidatePath);
        if (string.Equals(root, candidate, PathComparison))
        {
            return true;
        }

        return candidate.StartsWith(
            root + Path.DirectorySeparatorChar,
            PathComparison);
    }

    public static bool HasReparsePointInExistingAncestry(string path)
    {
        var currentPath = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            if ((File.Exists(currentPath) || Directory.Exists(currentPath))
                && IsReparsePoint(currentPath))
            {
                return true;
            }

            var parentPath = Path.GetDirectoryName(
                Path.TrimEndingDirectorySeparator(currentPath));
            if (string.IsNullOrWhiteSpace(parentPath)
                || string.Equals(parentPath, currentPath, PathComparison))
            {
                break;
            }

            currentPath = parentPath;
        }

        return false;
    }

    private static SortedDictionary<string, PayloadFile>? ReadActualPayloadFiles(
        string payloadRoot)
    {
        var result = new SortedDictionary<string, PayloadFile>(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(
                     payloadRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (IsReparsePoint(directory))
            {
                return null;
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     payloadRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (IsReparsePoint(filePath))
            {
                return null;
            }

            var relativePath = Path.GetRelativePath(payloadRoot, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!IsSafeRelativePayloadPath(relativePath))
            {
                return null;
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var length = stream.Length;
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!result.TryAdd(relativePath, new PayloadFile(length, sha256)))
            {
                return null;
            }
        }

        return result;
    }

    private static bool TryParseCanonicalManifest(
        byte[] bytes,
        out string sourceRevisionId,
        out SortedDictionary<string, PayloadFile> files)
    {
        sourceRevisionId = string.Empty;
        files = new SortedDictionary<string, PayloadFile>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Count() != 3
            || !root.TryGetProperty("contractVersion", out var contractVersion)
            || !root.TryGetProperty("sourceRevisionId", out var sourceRevision)
            || !root.TryGetProperty("files", out var fileArray)
            || !string.Equals(
                contractVersion.GetString(),
                ManifestContractVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        sourceRevisionId = sourceRevision.GetString()?.ToLowerInvariant() ?? string.Empty;
        if (sourceRevisionId.Length != 40
            || !sourceRevisionId.All(Uri.IsHexDigit)
            || fileArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in fileArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Count() != 3
                || !item.TryGetProperty("path", out var pathProperty)
                || !item.TryGetProperty("length", out var lengthProperty)
                || !item.TryGetProperty("sha256", out var shaProperty)
                || !lengthProperty.TryGetInt64(out var length)
                || length < 0)
            {
                return false;
            }

            var path = pathProperty.GetString();
            var sha256 = shaProperty.GetString();
            if (!IsSafeRelativePayloadPath(path)
                || !SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                    sha256,
                    out var normalizedSha256)
                || !files.TryAdd(path!, new PayloadFile(length, normalizedSha256)))
            {
                return false;
            }
        }

        var canonicalBytes = SerializeCanonicalManifest(sourceRevisionId, files);
        return bytes.AsSpan().SequenceEqual(canonicalBytes);
    }

    private static byte[] SerializeCanonicalManifest(
        string sourceRevisionId,
        IReadOnlyDictionary<string, PayloadFile> files)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", ManifestContractVersion);
            writer.WriteString("sourceRevisionId", sourceRevisionId);
            writer.WriteStartArray("files");
            foreach (var (path, file) in files)
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteNumber("length", file.Length);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static bool IsSafeRelativePayloadPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length != path.Trim().Length
            || Path.IsPathFullyQualified(path)
            || path.Contains('\\')
            || path.Contains(':')
            || path.IndexOfAny(['*', '?']) >= 0)
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.All(segment => segment.Length > 0
                                       && segment != "."
                                       && segment != "..");
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PayloadFile(long Length, string Sha256);
}
