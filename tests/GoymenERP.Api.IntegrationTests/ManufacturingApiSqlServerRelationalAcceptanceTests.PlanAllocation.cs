using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U3-a (URT-D21, URT-D3, URT-D22; API-20/21) on real SQL Server: the plan approval allocates every input in
/// Inventory (Kalan Modüller B-01) in the approval's own transaction; the order's input issue consumes that allocation;
/// a cancellation of an approved plan and an order's closure release what remains. Either module's refusal rolls both
/// back.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Plan_approval_allocates_its_inputs_and_the_order_issues_from_that_allocation_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var client = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-ALLOC-{Guid.NewGuid():N}");
        var plan = new SavePlanRequestV1(Guid.NewGuid(), await run.WorkCenterOfAsync(CompanyA),
            [new PlannedInputRequestV1(roll.Id, "600.125", roll.Version)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", plan)).StatusCode);

        var approver = await run.ApproverAsync(CompanyA);
        using var approved = await approver.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval",
            new ApprovePlanRequestV1(1), "approve-alloc");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        // Inventory holds exactly the planned kg for this plan; Manufacturing keeps the allocation's identity and the
        // label version Inventory returned.
        var allocationId = await run.ScalarAsync<Guid>(
            "SELECT Id FROM inventory.Allocations WHERE DemandKind = N'manufacturing.plan-input' AND DemandId = @plan "
            + "AND DemandLineId = @label AND Status = 1 AND RemainingKg = 600.125",
            ("@plan", plan.PlanId), ("@label", roll.Id));
        Assert.Equal(allocationId, await run.ScalarAsync<Guid>(
            "SELECT AllocationId FROM manufacturing.PlanInputAllocations WHERE PlanId = @plan AND ParentRollId = @label "
            + "AND AllocatedKg = 600.125 AND PlanVersion = 2",
            ("@plan", plan.PlanId), ("@label", roll.Id)));
        var labelVersion = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", roll.Id));
        Assert.Equal(long.Parse(roll.Version, System.Globalization.CultureInfo.InvariantCulture) + 1, labelVersion);
        Assert.Equal(labelVersion, await run.ScalarAsync<long>(
            "SELECT LabelVersionAfter FROM manufacturing.PlanInputAllocations WHERE PlanId = @plan",
            ("@plan", plan.PlanId)));

        // The same key replays the approval and allocates nothing twice.
        using var replay = await approver.PostWithKeyAsync($"{Base}/plans/{plan.PlanId}/approval",
            new ApprovePlanRequestV1(1), "approve-alloc");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan", ("@plan", plan.PlanId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId = @plan", ("@plan", plan.PlanId)));

        // The order's input enters at the allocation's label version; the issue consumes the allocation.
        var orderId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
            new CreateOrderRequestV1(orderId, plan.PlanId))).StatusCode);
        Assert.Equal(labelVersion, await run.ScalarAsync<long>(
            "SELECT ExpectedLabelVersion FROM manufacturing.OrderInputs WHERE OrderId = @order", ("@order", orderId)));
        await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues", Run.Issue(1, roll.Id, "600.125", At));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT Status FROM inventory.Allocations WHERE Id = @id AND RemainingKg = 0", ("@id", allocationId)));
    }

    [SqlServerFact]
    public async Task Two_plans_split_one_label_and_both_approve_and_issue_at_the_current_label_version_on_real_sql()
    {
        // URT-D3: one label's kilograms are split between two plans saved at the same label version. The first approval
        // moves the label; the second is allocated at the version Inventory holds now, and so is the first order's issue.
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var client = run.ClientFor(user, CompanyA);
        var approver = await run.ApproverAsync(CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-SPLIT-{Guid.NewGuid():N}");
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var first = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "600", roll.Version)], null);
        var second = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "400.125", roll.Version)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", second)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{first.PlanId}/approval",
            new ApprovePlanRequestV1(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{second.PlanId}/approval",
            new ApprovePlanRequestV1(1))).StatusCode);
        Assert.Equal(1000.125m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE LabelId = @label AND Status = 1",
            ("@label", roll.Id)));

        var orderId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/orders",
            new CreateOrderRequestV1(orderId, first.PlanId))).StatusCode);
        await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues", Run.Issue(1, roll.Id, "600", At));
        Assert.Equal(400.125m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE LabelId = @label AND Status = 1",
            ("@label", roll.Id)));
    }

    [SqlServerFact]
    public async Task An_allocation_refusal_rolls_back_the_approval_and_stores_nothing_on_either_side_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var client = run.ClientFor(user, CompanyA);
        var approver = await run.ApproverAsync(CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-SHORT-{Guid.NewGuid():N}");
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var first = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "600", roll.Version)], null);
        var second = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "400.126", roll.Version)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{first.PlanId}/approval",
            new ApprovePlanRequestV1(1))).StatusCode);
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        // 1000.125 − 600 leaves 400.125 kg free: 400.126 kg is refused, and the approval rolls back with it.
        using var refused = await approver.PostWithKeyAsync($"{Base}/plans/{second.PlanId}/approval",
            new ApprovePlanRequestV1(1), "approve-short");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, (await Problem(refused)).Code);
        var stored = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{second.PlanId}"))!;
        Assert.Equal(("Draft", 1L), (stored.Status, stored.Version));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan", ("@plan", second.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId = @plan", ("@plan", second.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanParameterSnapshots WHERE PlanId = @plan", ("@plan", second.PlanId)));
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Two_concurrent_approvals_of_one_plan_leave_one_approval_and_one_allocation_set_on_real_sql()
    {
        // Kalan Modüller B-01 review: Inventory's "one allocation per plan" check sees only active rows, so two racing
        // approvals must be settled by Manufacturing's plan version fence: one wins, the other stores nothing.
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        using var client = run.ClientFor(user, CompanyA);
        var roll = await run.StockedLabelAsync($"SYN-MFG-RACE-{Guid.NewGuid():N}");
        var other = await run.StockedLabelAsync($"SYN-MFG-RACE2-{Guid.NewGuid():N}");
        var plan = new SavePlanRequestV1(Guid.NewGuid(), await run.WorkCenterOfAsync(CompanyA),
            [new PlannedInputRequestV1(roll.Id, "500", roll.Version), new PlannedInputRequestV1(other.Id, "250", other.Version)],
            null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", plan)).StatusCode);
        var approver = await run.ApproverAsync(CompanyA);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => approver.PostWithKeyAsync(
            $"{Base}/plans/{plan.PlanId}/approval", new ApprovePlanRequestV1(1), $"approve-race-{index}")));
        Assert.Equal(1, attempts.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, attempts.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var attempt in attempts)
        {
            attempt.Dispose();
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan AND Status = 1", ("@plan", plan.PlanId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputAllocations WHERE PlanId = @plan", ("@plan", plan.PlanId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanParameterSnapshots WHERE PlanId = @plan", ("@plan", plan.PlanId)));
    }

    [SqlServerFact]
    public async Task Cancelling_an_approved_plan_and_closing_its_order_release_the_remaining_allocation_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose, ErpPermissions.ManufacturingPlansCancel]);
        using var client = run.ClientFor(user, CompanyA);
        var approver = await run.ApproverAsync(CompanyA);
        var workCenter = await run.WorkCenterOfAsync(CompanyA);

        // An approved plan without an order is cancelled: its whole allocation is released, no stock moves.
        var roll = await run.StockedLabelAsync($"SYN-MFG-CANCEL-{Guid.NewGuid():N}");
        var cancelled = new SavePlanRequestV1(Guid.NewGuid(), workCenter,
            [new PlannedInputRequestV1(roll.Id, "700", roll.Version)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostWithKeyAsync($"{Base}/plans", cancelled)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostWithKeyAsync($"{Base}/plans/{cancelled.PlanId}/approval",
            new ApprovePlanRequestV1(1))).StatusCode);
        using (var cancel = await client.PostWithKeyAsync($"{Base}/plans/{cancelled.PlanId}/cancellation",
                   new CancelPlanRequestV1(2, "Sipariş vazgeçildi")))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE DemandId = @plan AND Status = 3 "
            + "AND ReleaseReason = N'manufacturing.plan-cancelled'",
            ("@plan", cancelled.PlanId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @label AND Status = 1", ("@label", roll.Id)));

        // An order that used 600 of its 1000 planned kg closes balanced: the 400 kg left of its allocation is released.
        var (orderId, issuedRoll) = await run.OpenOrderAsync(user);
        var issued = await run.OkAsync(client, $"{Base}/orders/{orderId}/input-issues",
            Run.Issue(1, issuedRoll, "600", At));
        var balanced = await run.OkAsync(client, $"{Base}/orders/{orderId}/outputs",
            await run.OutputAsync(client, issued.Version, issuedRoll, "GoodProduct", "600", At.AddMinutes(1)));
        using (var close = await client.PostWithKeyAsync($"{Base}/orders/{orderId}/close",
                   new CloseOrderRequestV1(balanced.Version)))
        {
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE LabelId = @label AND Status = 3 "
            + "AND ReleaseReason = N'manufacturing.order-closed' AND ReleasedKg = 400",
            ("@label", issuedRoll)));
    }

    [SqlServerFact]
    public async Task The_plan_input_allocation_migrations_Down_passes_without_rows_and_refuses_while_one_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingPlanInputAllocations().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        await run.OpenOrderAsync(user);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations"));
        var refused = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51191, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanInputAllocations"));
    }
}
