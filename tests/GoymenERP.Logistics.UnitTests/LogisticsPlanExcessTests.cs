using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsPlanExcessTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(LoadingInstructionShipmentKind.Sale)]
    [InlineData(LoadingInstructionShipmentKind.ContractProcessingDelivery)]
    public void Separate_approved_capacity_keeps_plan_100_and_only_allows_loaded_and_shipped_120(LoadingInstructionShipmentKind kind)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), Now, kind);
        var line = instruction.AddLine(1, Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(100), Now);
        instruction.Issue(2, Now);
        Assert.Throws<LogisticsRuleException>(() => instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(120), MeasurementKind.Measured, Now));
        line.RestoreApprovedPlanExcessCapacity(Kg(20));
        Assert.Equal(3, instruction.Version); // Approval has its own version and cannot rewrite the original instruction history.
        instruction.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(120), MeasurementKind.Measured, Now);
        Assert.Equal((100m, 20m, 120m, 0m), (line.InstructedQuantity.Value, line.ApprovedPlanExcessQuantity.Value, line.LoadedQuantity.Value, line.RemainingToLoad.Value));
        Assert.Throws<LogisticsRuleException>(() => instruction.MarkShipped(4, Guid.NewGuid(), line.Id, Kg(121), Now));
        if (kind == LoadingInstructionShipmentKind.ContractProcessingDelivery)
            instruction.RecordWeighing(4, Guid.NewGuid(), WeighingDirection.TareFirst, Kg(100), Kg(220), "SENTETIK", Now);
        instruction.MarkShipped(instruction.Version, Guid.NewGuid(), line.Id, Kg(120), Now);
        Assert.Equal((100m, 120m), (line.InstructedQuantity.Value, line.ShippedQuantity.Value));
    }
    [Theory]
    [InlineData("tr-TR", "20.000", "20")]
    [InlineData("de-DE", "20.000", "20")]
    [InlineData("tr-TR", "20.125", "20.125")]
    [InlineData("de-DE", "20.125", "20.125")]
    public void Approved_quantities_are_canonical_invariant_and_preserve_all_three_decimals(string culture, string stored, string expected)
    {
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, LogisticsStore.PlanExcessQuantity(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture)));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
    }
    [Fact]
    public void Legacy_physical_request_hash_omits_null_excess_and_explicit_proof_is_not_discarded()
    {
        var load = new RecordLoadRequest("3", Guid.NewGuid(), "1", "Measured");
        Assert.DoesNotContain("planExcessUses", JsonSerializer.Serialize(load, LogisticsJson.Options));
        var explicitProof = load with { PlanExcessUses = [new(Guid.NewGuid(), "2", "1")] };
        Assert.NotEqual(LogisticsRequestHash.Compute(LogisticsCommands.RecordLoadV1, Guid.Empty, load),
            LogisticsRequestHash.Compute(LogisticsCommands.RecordLoadV1, Guid.Empty, explicitProof));
        Assert.Contains("planExcessUses", JsonSerializer.Serialize(explicitProof, LogisticsJson.Options));
    }
    [Fact]
    public void Approval_and_each_actual_use_and_reservation_are_company_bound_with_restrictive_source_keys()
    {
        using var db = Context(); var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(LogisticsPlanExcessRow), typeof(LogisticsPlanExcessUsageRow), typeof(LogisticsPlanExcessReservationRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
            Assert.Equal((18, 3), (entity.FindProperty("Quantity")!.GetPrecision(), entity.FindProperty("Quantity")!.GetScale()));
        }
        Assert.True(model.FindEntityType(typeof(LogisticsPlanExcessRow))!.FindProperty("Version")!.IsConcurrencyToken);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorded_usage_and_reservation_cannot_be_modified_or_deleted_before_database_access(bool delete)
    {
        await using var db = Context();
        var use = new LogisticsPlanExcessUsageRow { CompanyId = Company, ExceptionId = Guid.NewGuid(), ShareId = Guid.NewGuid(), Quantity = 1 };
        db.PlanExcessUsages.Attach(use);
        if (delete) db.Remove(use); else use.Quantity = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var reservation = new LogisticsPlanExcessReservationRow { CompanyId = Company, AllocationId = Guid.NewGuid(), Quantity = 1 };
        db.PlanExcessReservations.Attach(reservation);
        if (delete) db.Remove(reservation); else reservation.Quantity = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    private static Quantity Kg(decimal amount) => Quantity.Of(amount, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram);
    private static LogisticsDbContext Context()
    {
        var company = new CompanyContext(); company.Set(Company);
        return new(new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1").Options, company);
    }
}
