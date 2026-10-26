using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The first source-stamped Product technical definition released by decision 4A. This is registry
/// data only: it assigns no Product, code group, unit profile, producer grade, default, tolerance or
/// formula. A caller still has to bind a real Product card and Settings-owned unit profile explicitly.
/// </summary>
public static class ProductInitialTechnicalDefinitionV1
{
    public const string SourceRef = "issue66-5973142585-4A";
    public const string CutSheetPlateSourceRef = "issue66-6180299892-product-tech-family-02";

    public static readonly Guid HotRolledCoilFamilyId = Guid.Parse("b2b7079a-14ad-41ee-8d3a-35d55d1a21e9");
    public static readonly Guid ColdRolledCoilFamilyId = Guid.Parse("75e15f39-bfcb-4fea-9065-a602e2539439");
    public static readonly Guid HotRolledSlitCoilFamilyId = Guid.Parse("2ffdf417-c7b0-4cc5-8c58-40b5cd906c55");
    public static readonly Guid ColdRolledSlitCoilFamilyId = Guid.Parse("1917d6df-f2de-4717-80fc-56255161b0d0");
    public static readonly Guid TemplateId = Guid.Parse("ca5be23e-1f56-40c7-9e4a-6cdfc5e884b2");
    public static readonly Guid HotRolledCutSheetPlateFamilyId = Guid.Parse("075d01d5-c57c-44b9-903a-89c00e4db029");
    public static readonly Guid HotRolledCutSheetPlateTemplateId = Guid.Parse("57937348-393e-4982-a5a4-63eff8be40c9");

    // A fresh graph prevents consumers from mutating the definition used by another call.
    public static ProductFamilyDefinitionV2 Definition()
    {
        var families = new ProductFamilyV2[]
        {
            new(HotRolledCoilFamilyId, "hr-coil", "Sıcak Haddelenmiş Rulo Sac", "Sac",
                "Sıcak Haddelenmiş", "Rulo", ProductFamilyRuleSetsV2.GdcBandCoil, SourceRef),
            new(ColdRolledCoilFamilyId, "cr-coil", "Soğuk Haddelenmiş Rulo Sac", "Sac",
                "Soğuk Haddelenmiş", "Rulo", ProductFamilyRuleSetsV2.GdcBandCoil, SourceRef),
            new(HotRolledSlitCoilFamilyId, "hr-slit-coil", "Sıcak Haddelenmiş Dilinmiş Bant", "Sac",
                "Sıcak Haddelenmiş", "Dilinmiş Bant", ProductFamilyRuleSetsV2.GdcBandCoil, SourceRef),
            new(ColdRolledSlitCoilFamilyId, "cr-slit-coil", "Soğuk Haddelenmiş Dilinmiş Bant", "Sac",
                "Soğuk Haddelenmiş", "Dilinmiş Bant", ProductFamilyRuleSetsV2.GdcBandCoil, SourceRef),
            new(HotRolledCutSheetPlateFamilyId, "hr-cut-sheet-plate", "Sıcak Haddelenmiş Kesilmiş Sac/Levha", "Sac",
                "Sıcak Haddelenmiş", "Kesilmiş Sac/Levha", ProductFamilyRuleSetsV2.GdcCutSheetPlate,
                CutSheetPlateSourceRef),
        };

        var template = new ProductTemplateV2(
            TemplateId,
            "1",
            "gdc-flat-coil-v1",
            "GDC Rulo ve Dilinmiş Bant Teknik Kartı",
            new HashSet<Guid>
            {
                HotRolledCoilFamilyId,
                ColdRolledCoilFamilyId,
                HotRolledSlitCoilFamilyId,
                ColdRolledSlitCoilFamilyId,
            },
            [
                new("thickness-mm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
                new("width-mm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Width, true, null, false, null, null, null),
                new("steel-grade", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Quality, true, "gdc-steel-grades", false, null, null, null),
                new("coating", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Coating, true, "gdc-coatings", false, null, null, null),
                new("producer-grade", ProductTemplateFieldKindV2.DictionaryReference,
                    null, false, "gdc-producer-grades", true, null, null, "steel-grade"),
            ],
            SourceRef);

        var cutSheetPlateTemplate = new ProductTemplateV2(
            HotRolledCutSheetPlateTemplateId,
            "1",
            "gdc-hot-rolled-cut-sheet-plate-v1",
            "GDC Sıcak Haddelenmiş Kesilmiş Sac/Levha Teknik Kartı",
            new HashSet<Guid> { HotRolledCutSheetPlateFamilyId },
            [
                new("thickness-mm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
                new("width-mm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Width, true, null, false, null, null, null),
                new("length-mm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Length, true, null, false, null, null, null),
                new("steel-grade", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Quality, true, "gdc-steel-grades", false, null, null, null),
                new("coating", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Coating, true, "gdc-coatings", false, null, null, null),
                new("producer-grade", ProductTemplateFieldKindV2.DictionaryReference,
                    null, false, "gdc-producer-grades", true, null, null, "steel-grade"),
            ],
            CutSheetPlateSourceRef);

        var entries = new ProductDictionaryEntryV2[]
        {
            new("gdc-steel-grades", "S235JR", "S235JR", ProductDefinitionEntryStatusV2.Approved,
                "product-source-40eb0b82", null, null, false),
            new("gdc-producer-grades", "erdemir:3237", "Erdemir 3237 — S235JR+AR",
                ProductDefinitionEntryStatusV2.Approved, "erdemir-flat-2021-p16", "S235JR", null, false),
            new("gdc-producer-grades", "erdemir:4237", "Erdemir 4237 — S235JR+AR CTA",
                ProductDefinitionEntryStatusV2.Approved, "erdemir-flat-2021-p16", "S235JR", null, false),
            new("gdc-producer-grades", "tosyali:TOS3780", "Tosyalı TOS3780 — S235JR Düşük Si",
                ProductDefinitionEntryStatusV2.Approved, "tosyali-flat-undated-p14", "S235JR", null, false),
            new("gdc-coatings", "uncoated", "Kaplamasız", ProductDefinitionEntryStatusV2.Approved,
                "product-source-40eb0b82", null, null, true),
        };

        var definition = new ProductFamilyDefinitionV2(families, [template, cutSheetPlateTemplate], entries);
        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        return definition;
    }
}
