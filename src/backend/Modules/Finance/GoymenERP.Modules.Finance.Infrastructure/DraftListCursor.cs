using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The opaque draft-list cursor (FIN-T04b): the last row's ordering key, bound to the company that
/// read it and to the ordering. A malformed cursor, another company's or another ordering's decodes
/// to null and reads nothing.
/// </summary>
internal static class DraftListCursor
{
    private const string Ordering = "createdAtUtc desc, id desc";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>DEC-FIN-016: the customer instrument portfolio's own ordering, so neither list reads the other's cursor.</summary>
    public const string InstrumentOrdering = "receivedAtUtc desc, id desc";

    public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id) =>
        Encode(companyId, createdAtUtc, id, Ordering);

    public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id, string ordering) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new Payload(companyId, ordering, createdAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), id), Json)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (DateTimeOffset CreatedAtUtc, Guid Id)? Decode(string cursor, Guid companyId) =>
        Decode(cursor, companyId, Ordering);

    public static (DateTimeOffset CreatedAtUtc, Guid Id)? Decode(string cursor, Guid companyId, string ordering)
    {
        Payload? payload;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }

        if (payload is null || payload.Company != companyId || !string.Equals(payload.Ordering, ordering, StringComparison.Ordinal)
            || payload.Id == Guid.Empty || payload.CreatedAtUtc is null
            || !DateTimeOffset.TryParseExact(payload.CreatedAtUtc, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtUtc))
        {
            return null;
        }

        return (createdAtUtc, payload.Id);
    }

    private sealed record Payload(Guid Company, string? Ordering, string? CreatedAtUtc, Guid Id);
}
