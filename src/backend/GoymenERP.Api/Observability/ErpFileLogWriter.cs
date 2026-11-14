using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032: appends JSON lines to one UTC-daily file (<c>goymen-erp-api-yyyyMMdd.jsonl</c>) through a bounded queue.
/// The request path never waits on disk: when the queue is full the record is dropped and counted
/// (<see cref="ErpTelemetry.FileLogDroppedCounterName"/>), as diagnostic telemetry may lose data (spec §6.3).
/// A write failure drops the record, counts it and reopens the file on the next record. Expired daily files are
/// deleted only when a retention policy is configured, and only files whose whole name matches the daily pattern.
/// </summary>
public sealed class ErpFileLogWriter : IDisposable
{
    public const string FileNamePrefix = "goymen-erp-api-";

    public const string FileNameExtension = ".jsonl";

    public const string FileDateFormat = "yyyyMMdd";

    /// <summary>Longest wait for queued records to reach the file when the host stops.</summary>
    public static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ErpFileLogSettings settings;
    private readonly TimeProvider timeProvider;
    private readonly Channel<string> queue;
    private readonly Task processing;
    private StreamWriter? currentWriter;
    private DateOnly? currentDay;
    private int disposed;

    public ErpFileLogWriter(ErpFileLogSettings settings, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settings = settings;
        this.timeProvider = timeProvider;
        queue = Channel.CreateBounded<string>(
            new BoundedChannelOptions(settings.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            static _ => ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonQueueFull));
        processing = Task.Run(ProcessAsync);
    }

    public static string FileNameFor(DateOnly day)
    {
        return FileNamePrefix + day.ToString(FileDateFormat, CultureInfo.InvariantCulture) + FileNameExtension;
    }

    /// <summary>Queues one already-redacted JSON line. Never blocks; a full queue drops the line.</summary>
    public void Enqueue(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!queue.Writer.TryWrite(line))
        {
            ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonShutdown);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        queue.Writer.TryComplete();
        try
        {
            processing.Wait(ShutdownDrainTimeout);
        }
        catch (AggregateException)
        {
            // The loop handles its own IO failures; anything else is diagnostic loss, not a shutdown failure.
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            PruneExpiredFiles(Today());
            var reader = queue.Reader;
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var line))
                {
                    WriteLine(line);
                }

                Flush();
            }
        }
        finally
        {
            CloseCurrentFile();
        }
    }

    private void WriteLine(string line)
    {
        try
        {
            var writer = EnsureWriter();
            writer.Write(line);
            writer.Write('\n');
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonWriteFailed);
            CloseCurrentFile();
        }
    }

    private void Flush()
    {
        if (currentWriter is null)
        {
            return;
        }

        try
        {
            currentWriter.Flush();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonWriteFailed);
            CloseCurrentFile();
        }
    }

    private StreamWriter EnsureWriter()
    {
        var today = Today();
        if (currentWriter is not null && currentDay == today)
        {
            return currentWriter;
        }

        var dayChanged = currentDay is not null && currentDay != today;
        CloseCurrentFile();
        if (dayChanged)
        {
            PruneExpiredFiles(today);
        }

        Directory.CreateDirectory(settings.Directory);
        var stream = new FileStream(
            Path.Combine(settings.Directory, FileNameFor(today)),
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        currentWriter = new StreamWriter(stream, Utf8WithoutBom);
        currentDay = today;
        return currentWriter;
    }

    private void CloseCurrentFile()
    {
        var writer = currentWriter;
        currentWriter = null;
        if (writer is null)
        {
            return;
        }

        try
        {
            writer.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonWriteFailed);
        }
    }

    private void PruneExpiredFiles(DateOnly today)
    {
        if (settings.RetentionDays is not { } retentionDays || settings.RetentionPolicyRef is null)
        {
            return;
        }

        var oldestKeptDay = today.AddDays(-retentionDays);
        try
        {
            if (!Directory.Exists(settings.Directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(
                         settings.Directory,
                         FileNamePrefix + "*" + FileNameExtension,
                         SearchOption.TopDirectoryOnly))
            {
                if (TryParseFileDay(Path.GetFileName(path), out var fileDay) && fileDay < oldestKeptDay)
                {
                    TryDelete(path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Pruning is retried on the next day change; a failed scan never stops logging.
        }
    }

    internal static bool TryParseFileDay(string fileName, out DateOnly day)
    {
        day = default;
        var expectedLength = FileNamePrefix.Length + FileDateFormat.Length + FileNameExtension.Length;
        if (fileName.Length != expectedLength
            || !fileName.StartsWith(FileNamePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(FileNameExtension, StringComparison.Ordinal))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            fileName.AsSpan(FileNamePrefix.Length, FileDateFormat.Length),
            FileDateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out day);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file still held open elsewhere is retried at the next prune.
        }
    }

    private DateOnly Today()
    {
        return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    }
}
