using System.Diagnostics;

namespace GoymenERP.Modules.Documents.Infrastructure.Scanning;

/// <summary>
/// Runs one scanner process and reports how it ended. Injectable so that the Defender adapter's verdict mapping is
/// unit-tested without Defender; production uses <see cref="SystemDocumentScanProcessRunner"/>.
/// </summary>
public interface IDocumentScanProcessRunner
{
    /// <summary>
    /// Starts <paramref name="executablePath"/> with exactly these arguments (no shell), waits at most
    /// <paramref name="timeout"/>, and answers the exit code — or <see cref="DocumentScanProcessResult.TimedOut"/> after
    /// killing the process tree. The caller's cancellation also kills the process and is rethrown.
    /// </summary>
    Task<DocumentScanProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>How the scanner process ended: its exit code, or timed out (then no exit code).</summary>
public sealed record DocumentScanProcessResult(int? ExitCode, bool TimedOut)
{
    public static DocumentScanProcessResult Exited(int exitCode) => new(exitCode, false);

    public static DocumentScanProcessResult Timeout { get; } = new(null, true);
}

/// <summary>
/// The real process runner: <see cref="ProcessStartInfo.ArgumentList"/> (each argument quoted by the runtime, never a
/// shell), no window, output drained so the child cannot block on a full pipe, the whole tree killed on timeout or
/// cancellation.
/// </summary>
public sealed class SystemDocumentScanProcessRunner : IDocumentScanProcessRunner
{
    public async Task<DocumentScanProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Tarayıcı süreci başlatılamadı.");
        var drainOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var drainError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return DocumentScanProcessResult.Timeout;
        }

        await Task.WhenAll(drainOutput, drainError);
        return DocumentScanProcessResult.Exited(process.ExitCode);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already exited.
        }
    }
}
