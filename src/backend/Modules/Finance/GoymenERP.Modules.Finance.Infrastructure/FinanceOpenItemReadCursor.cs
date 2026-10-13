using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Opaque FI04 keyset cursor. It is integrity-by-binding rather than a security token: the server accepts only its
/// canonical encoding and binds it to the company, actor, fixed query and fixed ordering. It carries no permission;
/// every page revalidates both read authorities afresh.
/// </summary>
internal static class FinanceOpenItemReadCursor
{
    internal const string ListQuery = "open-items:v1";
    internal const string Ordering = "createdAtUtc desc, id desc";
    private const int MaximumLength = 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Encode(Guid companyId, string actorId, DateTimeOffset createdAtUtc, Guid id) =>
        Encode(companyId, actorId, createdAtUtc, id, ListQuery);

    internal static string Encode(
        Guid companyId,
        string actorId,
        DateTimeOffset createdAtUtc,
        Guid id,
        string query)
    {
        var payload = new Payload(
            companyId,
            actorId,
            query,
            Ordering,
            createdAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            id);
        return Convert.ToBase64String(Utf8.GetBytes(JsonSerializer.Serialize(payload, Json)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static (DateTimeOffset CreatedAtUtc, Guid Id)? Decode(
        string cursor,
        Guid companyId,
        string actorId)
    {
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaximumLength
            || cursor.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return null;
        }

        Payload? payload;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            payload = JsonSerializer.Deserialize<Payload>(Utf8.GetString(Convert.FromBase64String(padded)), Json);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }

        if (payload is null
            || payload.CompanyId != companyId
            || !string.Equals(payload.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(payload.Query, ListQuery, StringComparison.Ordinal)
            || !string.Equals(payload.Ordering, Ordering, StringComparison.Ordinal)
            || payload.Id == Guid.Empty
            || payload.CreatedAtUtc is null
            || !DateTimeOffset.TryParseExact(
                payload.CreatedAtUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var createdAtUtc)
            || createdAtUtc.Offset != TimeSpan.Zero
            || !string.Equals(
                cursor,
                Encode(payload.CompanyId, payload.ActorId ?? string.Empty, createdAtUtc, payload.Id,
                    payload.Query ?? string.Empty),
                StringComparison.Ordinal))
        {
            return null;
        }

        return (createdAtUtc, payload.Id);
    }

    private sealed record Payload(
        Guid CompanyId,
        string? ActorId,
        string? Query,
        string? Ordering,
        string? CreatedAtUtc,
        Guid Id);
}
