using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Infrastructure.Scanning;

/// <summary>The Defender adapter's settings (validated by <see cref="WindowsDefenderDocumentScanner.Create"/>).</summary>
/// <param name="ExecutablePath">Fully qualified path of <c>MpCmdRun.exe</c>.</param>
/// <param name="Timeout">How long one scan may take; longer is NotScanned (fail-closed).</param>
/// <param name="QuarantineDirectory">A local working folder for the temporary copy that is scanned before storing.</param>
/// <param name="MaximumConcurrentScans">How many MpCmdRun processes may run at once.</param>
public sealed record WindowsDefenderScannerOptions(string ExecutablePath, TimeSpan Timeout, string QuarantineDirectory,
    int MaximumConcurrentScans = WindowsDefenderScannerOptions.DefaultMaximumConcurrentScans)
{
    public const int DefaultMaximumConcurrentScans = 2;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(30);
}

/// <summary>
/// DEC-DOC-009's production malware scanner (user decision 2026-09-30: Windows Defender on the server). The upload's
/// bytes are first written to a QUARANTINED temporary copy (its own folder, never the object store), which
/// <c>MpCmdRun.exe -Scan -ScanType 3 -File &lt;copy&gt; -DisableRemediation</c> scans; only after the verdict does the
/// vault store anything. Verdicts, fail-closed:
/// <list type="bullet">
/// <item>exit code <see cref="CleanExitCode"/> (0) AND the copy still holds exactly the uploaded bytes → Clean;</item>
/// <item>exit code <see cref="ThreatExitCode"/> (2), or real-time protection refusing the copy as infected while it is
/// written → Infected (the upload is refused, nothing is stored);</item>
/// <item>any other exit code, a timeout, a process that cannot start, a copy that cannot be written or that changed or
/// vanished → Unavailable (stored NotScanned, never served).</item>
/// </list>
/// The copy is removed afterwards (it is a scan artefact, not a document). The process runner is injectable for tests.
/// </summary>
public sealed class WindowsDefenderDocumentScanner : IDocumentContentScannerV1, IDisposable
{
    public const string Id = "windows-defender-mpcmdrun-v1";
    public const int CleanExitCode = 0;
    public const int ThreatExitCode = 2;

    // HRESULT_FROM_WIN32(ERROR_VIRUS_INFECTED = 225) and (ERROR_VIRUS_DELETED = 226): real-time protection refused the file.
    private const int VirusInfectedHResult = unchecked((int)0x800700E1);
    private const int VirusDeletedHResult = unchecked((int)0x800700E2);

    private readonly WindowsDefenderScannerOptions options;
    private readonly IDocumentScanProcessRunner runner;
    private readonly SemaphoreSlim gate;

    private WindowsDefenderDocumentScanner(WindowsDefenderScannerOptions options, IDocumentScanProcessRunner runner)
    {
        this.options = options;
        this.runner = runner;
        gate = new SemaphoreSlim(options.MaximumConcurrentScans, options.MaximumConcurrentScans);
    }

    public string ScannerId => Id;

    public WindowsDefenderScannerOptions Options => options;

    /// <summary>
    /// Validates the settings (existing, fully qualified <c>MpCmdRun.exe</c>; a positive timeout up to
    /// <see cref="WindowsDefenderScannerOptions.MaximumTimeout"/>; a writable quarantine folder) and builds the adapter.
    /// Throws <see cref="InvalidOperationException"/> otherwise, so a misconfigured server fails at startup.
    /// </summary>
    public static WindowsDefenderDocumentScanner Create(WindowsDefenderScannerOptions options, IDocumentScanProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        if (string.IsNullOrWhiteSpace(options.ExecutablePath) || !Path.IsPathFullyQualified(options.ExecutablePath)
            || !File.Exists(options.ExecutablePath))
        {
            throw new InvalidOperationException($"Windows Defender MpCmdRun.exe bulunamadı: '{options.ExecutablePath}'. Tam yol verilmelidir.");
        }

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > WindowsDefenderScannerOptions.MaximumTimeout)
        {
            throw new InvalidOperationException("Windows Defender tarama zaman aşımı 1 saniye ile 30 dakika arasında olmalıdır.");
        }

        if (options.MaximumConcurrentScans is < 1 or > 16)
        {
            throw new InvalidOperationException("Eşzamanlı Defender taraması sayısı 1 ile 16 arasında olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(options.QuarantineDirectory) || !Path.IsPathFullyQualified(options.QuarantineDirectory))
        {
            throw new InvalidOperationException("Tarama karantina klasörü tam nitelikli bir yol olmalıdır.");
        }

        var quarantine = Path.GetFullPath(options.QuarantineDirectory);
        var probe = Path.Combine(quarantine, $".goymen-erp-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(quarantine);
            File.WriteAllBytes(probe, [0]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Tarama karantina klasörüne yazılamıyor: {quarantine}.", exception);
        }
        finally
        {
            TryDelete(probe);
        }

        return new WindowsDefenderDocumentScanner(options with { QuarantineDirectory = quarantine }, runner);
    }

    /// <summary>The exact MpCmdRun arguments for one file: custom scan of that file, report only (no remediation).</summary>
    public static IReadOnlyList<string> ScanArguments(string filePath) =>
        ["-Scan", "-ScanType", "3", "-File", filePath, "-DisableRemediation"];

    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, DocumentFileKind kind, CancellationToken cancellationToken = default)
    {
        var extension = DocumentContentPolicy.Info(kind).Extensions[0];
        await gate.WaitAsync(cancellationToken);
        var copy = Path.Combine(options.QuarantineDirectory, $"{Guid.NewGuid():N}{extension}");
        try
        {
            try
            {
                await using (var stream = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(content, cancellationToken);
                }
            }
            catch (IOException exception) when (exception.HResult is VirusInfectedHResult or VirusDeletedHResult)
            {
                return DocumentScanVerdictV1.Infected;
            }

            if (!await HoldsAsync(copy, content, cancellationToken))
            {
                return DocumentScanVerdictV1.Unavailable;
            }

            var result = await runner.RunAsync(options.ExecutablePath, ScanArguments(copy), options.Timeout, cancellationToken);
            if (result.TimedOut || result.ExitCode is not { } exitCode)
            {
                return DocumentScanVerdictV1.Unavailable;
            }

            return exitCode switch
            {
                // Clean only if what was scanned is still exactly what will be stored.
                CleanExitCode => await HoldsAsync(copy, content, cancellationToken) ? DocumentScanVerdictV1.Clean : DocumentScanVerdictV1.Unavailable,
                ThreatExitCode => DocumentScanVerdictV1.Infected,
                _ => DocumentScanVerdictV1.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            // Could not write the copy or start MpCmdRun: no verdict, never "clean".
            return DocumentScanVerdictV1.Unavailable;
        }
        finally
        {
            TryDelete(copy);
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private static async Task<bool> HoldsAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var actual = await File.ReadAllBytesAsync(path, cancellationToken);
            return actual.AsSpan().SequenceEqual(content.Span);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover scan copy is not a document; the runbook cleans the quarantine folder.
        }
    }
}
