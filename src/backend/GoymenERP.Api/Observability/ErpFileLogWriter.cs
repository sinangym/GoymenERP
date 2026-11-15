using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032: appends JSON lines to one UTC-daily file (<c>goymen-erp-api-yyyyMMdd.jsonl</c>) through a bounded queue
/// (<see cref="ErpFileLogQueue"/>). The request path never waits on disk. KLN-D51 (Sinan 2026-11-15): when the
/// queue is full the oldest information records are dropped, error records are kept, and the dropped count is
/// reported twice: on the <see cref="ErpTelemetry.FileLogDroppedCounterName"/> counter at once, and as a
/// <see cref="DropReportEventName"/> record written into the file itself when the writer catches up, so the gap
/// is visible where the log is read even with the metric exporter off. A write failure drops the record, counts
/// it (reported in the same record) and reopens the file on the next record. KLN-D52 (Sinan 2026-11-15): expired
/// daily files are deleted under the configured retention; deletion still runs only when a retention policy is
/// configured, and only for files whose whole name matches the daily pattern.
/// </summary>
public sealed class ErpFileLogWriter : IDisposable
{
    public const string FileNamePrefix = "goymen-erp-api-";

    public const string FileNameExtension = ".jsonl";

    public const string FileDateFormat = "yyyyMMdd";

    /// <summary>Event name of the record that reports dropped records (KLN-D51).</summary>
    public const string DropReportEventName = "FileLogRecordsDropped";

    public const string DropReportTemplate =
        "File log records were dropped. Information={DroppedInformation} Warning={DroppedWarning} "
        + "Error={DroppedError} WriteFailures={WriteFailures}";

    public const string DroppedInformationProperty = "DroppedInformation";

    public const string DroppedWarningProperty = "DroppedWarning";

    public const string DroppedErrorProperty = "DroppedError";

    public const string WriteFailuresProperty = "WriteFailures";

    /// <summary>Longest wait for queued records to reach the file when the host stops.</summary>
    public static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonWriterOptions DropReportJsonOptions = new() { Indented = false };

    private readonly ErpFileLogSettings settings;
    private readonly TimeProvider timeProvider;
    private readonly Func<ErpFileLogDropCounts, string> dropReportFormatter;
    private readonly ErpFileLogQueue queue;
    private readonly Channel<byte> wake;
    private readonly Task processing;
    private StreamWriter? currentWriter;
    private DateOnly? currentDay;
    private long pendingWriteFailures;
    private int disposed;

    public ErpFileLogWriter(ErpFileLogSettings settings, TimeProvider timeProvider)
        : this(settings, timeProvider, dropReportFormatter: null)
    {
    }

    /// <param name="settings">Resolved file log settings.</param>
    /// <param name="timeProvider">Clock for file days, pruning and the default drop report.</param>
    /// <param name="dropReportFormatter">
    /// Builds the JSON line that reports dropped records; null uses <see cref="FormatDefaultDropReport"/>.
    /// </param>
    public ErpFileLogWriter(
        ErpFileLogSettings settings,
        TimeProvider timeProvider,
        Func<ErpFileLogDropCounts, string>? dropReportFormatter)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settings = settings;
        this.timeProvider = timeProvider;
        this.dropReportFormatter =
            dropReportFormatter ?? new Func<ErpFileLogDropCounts, string>(FormatDefaultDropReport);
        queue = new ErpFileLogQueue(settings.QueueCapacity);

        // A one-slot wake-up signal: the records themselves live in the queue, so a lost extra signal is harmless.
        wake = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });

        // The loop must not inherit the constructing caller's logging scopes or current activity.
        using (ExecutionContext.SuppressFlow())
        {
            processing = Task.Run(ProcessAsync);
        }
    }

    public static string FileNameFor(DateOnly day)
    {
        return FileNamePrefix + day.ToString(FileDateFormat, CultureInfo.InvariantCulture) + FileNameExtension;
    }

    /// <summary>Queues one already-redacted JSON line as an information record.</summary>
    public void Enqueue(string line)
    {
        Enqueue(LogLevel.Information, line);
    }

    /// <summary>
    /// Queues one already-redacted JSON line. Never blocks; a full queue drops by the KLN-D51 order
    /// (<see cref="ErpFileLogQueue"/>) and counts the dropped record.
    /// </summary>
    public void Enqueue(LogLevel logLevel, string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var result = queue.Enqueue(logLevel, line);
        if (result.DroppedClass is { } droppedClass)
        {
            ErpTelemetry.RecordFileLogQueueFullDrop(droppedClass);
        }
        else if (!result.Queued)
        {
            ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonShutdown);
            return;
        }

        if (result.Queued)
        {
            wake.Writer.TryWrite(0);
        }
    }

    /// <summary>
    /// The drop report used when no host formatter is given: the same record shape as the logger provider's
    /// (<c>ErpFileLoggerProvider.FormatRecord</c>), without the environment and request fields.
    /// </summary>
    public string FormatDefaultDropReport(ErpFileLogDropCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, DropReportJsonOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", ErpFileLoggerProvider.SchemaVersion);
            json.WriteString(
                "timestamp",
                timeProvider.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("level", nameof(LogLevel.Warning));
            json.WriteString("category", DropReportCategory);
            json.WriteString("service", ErpTelemetry.ServiceName);
            json.WriteString("eventName", DropReportEventName);
            json.WriteStartObject("properties");
            json.WriteNumber(DroppedInformationProperty, counts.Information);
            json.WriteNumber(DroppedWarningProperty, counts.Warning);
            json.WriteNumber(DroppedErrorProperty, counts.Error);
            json.WriteNumber(WriteFailuresProperty, counts.WriteFailed);
            json.WriteEndObject();
            json.WriteString("template", DropReportTemplate);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The structured state of a drop report, in the shape a logger message carries.</summary>
    public static IReadOnlyList<KeyValuePair<string, object?>> DropReportState(ErpFileLogDropCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return
        [
            new(DroppedInformationProperty, counts.Information),
            new(DroppedWarningProperty, counts.Warning),
            new(DroppedErrorProperty, counts.Error),
            new(WriteFailuresProperty, counts.WriteFailed),
            new(ErpLogRedaction.OriginalFormatKey, DropReportTemplate),
        ];
    }

    /// <summary>Category of the drop report record.</summary>
    public static string DropReportCategory => typeof(ErpFileLogWriter).FullName!;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        queue.Complete();
        wake.Writer.TryComplete();
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
            var reader = wake.Reader;
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out _))
                {
                }

                while (DrainBatch())
                {
                }
            }

            // The signal is complete; records queued before completion are still written.
            while (DrainBatch())
            {
            }
        }
        finally
        {
            CloseCurrentFile();
        }
    }

    /// <summary>
    /// Writes at most one queue's worth of records, then the drop report and a flush, so a queue that keeps
    /// refilling under load still gets its drops reported. Returns true while records remain.
    /// </summary>
    private bool DrainBatch()
    {
        for (var written = 0; written < queue.Capacity && queue.TryDequeue(out var line); written++)
        {
            if (!TryWriteLine(line))
            {
                ErpTelemetry.RecordFileLogDropped(ErpTelemetry.DropReasonWriteFailed);
                pendingWriteFailures++;
            }
        }

        WriteDropReport();
        Flush();
        return queue.Count > 0;
    }

    /// <summary>
    /// Writes one record with the counts dropped since the last report (KLN-D51). The report is written directly,
    /// not queued, so it is never itself dropped by a full queue; if it cannot be written its counts are kept for
    /// the next report.
    /// </summary>
    private void WriteDropReport()
    {
        var counts = queue.TakeDropCounts() with { WriteFailed = pendingWriteFailures };
        if (counts.Total == 0)
        {
            return;
        }

        pendingWriteFailures = 0;
        if (!TryWriteLine(dropReportFormatter(counts)))
        {
            queue.RestoreDropCounts(counts);
            pendingWriteFailures += counts.WriteFailed;
        }
    }

    private bool TryWriteLine(string line)
    {
        try
        {
            var writer = EnsureWriter();
            writer.Write(line);
            writer.Write('\n');
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloseCurrentFile();
            return false;
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
            pendingWriteFailures++;
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
