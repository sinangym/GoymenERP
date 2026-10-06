using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Modules.Product.Infrastructure.Fixtures;

/// <summary>
/// Keeps disposable candidate fixtures compatible with the one reviewed normal Product code-group
/// release. The release may coexist unchanged; no other pre-existing group is adopted or repaired.
/// </summary>
internal static class ProductCandidateFixtureCodeGroupState
{
    public static bool IsEmptyOrExactReleasedBaseline(IReadOnlyCollection<ProductCodeGroupRow> groups) =>
        groups.Count == 0 || groups.Count == 1 && ContainsExact(groups, ProductInitialCodeGroupV1.Sac());

    public static bool MatchesFixtureWithOptionalReleasedBaseline(
        IReadOnlyCollection<ProductCodeGroupRow> groups,
        ProductCodeGroupRow fixture)
    {
        if (!ContainsExact(groups, fixture)) return false;
        return groups.Count == 1
            || groups.Count == 2 && ContainsExact(groups, ProductInitialCodeGroupV1.Sac());
    }

    private static bool ContainsExact(IEnumerable<ProductCodeGroupRow> groups, ProductCodeGroupRow expected) =>
        groups.Any(group => group.ToContract() == expected.ToContract());
}
