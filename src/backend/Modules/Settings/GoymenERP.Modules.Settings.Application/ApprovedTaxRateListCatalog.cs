using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// SAT-D181 (EK26): the reviewed source of the VAT rate and withholding ratio lists — deliberately EMPTY. SAT-D148: no rate enters
/// the system before Sinan or the müşavir approves it, and SAT-D146 routes every change through a proposal and a manager-class
/// approval; that maintenance (and its table) is a later slice. Until then every company's lists are "kaynak yok"
/// (<see cref="TaxRateListReasonsV1.RateListMissing"/>) and a Sales line cannot pick a listed rate. Nothing is hard-coded here.
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
}
