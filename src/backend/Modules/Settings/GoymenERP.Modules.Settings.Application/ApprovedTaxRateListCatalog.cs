using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// SAT-D181 (EK26): the reviewed source of the VAT rate and withholding ratio lists — deliberately EMPTY. SAT-D148: no rate enters
/// the system before Sinan or the müşavir approves it, so nothing is shipped or seeded: the lists are Settings data, entered in
/// the company's rate list tables (SAT-D145 direct maintenance under the tax master authority; the SAT-D146 proposal flow is a
/// later slice). Until a version is entered a company's list is "kaynak yok" (<see cref="TaxRateListReasonsV1.RateListMissing"/>)
/// and a Sales line cannot pick a listed rate. Nothing is hard-coded here.
/// </summary>
public static class ApprovedTaxRateListCatalog
{
    /// <summary>The list versions of <paramref name="companyId"/> (none until entered).</summary>
    public static IReadOnlyList<TaxRateListVersionDataV1> For(Guid companyId) => [];

    /// <summary>The version of <paramref name="kind"/> in force on <paramref name="on"/>: the latest effective, highest version that day.</summary>
    public static TaxRateListVersionDataV1? InForce(IReadOnlyList<TaxRateListVersionDataV1> versions, string kind, DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions
            .Where(list => !list.IsWithdrawn && string.Equals(list.Kind, kind, StringComparison.Ordinal) && list.EffectiveFrom <= on)
            .OrderBy(list => list.EffectiveFrom)
            .ThenBy(list => list.ListVersion)
            .LastOrDefault();
    }

    /// <summary>
    /// The versions of <paramref name="kind"/> that take effect after <paramref name="after"/>, one per date (the highest version
    /// of that date), in date order.
    /// </summary>
    public static IReadOnlyList<TaxRateListVersionDataV1> Scheduled(IReadOnlyList<TaxRateListVersionDataV1> versions, string kind,
        DateOnly after)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions
            .Where(list => !list.IsWithdrawn && string.Equals(list.Kind, kind, StringComparison.Ordinal) && list.EffectiveFrom > after)
            .GroupBy(list => list.EffectiveFrom)
            .Select(sameDay => sameDay.MaxBy(list => list.ListVersion)!)
            .OrderBy(list => list.EffectiveFrom)
            .ToArray();
    }
}
