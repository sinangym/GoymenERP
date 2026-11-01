using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// SAT-D158 (Sinan, 2026-11-01, <c>docs/decisions/SINAN_KARARLARI_2026-11-01_SATIS_EK21.md</c>): the reviewed version-0 buyer
/// situation list — the five situations of SAT-D140 with their tax effect as data — the SAME list in each of GDC, GSP and GIN
/// (each company its own list, SAT-D133), in the shape of <see cref="ApprovedTaxItemTypeDefaultCatalog"/>: versioned source data
/// reviewed like code, the BASE of each company's list timeline. A list version published in Settings replaces it from its own
/// date (Müdür/Admin may add situations, SAT-D158), so nothing is hard-coded in the calculator.
/// <list type="table">
/// <item><term>Normal</term><description>Normal alıcı: the item-type default as it is.</description></item>
/// <item><term>NotSubjectToWithholding / FinalConsumer</term><description>Tevkifata tabi olmayan alıcı / nihai tüketici: no
/// withholding, VAT unchanged (SAT-D74).</description></item>
/// <item><term>ExportRegistered / ExemptionCertificate</term><description>İhraç kayıtlı / istisna belgeli: KDV %0 with an
/// exemption code chosen from the list, no withholding, the buyer's document (no, date) required on the order
/// (SAT-D147).</description></item>
/// </list>
/// The catalog's effective date is the tax master catalog's (<see cref="ApprovedTaxItemTypeDefaultCatalog.EffectiveFrom"/>): the
/// situation list itself is SAT-D140 (EK19); it is the base of the timeline and no order or invoice predates it.
/// </summary>
public static class ApprovedTaxBuyerSituationCatalog
{
    /// <summary>The catalog's list version: below every Settings version (those start at 1), so a same-day version wins.</summary>
    public const long CatalogListVersion = 0;

    public const string Source =
        "SAT-D158 (SINAN_KARARLARI_2026-11-01_SATIS_EK21.md); the SAT-D140 situations with the SAT-D74/D93/D147 effects";

    public static readonly DateOnly EffectiveFrom = ApprovedTaxItemTypeDefaultCatalog.EffectiveFrom;

    /// <summary>The five reviewed situations (ordinal by code).</summary>
    public static IReadOnlyList<TaxBuyerSituationEntryViewV1> Entries { get; } =
    [
        new(TaxBuyerSituationsV1.ExemptionCertificate, "İstisna belgeli alıcı", WithholdingApplies: false,
            TaxBuyerSituationVatModesV1.ZeroWithExemptionCode, DocumentRequired: true),
        new(TaxBuyerSituationsV1.ExportRegistered, "İhraç kayıtlı satış", WithholdingApplies: false,
            TaxBuyerSituationVatModesV1.ZeroWithExemptionCode, DocumentRequired: true),
        new(TaxBuyerSituationsV1.FinalConsumer, "Nihai tüketici", WithholdingApplies: false,
            TaxBuyerSituationVatModesV1.Normal, DocumentRequired: false),
        new(TaxBuyerSituationsV1.Normal, "Normal alıcı", WithholdingApplies: true,
            TaxBuyerSituationVatModesV1.Normal, DocumentRequired: false),
        new(TaxBuyerSituationsV1.NotSubjectToWithholding, "Tevkifata tabi olmayan alıcı", WithholdingApplies: false,
            TaxBuyerSituationVatModesV1.Normal, DocumentRequired: false),
    ];

    private static readonly IReadOnlySet<Guid> Companies = new HashSet<Guid>
    {
        ApprovedUnitProfileCatalog.GdcCompanyId,
        ApprovedUnitProfileCatalog.GspCompanyId,
        ApprovedUnitProfileCatalog.GinCompanyId,
    };

    /// <summary>The catalog version of <paramref name="companyId"/> (none for any other company: "kaynak yok").</summary>
    public static IReadOnlyList<TaxBuyerSituationListVersionDataV1> For(Guid companyId) =>
        Companies.Contains(companyId)
            ? [new TaxBuyerSituationListVersionDataV1(CatalogListVersion, EffectiveFrom, TaxDefaultSourcesV1.Catalog, Entries)]
            : [];
}
