using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryExactCostPoolModelTests
{
    [Fact]
    public void Forward_fix_preserves_exact_pool_and_recognition_mass_before_every_down_change()
    {
        var migration = new AddInventoryExactCostPools();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51968", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ExactCostPools]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[RemainingQuantityRatioJson]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ConsumedQuantityKg] IS NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.IsType<DropTableOperation>(migration.DownOperations[1]);
    }

    [Fact]
    public void Private_exact_pool_is_company_location_product_currency_unique_and_version_fenced()
    {
        using var context = Context();
        var row = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ExactCostPoolRow))!;
        Assert.Equal(["CompanyId", "LocationId", "ProductId", "CurrencyId"], row.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("nvarchar(max)", row.FindProperty(nameof(ExactCostPoolRow.QuantityRatioJson))!.GetColumnType());
        Assert.True(row.FindProperty(nameof(ExactCostPoolRow.RowVersion))!.IsConcurrencyToken);
        Assert.Equal((18, 4), (row.FindProperty(nameof(ExactCostPoolRow.AverageCostPerKg))!.GetPrecision(),
            row.FindProperty(nameof(ExactCostPoolRow.AverageCostPerKg))!.GetScale()));
        Assert.Single(row.GetDeclaredQueryFilters());
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OrganizationLocationReference)
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ExactCostPools_State" && check.Sql.Contains("ISJSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Recognition_preserves_an_exact_mass_ratio_when_no_three_decimal_scalar_can_represent_it()
    {
        using var context = Context();
        var row = context.Model.FindEntityType(typeof(MovementCostRecognitionRow))!;
        Assert.True(row.FindProperty(nameof(MovementCostRecognitionRow.RemainingQuantityKg))!.IsNullable);
        Assert.True(row.FindProperty(nameof(MovementCostRecognitionRow.ConsumedQuantityKg))!.IsNullable);
        Assert.Equal("nvarchar(max)", row.FindProperty(nameof(MovementCostRecognitionRow.RemainingQuantityRatioJson))!.GetColumnType());
        Assert.Equal("nvarchar(max)", row.FindProperty(nameof(MovementCostRecognitionRow.ConsumedQuantityRatioJson))!.GetColumnType());
        Assert.Single(row.GetDeclaredQueryFilters());
    }

    [Fact]
    public void A_partial_or_contradictory_private_pool_is_never_published_as_a_whole_position_average()
    {
        var legacy = new AverageCostLedgerRow { Version = 2, QuantityKg = 2m, AverageCostPerKg = 10m, Status = 1 };
        var exact = new ExactCostPoolRow { Version = 2, Status = 1, AverageCostPerKg = 10m,
            QuantityRatioJson = "{\"numerator\":\"4\",\"denominator\":\"3\"}" };
        Assert.False(InventoryAverageCostReadPort.ExactMatches(exact, legacy, 2m));
        exact.QuantityRatioJson = "{\"numerator\":\"2\",\"denominator\":\"1\"}";
        Assert.True(InventoryAverageCostReadPort.ExactMatches(exact, legacy, 2m));
        exact.Version = 3;
        Assert.False(InventoryAverageCostReadPort.ExactMatches(exact, legacy, 2m));
        exact.Version = 2; exact.QuantityRatioJson = "{\"numerator\":\"4\",\"denominator\":\"2\"}";
        Assert.False(InventoryAverageCostReadPort.ExactMatches(exact, legacy, 2m));
    }

    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer("Server=unused;Database=ModelOnly;Integrated Security=True;TrustServerCertificate=True").Options, company);
    }
}
