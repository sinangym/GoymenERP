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
        // Validate the one extra row requested by ProductHistoryReader before discarding it.
        // Otherwise a missing event exactly across the 50/51 boundary would be hidden by
        // truncation even though the Audit owner supplied the sentinel that proves the gap.
        var verified = newestFirst.Take(ProductHistoryApiV1.PageSize + 1).ToArray();
        var publishedCount = Math.Min(verified.Length, ProductHistoryApiV1.PageSize);
        var entries = new List<ProductHistoryEntryView>(publishedCount);
        var seen = new HashSet<Guid>();
        DateTimeOffset? previous = null;
        for (var index = 0; index < verified.Length; index++)
        {
            var record = verified[index];
            if (record is null || record.EventId == Guid.Empty || !seen.Add(record.EventId))
                throw Invalid();
            if (record.OccurredAtUtc.Offset != TimeSpan.Zero || (previous is { } newer && record.OccurredAtUtc > newer))
                throw Invalid();
            previous = record.OccurredAtUtc;
            var entry = Entry(productId, record);
            if (index < publishedCount) entries.Add(entry);
        }

        // ONE CHAIN, newest first: every record's predecessor is the next older record.
        // A gap (a missing 2 -> 3), a creation event that is not the oldest, and equal-time
        // records in the wrong order all break this equality; each of them would otherwise
        // read as a complete history. No per-record rule can see any of the three.
        for (var i = 0; i + 1 < verified.Length; i++)
        {
            if (verified[i].VersionBefore != verified[i + 1].VersionAfter) throw Invalid();
        }

        // Classification changes form their own +1 chain while preserving the card's actual Product version.
        // Check both chains, including interleaved Product edits; a missing class revision must not disappear.
        var classes=verified.Where(x=>x.Operation==ProductHistoryApiV1.SetCogsClassification).ToArray();
        for(var i=0;i+1<classes.Length;i++)
        {
            using var newer=Parse(classes[i].BeforeJson);using var older=Parse(classes[i+1].AfterJson);
            var before=ClassificationSnapshot(newer.RootElement,productId,classes[i].VersionAfter);
            var after=ClassificationSnapshot(older.RootElement,productId,classes[i+1].VersionAfter);
            if(before.Version!=after.Version || before.Kind!=after.Kind || before.ChangedInCompanyId!=after.ChangedInCompanyId
                || before.UpdatedAtUtc!=after.UpdatedAtUtc)throw Invalid();
        }
        if(!truncated && classes.Length>0)
        {
            using var first=Parse(classes[^1].BeforeJson);
            if(ClassificationSnapshot(first.RootElement,productId,classes[^1].VersionAfter).Version!=0)throw Invalid();
        }

        // An UNCUT list reaches back to the creation event. Only the page cut may hide it,
        // and that case is reported separately as Truncated. A creation is version 1 by the
        // per-record rule above, so the operation is the whole test here.
        if (!truncated && verified.Length > 0
            && verified[^1].Operation != ProductHistoryApiV1.DraftCreate)
        {
            throw Invalid();
        }

        return new(productId, entries, truncated);
    }

    private static ProductHistoryEntryView Entry(Guid productId, ProductAuditRecordV1 record)
    {
        if (record.ProductId != productId || string.IsNullOrWhiteSpace(record.Reason) || record.VersionAfter < 1)
            throw Invalid();

        if (record.Operation == ProductHistoryApiV1.SetCogsClassification) return ClassificationEntry(productId,record);
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
            case ProductHistoryApiV1.Deactivate:
            case ProductHistoryApiV1.Reactivate:
            {
                if (record.VersionBefore is not (>= 1 and < long.MaxValue) || record.VersionAfter != record.VersionBefore + 1)
                    throw Invalid();
                using var before = Parse(record.BeforeJson);
                RequireSnapshot(before.RootElement, productId, record.VersionBefore.Value);
                // A deactivation or reactivation event whose own snapshots do not show that very move
                // contradicts its envelope: an integrity fault, never a quietly shown row.
                if (record.Operation is ProductHistoryApiV1.Deactivate or ProductHistoryApiV1.Reactivate)
                {
                    var (from, to) = record.Operation == ProductHistoryApiV1.Deactivate
                        ? ("Active", "Inactive") : ("Inactive", "Active");
                    if (LifecycleOf(before.RootElement) != from || LifecycleOf(after.RootElement) != to)
                        throw Invalid();
                }
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

    private static ProductHistoryEntryView ClassificationEntry(Guid productId,ProductAuditRecordV1 record)
    {
        if (record.VersionBefore is not >= 1 || record.VersionBefore != record.VersionAfter) throw Invalid();
        using var before=Parse(record.BeforeJson);using var after=Parse(record.AfterJson);
        var old=ClassificationSnapshot(before.RootElement,productId,record.VersionAfter);
        var current=ClassificationSnapshot(after.RootElement,productId,record.VersionAfter);
        if(old.Version==long.MaxValue || current.Version!=old.Version+1 || current.Kind is null || old.Kind==current.Kind
            || current.CompanyId!=old.CompanyId || current.ChangedInCompanyId!=current.CompanyId)throw Invalid();
        var version=record.VersionAfter.ToString(CultureInfo.InvariantCulture);
        return new(record.EventId,record.Operation,record.OccurredAtUtc,version,version,record.Reason,["cogsClassification"]);
    }
    private static (long Version,int? Kind,Guid CompanyId,Guid? ChangedInCompanyId,DateTimeOffset? UpdatedAtUtc) ClassificationSnapshot(JsonElement element,Guid productId,long productVersion)
    {
        if(element.ValueKind!=JsonValueKind.Object)throw Invalid();
        var expected=new HashSet<string>(["companyId","productId","productVersion","classificationVersion","classification",
            "changedInCompanyId","updatedAtUtc","sourceVersion"],StringComparer.Ordinal);
        var fields=element.EnumerateObject().ToArray();
        if(fields.Length!=expected.Count || fields.Any(x=>!expected.Remove(x.Name)) || expected.Count!=0)throw Invalid();
        if(!element.GetProperty("companyId").TryGetGuid(out var company)||company==Guid.Empty
            || !element.GetProperty("productId").TryGetGuid(out var product)||product!=productId
            || element.GetProperty("productVersion").ValueKind!=JsonValueKind.String
            || element.GetProperty("productVersion").GetString()!=productVersion.ToString(CultureInfo.InvariantCulture)
            || element.GetProperty("sourceVersion").GetString()!=ProductCogsClassificationContractsV1.SourceVersion)throw Invalid();
        var versionText=element.GetProperty("classificationVersion");
        if(versionText.ValueKind!=JsonValueKind.String || !long.TryParse(versionText.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out var version)
            || version<0 || version.ToString(CultureInfo.InvariantCulture)!=versionText.GetString())throw Invalid();
        var kind=element.GetProperty("classification");var origin=element.GetProperty("changedInCompanyId");var instant=element.GetProperty("updatedAtUtc");
        if(version==0)
        {
            if(kind.ValueKind!=JsonValueKind.Null || origin.ValueKind!=JsonValueKind.Null || instant.ValueKind!=JsonValueKind.Null)throw Invalid();
            return(0,null,company,null,null);
        }
        if(!kind.TryGetInt32(out var parsedKind)||parsedKind is not (1 or 2) || !origin.TryGetGuid(out var changed)||changed==Guid.Empty
            || !instant.TryGetDateTimeOffset(out var at)||at.Offset!=TimeSpan.Zero)throw Invalid();
        return(version,parsedKind,company,changed,at);
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

    private static string? LifecycleOf(JsonElement card) =>
        Read(card, "lifecycle") is { ValueKind: JsonValueKind.String } lifecycle ? lifecycle.GetString() : null;

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
