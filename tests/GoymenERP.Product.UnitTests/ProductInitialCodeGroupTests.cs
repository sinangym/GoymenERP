using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductInitialCodeGroupTests
{
    [Fact]
    public void Approved_source_defines_the_exact_Sac_group()
    {
        var group = ProductInitialCodeGroupV1.Sac();

        Assert.Equal("issue66-5979389135", ProductInitialCodeGroupV1.SourceRef);
        Assert.Equal(Guid.Parse("4a97dc56-91c7-57f3-bf78-0652ab82b266"), ProductInitialCodeGroupV1.NamespaceId);
        Assert.Equal(Guid.Parse("829e576d-c635-502a-97b8-9d5074c9d6a0"), group.Id);
        Assert.Equal("Sac", group.Name);
        Assert.Equal("SAC", group.Prefix);
    }

    [Fact]
    public void Data_only_release_migration_is_discoverable_without_model_drift()
    {
        using var context = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "product"))
            .Options);

        Assert.Contains(ReleaseInitialProductCodeGroup.MigrationId, context.Database.GetMigrations());
    }
}
