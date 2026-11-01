using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// SAT-D139 (Sinan, 2026-10-31, <c>docs/decisions/SINAN_KARARLARI_2026-10-31_SATIS_EK19.md</c>): the reviewed default tax per
/// item type, the SAME values in each of GDC, GSP and GIN (each company its own catalog entries, SAT-D133), as versioned source
/// data in the shape of <see cref="ApprovedSalesPolicyCatalog"/> / the SAT-D120 code list catalog: a new reviewed value is a new
/// version reviewed like code. It is the BASE of each item type's timeline: a Settings record entered later replaces it from its
/// own date on (same withdraw/replace rules), so the default stays editable in Settings and nothing is hard-coded in a
/// calculator.
/// <list type="table">
/// <item><term>Steel (demir)</term><description>KDV %20, tevkifat 5/10 (withheld share 50 %); the UBL-TR withholding code is not
/// decided (mali müşavir) — "kaynak yok" until the withholding code list carries it and a record names it.</description></item>
/// <item><term>Scrap (hurda)</term><description>KDV yok, tevkifat yok; the UBL-TR exemption code and legal basis are not decided
/// (EK19 implementation note) — "kaynak yok", so a scrap line's invoice waits.</description></item>
/// <item><term>Service (hizmet)</term><description>KDV %20, tevkifat yok.</description></item>
/// </list>
/// Ids are UUIDv5 in namespace <c>06b79866-3841-5131-89a7-a3e14871417b</c> (= UUIDv5(URL,
/// "https://github.com/sinangym/GoymenERP/settings/tax-item-type-defaults")), names
/// <c>tax-item-type-default:&lt;company id&gt;:&lt;item type&gt;:v1</c>; assigned once, never reused.
/// </summary>
public static class ApprovedTaxItemTypeDefaultCatalog
{
    /// <summary>The catalog's record version: below every Settings record (those start at 1), so a same-day record wins.</summary>
    public const long CatalogRecordVersion = 0;

    public const string Source = "SAT-D139 (SINAN_KARARLARI_2026-10-31_SATIS_EK19.md); Sinan's explicit default per item type";

    public static readonly DateOnly EffectiveFrom = new(2026, 10, 31);

    public static IReadOnlyList<TaxItemTypeDefaultDataV1> Entries { get; } = Build();

    /// <summary>The catalog entries of <paramref name="companyId"/> (none for any other company: "kaynak yok").</summary>
    public static IReadOnlyList<TaxItemTypeDefaultDataV1> For(Guid companyId) =>
        Entries.Where(entry => entry.CompanyId == companyId).ToArray();

    private static TaxItemTypeDefaultDataV1[] Build()
    {
        (Guid Company, Guid Steel, Guid Scrap, Guid Service)[] companies =
        [
            (ApprovedUnitProfileCatalog.GdcCompanyId, Guid.Parse("74e7f161-192d-5d1e-9a8a-cb17e0fd1e2c"),
                Guid.Parse("a363c586-f45f-5b9e-98ba-80b22f08d3c1"), Guid.Parse("9545fe73-c37f-5050-aae7-eb02abd0eb0a")),
            (ApprovedUnitProfileCatalog.GspCompanyId, Guid.Parse("f2a1560f-65ab-521f-938a-5961b74ae2fe"),
                Guid.Parse("a9e5b003-9a37-5e33-9f56-ed0f3393344f"), Guid.Parse("d2be45e7-fbc0-5619-995e-ece453d3879b")),
            (ApprovedUnitProfileCatalog.GinCompanyId, Guid.Parse("facefafb-e9ee-53a6-9592-ca157aea73a0"),
                Guid.Parse("bd4884fa-93fe-51e6-925c-9118ae7c4c1c"), Guid.Parse("27ca9633-32e4-50ea-9a85-d3fe13432d8b")),
        ];
        return companies.SelectMany(company => new[]
        {
            Entry(company.Company, company.Steel, TaxItemTypesV1.Steel, vatPercent: 20m, withholdingPercent: 50m),
            Entry(company.Company, company.Scrap, TaxItemTypesV1.Scrap, vatPercent: null, withholdingPercent: null),
            Entry(company.Company, company.Service, TaxItemTypesV1.Service, vatPercent: 20m, withholdingPercent: null),
        }).ToArray();
    }

    private static TaxItemTypeDefaultDataV1 Entry(Guid companyId, Guid id, string itemType, decimal? vatPercent,
        decimal? withholdingPercent) =>
        new(id, companyId, itemType, TaxDefaultSourcesV1.Catalog, CatalogRecordVersion, EffectiveFrom,
            VatApplies: vatPercent is not null, VatPercent: vatPercent, ExemptionCode: null,
            WithholdingApplies: withholdingPercent is not null, WithholdingPercent: withholdingPercent, WithholdingCode: null);
}
