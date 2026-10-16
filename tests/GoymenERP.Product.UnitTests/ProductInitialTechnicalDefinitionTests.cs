using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductInitialTechnicalDefinitionTests
{
    [Fact]
    public void Released_definition_is_exact_source_stamped_and_well_formed()
    {
        var definition = ProductInitialTechnicalDefinitionV1.Definition();

        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        Assert.Equal(8, definition.Families.Count);
        Assert.Equal(
            ["cr-coil", "cr-slit-coil", "hr-coil", "hr-cut-sheet-plate", "hr-slit-coil",
             "welded-rectangle-profile", "welded-round-pipe", "welded-square-profile"],
            definition.Families.Select(family => family.Code).Order(StringComparer.Ordinal));
        Assert.All(definition.Families.Where(family => family.RuleSet == ProductFamilyRuleSetsV2.GdcBandCoil), family =>
        {
            Assert.Equal("Sac", family.Cins);
            Assert.Equal(ProductFamilyRuleSetsV2.GdcBandCoil, family.RuleSet);
            Assert.Equal(ProductInitialTechnicalDefinitionV1.SourceRef, family.SourceRef);
        });

        var cutSheet = definition.Families.Single(family => family.Code == "hr-cut-sheet-plate");
        Assert.Equal(("Sac", "Sıcak Haddelenmiş", "Kesilmiş Sac/Levha", ProductFamilyRuleSetsV2.GdcCutSheetPlate,
                ProductInitialTechnicalDefinitionV1.CutSheetPlateSourceRef),
            (cutSheet.Cins, cutSheet.Tur, cutSheet.Tip, cutSheet.RuleSet, cutSheet.SourceRef));

        Assert.Equal(
            [
                ("welded-rectangle-profile", "Profil", "Kaynaklı", "Dikdörtgen",
                    ProductFamilyRuleSetsV2.GdcWeldedRectangleProfile),
                ("welded-round-pipe", "Boru", "Kaynaklı", "Yuvarlak",
                    ProductFamilyRuleSetsV2.GdcWeldedRoundPipe),
                ("welded-square-profile", "Profil", "Kaynaklı", "Kare",
                    ProductFamilyRuleSetsV2.GdcWeldedSquareProfile),
            ],
            definition.Families.Where(family => family.SourceRef == ProductInitialTechnicalDefinitionV1.WeldedTubeProfileSourceRef)
                .OrderBy(family => family.Code)
                .Select(family => (family.Code, family.Cins, family.Tur, family.Tip, family.RuleSet)));

        Assert.Equal(5, definition.Templates.Count);
        var template = definition.Templates.Single(candidate => candidate.Id == ProductInitialTechnicalDefinitionV1.TemplateId);
        Assert.Equal((ProductInitialTechnicalDefinitionV1.TemplateId, "1", "gdc-flat-coil-v1"),
            (template.Id, template.Version, template.Code));
        Assert.Equal(definition.Families.Where(family => family.RuleSet == ProductFamilyRuleSetsV2.GdcBandCoil)
            .Select(family => family.Id).Order(), template.FamilyIds.Order());
        Assert.Equal(
            ["thickness-mm", "width-mm", "steel-grade", "coating", "producer-grade"],
            template.Fields.Select(field => field.Key));
        Assert.Equal(
            [ProductTemplateFieldRolesV2.Thickness, ProductTemplateFieldRolesV2.Width,
             ProductTemplateFieldRolesV2.Quality, ProductTemplateFieldRolesV2.Coating],
            template.Fields.Where(field => field.Role is not null).Select(field => field.Role));
        var producer = template.Fields.Single(field => field.Key == "producer-grade");
        Assert.False(producer.Required);
        Assert.True(producer.AllowsCandidate);
        Assert.Equal("steel-grade", producer.ParentField);

        var cutSheetTemplate = definition.Templates.Single(candidate =>
            candidate.Id == ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId);
        Assert.Equal(("1", "gdc-hot-rolled-cut-sheet-plate-v1", ProductInitialTechnicalDefinitionV1.CutSheetPlateSourceRef),
            (cutSheetTemplate.Version, cutSheetTemplate.Code, cutSheetTemplate.SourceRef));
        Assert.Equal([ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId], cutSheetTemplate.FamilyIds);
        Assert.Equal(
            ["thickness-mm", "width-mm", "length-mm", "steel-grade", "coating", "producer-grade"],
            cutSheetTemplate.Fields.Select(field => field.Key));
        Assert.Equal(
            [ProductTemplateFieldRolesV2.Thickness, ProductTemplateFieldRolesV2.Width,
             ProductTemplateFieldRolesV2.Length, ProductTemplateFieldRolesV2.Quality,
             ProductTemplateFieldRolesV2.Coating],
            cutSheetTemplate.Fields.Where(field => field.Role is not null).Select(field => field.Role));

        AssertTemplate(definition, ProductInitialTechnicalDefinitionV1.WeldedRoundPipeTemplateId,
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeFamilyId, "gdc-welded-round-pipe-v1",
            ["outside-diameter-mm", "wall-thickness-mm", "length-mm", "steel-grade", "coating", "producer-grade"],
            [ProductTemplateFieldRolesV2.OutsideDiameter, ProductTemplateFieldRolesV2.WallThickness,
             ProductTemplateFieldRolesV2.Length, ProductTemplateFieldRolesV2.Quality,
             ProductTemplateFieldRolesV2.Coating]);
        AssertTemplate(definition, ProductInitialTechnicalDefinitionV1.WeldedSquareProfileTemplateId,
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileFamilyId, "gdc-welded-square-profile-v1",
            ["section-width-mm", "section-height-mm", "wall-thickness-mm", "length-mm", "steel-grade", "coating",
             "producer-grade"],
            [ProductTemplateFieldRolesV2.SectionWidth, ProductTemplateFieldRolesV2.SectionHeight,
             ProductTemplateFieldRolesV2.WallThickness, ProductTemplateFieldRolesV2.Length,
             ProductTemplateFieldRolesV2.Quality, ProductTemplateFieldRolesV2.Coating]);
        AssertTemplate(definition, ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileTemplateId,
            ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileFamilyId, "gdc-welded-rectangle-profile-v1",
            ["section-width-mm", "section-height-mm", "wall-thickness-mm", "length-mm", "steel-grade", "coating",
             "producer-grade"],
            [ProductTemplateFieldRolesV2.SectionWidth, ProductTemplateFieldRolesV2.SectionHeight,
             ProductTemplateFieldRolesV2.WallThickness, ProductTemplateFieldRolesV2.Length,
             ProductTemplateFieldRolesV2.Quality, ProductTemplateFieldRolesV2.Coating]);

        Assert.Equal(5, definition.Entries.Count);
        Assert.All(definition.Entries, entry => Assert.Equal(ProductDefinitionEntryStatusV2.Approved, entry.Status));
        Assert.DoesNotContain(definition.Entries, entry => entry.Dictionary.Contains("unit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(definition.Entries, entry => entry.Code is "3237" or "4237" or "TOS3780");
        Assert.Equal("S235JR", definition.Entries.Single(entry => entry.Code == "erdemir:3237").ParentCode);
        Assert.Equal("S235JR", definition.Entries.Single(entry => entry.Code == "erdemir:4237").ParentCode);
        Assert.Equal("S235JR", definition.Entries.Single(entry => entry.Code == "tosyali:TOS3780").ParentCode);
        var uncoated = definition.Entries.Single(entry => entry.Code == "uncoated");
        Assert.True(uncoated.IsExplicitNone);
        Assert.Null(uncoated.MassGm2);
    }

    [Fact]
    public void Data_only_release_migration_is_discoverable_without_model_drift()
    {
        using var context = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "product"))
            .Options);

        Assert.Contains(ReleaseInitialProductTechnicalDefinition.MigrationId,
            context.Database.GetMigrations());
        Assert.Contains(ReleaseHotRolledCutSheetPlateTechnicalDefinition.MigrationId,
            context.Database.GetMigrations());
        Assert.Contains(ReleaseWeldedTubeProfileTechnicalDefinition.MigrationId,
            context.Database.GetMigrations());
    }

    private static void AssertTemplate(ProductFamilyDefinitionV2 definition, Guid templateId, Guid familyId,
        string code, string[] keys, string[] roles)
    {
        var template = definition.Templates.Single(candidate => candidate.Id == templateId);
        Assert.Equal(("1", code, ProductInitialTechnicalDefinitionV1.WeldedTubeProfileSourceRef),
            (template.Version, template.Code, template.SourceRef));
        Assert.Equal([familyId], template.FamilyIds);
        Assert.Equal(keys, template.Fields.Select(field => field.Key));
        Assert.Equal(roles, template.Fields.Where(field => field.Role is not null).Select(field => field.Role));
    }
}
