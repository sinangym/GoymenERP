using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// PRD-CARD-1: the screens are given the RELEASED definition's own fields, dictionaries and names, and nothing
/// else. Every family, template, field, dictionary and code here is SYNTHETIC test data with the shape of the
/// decided release (#66 5739827887), never its values, and never seeded.
/// </summary>
public sealed class ProductCardPresentationTests
{
    [Fact]
    public void Unit_names_follow_each_assigned_settings_identity_without_stock_fallback()
    {
        var kilogram = Guid.NewGuid();
        var piece = Guid.NewGuid();
        var labels = ProductCardPresentation.WithUnitLabels(new("Group", null, null, null),
            new(Stock: kilogram, Sales: piece, Purchase: Guid.NewGuid()),
            new Dictionary<Guid, string> { [kilogram] = "kg", [piece] = "piece" });
        Assert.Equal("kg", labels.PrimaryStockUnit);
        Assert.Equal(new Dictionary<string, string> { ["stock"] = "kg", ["sales"] = "piece" }, labels.Units);
        Assert.Null(ProductCardPresentation.WithUnitLabels(labels, new(), new Dictionary<Guid, string>()).Units);
    }

    private static readonly Guid HotCoil = Guid.Parse("7a2f1c40-0b51-4f8e-9c33-51d0a7e6b001");
    private static readonly Guid TemplateId = Guid.Parse("7a2f1c40-0b51-4f8e-9c33-51d0a7e6b011");
    private const string Thickness = "syn-thickness-mm";
    private const string Grade = "syn-grade";
    private const string Coating = "syn-coating";
    private const string Designation = "syn-designation";

    [Fact]
    public void A_template_carries_exactly_its_released_fields_in_order_and_nothing_else()
    {
        var template = Assert.Single(ProductCardPresentation.Templates(Definition()));

        Assert.Equal((TemplateId, "1", "Sentetik bant/rulo"), (template.Id, template.Version, template.DisplayName));
        Assert.Equal([Thickness, Grade, Coating, Designation], template.Fields!.Select(field => field.Key));
        var thickness = template.Fields![0];
        Assert.Equal(("measureMillimetre", "thickness", true, null, false), (thickness.Kind, thickness.Role,
            thickness.Required, thickness.Dictionary, thickness.AllowsCandidate));
        // A conditional field carries its condition; the form renders it only while the condition holds.
        var designation = template.Fields![3];
        Assert.Equal(("dictionaryReference", null, false, "syn-designations", Coating, "syn-zinc"),
            (designation.Kind, designation.Role, designation.Required, designation.Dictionary,
                designation.AppliesWhenField, designation.AppliesWhenCode));
    }

    [Fact]
    public void A_dictionary_carries_its_entries_with_status_explicit_none_and_parent()
    {
        var dictionaries = ProductCardPresentation.Dictionaries(Definition()).ToArray();

        Assert.Equal(["syn-coatings", "syn-designations", "syn-grades"], dictionaries.Select(dictionary => dictionary.Name));
        var grades = dictionaries.Single(dictionary => dictionary.Name == "syn-grades").Entries;
        Assert.Equal(["SYN-A", "SYN-C"], grades.Select(entry => entry.Code));
        // A candidate is shown as a candidate and carries no invented meaning; it never satisfies a required field.
        Assert.Equal(["approved", "candidate"], grades.Select(entry => entry.Status));
        var uncoated = dictionaries.Single(dictionary => dictionary.Name == "syn-coatings").Entries
            .Single(entry => entry.Code == "syn-none");
        Assert.True(uncoated.IsExplicitNone);
        Assert.Null(uncoated.MassGm2);
    }

    [Fact]
    public void A_family_carries_the_facts_it_fixes_and_the_template_serving_it()
    {
        var family = Assert.Single(ProductCardPresentation.FamilyFacts(Definition()));

        Assert.Equal((HotCoil, "SYN-HR-COIL", "Sentetik sıcak rulo"), (family.Id, family.Code, family.DisplayName));
        // Cins / Tür / Tip are the family's, never typed on a card.
        Assert.Equal(("Sentetik cins", "Sentetik tür", "Sentetik tip"), (family.Cins, family.Tur, family.Tip));
        Assert.Equal((TemplateId, "1"), (family.TemplateId, family.TemplateVersion));
    }

    [Fact]
    public void A_reopened_card_shows_names_for_its_family_template_and_controlled_values()
    {
        var labels = ProductCardPresentation.Labels("Sentetik grup", Definition(), HotCoil, References(Attributes()));

        Assert.Equal(("Sentetik grup", "Sentetik sıcak rulo", "Sentetik bant/rulo"), (labels.Group, labels.Family, labels.Template));
        Assert.Equal(new ProductFamilyFactsLabels("Sentetik cins", "Sentetik tür", "Sentetik tip"), labels.FamilyFacts);
        // The display name of each stored controlled value; a measure has none, and neither has an unknown code.
        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Grade] = "Sentetik SYN-A",
            [Coating] = "Sentetik syn-none",
        }, labels.Attributes);
    }

    [Fact]
    public void Without_a_release_or_a_released_family_there_are_no_options_and_no_names()
    {
        Assert.Empty(ProductCardPresentation.Templates(null));
        Assert.Empty(ProductCardPresentation.FamilyFacts(null));
        Assert.Empty(ProductCardPresentation.Dictionaries(null));

        foreach (var labels in new[]
                 {
                     ProductCardPresentation.Labels("Sentetik grup", null, HotCoil, References(Attributes())),
                     ProductCardPresentation.Labels("Sentetik grup", Definition(), Guid.NewGuid(), References(Attributes())),
                     ProductCardPresentation.Labels("Sentetik grup", Definition(), null, References(Attributes())),
                 })
        {
            Assert.Equal("Sentetik grup", labels.Group);
            Assert.Null(labels.Family);
            Assert.Null(labels.Template);
            Assert.Null(labels.FamilyFacts);
            Assert.Null(labels.Attributes);
        }
    }

    [Fact]
    public void An_unknown_or_uncontrolled_value_gets_no_name_rather_than_its_code()
    {
        var attributes = Attributes();
        attributes[Grade] = "SYN-NOT-IN-THE-RELEASE";

        var labels = ProductCardPresentation.Labels(null, Definition(), HotCoil, References(attributes));

        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { [Coating] = "Sentetik syn-none" }, labels.Attributes);
    }

    private static Dictionary<string, object?> Attributes() => new()
    {
        [Thickness] = 1.5m,
        [Grade] = "SYN-A",
        [Coating] = "syn-none",
    };

    private static ProductReferences References(Dictionary<string, object?> attributes) => new(TemplateId, "1",
        attributes.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal));

    private static ProductFamilyDefinitionV2 Definition() => new(
        [new(HotCoil, "SYN-HR-COIL", "Sentetik sıcak rulo", "Sentetik cins", "Sentetik tür", "Sentetik tip",
            ProductFamilyRuleSetsV2.GdcBandCoil, "SYNTHETIC")],
        [new(TemplateId, "1", "SYN-BAND-COIL", "Sentetik bant/rulo", new HashSet<Guid> { HotCoil },
        [
            new(Thickness, ProductTemplateFieldKindV2.MeasureMillimetre, ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
            new(Grade, ProductTemplateFieldKindV2.DictionaryReference, ProductTemplateFieldRolesV2.Quality, true, "syn-grades", false, null, null, null),
            new(Coating, ProductTemplateFieldKindV2.DictionaryReference, ProductTemplateFieldRolesV2.Coating, true, "syn-coatings", false, null, null, null),
            new(Designation, ProductTemplateFieldKindV2.DictionaryReference, null, false, "syn-designations", false, Coating, "syn-zinc", null),
        ], "SYNTHETIC")],
        [
            new("syn-grades", "SYN-A", "Sentetik SYN-A", ProductDefinitionEntryStatusV2.Approved, "SYNTHETIC", null, null, false),
            new("syn-grades", "SYN-C", "Sentetik aday SYN-C", ProductDefinitionEntryStatusV2.Candidate, "SYNTHETIC", null, null, false),
            new("syn-coatings", "syn-none", "Sentetik syn-none", ProductDefinitionEntryStatusV2.Approved, "SYNTHETIC", null, null, true),
            new("syn-coatings", "syn-zinc", "Sentetik syn-zinc", ProductDefinitionEntryStatusV2.Approved, "SYNTHETIC", null, null, false),
            new("syn-designations", "SYN-Z1", "Sentetik SYN-Z1", ProductDefinitionEntryStatusV2.Approved, "SYNTHETIC", null, 100m, false),
        ]);
}
