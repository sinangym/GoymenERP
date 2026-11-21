using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cancelling_a_numbered_order_keeps_one_gap_and_the_next_order_does_not_reuse_its_number_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (orderId, _, _) = await ApprovedAcceptanceOrderAsync(run, 10m);
        // The borrowed path must not commit the host's cancellation, number gap or either audit.
        // Retrying the same key after rollback must perform the real cancellation below.
        await using (var scope = run.Store(CompanyA))
        {
            await using var hostTransaction = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var staged = await scope.Store.MutateOrderAsync(run.Envelope("number-cancel"), Hash("number-cancel"),
                PurchasingCommands.OrderCancelV1, orderId,
                (order, now) => order.Cancel(order.Version, run.Actor, "SENTETIK numara boÅŸluÄŸu", now), OrderRemainderCancelled);
            Assert.Equal("ALI-2026-000001", staged!.Order.DocumentNumber);
            Assert.Same(hostTransaction, scope.Context.Database.CurrentTransaction);
            Assert.NotNull(hostTransaction.GetDbTransaction().Connection);
            await hostTransaction.RollbackAsync();
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[DocumentNumberCancellations]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseOrders] WHERE [DocumentNumberCancelledAtUtc] IS NOT NULL"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseAttempts] WHERE [CommandName] = N'" + PurchasingCommands.OrderCancelV1 + "'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] IN (N'settings.document-number.cancelled.v1', N'purchasing.purchase-order.remainder-cancelled.v1')"));
        await using (var scope = run.Store(CompanyA))
        {
            var cancelled = await scope.Store.MutateOrderAsync(run.Envelope("number-cancel"), Hash("number-cancel"),
                PurchasingCommands.OrderCancelV1, orderId,
                (order, now) => order.Cancel(order.Version, run.Actor, "SENTETIK numara boÅŸluÄŸu", now), OrderRemainderCancelled);
            Assert.Equal("ALI-2026-000001", cancelled!.Order.DocumentNumber);
            var replay = await scope.Store.MutateOrderAsync(run.Envelope("number-cancel"), Hash("number-cancel"),
                PurchasingCommands.OrderCancelV1, orderId, (_, _) => throw new InvalidOperationException("Cancel replay must not mutate."), OrderRemainderCancelled);
            Assert.Equal(PurchasingWriteOutcome.Replayed, replay!.Outcome);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[DocumentNumberCancellations]"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseOrders] WHERE [DocumentNumberCancelledAtUtc] IS NOT NULL"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseAttempts] WHERE [CommandName] = N'" + PurchasingCommands.OrderCancelV1 + "'"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] IN (N'settings.document-number.cancelled.v1', N'purchasing.purchase-order.remainder-cancelled.v1')"));
        await using var nextScope = run.Store(CompanyA);
        var owners = Owners.For(CompanyA);
        var next = Order(owners, null, null, 1m);
        var result = await nextScope.Store.StoreOrderAsync(run.Envelope("number-after-cancel"), Hash("number-after-cancel"),
            owners.Account, Day, () => next, VersionsOf(next));
        Assert.Equal("ALI-2026-000002", result.Order.DocumentNumber);
    }

    [SqlServerFact]
    public async Task Authority_lost_after_order_save_rolls_back_the_order_number_counter_and_both_audits_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var authority = new NumberTestAuthority();
        await using (var scope = run.Store(CompanyA, new NumberAuthorityRevocationInterceptor(authority), authority))
        {
            var order = Order(owners, null, null, 1m);
            var refused = await Assert.ThrowsAsync<PurchasingStoreException>(() => scope.Store.StoreOrderAsync(
                run.Envelope("number-revoked"), Hash("number-revoked"), owners.Account, Day, () => order, VersionsOf(order)));
            Assert.Equal(PurchasingStoreCodes.CompanyContextMismatch, refused.Code);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseOrders]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseAttempts]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[DocumentNumberCounters]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] IN ('settings.document-number.reserved.v1','purchasing.purchase-order.created.v1')"));
        await using var retry = run.Store(CompanyA);
        var next = Order(owners, null, null, 1m);
        var recorded = await retry.Store.StoreOrderAsync(run.Envelope("number-retry"), Hash("number-retry"), owners.Account, Day, () => next, VersionsOf(next));
        Assert.Equal("ALI-2026-000001", recorded.Order.DocumentNumber);
    }

    [SqlServerFact]
    public async Task Purchase_order_number_and_audit_commit_once_replay_without_renumbering_and_reset_annually_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseOrderDocumentNumbers().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await using (var scope = run.Store(CompanyA))
        {
            var order = Order(owners, null, null, 2m);
            var result = await scope.Store.StoreOrderAsync(run.Envelope("number-order"), Hash("number-order"), owners.Account, Day,
                () => order, VersionsOf(order));
            Assert.Equal("ALI-2026-000001", result.Order.DocumentNumber);
            var replay = await scope.Store.StoreOrderAsync(run.Envelope("number-order"), Hash("number-order"), owners.Account, Day,
                () => throw new InvalidOperationException("Replay must not create or number another order."), NoVersions);
            Assert.Equal((result.Order.Order.Id, result.Order.DocumentNumber, PurchasingWriteOutcome.Replayed),
                (replay.Order.Order.Id, replay.Order.DocumentNumber, replay.Outcome));
            Assert.Equal(result.Order.DocumentNumber, (await scope.Store.FindOrderAsync(order.Id))!.DocumentNumber);
        }
        await using (var scope = run.Store(CompanyA))
        {
            var order = Order(owners, null, null, 3m);
            var result = await scope.Store.StoreOrderAsync(run.Envelope("number-next-year"), Hash("number-next-year"), owners.Account,
                new DateOnly(2027, 1, 1), () => order, VersionsOf(order));
            Assert.Equal("ALI-2027-000001", result.Order.DocumentNumber);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseOrders] WHERE [DocumentNumber] IS NOT NULL"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'purchasing.purchase-order.created.v1'"));
        Assert.Equal(51957, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
    }

    [SqlServerFact]
    public async Task An_unconfigured_number_series_does_not_create_an_order_or_invent_a_prefix_on_real_sql()
    {
        await using var run = await Run.StartAsync(configureNumbers: false);
        var owners = Owners.For(CompanyA);
        await using var scope = run.Store(CompanyA);
        var order = Order(owners, null, null, 1m);
        var refusal = await Assert.ThrowsAsync<PurchasingStoreException>(() => scope.Store.StoreOrderAsync(
            run.Envelope("no-number-policy"), Hash("no-number-policy"), owners.Account, Day, () => order, VersionsOf(order)));
        Assert.Equal("purchasing_document_number_owner_unavailable", refusal.Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [purchasing].[PurchaseOrders]"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
    }

    // These SQL store facts use the real number owner and transaction; authorization itself remains explicitly synthetic.
    private sealed class NumberTestUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; }
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class NumberTestAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(Allowed, Allowed, new HashSet<string> { "SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class NumberAuthorityRevocationInterceptor(NumberTestAuthority authority) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            authority.Allowed = false;
            return ValueTask.FromResult(result);
        }
    }
}
