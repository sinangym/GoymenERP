using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// The Product migrations are discoverable and the model snapshot is exactly the model, as the other
/// modules' persistence tests hold (PRD-FAM-1 added the family registry by hand). A difference is listed
/// operation by operation, so a drift names its table and column instead of a bare false.
/// </summary>
public sealed class ProductMigrationSnapshotTests
{
    [Fact]
    public void The_migrations_are_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.Contains(migrations, migration => migration.EndsWith("_InitialProductDrafts", StringComparison.Ordinal));
        Assert.Contains(migrations, migration => migration.EndsWith("_AddProductFamilyRegistry", StringComparison.Ordinal));
        Assert.Contains(migrations, migration => migration.EndsWith(
            "_ReleaseHotRolledCutSheetPlateTechnicalDefinition", StringComparison.Ordinal));
        Assert.Contains(migrations, migration => migration.EndsWith(
            "_ReleaseWeldedTubeProfileTechnicalDefinition", StringComparison.Ordinal));

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshot);
        if (snapshot is IMutableModel mutable)
        {
            snapshot = mutable.FinalizeModel();
        }

        snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshot.GetRelationalModel(), context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.True(differences.Count == 0, "Snapshot differs from the model: " + string.Join(" | ", differences.Select(Describe)));
    }

    [Fact]
    public void A_card_names_a_released_family_or_none_and_the_registry_is_keyed_as_released()
    {
        using var context = Context();
        var products = context.Model.FindEntityType(typeof(ProductRow))!;
        var family = Assert.Single(products.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(ProductFamilyRow));
        Assert.Equal(["FamilyId"], family.Properties.Select(property => property.Name));
        Assert.False(family.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, family.DeleteBehavior);

        Assert.Equal(["Dictionary", "Code"],
            context.Model.FindEntityType(typeof(ProductDictionaryEntryRow))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(["Id", "Version"],
            context.Model.FindEntityType(typeof(ProductTemplateRow))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    private static string Describe(MigrationOperation operation)
    {
        var type = operation.GetType();
        var table = type.GetProperty("Table")?.GetValue(operation) ?? type.GetProperty("Name")?.GetValue(operation);
        var name = type.GetProperty("Table") is null ? null : type.GetProperty("Name")?.GetValue(operation);
        return $"{type.Name} {table}{(name is null ? "" : "." + name)}";
    }

    private static ProductDbContext Context() => new(new DbContextOptionsBuilder<ProductDbContext>()
        .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;",
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "product"))
        .Options);
}
