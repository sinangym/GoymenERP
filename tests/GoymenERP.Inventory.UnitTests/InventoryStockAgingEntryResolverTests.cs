using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>SENTETIK ids. WP14: a stock label's entry date and its inheritance through splits and merges.</summary>
public sealed class InventoryStockAgingEntryResolverTests
{
    private static readonly Guid Root = Guid.Parse("1a000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherRoot = Guid.Parse("1a000000-0000-4000-8000-000000000002");
    private static readonly Guid Child = Guid.Parse("1a000000-0000-4000-8000-000000000003");
    private static readonly Guid GrandChild = Guid.Parse("1a000000-0000-4000-8000-000000000004");
    private static readonly Guid Merged = Guid.Parse("1a000000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset RootEntry = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OtherEntry = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_split_descendant_inherits_the_root_entry_and_a_merge_takes_the_oldest_root()
    {
        var parents = new Dictionary<Guid, List<Guid>> { [Child] = [Root], [GrandChild] = [Child], [Merged] = [OtherRoot, Child] };
        var entries = InventoryStockAgingEntryResolver.Resolve([Root, GrandChild, Merged], parents,
            new Dictionary<Guid, DateTimeOffset> { [Root] = RootEntry, [OtherRoot] = OtherEntry }, new HashSet<Guid>());
        Assert.Equal(((DateTimeOffset?)RootEntry, false), entries[Root]);
        Assert.Equal(((DateTimeOffset?)RootEntry, true), entries[GrandChild]);
        Assert.Equal(((DateTimeOffset?)RootEntry, true), entries[Merged]);
    }

    [Fact]
    public void An_unknown_or_truncated_ancestor_or_a_cycle_leaves_the_entry_unknown()
    {
        var parents = new Dictionary<Guid, List<Guid>> { [Child] = [Root], [Merged] = [OtherRoot, Child] };
        // The other root has no incoming movement: the merge cannot take an oldest date from a partial set.
        var partial = InventoryStockAgingEntryResolver.Resolve([Merged, Child], parents,
            new Dictionary<Guid, DateTimeOffset> { [Root] = RootEntry }, new HashSet<Guid>());
        Assert.Null(partial[Merged].Entry);
        Assert.Equal((DateTimeOffset?)RootEntry, partial[Child].Entry);

        var truncated = InventoryStockAgingEntryResolver.Resolve([Child], parents,
            new Dictionary<Guid, DateTimeOffset> { [Root] = RootEntry }, new HashSet<Guid> { Root });
        Assert.Null(truncated[Child].Entry);

        var cycle = InventoryStockAgingEntryResolver.Resolve([Child], new Dictionary<Guid, List<Guid>> { [Child] = [Root], [Root] = [Child] },
            new Dictionary<Guid, DateTimeOffset> { [Root] = RootEntry }, new HashSet<Guid>());
        Assert.Null(cycle[Child].Entry);
    }
}
