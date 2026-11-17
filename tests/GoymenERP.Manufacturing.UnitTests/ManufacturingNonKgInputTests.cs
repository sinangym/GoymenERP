using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingNonKgInputTests
{
    [Fact]
    public void The_non_kg_fact_migration_refuses_before_losing_any_actual_measurement()
    {
        var migration = new AddManufacturingNonKgInputFacts();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51963", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
        Assert.Contains("[manufacturing].[NonKgInputFacts]", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("0", true)] [InlineData("1", true)] [InlineData("0.0000000000000000000000000001", true)]
    [InlineData("1.000", false)] [InlineData("-1", false)] [InlineData("1e3", false)] [InlineData(" 1", false)]
    [InlineData("79228162514264337593543950336", false)]
    public void Actual_non_kg_quantities_are_exact_canonical_decimal_text_without_rounding(string text, bool valid) =>
        Assert.Equal(valid, ManufacturingNonKgInputConsumptionV1.CanonicalQuantity(text) is not null);

    [Fact]
    public void Corrections_are_immutable_replacements_and_do_not_change_the_previous_fact_or_accept_branches()
    {
        var first = Fact(); var correction = first with { FactId = Guid.NewGuid(), Revision = 2, Quantity = "0", ReplacesFactId = first.FactId };
        var history = new ManufacturingNonKgInputHistoryV1(Guid.NewGuid(), Guid.NewGuid(), 3, 2, [first, correction]);
        history.RequireValid(); Assert.Equal("2.25", first.Quantity);
        Assert.Throws<ArgumentException>(() => (history with { Revision = 3, Facts = [first, correction, correction with { FactId = Guid.NewGuid(), Revision = 3 }] }).RequireValid());
        Assert.Throws<ArgumentException>(() => (first with { Unit = ManufacturingNonKgInputUnitV1.Metre }).RequireValid());
        Assert.Throws<ArgumentException>(() => (first with { Quantity = "0" }).RequireValid());
    }

    [Fact]
    public async Task Actual_consumption_storage_is_company_scoped_append_only_and_replaces_only_one_fact_in_the_same_order()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var context = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unopened.invalid;Database=synthetic;Integrated Security=true;Encrypt=false").Options, company);
        var model = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NonKgInputRow))!;
        Assert.Equal(["CompanyId", "OrderId", "Revision"], model.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(model.GetDeclaredQueryFilters()); Assert.Contains(model.GetIndexes(), index => index.IsUnique && index.GetFilter() == "[ReplacesFactId] IS NOT NULL");
        var row = new NonKgInputRow { CompanyId = company.CompanyId, OrderId = Guid.NewGuid(), FactId = Guid.NewGuid(), Revision = 1, Quantity = "2.25" };
        context.NonKgInputs.Attach(row); row.Quantity = "5";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear(); context.NonKgInputs.Add(new NonKgInputRow { CompanyId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
    private static ManufacturingNonKgInputConsumptionV1 Fact() => new(Guid.NewGuid(), 1, ManufacturingNonKgInputKindV1.Paint,
        "Synthetic real paint", ManufacturingNonKgInputUnitV1.Litre, "2.25", DateTimeOffset.Parse("2026-11-17T09:00:00Z"), null, "Synthetic actual measurement");
}
