using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.SqlServer;
using Microsoft.Data.SqlClient;

if (!RcsiReadinessProcessContainment.TryEnterOwnProcessGroup())
{
    return (int)SqlServerRcsiReadinessExitCode.InvalidBinding;
}

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
            cancellationToken),
    standardInput: Console.OpenStandardInput());

internal static class SqlServerRcsiReadinessCli
{
    private const int MaximumConnectionSecretUtf8Bytes = ushort.MaxValue;
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
        "DOTNET_DiagnosticPorts",
        "DOTNET_DefaultDiagnosticPortSuspend",
        "DOTNET_EnableDiagnostics",
        "DOTNET_EnableDiagnostics_Debugger",
        "DOTNET_EnableDiagnostics_Profiler",
        "DOTNET_EnableDiagnostics_IPC",
        "LD_PRELOAD",
        "LD_AUDIT",
        "DYLD_INSERT_LIBRARIES",
        "DYLD_LIBRARY_PATH",
    ];
    private const string Usage =
        "Usage: GoymenERP.SqlServerRcsiReadiness "
        + "(--connection-string-env <ENV_NAME> | --connection-secret-stdin true) "
        + "--expected-database <DATABASE> "
        + "--target-label <LABEL> "
        + "--expected-logical-endpoint-sha256 <SHA256> "
        + "[--expected-logical-endpoint-sha256 <SHA256> ...] "
        + "--run-id <UUID> --tested-commit <40HEX> "
        + "--expected-wrapper-script-sha256 <SHA256> "
        + "--observed-wrapper-script-sha256 <SHA256> "
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
        SqlServerRcsiReadinessRuntimePayloadVerifierDelegate? verifyRuntimePayload = null,
        Stream? standardInput = null)
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
            || (!options.ConnectionSecretFromStandardInput
                && !IsSafeEnvironmentVariableName(options.ConnectionStringEnvironmentVariable))
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
                options.ExpectedWrapperScriptSha256,
                options.ObservedWrapperScriptSha256,
                options.ExpectedToolAssemblySha256,
                toolArtifactIdentity.AssemblySha256,
                toolArtifactIdentity.RuntimePayloadManifestSha256,
                toolArtifactIdentity.EntryExecutableSha256,
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
                runContext.ExpectedWrapperScriptSha256,
                runContext.ObservedWrapperScriptSha256,
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

        string? connectionString;
        if (options.ConnectionSecretFromStandardInput)
        {
            connectionString = await ReadConnectionStringFromStandardInputAsync(
                standardInput,
                options.Timeout,
                cancellationToken);
        }
        else
        {
            connectionString = getEnvironmentVariable(
                options.ConnectionStringEnvironmentVariable!);
        }
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
                case "--connection-secret-stdin":
                    if (!string.Equals(value, "true", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    options.ConnectionSecretFromStandardInput = true;
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
                case "--expected-wrapper-script-sha256":
                    options.ExpectedWrapperScriptSha256 = value;
                    break;
                case "--observed-wrapper-script-sha256":
                    options.ObservedWrapperScriptSha256 = value;
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

        return (options.ConnectionSecretFromStandardInput
                   ^ !string.IsNullOrWhiteSpace(options.ConnectionStringEnvironmentVariable))
               && !string.IsNullOrWhiteSpace(options.ExpectedDatabase)
               && !string.IsNullOrWhiteSpace(options.TargetLabel)
               && options.ApprovedLogicalEndpointSha256.Count > 0
               && !string.IsNullOrWhiteSpace(options.RunId)
               && !string.IsNullOrWhiteSpace(options.TestedCommit)
               && !string.IsNullOrWhiteSpace(options.ExpectedWrapperScriptSha256)
               && !string.IsNullOrWhiteSpace(options.ObservedWrapperScriptSha256)
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

        if (!value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            return false;
        }

        return !new[] { "DOTNET_", "CORECLR_", "COR_", "COMPLUS_", "LD_", "DYLD_" }
            .Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ReadConnectionStringFromStandardInputAsync(
        Stream? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (standardInput is null || !standardInput.CanRead)
        {
            return null;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var bytesRead = await standardInput.ReadAsync(
                    chunk.AsMemory(),
                    timeoutSource.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                if (buffer.Length + bytesRead > MaximumConnectionSecretUtf8Bytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), timeoutSource.Token);
            }
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(buffer.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is DecoderFallbackException or IOException or NotSupportedException)
        {
            return null;
        }
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
                   options.ExpectedWrapperScriptSha256,
                   out _)
               && SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                   options.ObservedWrapperScriptSha256,
                   out _)
               && SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                   options.ExpectedToolAssemblySha256,
                   out _)
               && SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                   options.ExpectedRuntimePayloadManifestSha256,
                   out _)
               && SqlServerRcsiReadinessRuntimePayloadVerifier
                   .IsSafeCanonicalAbsolutePath(options.RuntimePayloadRoot)
               && SqlServerRcsiReadinessRuntimePayloadVerifier
                   .IsSafeCanonicalAbsolutePath(options.RuntimePayloadManifestPath)
               && observedAtUtc.Offset == TimeSpan.Zero;
    }

    private static bool TryGetFreshOutputPath(
        string? outputPath,
        out string fullOutputPath)
    {
        fullOutputPath = string.Empty;
        try
        {
            if (!SqlServerRcsiReadinessRuntimePayloadVerifier
                    .IsSafeCanonicalAbsolutePath(outputPath))
            {
                return false;
            }

            fullOutputPath = Path.GetFullPath(outputPath!);
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
            if (!SqlServerRcsiReadinessRuntimePayloadVerifier
                    .IsSafeCanonicalAbsolutePath(outputPath))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(outputPath!);
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

            if (OperatingSystem.IsWindows())
            {
                File.Move(temporaryPath, fullOutputPath, overwrite: false);
                temporaryFileCreated = false;
            }
            else if (LinkNewEvidenceFile(temporaryPath, fullOutputPath) != 0)
            {
                // Unix File.Move may race between lstat and rename even with overwrite:false.
                // link atomically claims a new name for the fully flushed inode and never
                // replaces an existing destination. Unsupported filesystems fail closed.
                return false;
            }

            // On Unix the finally block removes only our temporary name, not the winner.
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or DllNotFoundException
            or EntryPointNotFoundException)
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

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkNewEvidenceFile(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

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

        public bool ConnectionSecretFromStandardInput { get; set; }

        public string? ExpectedDatabase { get; set; }

        public string? TargetLabel { get; set; }

        public List<string> ApprovedLogicalEndpointSha256 { get; } = [];

        public string? RunId { get; set; }

        public string? TestedCommit { get; set; }

        public string? ExpectedWrapperScriptSha256 { get; set; }

        public string? ObservedWrapperScriptSha256 { get; set; }

        public string? ExpectedToolAssemblySha256 { get; set; }

        public string? RuntimePayloadRoot { get; set; }

        public string? RuntimePayloadManifestPath { get; set; }

        public string? ExpectedRuntimePayloadManifestSha256 { get; set; }

        public TimeSpan Timeout { get; set; }

        public string? OutputPath { get; set; }
    }
}

internal static partial class RcsiReadinessProcessContainment
{
    public static bool TryEnterOwnProcessGroup()
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            return SetProcessGroup(0, 0) == 0;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static extern int SetProcessGroup(int processId, int processGroupId);
}

internal delegate bool SqlServerRcsiReadinessRuntimePayloadVerifierDelegate(
    string? payloadRoot,
    string? manifestPath,
    string? expectedManifestSha256,
    out SqlServerRcsiReadinessToolArtifactIdentity identity);

internal sealed record SqlServerRcsiReadinessToolArtifactIdentity(
    string SourceRevisionId,
    string AssemblySha256,
    string RuntimePayloadManifestSha256,
    string EntryExecutablePath,
    string EntryExecutableSha256)
{
    public static bool TryReadExecutingAssembly(
        out string sourceRevisionId,
        out string assemblySha256,
        out string assemblyFileName,
        out string assemblyPath)
    {
        sourceRevisionId = string.Empty;
        assemblySha256 = string.Empty;
        assemblyFileName = string.Empty;
        assemblyPath = string.Empty;
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

            assemblyPath = assembly.Location;
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
        "GOYMEN-ERP-RCSI-RUNTIME-PAYLOAD-V2";
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool TryVerify(
        string? payloadRoot,
        string? manifestPath,
        string? expectedManifestSha256,
        out SqlServerRcsiReadinessToolArtifactIdentity identity) =>
        TryVerifyCore(
            payloadRoot,
            manifestPath,
            expectedManifestSha256,
            Environment.ProcessPath,
            executingAssemblyPathOverride: null,
            requireCurrentEntryAssembly: true,
            out identity);

    internal static bool TryVerifyForTesting(
        string? payloadRoot,
        string? manifestPath,
        string? expectedManifestSha256,
        string? processExecutablePath,
        out SqlServerRcsiReadinessToolArtifactIdentity identity) =>
        TryVerifyCore(
            payloadRoot,
            manifestPath,
            expectedManifestSha256,
            processExecutablePath,
            string.IsNullOrWhiteSpace(payloadRoot)
                ? null
                : Path.Combine(payloadRoot, "GoymenERP.SqlServerRcsiReadiness.dll"),
            requireCurrentEntryAssembly: false,
            out identity);

    private static bool TryVerifyCore(
        string? payloadRoot,
        string? manifestPath,
        string? expectedManifestSha256,
        string? processExecutablePath,
        string? executingAssemblyPathOverride,
        bool requireCurrentEntryAssembly,
        out SqlServerRcsiReadinessToolArtifactIdentity identity)
    {
        identity = null!;
        try
        {
            if (string.IsNullOrWhiteSpace(payloadRoot)
                || string.IsNullOrWhiteSpace(manifestPath)
                || !IsSafeCanonicalAbsolutePath(payloadRoot)
                || !IsSafeCanonicalAbsolutePath(manifestPath)
                || !IsSafeCanonicalAbsolutePath(processExecutablePath)
                || !SqlServerLogicalEndpointFingerprint.TryNormalizeSha256(
                    expectedManifestSha256,
                    out var normalizedExpectedManifestSha256)
                || !SqlServerRcsiReadinessToolArtifactIdentity.TryReadExecutingAssembly(
                    out var sourceRevisionId,
                    out var executingAssemblySha256,
                    out var executingAssemblyFileName,
                    out var executingAssemblyPath))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(executingAssemblyPathOverride))
            {
                if (!IsSafeCanonicalAbsolutePath(executingAssemblyPathOverride)
                    || !File.Exists(executingAssemblyPathOverride))
                {
                    return false;
                }

                executingAssemblyPath = Path.GetFullPath(executingAssemblyPathOverride);
                executingAssemblyFileName = Path.GetFileName(executingAssemblyPath);
                using var overrideAssemblyStream = new FileStream(
                    executingAssemblyPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                executingAssemblySha256 = Convert.ToHexString(
                        SHA256.HashData(overrideAssemblyStream))
                    .ToLowerInvariant();
            }

            var fullPayloadRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(payloadRoot));
            var fullManifestPath = Path.GetFullPath(manifestPath);
            var fullProcessExecutablePath = Path.GetFullPath(processExecutablePath!);
            var fullExecutingAssemblyPath = Path.GetFullPath(executingAssemblyPath);
            if (!Directory.Exists(fullPayloadRoot)
                || !File.Exists(fullManifestPath)
                || !File.Exists(fullProcessExecutablePath)
                || HasReparsePointInExistingAncestry(fullPayloadRoot)
                || HasReparsePointInExistingAncestry(fullManifestPath)
                || HasReparsePointInExistingAncestry(fullProcessExecutablePath)
                || HasReparsePointInExistingAncestry(fullExecutingAssemblyPath)
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
                    out var manifestEntryExecutablePath,
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
                manifestEntryExecutablePath,
                "GoymenERP.BuildingBlocks.SqlServer.dll",
                "GoymenERP.SqlServerRcsiReadiness.deps.json",
                "GoymenERP.SqlServerRcsiReadiness.runtimeconfig.json",
                "System.Private.CoreLib.dll",
            };
            var requiredNativeRuntimeFiles = OperatingSystem.IsWindows()
                ? new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll" }
                : OperatingSystem.IsMacOS()
                    ? new[] { "libhostfxr.dylib", "libhostpolicy.dylib", "libcoreclr.dylib" }
                    : new[] { "libhostfxr.so", "libhostpolicy.so", "libcoreclr.so" };
            var runtimeConfigPath = Path.Combine(
                fullPayloadRoot,
                "GoymenERP.SqlServerRcsiReadiness.runtimeconfig.json");
            if (requiredFiles.Concat(requiredNativeRuntimeFiles)
                    .Any(requiredFile => !manifestFiles.ContainsKey(requiredFile))
                || !IsSelfContainedRuntimeConfig(runtimeConfigPath)
                || !string.Equals(
                    manifestFiles[executingAssemblyFileName].Sha256,
                    executingAssemblySha256,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var assembly = typeof(SqlServerRcsiReadinessCli).Assembly;
            var expectedAssemblyPath = Path.GetFullPath(
                Path.Combine(fullPayloadRoot, executingAssemblyFileName));
            var expectedProcessExecutablePath = Path.GetFullPath(
                Path.Combine(
                    fullPayloadRoot,
                    manifestEntryExecutablePath.Replace('/', Path.DirectorySeparatorChar)));
            if ((requireCurrentEntryAssembly
                 && !ReferenceEquals(Assembly.GetEntryAssembly(), assembly))
                || !string.Equals(
                    fullExecutingAssemblyPath,
                    expectedAssemblyPath,
                    PathComparison)
                || !string.Equals(
                    fullProcessExecutablePath,
                    expectedProcessExecutablePath,
                    PathComparison))
            {
                return false;
            }

            using var processExecutableStream = new FileStream(
                fullProcessExecutablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var processExecutableSha256 = Convert.ToHexString(
                    SHA256.HashData(processExecutableStream))
                .ToLowerInvariant();
            if (!string.Equals(
                    manifestFiles[manifestEntryExecutablePath].Sha256,
                    processExecutableSha256,
                    StringComparison.Ordinal))
            {
                return false;
            }

            identity = new SqlServerRcsiReadinessToolArtifactIdentity(
                sourceRevisionId,
                executingAssemblySha256,
                manifestSha256,
                manifestEntryExecutablePath,
                processExecutableSha256);
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

    public static bool IsSafeCanonicalAbsolutePath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)
                || !Path.IsPathFullyQualified(path)
                || path.IndexOf('\0') >= 0)
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows())
            {
                if (path.Length < 3
                    || !char.IsAsciiLetter(path[0])
                    || path[1] != ':'
                    || path[2] != Path.DirectorySeparatorChar
                    || path.AsSpan(2).Contains(':')
                    || path.Contains('/')
                    || path.Split(Path.DirectorySeparatorChar)
                        .Any(segment => segment.Contains('~')))
                {
                    return false;
                }

                return string.Equals(
                    Path.TrimEndingDirectorySeparator(path),
                    Path.TrimEndingDirectorySeparator(fullPath),
                    StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                Path.TrimEndingDirectorySeparator(path),
                Path.TrimEndingDirectorySeparator(fullPath),
                StringComparison.Ordinal);
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
        out string entryExecutablePath,
        out SortedDictionary<string, PayloadFile> files)
    {
        sourceRevisionId = string.Empty;
        entryExecutablePath = string.Empty;
        files = new SortedDictionary<string, PayloadFile>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Count() != 4
            || !root.TryGetProperty("contractVersion", out var contractVersion)
            || !root.TryGetProperty("sourceRevisionId", out var sourceRevision)
            || !root.TryGetProperty("entryExecutablePath", out var entryExecutable)
            || !root.TryGetProperty("files", out var fileArray)
            || !string.Equals(
                contractVersion.GetString(),
                ManifestContractVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        sourceRevisionId = sourceRevision.GetString()?.ToLowerInvariant() ?? string.Empty;
        entryExecutablePath = entryExecutable.GetString() ?? string.Empty;
        if (sourceRevisionId.Length != 40
            || !sourceRevisionId.All(Uri.IsHexDigit)
            || !IsSafeRelativePayloadPath(entryExecutablePath)
            || entryExecutablePath.Contains('/')
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

        if (!files.ContainsKey(entryExecutablePath))
        {
            return false;
        }

        var canonicalBytes = SerializeCanonicalManifest(
            sourceRevisionId,
            entryExecutablePath,
            files);
        return bytes.AsSpan().SequenceEqual(canonicalBytes);
    }

    private static byte[] SerializeCanonicalManifest(
        string sourceRevisionId,
        string entryExecutablePath,
        IReadOnlyDictionary<string, PayloadFile> files)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", ManifestContractVersion);
            writer.WriteString("sourceRevisionId", sourceRevisionId);
            writer.WriteString("entryExecutablePath", entryExecutablePath);
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

    private static bool IsSelfContainedRuntimeConfig(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions)
            || runtimeOptions.ValueKind != JsonValueKind.Object
            || runtimeOptions.TryGetProperty("framework", out _)
            || runtimeOptions.TryGetProperty("frameworks", out _)
            || !runtimeOptions.TryGetProperty("includedFrameworks", out var frameworks)
            || frameworks.ValueKind != JsonValueKind.Array
            || frameworks.GetArrayLength() == 0)
        {
            return false;
        }

        return frameworks.EnumerateArray().All(framework =>
            framework.ValueKind == JsonValueKind.Object
            && framework.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(name.GetString())
            && framework.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(version.GetString()));
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PayloadFile(long Length, string Sha256);
}
