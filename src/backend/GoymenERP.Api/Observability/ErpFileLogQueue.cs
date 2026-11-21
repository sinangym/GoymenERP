namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032 / KLN-D51 (Sinan 2026-11-15): the bounded in-memory queue between the file logger and the file writer.
/// When the queue is full the oldest information-class record (Trace, Debug, Information) is dropped to make room;
/// error records (Error, Critical) are never dropped to make room for another record. Every dropped record is
/// counted by its level class so the writer can report the dropped count.
/// <para>
/// Order of preference when the queue is full (an implementation detail of KLN-D51, reversible):
/// <list type="number">
/// <item>the oldest queued information record is dropped and the new record is queued;</item>
/// <item>with no queued information record, a new information record is dropped;</item>
/// <item>otherwise the oldest queued warning is dropped and the new warning or error is queued;</item>
/// <item>when only errors are queued, the new record is dropped (queued errors are kept).</item>
/// </list>
/// Records leave the queue in the order they arrived. All members are thread safe.
/// </para>
/// </summary>
public sealed class ErpFileLogQueue
{
    private readonly object sync = new();
    private readonly Queue<Entry> informationEntries = new();
    private readonly Queue<Entry> warningEntries = new();
    private readonly Queue<Entry> errorEntries = new();
    private long nextSequence;
    private long droppedInformation;
    private long droppedWarning;
    private long droppedError;
    private bool completed;

    public ErpFileLogQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (sync)
            {
                return CountUnsafe();
            }
        }
    }

    public bool IsCompleted
    {
        get
        {
            lock (sync)
            {
                return completed;
            }
        }
    }

    /// <summary>The class a level is queued, dropped and counted under.</summary>
    public static ErpFileLogLevelClass ClassOf(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Error or LogLevel.Critical => ErpFileLogLevelClass.Error,
            LogLevel.Warning => ErpFileLogLevelClass.Warning,
            _ => ErpFileLogLevelClass.Information,
        };
    }

    /// <summary>
    /// Queues one line. Never blocks. Returns what happened, including the class of a record dropped to make
    /// room. After <see cref="Complete"/> the line is refused and not counted here (the writer counts it as a
    /// shutdown drop).
    /// </summary>
    public ErpFileLogEnqueueResult Enqueue(LogLevel logLevel, string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var levelClass = ClassOf(logLevel);
        lock (sync)
        {
            if (completed)
            {
                return ErpFileLogEnqueueResult.Refused;
            }

            ErpFileLogLevelClass? droppedClass = null;
            if (CountUnsafe() >= Capacity)
            {
                ErpFileLogLevelClass evicted;
                if (informationEntries.Count > 0)
                {
                    informationEntries.Dequeue();
                    evicted = ErpFileLogLevelClass.Information;
                }
                else if (levelClass == ErpFileLogLevelClass.Information)
                {
                    CountDrop(levelClass);
                    return new ErpFileLogEnqueueResult(Queued: false, DroppedClass: levelClass);
                }
                else if (warningEntries.Count > 0)
                {
                    warningEntries.Dequeue();
                    evicted = ErpFileLogLevelClass.Warning;
                }
                else
                {
                    // Only errors are queued: they are kept and the new record is the one dropped.
                    CountDrop(levelClass);
                    return new ErpFileLogEnqueueResult(Queued: false, DroppedClass: levelClass);
                }

                CountDrop(evicted);
                droppedClass = evicted;
            }

            QueueFor(levelClass).Enqueue(new Entry(nextSequence++, line));
            return new ErpFileLogEnqueueResult(Queued: true, DroppedClass: droppedClass);
        }
    }

    /// <summary>Takes the oldest queued line across all level classes.</summary>
    public bool TryDequeue(out string line)
    {
        lock (sync)
        {
            var oldest = Older(Older(null, informationEntries), warningEntries);
            oldest = Older(oldest, errorEntries);

            if (oldest is null)
            {
                line = string.Empty;
                return false;
            }

            line = oldest.Dequeue().Line;
            return true;
        }
    }

    /// <summary>Returns the records dropped since the last call and resets the counters.</summary>
    public ErpFileLogDropCounts TakeDropCounts()
    {
        lock (sync)
        {
            var counts = new ErpFileLogDropCounts(droppedInformation, droppedWarning, droppedError, WriteFailed: 0);
            droppedInformation = 0;
            droppedWarning = 0;
            droppedError = 0;
            return counts;
        }
    }

    /// <summary>Puts counts back when the report that carried them could not be written.</summary>
    public void RestoreDropCounts(ErpFileLogDropCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        lock (sync)
        {
            droppedInformation += counts.Information;
            droppedWarning += counts.Warning;
            droppedError += counts.Error;
        }
    }

    /// <summary>Refuses new lines; queued lines can still be taken.</summary>
    public void Complete()
    {
        lock (sync)
        {
            completed = true;
        }
    }

    private static Queue<Entry>? Older(Queue<Entry>? current, Queue<Entry> candidate)
    {
        if (candidate.Count == 0)
        {
            return current;
        }

        return current is null || candidate.Peek().Sequence < current.Peek().Sequence ? candidate : current;
    }

    private int CountUnsafe()
    {
        return informationEntries.Count + warningEntries.Count + errorEntries.Count;
    }

    private Queue<Entry> QueueFor(ErpFileLogLevelClass levelClass)
    {
        return levelClass switch
        {
            ErpFileLogLevelClass.Error => errorEntries,
            ErpFileLogLevelClass.Warning => warningEntries,
            _ => informationEntries,
        };
    }

    private void CountDrop(ErpFileLogLevelClass levelClass)
    {
        switch (levelClass)
        {
            case ErpFileLogLevelClass.Error:
                droppedError++;
                break;
            case ErpFileLogLevelClass.Warning:
                droppedWarning++;
                break;
            default:
                droppedInformation++;
                break;
        }
    }

    private readonly record struct Entry(long Sequence, string Line);
}

public enum ErpFileLogLevelClass
{
    Information,
    Warning,
    Error,
}

/// <summary>What <see cref="ErpFileLogQueue.Enqueue"/> did with a line.</summary>
/// <param name="Queued">The new line is in the queue.</param>
/// <param name="DroppedClass">The class of the record dropped (the new one or an older one), or null.</param>
public sealed record ErpFileLogEnqueueResult(bool Queued, ErpFileLogLevelClass? DroppedClass)
{
    public static readonly ErpFileLogEnqueueResult Refused = new(Queued: false, DroppedClass: null);
}

/// <summary>Records dropped since the last report, by level class, plus records lost to a write failure.</summary>
public sealed record ErpFileLogDropCounts(long Information, long Warning, long Error, long WriteFailed)
{
    public long QueueFull => Information + Warning + Error;

    public long Total => QueueFull + WriteFailed;
}
