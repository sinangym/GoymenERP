using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-01 (URT-D3, URT-D21, URT-D22; Manufacturing API-20/21): plan-input allocations on real SQL Server. They share the
/// Sales physical allocation's rows and free-kg rule, so neither side can hold the same kilogram twice.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Plan_allocation_holds_every_input_once_replays_and_closes_those_kilograms_to_Sales()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedManufacturingParentAsync(run, "SYN-PLAN-ALLOC-A", 100m);
        var second = await SeedManufacturingParentAsync(run, "SYN-PLAN-ALLOC-B", 50m);
        var planId = Guid.NewGuid();
        var request = PlanRequest(planId,
            CompanyLine(first.Label.Id, "2", "60.125"),
            CompanyLine(second.Label.Id, "2", "50"));
        var metadata = ManufacturingMetadata(run, "plan-allocate");

        var allocated = await CommitPlanAllocationAsync(run, request, metadata);
        Assert.False(allocated.IsReplay);
        Assert.Equal(2, allocated.Allocations.Count);
        Assert.All(allocated.Allocations, allocation => Assert.Equal("3", allocation.LabelVersion));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandKind = 'manufacturing.plan-input' "
            + "AND DemandId = @plan AND DemandLineId = LabelId AND Status = 1 AND ReservedUntilUtc IS NULL",
            ("@plan", planId)));
        Assert.Equal(60.125m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE DemandId = @plan AND LabelId = @label",
            ("@plan", planId), ("@label", first.Label.Id)));

        // Same command again (a retried approval): the stored result comes back and nothing is written twice.
        var replay = await CommitPlanAllocationAsync(run, request,
            metadata with { CorrelationId = "plan-allocate-retry" });
        Assert.True(replay.IsReplay);
        Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan", ("@plan", planId)));

        // URT-D21: what the plan holds is not free for Sales. 100 − 60.125 leaves exactly 39.875 kg.
        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.CreateAsync(
                AllocationRequest(new PhysicalSource(first.Label.Id, first.Label.ProductId, 3), "39.876"),
                Metadata(run)));
            Assert.Equal(InventoryAllocationCodesV1.InsufficientFreeStock, refused.Code);
            var fits = await scope.Store.CreateAsync(
                AllocationRequest(new PhysicalSource(first.Label.Id, first.Label.ProductId, 3), "39.875"),
                Metadata(run));
            Assert.Equal("39.875", fits.Allocation.AllocatedKg);
        }

        // A plan is allocated once: a second approval under another key is refused, not doubled.
        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(planId, CompanyLine(second.Label.Id, "3", "1")),
            ManufacturingMetadata(run, "plan-allocate-again"), 409,
            InventoryManufacturingRealizationCodesV1.ConcurrentChange);
    }

    [SqlServerFact]
    public async Task Plan_allocation_is_all_or_nothing_and_refuses_stale_label_or_too_many_kilograms()
    {
        await using var run = await Run.StartAsync();
        var fits = await SeedManufacturingParentAsync(run, "SYN-PLAN-ATOMIC-FITS", 40m);
        var tooSmall = await SeedManufacturingParentAsync(run, "SYN-PLAN-ATOMIC-SMALL", 10m);
        var planId = Guid.NewGuid();

        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(planId, CompanyLine(fits.Label.Id, "2", "40"), CompanyLine(tooSmall.Label.Id, "2", "10.001")),
            ManufacturingMetadata(run, "plan-atomic"), 409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock);
        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(planId, CompanyLine(fits.Label.Id, "1", "1")),
            ManufacturingMetadata(run, "plan-stale"), 409,
            InventoryManufacturingRealizationCodesV1.LabelVersionConflict);
        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(planId, CompanyLine(fits.Label.Id, "2", "1"), CompanyLine(fits.Label.Id, "2", "1")),
            ManufacturingMetadata(run, "plan-duplicate-label"), 400,
            InventoryManufacturingRealizationCodesV1.InvalidRequest);

        // Nothing of the refused plan survived: no allocation, no command, no label version bump.
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan", ("@plan", planId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @plan", ("@plan", planId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", fits.Label.Id)));
    }

    [SqlServerFact]
    public async Task Customer_owned_stock_is_allocated_only_to_the_input_planned_for_that_customer()
    {
        await using var run = await Run.StartAsync();
        var customer = Guid.NewGuid();
        var customerStock = await SeedManufacturingParentAsync(run, "SYN-PLAN-CUSTOMER", 30m,
            StockPartyKind.Counterparty, customer, StockPartyKind.Company, CompanyA);
        var companyStock = await SeedManufacturingParentAsync(run, "SYN-PLAN-COMPANY", 30m);

        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(Guid.NewGuid(), CompanyLine(customerStock.Label.Id, "2", "5")),
            ManufacturingMetadata(run, "customer-as-company"), 409,
            InventoryManufacturingRealizationCodesV1.AllocationOwnerMismatch);
        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(Guid.NewGuid(), CustomerLine(customerStock.Label.Id, "2", "5", Guid.NewGuid())),
            ManufacturingMetadata(run, "customer-other"), 409,
            InventoryManufacturingRealizationCodesV1.AllocationOwnerMismatch);
        await AssertPlanAllocationRefusalAsync(run,
            PlanRequest(Guid.NewGuid(), CustomerLine(companyStock.Label.Id, "2", "5", customer)),
            ManufacturingMetadata(run, "company-as-customer"), 409,
            InventoryManufacturingRealizationCodesV1.AllocationOwnerMismatch);

        var planId = Guid.NewGuid();
        var allocated = await CommitPlanAllocationAsync(run,
            PlanRequest(planId, CustomerLine(customerStock.Label.Id, "2", "30", customer)),
            ManufacturingMetadata(run, "customer-own"));
        Assert.Single(allocated.Allocations);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan AND OwnerKind = 2 AND OwnerId = @owner",
            ("@plan", planId), ("@owner", customer)));
    }

    [SqlServerFact]
    public async Task Input_issue_consumes_its_own_allocation_within_its_remaining_and_release_frees_the_rest()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-PLAN-CONSUME", 100m);
        var other = await SeedManufacturingParentAsync(run, "SYN-PLAN-CONSUME-OTHER", 10m);
        var planId = Guid.NewGuid();
        var allocated = await CommitPlanAllocationAsync(run,
            PlanRequest(planId, CompanyLine(parent.Label.Id, "2", "60"), CompanyLine(other.Label.Id, "2", "10")),
            ManufacturingMetadata(run, "consume-allocate"));
        var allocationId = allocated.Allocations.Single(item => item.LabelId == parent.Label.Id).AllocationId;
        var otherAllocationId = allocated.Allocations.Single(item => item.LabelId == other.Label.Id).AllocationId;

        // Sales holds the other 40 kg, so only the plan's own 60 kg can feed this issue.
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(
                AllocationRequest(new PhysicalSource(parent.Label.Id, parent.Label.ProductId, 3), "40"), Metadata(run));
        }

        var orderId = Guid.NewGuid();
        await AssertIssueRefusalAsync(run, IssueRequest(orderId, parent.Label.Id, "4", "1"),
            ManufacturingMetadata(run, "consume-unallocated"), 409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, ManufacturingSettings(run));
        await AssertIssueRefusalAsync(run,
            IssueRequest(orderId, parent.Label.Id, "4", "1") with { AllocationId = otherAllocationId },
            ManufacturingMetadata(run, "consume-wrong-label"), 409,
            InventoryManufacturingRealizationCodesV1.AllocationNotUsable, ManufacturingSettings(run));
        await AssertIssueRefusalAsync(run,
            IssueRequest(orderId, parent.Label.Id, "4", "60.001") with { AllocationId = allocationId },
            ManufacturingMetadata(run, "consume-over"), 409,
            InventoryManufacturingRealizationCodesV1.AllocationExceeded, ManufacturingSettings(run));

        var issue = IssueRequest(orderId, parent.Label.Id, "4", "25") with { AllocationId = allocationId };
        var issueMetadata = ManufacturingMetadata(run, "consume-part");
        var issued = await CommitIssueAsync(run, issue, issueMetadata);
        Assert.Equal("5", issued.ParentLabelVersion);
        Assert.Equal((1, 35m), (
            await run.ScalarAsync<int>("SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)),
            await run.ScalarAsync<decimal>("SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id",
                ("@id", allocationId))));
        var issueReplay = await CommitIssueAsync(run, issue, issueMetadata with { CorrelationId = "consume-retry" });
        Assert.True(issueReplay.IsReplay);
        Assert.Equal(35m, await run.ScalarAsync<decimal>(
            "SELECT RemainingKg FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)));

        // Plan cancelled: the remaining 35 kg and the untouched 10 kg go back to free stock, without a movement.
        var movementsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var release = new InventoryManufacturingPlanReleaseRequestV1(CompanyA, planId, "5",
            InventoryManufacturingPlanReleaseReasonV1.PlanCancelled, ManufacturingAt.AddMinutes(5));
        var released = await CommitPlanReleaseAsync(run, release, ManufacturingMetadata(run, "release"));
        Assert.Equal(new[] { ("10", other.Label.Id), ("35", parent.Label.Id) }.OrderBy(item => item.Item2),
            released.Released.Select(item => (item.ReleasedKg, item.LabelId)).OrderBy(item => item.Item2));
        Assert.Equal(movementsBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal((3, 35m, "manufacturing.plan-cancelled"), (
            await run.ScalarAsync<int>("SELECT Status FROM inventory.Allocations WHERE Id = @id", ("@id", allocationId)),
            await run.ScalarAsync<decimal>("SELECT ReleasedKg FROM inventory.Allocations WHERE Id = @id",
                ("@id", allocationId)),
            await run.ScalarAsync<string>("SELECT ReleaseReason FROM inventory.Allocations WHERE Id = @id",
                ("@id", allocationId))));

        // The read port keeps consumed and released apart.
        await using (var context = run.Context(CompanyA))
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var views = await new InventoryManufacturingPlanAllocationReadPort(context, company).GetForPlanAsync(planId);
            var view = views.Single(item => item.AllocationId == allocationId);
            Assert.Equal(("60", "0", "25", InventoryManufacturingPlanAllocationStatusV1.Released),
                (view.AllocatedKg, view.RemainingKg, view.ConsumedKg, view.Status));
        }

        // Freed kg are free again; a second release under another key finds nothing active and succeeds.
        await using (var scope = run.Store(CompanyA))
        {
            var freed = await scope.Store.CreateAsync(
                AllocationRequest(new PhysicalSource(parent.Label.Id, parent.Label.ProductId, 6), "35"), Metadata(run));
            Assert.Equal("35", freed.Allocation.AllocatedKg);
        }

        var again = await CommitPlanReleaseAsync(run, release, ManufacturingMetadata(run, "release-again"));
        Assert.Empty(again.Released);
    }

    [SqlServerFact]
    public async Task Rolling_back_the_plan_input_migration_is_refused_while_a_plan_input_exists()
    {
        await using var run = await Run.StartAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-PLAN-ROLLBACK", 10m);
        await CommitPlanAllocationAsync(run, PlanRequest(Guid.NewGuid(), CompanyLine(parent.Label.Id, "2", "5")),
            ManufacturingMetadata(run, "rollback"));

        await using var context = run.Context(CompanyA);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>()
            .MigrateAsync("20261002110000_AddInventoryLabelClassification"));
        Assert.Equal(547, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.__EFMigrationsHistory "
            + "WHERE MigrationId LIKE N'%_AddManufacturingPlanInputAllocations'"));
    }

    private static InventoryManufacturingPlanAllocationRequestV1 PlanRequest(Guid planId,
        params InventoryManufacturingPlanInputLineV1[] lines) => new(
        CompanyA, planId, "2", lines, "Üretim planı onayı (sentetik)", ManufacturingAt.AddMinutes(1));

    private static InventoryManufacturingPlanInputLineV1 CompanyLine(Guid labelId, string labelVersion,
        string quantity) => new(labelId, labelVersion, quantity, InventoryManufacturingPlanInputOwnershipV1.Company, null);

    private static InventoryManufacturingPlanInputLineV1 CustomerLine(Guid labelId, string labelVersion,
        string quantity, Guid party) => new(labelId, labelVersion, quantity,
        InventoryManufacturingPlanInputOwnershipV1.CustomerOwned, party);

    private static async Task<InventoryManufacturingPlanAllocationResultV1> CommitPlanAllocationAsync(
        Run run, InventoryManufacturingPlanAllocationRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StagePlanInputAllocationAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<InventoryManufacturingPlanReleaseResultV1> CommitPlanReleaseAsync(
        Run run, InventoryManufacturingPlanReleaseRequestV1 request, InventoryCommandMetadata metadata)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var result = await participant.StagePlanRemainderReleaseAsync(request, metadata);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertPlanAllocationRefusalAsync(Run run,
        InventoryManufacturingPlanAllocationRequestV1 request, InventoryCommandMetadata metadata,
        int status, string code)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await InventoryManufacturingRealizationParticipant.EnlistAsync(
            connection, transaction, CompanyA, ManufacturingSettings(run));
        var error = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            participant.StagePlanInputAllocationAsync(request, metadata));
        Assert.Equal((status, code), (error.Status, error.Code));
        await transaction.RollbackAsync();
    }
}
