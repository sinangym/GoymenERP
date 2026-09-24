using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// Projects canonical Product audit events into <see cref="ProductHistoryView"/>.
/// Fails closed: an event that does not belong to the Product, has an unknown operation,
/// an inconsistent version transition, out-of-order time or a snapshot that contradicts
/// its own envelope is an integrity fault, never a silently skipped or repaired row.
///
/// The records are also checked AGAINST EACH OTHER, because a per-record check cannot see
/// a missing event: the list must be one unbroken version chain, and an uncut list must
/// reach back to the creation event. Without those two rules a history with a hole, or one
/// that starts in the middle, is returned as if it were the whole story.
/// </summary>
public static class ProductHistoryProjector
{
    public static ProductHistoryView Project(Guid productId, IReadOnlyList<ProductAuditRecordV1> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        if (productId == Guid.Empty) throw new ArgumentException("A Product id is required.", nameof(productId));

        var truncated = newestFirst.Count > ProductHistoryApiV1.PageSize;
        var records = newestFirst.Take(ProductHistoryApiV1.PageSize).ToArray();
        var entries = new List<ProductHistoryEntryView>(records.Length);
        var seen = new HashSet<Guid>();
        DateTimeOffset? previous = null;
        foreach (var record in records)
        {
            if (record is null || record.EventId == Guid.Empty || !seen.Add(record.EventId))
                throw Invalid();
            if (record.OccurredAtUtc.Offset != TimeSpan.Zero || (previous is { } newer && record.OccurredAtUtc > newer))
                throw Invalid();
            previous = record.OccurredAtUtc;
            entries.Add(Entry(productId, record));
        }

        // ONE CHAIN, newest first: every record's predecessor is the next older record.
        // A gap (a missing 2 -> 3), a creation event that is not the oldest, and equal-time
        // records in the wrong order all break this equality; each of them would otherwise
        // read as a complete history. No per-record rule can see any of the three.
        for (var i = 0; i + 1 < records.Length; i++)
        {
            if (records[i].VersionBefore != records[i + 1].VersionAfter) throw Invalid();
        }

        // An UNCUT list reaches back to the creation event. Only the page cut may hide it,
        // and that case is reported separately as Truncated. A creation is version 1 by the
        // per-record rule above, so the operation is the whole test here.
        if (!truncated && records.Length > 0
            && records[^1].Operation != ProductHistoryApiV1.DraftCreate)
        {
            throw Invalid();
        }

        return new(productId, entries, truncated);
    }

    private static ProductHistoryEntryView Entry(Guid productId, ProductAuditRecordV1 record)
    {
        if (record.ProductId != productId || string.IsNullOrWhiteSpace(record.Reason) || record.VersionAfter < 1)
            throw Invalid();

        using var after = Parse(record.AfterJson);
        RequireSnapshot(after.RootElement, productId, record.VersionAfter);
        var afterVersion = record.VersionAfter.ToString(CultureInfo.InvariantCulture);

        switch (record.Operation)
        {
            case ProductHistoryApiV1.DraftCreate:
            {
                if (record.VersionBefore is not null || record.VersionAfter != 1) throw Invalid();
                using var before = Parse(record.BeforeJson);
                if (before.RootElement.ValueKind != JsonValueKind.Object || before.RootElement.EnumerateObject().Any())
                    throw Invalid();
                return new(record.EventId, record.Operation, record.OccurredAtUtc, null, afterVersion,
                    record.Reason, []);
            }
            case ProductHistoryApiV1.DraftBasicEdit:
            case ProductHistoryApiV1.Activate:
            {
                if (record.VersionBefore is not (>= 1 and < long.MaxValue) || record.VersionAfter != record.VersionBefore + 1)
                    throw Invalid();
                using var before = Parse(record.BeforeJson);
                RequireSnapshot(before.RootElement, productId, record.VersionBefore.Value);
                var changed = ProductHistoryFields.All
                    .Where(field => !JsonElement.DeepEquals(Read(before.RootElement, field), Read(after.RootElement, field)))
                    .ToArray();
                return new(record.EventId, record.Operation, record.OccurredAtUtc,
                    record.VersionBefore.Value.ToString(CultureInfo.InvariantCulture), afterVersion, record.Reason, changed);
            }
            default:
                throw Invalid();
        }
    }

    private static void RequireSnapshot(JsonElement card, Guid productId, long version)
    {
        if (card.ValueKind != JsonValueKind.Object
            || !card.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object
            || !product.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(id.GetString(), "D", out var parsed) || parsed != productId
            || !product.TryGetProperty("version", out var stored) || stored.ValueKind != JsonValueKind.String
            || stored.GetString() != version.ToString(CultureInfo.InvariantCulture)
            || !card.TryGetProperty("references", out var references) || references.ValueKind != JsonValueKind.Object)
            throw Invalid();
    }

    /// <summary>Reads a change key; a missing or null container reads as JSON null.</summary>
    private static JsonElement Read(JsonElement card, string field)
    {
        var path = field.Split('.');
        // RequireSnapshot has already proven both containers exist.
        if (!card.TryGetProperty(path[0] == "references" ? "references" : "product", out var current))
            throw Invalid();
        var segments = path[0] == "references" ? path.Skip(1) : path;
        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return Null;
            current = next;
        }

        return current;
    }

    private static readonly JsonElement Null = JsonDocument.Parse("null").RootElement.Clone();

    private static JsonDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw Invalid();
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() => new("A Product history event failed its integrity checks.");
}
