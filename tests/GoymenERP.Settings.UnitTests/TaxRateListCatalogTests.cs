using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>SAT-D181 / SAT-D148: the VAT rate and withholding ratio lists start empty (nothing hard-coded); a version's selection. SENTETIK.</summary>
public sealed class TaxRateListCatalogTests
{
    [Fact]
    public void Every_company_starts_with_no_rate_list_so_nothing_can_be_picked()
    {
        Assert.Empty(ApprovedTaxRateListCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId));
        Assert.Null(ApprovedTaxRateListCatalog.InForce(ApprovedTaxRateListCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId),
            TaxRateListKindsV1.VatRate, new DateOnly(2026, 11, 2)));
        Assert.Equal([TaxRateListKindsV1.VatRate, TaxRateListKindsV1.WithholdingRatio], TaxRateListKindsV1.All);
        Assert.False(TaxRateListKindsV1.IsKnown("Other"));
    }

    [Fact]
    public void The_version_in_force_is_the_latest_effective_highest_version_of_its_kind_and_a_withdrawn_one_never_counts()
    {
        var day = new DateOnly(2026, 11, 2);
        IReadOnlyList<TaxRateListVersionDataV1> versions =
        [
            new(TaxRateListKindsV1.VatRate, 1, day.AddDays(-10), [new("20", "SENTETIK", 20m)]),
            new(TaxRateListKindsV1.VatRate, 2, day.AddDays(-1), [new("10", "SENTETIK", 10m)]),
            new(TaxRateListKindsV1.VatRate, 3, day.AddDays(-1), [new("1", "SENTETIK", 1m)], IsWithdrawn: true),
            new(TaxRateListKindsV1.VatRate, 4, day.AddDays(1), [new("18", "SENTETIK", 18m)]),
            new(TaxRateListKindsV1.WithholdingRatio, 5, day, [new("5/10", "SENTETIK", 50m)]),
        ];

        Assert.Equal(2, ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.VatRate, day)!.ListVersion);
        Assert.Equal(5, ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.WithholdingRatio, day)!.ListVersion);
        Assert.Null(ApprovedTaxRateListCatalog.InForce(versions, TaxRateListKindsV1.WithholdingRatio, day.AddDays(-1)));
    }
}
