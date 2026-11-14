using System.Globalization;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032 / ERP-000L §6.3: the redaction rule of the structured file log. The log keeps the message template and
/// operational values only; it never writes the rendered message (it repeats the values) and replaces every value
/// whose name or type can carry tenant, actor, party, credential, contact, path or free text with a marker.
/// Unknown names are kept only for scalar values; any other object is written as a marker, not serialized.
/// </summary>
public static class ErpLogRedaction
{
    public const string RedactedMarker = "[redacted]";

    public const string ObjectMarker = "[object]";

    public const string UnstructuredTemplate = "[unstructured]";

    public const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>Longest scalar string kept in a log record; longer values are cut.</summary>
    public const int MaxStringValueLength = 256;

    private static readonly string[] AllowedIdentifierNames =
    [
        "CorrelationId",
        "CausationId",
        "TraceId",
        "SpanId",
        "EventId",
    ];

    private static readonly string[] SensitiveNameFragments =
    [
        "company",
        "tenant",
        "actor",
        "user",
        "party",
        "customer",
        "supplier",
        "employee",
        "token",
        "secret",
        "password",
        "credential",
        "key",
        "vkn",
        "tckn",
        "iban",
        "phone",
        "email",
        "mail",
        "address",
        "name",
        "body",
        "payload",
        "path",
        "query",
        "url",
        "uri",
        "sql",
        "header",
        "cookie",
        "authorization",
        "ip",
        "text",
        "note",
        "comment",
        "description",
        "prompt",
        "message",
        "reason",
        "file",
    ];

    /// <summary>Returns true when a structured property must never be written verbatim.</summary>
    public static bool IsSensitiveName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var allowed in AllowedIdentifierNames)
        {
            if (string.Equals(name, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (name.EndsWith("Id", StringComparison.Ordinal)
            || name.EndsWith("Ids", StringComparison.Ordinal)
            || name.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var fragment in SensitiveNameFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the value to write for a structured property, already redacted. Scalars are kept, strings are cut,
    /// identifiers and sensitive names become <see cref="RedactedMarker"/>, other objects <see cref="ObjectMarker"/>.
    /// </summary>
    public static object? RedactValue(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (value is null)
        {
            return null;
        }

        if (IsSensitiveName(name) || value is Guid)
        {
            return RedactedMarker;
        }

        return value switch
        {
            bool or byte or sbyte or short or ushort or int or uint or long or ulong => value,
            float or double or decimal => value,
            Enum enumValue => enumValue.ToString(),
            TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            string text => RedactString(text),
            _ => ObjectMarker,
        };
    }

    private static string RedactString(string value)
    {
        // An innocuous name can still carry an identifier or a contact value; those shapes are never kept.
        if (Guid.TryParse(value, out _) || value.Contains('@', StringComparison.Ordinal))
        {
            return RedactedMarker;
        }

        return Truncate(value);
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxStringValueLength ? value : value[..MaxStringValueLength];
    }
}
