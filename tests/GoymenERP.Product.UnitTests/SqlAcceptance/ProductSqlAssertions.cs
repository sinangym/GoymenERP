using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests.SqlAcceptance;

internal static class ProductSqlAssertions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Used for rejected/stale/conflicting commands, rollback and replay. This does
    // not decide the business outcome of a submitted edit with identical values.
    public static void Unchanged(ProductSqlSnapshot before, ProductSqlSnapshot after)
    {
        Assert.Equal(before.Products.ToArray(), after.Products.ToArray());
        CatalogUnchanged(before, after);
        Assert.Equal(before.Counters.ToArray(), after.Counters.ToArray());
        Assert.Equal(before.Registrations.ToArray(), after.Registrations.ToArray());
    }

    public static void Created(ProductSqlSnapshot before, ProductSqlSnapshot after,
        ProductMutationResponse response, Guid codeGroupId) =>
        CreatedBatch(before, after, [response], [codeGroupId]);

    // Both absent-counter insertion races and existing-counter version races can
    // use this after the owner resolves/retries entire attempts. The caller must
    // separately prove actual interleaving, statuses and audit/terminal effects.
    public static void CreatedBatch(ProductSqlSnapshot before, ProductSqlSnapshot after,
        IReadOnlyList<ProductMutationResponse> responses, IReadOnlyList<Guid> codeGroupIds)
    {
        Assert.NotEmpty(responses);
        Assert.Equal(responses.Count, codeGroupIds.Count);
        Assert.Equal(responses.Count, responses.Select(item => item.Card.Product.Id).Distinct().Count());
        Assert.Equal(before.Products.Count + responses.Count, after.Products.Count);
        CatalogUnchanged(before, after);
        Assert.Equal(before.Products.ToArray(), after.Products.Where(row => before.Products.Any(old => old.Id == row.Id)).ToArray());
        Assert.Equal(before.Registrations.Count + responses.Count, after.Registrations.Count);
        Assert.Equal(before.Registrations.ToArray(), after.Registrations.Where(row => before.Registrations.Any(old => old.Code == row.Code)).ToArray());
        var sequences = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var index = 0; index < responses.Count; index++)
        {
            var response = responses[index];
            Assert.False(response.IsReplay);
            Assert.DoesNotContain(before.Products, row => row.Id == response.Card.Product.Id);
            var row = Assert.Single(after.Products, row => row.Id == response.Card.Product.Id);
            Assert.Equal(codeGroupIds[index], row.GroupId);
            Assert.Equal(1, row.Version);
            Assert.Equal((int)ProductLifecycle.Draft, row.Lifecycle);
            Assert.Equal(row.CreatedAtUtc, row.UpdatedAtUtc);
            CardMatches(after, row, response.Card);
            var (prefix, sequence) = SplitCode(row.Code);
            Assert.Equal(Assert.Single(after.Groups, group => group.Id == row.GroupId).Prefix, prefix);
            Assert.DoesNotContain(before.Registrations, alias => alias.Code == row.Code);
            Assert.True(sequence > (before.Counters.SingleOrDefault(counter => counter.Prefix == prefix)?.LastSequence ?? 0));
            sequences[prefix] = Math.Max(sequences.GetValueOrDefault(prefix), sequence);
        }
        Assert.Equal(before.Counters.Where(row => !sequences.ContainsKey(row.Prefix)).ToArray(),
            after.Counters.Where(row => !sequences.ContainsKey(row.Prefix)).ToArray());
        foreach (var (prefix, maximum) in sequences)
            Assert.Equal(maximum, Assert.Single(after.Counters, row => row.Prefix == prefix).LastSequence);
        UniqueCurrentCodes(after);
    }

    public static void ImportedBatch(ProductSqlSnapshot before, ProductSqlSnapshot after,
        IReadOnlyList<ProductImportCreatedV1> imported)
    {
        Assert.NotEmpty(imported);
        Assert.Equal(imported.Count, imported.Select(item => item.ProductId).Distinct().Count());
        Assert.Equal(imported.Count, imported.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(imported.Count, imported.Select(item => item.LegacyCode).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(before.Products.Count + imported.Count, after.Products.Count);
        CatalogUnchanged(before, after);
        Assert.Equal(before.Products.ToArray(),
            after.Products.Where(row => before.Products.Any(old => old.Id == row.Id)).ToArray());
        Assert.Equal(before.Registrations.Count + imported.Count * 2, after.Registrations.Count);
        Assert.Equal(before.Registrations.ToArray(),
            after.Registrations.Where(row => before.Registrations.Any(old => old.Code == row.Code)).ToArray());

        var sequences = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in imported)
        {
            var row = Assert.Single(after.Products, row => row.Id == item.ProductId);
            Assert.Equal(item.Code, row.Code);
            Assert.Equal(item.Name, row.Name);
            Assert.Equal(item.GroupId, row.GroupId);
            Assert.Equal(1, row.Version);
            Assert.Equal((int)ProductLifecycle.Draft, row.Lifecycle);
            Assert.Equal(row.CreatedAtUtc, row.UpdatedAtUtc);
            Assert.DoesNotContain(before.Products, old => old.Id == row.Id);
            Assert.DoesNotContain(before.Registrations, old => old.Code == item.LegacyCode);
            Assert.Equal(item.ProductId,
                Assert.Single(after.Registrations, registration => registration.Code == item.Code && registration.IsCurrent).ProductId);
            Assert.Equal(item.ProductId,
                Assert.Single(after.Registrations, registration => registration.Code == item.LegacyCode && !registration.IsCurrent).ProductId);
            var (prefix, sequence) = SplitCode(item.Code);
            Assert.Equal(Assert.Single(after.Groups, group => group.Id == item.GroupId).Prefix, prefix);
            sequences[prefix] = Math.Max(sequences.GetValueOrDefault(prefix), sequence);
        }

        Assert.Equal(before.Counters.Where(row => !sequences.ContainsKey(row.Prefix)).ToArray(),
            after.Counters.Where(row => !sequences.ContainsKey(row.Prefix)).ToArray());
        foreach (var (prefix, maximum) in sequences)
            Assert.Equal(maximum, Assert.Single(after.Counters, row => row.Prefix == prefix).LastSequence);
        UniqueCurrentCodes(after);
    }

    public static void Edited(ProductSqlSnapshot before, ProductSqlSnapshot after, ProductMutationResponse response)
    {
        Assert.False(response.IsReplay);
        CatalogUnchanged(before, after);
        Assert.Equal(before.Counters.ToArray(), after.Counters.ToArray());
        Assert.Equal(before.Registrations.ToArray(), after.Registrations.ToArray());
        Assert.Equal(before.Products.Count, after.Products.Count);
        var id = response.Card.Product.Id;
        var old = Assert.Single(before.Products, row => row.Id == id);
        var current = Assert.Single(after.Products, row => row.Id == id);
        Assert.Equal(before.Products.Where(row => row.Id != id).ToArray(), after.Products.Where(row => row.Id != id).ToArray());
        Assert.Equal(old.Code, current.Code);
        Assert.Equal(old.GroupId, current.GroupId);
        Assert.Equal(old.CreatedAtUtc, current.CreatedAtUtc);
        Assert.Equal((int)ProductLifecycle.Draft, old.Lifecycle);
        Assert.Equal(old.Lifecycle, current.Lifecycle);
        Assert.Equal(checked(old.Version + 1), current.Version);
        Assert.True(current.UpdatedAtUtc >= old.UpdatedAtUtc);
        CardMatches(after, current, response.Card);
        UniqueCurrentCodes(after);
    }

    public static void Replayed(ProductSqlSnapshot beforeRetry, ProductSqlSnapshot afterRetry,
        ProductMutationResponse original, ProductMutationResponse replay)
    {
        Unchanged(beforeRetry, afterRetry);
        Assert.True(replay.IsReplay);
        // In particular, replaying create after a later edit returns the ORIGINAL
        // card, not the current persisted version. HTTP correlation is owner evidence.
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(original.Card, Json),
            JsonSerializer.SerializeToNode(replay.Card, Json)), "Replay changed the original canonical Product card.");
    }

    private static void CatalogUnchanged(ProductSqlSnapshot before, ProductSqlSnapshot after) =>
        Assert.Equal(before.Groups.ToArray(), after.Groups.ToArray());

    private static void CardMatches(ProductSqlSnapshot snapshot, ProductStoredRecord row, ProductCardView actual)
    {
        var group = Assert.Single(snapshot.Groups, group => group.Id == row.GroupId);
        var expected = new ProductCardView(ProductDraftView.FromProduct(row.RestoreProduct(), new(group.Name, null, null, null)), row.RestoreReferences());
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected, Json),
            JsonSerializer.SerializeToNode(actual, Json)), "The response differs from the persisted Product identity, state, roles or references.");
    }

    private static void UniqueCurrentCodes(ProductSqlSnapshot snapshot)
    {
        Assert.Equal(snapshot.Products.Count, snapshot.Products.Select(row => row.Id).Distinct().Count());
        Assert.Equal(snapshot.Products.Count, snapshot.Products.Select(row => row.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(snapshot.Registrations.Count, snapshot.Registrations.Select(row => row.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(snapshot.Counters.Count, snapshot.Counters.Select(row => row.Prefix).Distinct(StringComparer.Ordinal).Count());
        foreach (var alias in snapshot.Registrations)
            Assert.Single(snapshot.Products, row => row.Id == alias.ProductId);
        foreach (var product in snapshot.Products)
        {
            var current = Assert.Single(snapshot.Registrations, row => row.ProductId == product.Id && row.IsCurrent);
            Assert.Equal(product.Code, current.Code);
            Assert.Single(snapshot.Groups, row => row.Id == product.GroupId);
        }
    }

    private static (string Prefix, long Sequence) SplitCode(string value)
    {
        var code = ProductCode.Parse(value).Value;
        var separator = code.LastIndexOf('-');
        return (code[..separator], long.Parse(code[(separator + 1)..], CultureInfo.InvariantCulture));
    }
}
