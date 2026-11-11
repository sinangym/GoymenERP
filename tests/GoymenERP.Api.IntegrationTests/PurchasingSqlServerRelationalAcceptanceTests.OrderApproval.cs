using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A2a-5a (ALS-D3, Fable F-015) on real SQL Server: the order's preparer is the create envelope's actor (CreatedBy) and never
/// approves it; another actor's approval stores who and when (ApprovedBy, ApprovedAtUtc) with the order, in the audit and on reload;
/// <c>CK_PurchaseOrders_Approval</c> refuses on a direct write what the domain refuses (approver = preparer in binary collation,
/// half the facts, facts on a draft); an order approved before A2a-5a (no approver) still loads; the migration's Down refuses while an
/// approver is recorded. SENTETIK owners and actors.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_preparer_cannot_approve_and_another_actors_approval_is_stored_with_who_and_when()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        PurchaseOrder built = Order(owners, null, null, 10m);
        Guid orderId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreOrderAsync(run.Envelope("k-sod-create"), Hash("sod-create"), owners.Account, Day, () => built,
                VersionsOf(built))).Order;
            orderId = stored.Order.Id;
            // The store stamps the create actor as the preparer: the returned order, the row and the audit agree.
            Assert.Equal(run.Actor, stored.Order.PreparedBy);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.created.v1' AND EntityId = @id "
            + "AND AfterJson LIKE @preparer", ("@id", orderId.ToString("D")), ("@preparer", $"%\"preparedBy\":\"{run.Actor}\"%")));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateOrderAsync(run.Envelope("k-sod-self"), Hash("sod-self"),
                PurchasingCommands.OrderApproveV1, orderId, (order, now) => order.Approve(order.Version, run.Actor, now), Approved));
            Assert.Equal(PurchasingRuleCodes.OrderSameActor, refused.Code);
        }

        Assert.Equal((1, 2L), (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
            await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id AND ApprovedBy IS NOT NULL",
            ("@id", orderId)));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var approved = await a.Store.MutateOrderAsync(run.Envelope("k-sod-approve"), Hash("sod-approve"), PurchasingCommands.OrderApproveV1, orderId,
                (order, now) => order.Approve(order.Version, OrderApprover, now), Approved);
            Assert.Equal((PurchaseOrderStatus.Approved, 3L, OrderApprover), (approved!.Order.Order.Status, approved.Order.Order.Version,
                approved.Order.Order.Approval!.ApprovedBy));
        }

        Assert.Equal(OrderApprover, await run.ScalarAsync<string>("SELECT ApprovedBy FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id AND ApprovedAtUtc IS NOT NULL",
            ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.approved.v1' AND EntityId = @id "
            + "AND AfterJson LIKE @approver", ("@id", orderId.ToString("D")), ("@approver", $"%\"approvedBy\":\"{OrderApprover}\"%")));

        // The reload replays the stored approval through the rule: preparer and approver come back as written.
        await using (StoreScope a = run.Store(CompanyA))
        {
            var reloaded = await a.Store.FindOrderAsync(orderId);
            Assert.Equal((run.Actor, OrderApprover), (reloaded!.Order.PreparedBy, reloaded.Order.Approval!.ApprovedBy));
        }
    }

    [SqlServerFact]
    public async Task The_approval_check_refuses_on_a_direct_write_what_the_domain_refuses_and_a_legacy_approval_still_loads()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        PurchaseOrder draft = Order(owners, null, null, 5m);
        PurchaseOrder legacy = Order(owners, null, null, 6m);
        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.StoreOrderAsync(run.Envelope("k-chk-draft"), Hash("chk-draft"), owners.Account, Day, () => draft, VersionsOf(draft));
            await a.Store.StoreOrderAsync(run.Envelope("k-chk-legacy"), Hash("chk-legacy"), owners.Account, Day, () => legacy, VersionsOf(legacy));
        }

        // An order approved before A2a-5a: approved, no approver (the column did not exist).
        await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET Status = 2, Version = 3 WHERE Id = @id", ("@id", legacy.Id));

        foreach (var (sql, id) in new[]
                 {
                     // The preparer as approver.
                     ("UPDATE purchasing.PurchaseOrders SET ApprovedBy = CreatedBy, ApprovedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @id", legacy.Id),
                     // Half the facts.
                     ("UPDATE purchasing.PurchaseOrders SET ApprovedBy = N'SENTETIK-other' WHERE Id = @id", legacy.Id),
                     ("UPDATE purchasing.PurchaseOrders SET ApprovedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @id", legacy.Id),
                     ("UPDATE purchasing.PurchaseOrders SET ApprovedBy = N'', ApprovedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @id", legacy.Id),
                     // Approval facts on a draft.
                     ("UPDATE purchasing.PurchaseOrders SET ApprovedBy = N'SENTETIK-other', ApprovedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @id", draft.Id),
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", id)));
            Assert.Equal(547, refused.Number);
            Assert.Contains("CK_PurchaseOrders_Approval", refused.Message, StringComparison.Ordinal);
        }

        // A case variant of the preparer is another actor in binary collation, as in the domain's ordinal rule.
        await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET ApprovedBy = UPPER(CreatedBy), ApprovedAtUtc = SYSDATETIMEOFFSET() WHERE Id = @id",
            ("@id", legacy.Id));
        await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET ApprovedBy = NULL, ApprovedAtUtc = NULL WHERE Id = @id", ("@id", legacy.Id));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var reloaded = await a.Store.FindOrderAsync(legacy.Id);
            Assert.Equal((PurchaseOrderStatus.Approved, 3L), (reloaded!.Order.Status, reloaded.Order.Version));
            Assert.Null(reloaded.Order.Approval);
            Assert.Equal(run.Actor, reloaded.Order.PreparedBy);
        }
    }

    [SqlServerFact]
    public async Task The_approval_migrations_Down_refuses_while_an_approver_is_recorded()
    {
        await using Run run = await Run.StartAsync();
        var migration = new AddPurchaseOrderApproval();
        string guardSql = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Owners owners = Owners.For(CompanyA);
        PurchaseOrder built = Order(owners, null, null, 3m);
        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.StoreOrderAsync(run.Envelope("k-down-create"), Hash("down-create"), owners.Account, Day, () => built, VersionsOf(built));
        }

        // No approver recorded yet: the guard passes (and changes nothing).
        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-down-approve"), Hash("down-approve"), PurchasingCommands.OrderApproveV1, built.Id,
                (order, now) => order.Approve(order.Version, OrderApprover, now), Approved);
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51172, guard.Number);
        Assert.Equal(OrderApprover, await run.ScalarAsync<string>("SELECT ApprovedBy FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", built.Id)));
    }
}
