using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryMovementCostModelTests
{
    [Fact]
    public void Cost_stamps_are_append_only_same_company_movement_lines_with_independent_currency_keys()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var stamp = model.FindEntityType(typeof(MovementCostStampRow))!;
        Assert.Equal(["CompanyId", "MovementId", "LineNumber", "CurrencyId"], stamp.FindPrimaryKey()!.Properties.Select(p => p.Name));
        var line = Assert.Single(stamp.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(MovementLineRow));
        Assert.Equal(["CompanyId", "MovementId", "LineNumber"], line.Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Restrict, line.DeleteBehavior);
        Assert.Equal((38, 18), (stamp.FindProperty(nameof(MovementCostStampRow.Amount))!.GetPrecision(), stamp.FindProperty(nameof(MovementCostStampRow.Amount))!.GetScale()));
        Assert.Contains(stamp.GetCheckConstraints(), c => c.Name == "CK_MovementCostStamps_Currency" && c.Sql.Contains("[EntryExchangeRate] > 0", StringComparison.Ordinal));
        foreach (var type in new[] { typeof(MovementCostStampRow), typeof(CurrencyAverageCostLedgerRow), typeof(PendingMovementCostRow),
                     typeof(MovementCostRecognitionRow), typeof(ManufacturingCostGenerationRow), typeof(ManufacturingCostGenerationHistoryRow) })
            Assert.Single(context.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task Editing_an_original_stamp_or_recognition_is_rejected_before_sql()
    {
        await using var context = Context();
        var row = new MovementCostStampRow { CompanyId = Company, MovementId = Guid.NewGuid(), LineNumber = 1,
            CurrencyId = Guid.NewGuid(), StampVersion = 1, Status = 2, Reason = "source_missing" };
        context.Attach(row); row.Amount = 12m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void Return_source_is_absent_from_legacy_json_and_explicit_source_is_immutable_payload()
    {
        var request = new InventoryManufacturingInputReturnRequestV1(Company, Guid.NewGuid(), Guid.NewGuid(), "2",
            Guid.NewGuid(), "2", Guid.NewGuid(), "1", DateTimeOffset.UtcNow);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("sourceIssueMovementId", JsonSerializer.Serialize(request, json), StringComparison.Ordinal);
        var explicitSource = request with { SourceIssueMovementId = Guid.NewGuid() };
        Assert.Contains(explicitSource.SourceIssueMovementId!.Value.ToString("D"), JsonSerializer.Serialize(explicitSource, json), StringComparison.Ordinal);
    }

    [Fact]
    public void Recorded_label_amount_is_independent_of_the_rounded_unit_in_generation_contract()
    {
        var output = new InventoryManufacturingCostGenerationOutputV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3m,
            0.3367m, null, 1.01m);
        Assert.NotEqual(output.TlAmount, output.TlUnitCostPerKg * output.NetQuantityKg);
        Assert.Equal(1.01m, JsonSerializer.Deserialize<InventoryManufacturingCostGenerationOutputV1>(JsonSerializer.Serialize(output))!.TlAmount);
    }

    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static InventoryDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;").Options, company);
    }
}
