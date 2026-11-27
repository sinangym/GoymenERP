using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real DbContext SaveChanges/ChangeTracker guard; InMemory proves no SQL constraint or transaction acceptance.</summary>
public sealed class LogisticsCustomerReturnPersistenceGuardTests
{
    [Fact]
    public async Task Complete_weighing_append_and_measured_rejection_are_two_real_SaveChanges_forward_transitions()
    {
        await using var db = Database(); var row = Row(); db.CustomerReturns.Add(row); await db.SaveChangesAsync();
        Weigh(row); await db.SaveChangesAsync(); Assert.Equal((1, 2L), (row.Status, row.Version));
        Reject(row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var stored = await db.CustomerReturns.SingleAsync(); Assert.Equal((3, 3L, (decimal?)13.125m), (stored.Status, stored.Version, stored.NetWeighingKg));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_unweighed_kilogram_decisions_keep_the_real_one_to_two_SaveChanges_transition(bool approve)
    {
        await using var db = Database(); var row = Row(); row.QuantityUnit = 1; db.CustomerReturns.Add(row); await db.SaveChangesAsync();
        Reject(row);
        if (approve) { row.Status = 2; row.InventoryMovementId = Guid.NewGuid(); row.OriginalLabelId = Guid.NewGuid(); row.OriginalShipmentMovementId = Guid.NewGuid(); }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var stored = await db.CustomerReturns.SingleAsync(); Assert.Equal((approve ? 2 : 3, 2L), (stored.Status, stored.Version)); Assert.Null(stored.WeighingId);
    }

    [Theory]
    [InlineData("partial")] [InlineData("overwrite")] [InlineData("clear")] [InlineData("terminal-attach")]
    [InlineData("version-jump")] [InlineData("source")] [InlineData("decision-fields")]
    [InlineData("decision-overwrite")] [InlineData("decision-version-jump")] [InlineData("empty-pending-bump")]
    public async Task SaveChanges_refuses_weighing_or_decision_mutation_outside_the_two_exact_transitions(string fault)
    {
        await using var db = Database(); var row = Row(); db.CustomerReturns.Add(row); await db.SaveChangesAsync();
        if (fault is "overwrite" or "clear" or "decision-overwrite" or "decision-version-jump") { Weigh(row); await db.SaveChangesAsync(); }
        if (fault == "terminal-attach") { Reject(row); await db.SaveChangesAsync(); }
        Fault(row, fault);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    internal static void Fault(CustomerReturnRow row, string fault)
    {
        switch (fault)
        {
            case "partial": row.WeighingId = Guid.NewGuid(); row.Version = 2; break;
            case "overwrite": row.WeighingId = Guid.NewGuid(); row.Version++; break;
            case "clear": row.WeighingId = null; row.WeighingDirection = null; row.FirstWeighingKg = null; row.SecondWeighingKg = null;
                row.NetWeighingKg = null; row.WeighingRecordedBy = null; row.WeighingRecordedAtUtc = null; row.Version++; break;
            case "terminal-attach": Weigh(row); row.Version = 3; break;
            case "version-jump": Weigh(row); row.Version = 3; break;
            case "source": Weigh(row); row.SourceDeliveryId = Guid.NewGuid(); break;
            case "decision-fields": Weigh(row); row.DecidedBy = "checker"; break;
            case "decision-overwrite": Reject(row); row.NetWeighingKg = 1; break;
            case "decision-version-jump": Reject(row); row.Version++; break;
            case "empty-pending-bump": row.Version = 2; break;
            default: throw new ArgumentException(nameof(fault));
        }
    }
    internal static void Weigh(CustomerReturnRow row)
    {
        row.WeighingId = Guid.NewGuid(); row.WeighingDirection = 2; row.FirstWeighingKg = 1013.125m; row.SecondWeighingKg = 1000m;
        row.NetWeighingKg = 13.125m; row.WeighingRecordedBy = "operator"; row.WeighingRecordedAtUtc = DateTimeOffset.UtcNow; row.Version = 2;
    }
    internal static void Reject(CustomerReturnRow row)
    { row.Status = 3; row.Version++; row.DecidedBy = "checker"; row.DecidedAtUtc = DateTimeOffset.UtcNow; }
    private static CustomerReturnRow Row() => new()
    {
        CompanyId = TestCompanyIds.CompanyA, CustomerReturnId = Guid.NewGuid(), LoadingInstructionId = Guid.NewGuid(), LoadingInstructionLineId = Guid.NewGuid(),
        SourceDeliveryId = Guid.NewGuid(), SourceShipmentId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(), SourceOrderLineId = Guid.NewGuid(), UnitId = Guid.NewGuid(),
        QuantityUnit = 2, Quantity = 1, SourceDeliveredQuantity = 2, Reason = "SYNTHETIC", RequestedBy = "maker", OccurredAtUtc = DateTimeOffset.MinValue,
        RequestedAtUtc = DateTimeOffset.UtcNow, Status = 1, Version = 1,
    };
    private static LogisticsDbContext Database()
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        return new(new DbContextOptionsBuilder<LogisticsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
    }
}
