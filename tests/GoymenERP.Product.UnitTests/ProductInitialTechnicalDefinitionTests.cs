using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductInitialTechnicalDefinitionTests
{
    [Fact]
    public void Decision_4A_definition_is_exact_source_stamped_and_well_formed()
    {
        var definition = ProductInitialTechnicalDefinitionV1.Definition();

        ProductFamilyDefinitionRulesV2.RequireWellFormed(definition);
        Assert.Equal(4, definition.Families.Count);
        Assert.Equal(
            ["cr-coil", "cr-slit-coil", "hr-coil", "hr-slit-coil"],
            definition.Families.Select(family => family.Code).Order(StringComparer.Ordinal));
        Assert.All(definition.Families, family =>
        {
            Assert.Equal("Sac", family.Cins);
            Assert.Equal(ProductFamilyRuleSetsV2.GdcBandCoil, family.RuleSet);
            Assert.Equal(ProductInitialTechnicalDefinitionV1.SourceRef, family.SourceRef);
        });

        var template = Assert.Single(definition.Templates);
        Assert.Equal((ProductInitialTechnicalDefinitionV1.TemplateId, "1", "gdc-flat-coil-v1"),
            (template.Id, template.Version, template.Code));
        Assert.Equal(definition.Families.Select(family => family.Id).Order(), template.FamilyIds.Order());
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
    }
}
