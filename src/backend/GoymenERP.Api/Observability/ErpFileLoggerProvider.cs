using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032: the structured file log. Each record is one JSON line (schema version <see cref="SchemaVersion"/>) with
/// the UTC time, level, category, event id/name, message template, redacted template values
/// (<see cref="ErpLogRedaction"/>), the request's correlation/causation/source channel, the trace/span id, and the
/// exception type chain. The rendered message, scope values outside the correlation allowlist, request paths and
/// exception messages are never written. Level filtering follows the standard <c>Logging</c> configuration under
/// the <c>ErpFile</c> provider alias.
/// </summary>
[ProviderAlias(ProviderAliasName)]
public sealed class ErpFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    public const string ProviderAliasName = "ErpFile";

    public const int SchemaVersion = 1;

    /// <summary>Deepest inner-exception level written to a record.</summary>
    public const int MaxExceptionDepth = 8;

    private static readonly string[] AllowedScopeKeys = ["CorrelationId", "CausationId", "SourceChannel"];

    private static readonly JsonWriterOptions JsonOptions = new() { Indented = false };

    private readonly ErpFileLogSettings settings;
    private readonly string environmentName;
    private readonly TimeProvider timeProvider;
    private readonly ErpFileLogWriter writer;
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    public ErpFileLoggerProvider(ErpFileLogSettings settings, string environmentName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settings = settings;
        this.environmentName = environmentName;
        this.timeProvider = timeProvider;
        writer = new ErpFileLogWriter(settings, timeProvider, FormatDropReport);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ErpFileLogger(categoryName, this);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        this.scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        writer.Dispose();
    }

    internal IExternalScopeProvider ScopeProvider => scopeProvider;

    internal void Write<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception)
    {
        writer.Enqueue(logLevel, FormatRecord(categoryName, logLevel, eventId, state, exception));
    }

    /// <summary>
    /// KLN-D51: the record that reports dropped records, in the same redacted shape as every other record.
    /// It is written by the writer's own loop, so it carries no request correlation or trace.
    /// </summary>
    public string FormatDropReport(ErpFileLogDropCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return FormatRecord(
            ErpFileLogWriter.DropReportCategory,
            LogLevel.Warning,
            new EventId(0, ErpFileLogWriter.DropReportEventName),
            ErpFileLogWriter.DropReportState(counts),
            exception: null);
    }

    /// <summary>Builds the redacted JSON line for one record; public for tests and for the format contract.</summary>
    public string FormatRecord<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", SchemaVersion);
            json.WriteString(
                "timestamp",
                timeProvider.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("level", LevelName(logLevel));
            json.WriteString("category", categoryName);
            json.WriteString("service", ErpTelemetry.ServiceName);
            json.WriteString("environment", environmentName);
            if (eventId.Id != 0)
            {
                json.WriteNumber("eventId", eventId.Id);
            }

            if (!string.IsNullOrEmpty(eventId.Name))
            {
                json.WriteString("eventName", eventId.Name);
            }

            WriteState(json, state);
            WriteScopes(json);

            var activity = Activity.Current;
            if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C)
            {
                json.WriteString("traceId", activity.TraceId.ToHexString());
                json.WriteString("spanId", activity.SpanId.ToHexString());
            }

            if (exception is not null)
            {
                WriteException(json, exception);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteState<TState>(Utf8JsonWriter json, TState state)
    {
        string? template = null;
        var wroteProperties = false;
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Key, ErpLogRedaction.OriginalFormatKey, StringComparison.Ordinal))
                {
                    template = pair.Value as string;
                    continue;
                }

                if (!wroteProperties)
                {
                    json.WriteStartObject("properties");
                    wroteProperties = true;
                }

                WriteValue(json, pair.Key, ErpLogRedaction.RedactValue(pair.Key, pair.Value));
            }
        }

        if (wroteProperties)
        {
            json.WriteEndObject();
        }

        json.WriteString("template", template ?? ErpLogRedaction.UnstructuredTemplate);
    }

    private void WriteScopes(Utf8JsonWriter json)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        scopeProvider.ForEachScope(
            static (scope, values) =>
            {
                if (scope is not IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    return;
                }

                foreach (var pair in pairs)
                {
                    if (pair.Value is string text
                        && Array.IndexOf(AllowedScopeKeys, pair.Key) >= 0
                        && text.Length <= ErpLogRedaction.MaxStringValueLength)
                    {
                        // Inner scopes win: the last value seen for a key is the innermost one.
                        values[pair.Key] = text;
                    }
                }
            },
            captured);

        foreach (var key in AllowedScopeKeys)
        {
            if (captured.TryGetValue(key, out var value))
            {
                json.WriteString(char.ToLowerInvariant(key[0]) + key[1..], value);
            }
        }
    }

    /// <summary>
    /// Names the most specific well-known exception type the exception is. The type is matched without reflection
    /// (the backend has a zero reflection allow-list), so an exception outside this list is written as its nearest
    /// listed base type, or as <see cref="Exception"/>. Derived types are listed before their base types.
    /// </summary>
    public static string DescribeExceptionType(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var name = exception switch
        {
            TaskCanceledException => typeof(TaskCanceledException).FullName,
            OperationCanceledException => typeof(OperationCanceledException).FullName,
            ObjectDisposedException => typeof(ObjectDisposedException).FullName,
            InvalidOperationException => typeof(InvalidOperationException).FullName,
            ArgumentNullException => typeof(ArgumentNullException).FullName,
            ArgumentOutOfRangeException => typeof(ArgumentOutOfRangeException).FullName,
            ArgumentException => typeof(ArgumentException).FullName,
            TimeoutException => typeof(TimeoutException).FullName,
            UnauthorizedAccessException => typeof(UnauthorizedAccessException).FullName,
            System.IO.IOException => typeof(System.IO.IOException).FullName,
            JsonException => typeof(JsonException).FullName,
            FormatException => typeof(FormatException).FullName,
            NotImplementedException => typeof(NotImplementedException).FullName,
            NotSupportedException => typeof(NotSupportedException).FullName,
            KeyNotFoundException => typeof(KeyNotFoundException).FullName,
            NullReferenceException => typeof(NullReferenceException).FullName,
            OverflowException => typeof(OverflowException).FullName,
            ArithmeticException => typeof(ArithmeticException).FullName,
            OutOfMemoryException => typeof(OutOfMemoryException).FullName,
            AggregateException => typeof(AggregateException).FullName,
            _ => typeof(Exception).FullName,
        };

        return name!;
    }

    private void WriteException(Utf8JsonWriter json, Exception exception)
    {
        json.WriteStartObject("exception");
        json.WriteString("type", DescribeExceptionType(exception));
        json.WriteNumber("hResult", exception.HResult);
        var inner = exception.InnerException;
        if (inner is not null)
        {
            json.WriteStartArray("innerTypes");
            for (var depth = 0; inner is not null && depth < MaxExceptionDepth; depth++)
            {
                json.WriteStringValue(DescribeExceptionType(inner));
                inner = inner.InnerException;
            }

            json.WriteEndArray();
        }

        if (settings.IncludeExceptionStackTrace && exception.StackTrace is { } stackTrace)
        {
            json.WriteString("stackTrace", stackTrace);
        }

        json.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter json, string name, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNull(name);
                break;
            case bool boolean:
                json.WriteBoolean(name, boolean);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                json.WriteNumber(name, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong unsigned:
                json.WriteNumber(name, unsigned);
                break;
            case decimal number:
                json.WriteNumber(name, number);
                break;
            case float or double:
                var real = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsFinite(real))
                {
                    json.WriteNumber(name, real);
                }
                else
                {
                    json.WriteString(name, real.ToString(CultureInfo.InvariantCulture));
                }

                break;
            default:
                json.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static string LevelName(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => "None",
        };
    }
}

internal sealed class ErpFileLogger(string categoryName, ErpFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return provider.ScopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // The formatter is intentionally not called: the rendered message repeats template values unredacted.
        provider.Write(categoryName, logLevel, eventId, state, exception);
    }
}
