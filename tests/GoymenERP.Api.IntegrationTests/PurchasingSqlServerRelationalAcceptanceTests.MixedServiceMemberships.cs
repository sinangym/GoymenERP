using System.Data;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Schema/restore prerequisite evidence, not a mixed-order admission, approval, command hash or audit acceptance.
    // Fixture stages the exact server-only metadata in an explicit transaction; no new runtime command is registered.
    [SqlServerFact]
    public async Task Mixed_Draft_membership_restarts_from_the_exact_prepared_service_revision_without_replaying_approval()
    {
        await using var run = await Run.StartAsync();
        var f = await MembershipSourcesAsync(run, CompanyA);
        await StageMembershipFixtureAsync(run, f);
        await using (var service = ServiceScope(run, CompanyA))
            await service.Store.WriteAsync(f.Service.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("membership-child-later-approve"),
                Hash("membership-child-later-approve"), "SENTETIK child owner later transition",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, OrderApprover, old!.UpdatedAtUtc)), _ => Task.CompletedTask, default);
        await using var restart = run.Store(CompanyA);
        var stored = (await restart.Store.FindOrderAsync(f.Parent.Order.Id))!;
        Assert.Equal((3L, PurchaseOrderStatus.Draft), (stored.Order.Version, stored.Order.Status));
        Assert.Null(stored.Order.Approval); Assert.Empty(stored.Order.Acceptances);
        var member = Assert.Single(stored.Order.ServiceMemberships);
        Assert.Equal((f.Service.OrderId, 1L, f.Parent.CompanyAccountId), (member.ServiceOrderId, member.PreparedVersion, member.CompanyAccountId));
        Assert.Equal(f.Service.Lines.Select(x => x.LineId), member.LineIds);
        Assert.Equal(f.BoundAt, Assert.Single(stored.Order.ServiceMembershipBatches).RecordedAtUtc);
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@company AND OrderId=@id",
            ("@company", CompanyA), ("@id", f.Service.OrderId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships WHERE CompanyId=@company AND OrderId=@id",
            ("@company", CompanyA), ("@id", f.Parent.Order.Id)));
        Assert.Equal(PurchaseOrder.ServiceMembershipUnavailable, Assert.Throws<PurchasingRuleException>(() =>
            stored.Order.Approve(3, OrderApprover, f.BoundAt)).Code);
        Assert.Null(await restart.Store.FindReplayAsync(run.Envelope("SENTETIK-no-membership-command"), Hash("SENTETIK-no-membership-command"), PurchasingCommands.OrderApproveV1));
        await using var foreign = run.Store(CompanyB);
        Assert.Null(await foreign.Store.FindOrderAsync(f.Parent.Order.Id));
    }

    [SqlServerFact]
    public async Task Mixed_membership_unique_child_is_company_scoped_and_a_failed_second_parent_binding_rolls_back_its_version()
    {
        await using var run = await Run.StartAsync();
        var first = await MembershipSourcesAsync(run, CompanyA); await StageMembershipFixtureAsync(run, first);
        var second = await MembershipSourcesAsync(run, CompanyA, sharedService: first.Service);
        await Assert.ThrowsAsync<DbUpdateException>(() => StageMembershipFixtureAsync(run, second));
        await using (var read = run.Store(CompanyA))
        {
            var untouched = (await read.Store.FindOrderAsync(second.Parent.Order.Id))!;
            Assert.Equal(2L, untouched.Order.Version); Assert.Empty(untouched.Order.ServiceMemberships);
        }
        var other = await MembershipSourcesAsync(run, CompanyB, serviceId: first.Service.OrderId);
        await StageMembershipFixtureAsync(run, other);
        await using (var read = run.Store(CompanyB))
            Assert.Equal(other.Parent.Order.Id, Assert.Single((await read.Store.FindOrderAsync(other.Parent.Order.Id))!.Order.ServiceMemberships).PurchaseOrderId);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships WHERE ServiceOrderId=@id", ("@id", first.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_membership_rollback_removes_the_entire_binding_slot_and_missing_history_is_not_inferred_from_supplier_or_date()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA);
        await StageMembershipFixtureAsync(run, f, commit: false);
        await using (var restart = run.Store(CompanyA))
        {
            var untouched = (await restart.Store.FindOrderAsync(f.Parent.Order.Id))!;
            Assert.Equal(2L, untouched.Order.Version); Assert.Empty(untouched.Order.ServiceMemberships);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships WHERE CompanyId=@company AND OrderId=@id",
            ("@company", CompanyA), ("@id", f.Parent.Order.Id)));
        await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET Version=3 WHERE CompanyId=@company AND Id=@id",
            ("@company", CompanyA), ("@id", f.Parent.Order.Id));
        await using var corrupted = run.Store(CompanyA);
        await Assert.ThrowsAsync<InvalidOperationException>(() => corrupted.Store.FindOrderAsync(f.Parent.Order.Id));
    }

    [SqlServerFact]
    public async Task Mixed_membership_Down_guard_refuses_existing_history_before_any_drop()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA);
        await StageMembershipFixtureAsync(run, f);
        var migration = new AddPurchaseOrderServiceMemberships();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 52361", guard.Sql, StringComparison.Ordinal);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard.Sql));
        Assert.Equal(52361, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships WHERE CompanyId=@company AND OrderId=@id",
            ("@company", CompanyA), ("@id", f.Parent.Order.Id)));
        await using var restart = run.Store(CompanyA);
        Assert.Single((await restart.Store.FindOrderAsync(f.Parent.Order.Id))!.Order.ServiceMemberships);
    }

    private sealed record MembershipSources(StoredPurchaseOrder Parent, PurchaseServiceOrderState Service, DateTimeOffset BoundAt);
    private static async Task<MembershipSources> MembershipSourcesAsync(Run run, Guid company,
        PurchaseServiceOrderState? sharedService = null, Guid? serviceId = null)
    {
        var owners = sharedService is null ? Owners.For(company) : new Owners(company, sharedService.SupplierPartyId, sharedService.CompanyAccountId);
        var draft = sharedService ?? ServiceDraft(run) with { CompanyId = company, OrderId = serviceId ?? Guid.NewGuid(),
            SupplierPartyId = owners.Party, CompanyAccountId = owners.Account };
        PurchaseServiceOrderState service;
        if (sharedService is null)
        {
            await using var child = ServiceScope(run, company);
            service = (await child.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create,
                run.Envelope("membership-child-" + draft.OrderId.ToString("N")), Hash("membership-child-" + draft.OrderId.ToString("N")),
                "SENTETIK membership prerequisite", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default))!.Order!;
        }
        else service = sharedService;
        var money = PurchaseOrderMoney.Of(service.Money.CurrencyId, service.Money.CurrencyCode,
            service.Money.ProfileId, service.Money.ProfileVersion, service.Money.Scale, service.Money.UnitPriceScale);
        var order = PurchaseOrder.Create(Guid.NewGuid(), company, owners.Party, money, T0);
        order.AddLine(order.Version, Guid.NewGuid(), Product, KgUnit, Kg(4m), T0);
        await using var parent = run.Store(company);
        var stored = (await parent.Store.StoreOrderAsync(run.Envelope("membership-parent-" + order.Id.ToString("N")),
            Hash("membership-parent-" + order.Id.ToString("N")), owners.Account, Day, () => order, VersionsOf(order))).Order;
        return new(stored, service, T0.AddMinutes(1));
    }

    private static async Task StageMembershipFixtureAsync(Run run, MembershipSources f, bool commit = true)
    {
        await using var scope = run.Store(f.Parent.Order.CompanyId);
        await using var tx = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var parent = await scope.Context.Orders.SingleAsync(x => x.Id == f.Parent.Order.Id);
        _ = await scope.Context.ServiceOrderRevisions.SingleAsync(x => x.OrderId == f.Service.OrderId && x.Version == f.Service.Version);
        f.Parent.Order.BindServiceOrders(2, f.Parent.CompanyAccountId, [f.Service],
            [new(f.Service.OrderId, f.Service.Version, f.Service.Lines.Select(x => x.LineId).ToArray())], f.BoundAt);
        parent.Version = f.Parent.Order.Version; parent.UpdatedAtUtc = f.BoundAt;
        scope.Context.OrderServiceMemberships.AddRange(PurchasingStore.ServiceMembershipRows(f.Parent.Order));
        await scope.Context.SaveChangesAsync();
        if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
    }
}
