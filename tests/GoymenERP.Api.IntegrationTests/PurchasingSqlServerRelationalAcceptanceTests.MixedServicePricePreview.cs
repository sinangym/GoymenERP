using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual read-only owner/SQL artifact path. Authority callbacks are controlled and no HTTP/first-use acceptance is claimed.
    [SqlServerFact]
    public async Task Mixed_Approved_price_read_uses_current_accepted_child_and_genuine_prior_approval_without_a_new_attempt_or_audit()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        await using (var child = ServiceScope(run, CompanyA))
            _ = await child.Store.WriteAsync(f.Service.OrderId, PurchaseServiceOrderCommandsV1.Accept,
                run.Envelope("price-preview-partial"), Hash("price-preview-partial"), "SENTETIK partial native service",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, Guid.NewGuid(), old!.Lines[0].LineId, 1m,
                    old.BusinessDate, run.Actor, "SENTETIK partial", DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default);
        var attempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        var childAttempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using var read = MixedApprovalScope(run); var fences = 0;
        var result = (await read.Store.ReadMixedServicePriceRevisionPreviewAsync(f.Parent.Order.Id, 4, ct => {
            ct.ThrowIfCancellationRequested(); fences++; return Task.CompletedTask; }))!;
        Assert.Equal(4L, result.Parent.Order.Version); Assert.Equal("Approved", result.Parent.Order.Status.ToString());
        var actual = Assert.Single(result.Services); Assert.Equal((3L, 1m), (actual.Version, actual.Lines[0].AcceptedQuantity));
        Assert.Single(actual.Acceptances); Assert.Equal(1L, result.Parent.Order.ServiceMemberships[0].PreparedVersion);
        Assert.True(fences > 8); Assert.Empty(read.Database.ChangeTracker.Entries()); Assert.False(read.Writer.HasPendingDrafts);
        Assert.Null(read.Database.Database.CurrentTransaction);
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(childAttempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        // The existing Draft approval read stays a separate purpose and cannot approve from an Approved price read.
        Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => read.Store.ReadMixedOrderApprovalPreviewAsync(f.Parent.Order.Id, 4,
            (_, _, _) => throw new InvalidOperationException("No Draft preparation is allowed here."), _ => Task.CompletedTask))).Status);
    }

    [SqlServerFact]
    public async Task Mixed_price_read_fences_every_owner_await_and_preserves_borrowed_transaction_or_caller_audit()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run); var boundaries = 0;
        await using (var baseline = MixedApprovalScope(run))
            _ = await baseline.Store.ReadMixedServicePriceRevisionPreviewAsync(f.Parent.Order.Id, 4, _ => { boundaries++; return Task.CompletedTask; });
        Assert.True(boundaries > 8);
        foreach (var cancel in new[] { false, true })
            for (var at = 1; at <= boundaries; at++)
            {
                await using var read = MixedApprovalScope(run); using var cancellation = new CancellationTokenSource(); var calls = 0;
                async Task Fence(CancellationToken ct)
                {
                    if (++calls != at) return;
                    await Task.Yield(); // A normally completed callback cannot release stale authority/cancellation.
                    if (cancel) cancellation.Cancel(); else throw new PurchasingServiceException(403, "forbidden");
                }
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.Store.ReadMixedServicePriceRevisionPreviewAsync(
                    f.Parent.Order.Id, 4, Fence, cancellation.Token));
                else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => read.Store.ReadMixedServicePriceRevisionPreviewAsync(
                    f.Parent.Order.Id, 4, Fence))).Status);
                Assert.Equal(at, calls); Assert.Empty(read.Database.ChangeTracker.Entries()); Assert.False(read.Writer.HasPendingDrafts);
                Assert.Null(read.Database.Database.CurrentTransaction);
            }
        foreach (var pending in new[] { false, true })
        {
            await using var caller = MixedApprovalScope(run); await using var tx = pending ? null : await caller.Database.Database.BeginTransactionAsync();
            if (pending) caller.Collector.Enqueue(new AuditEventDraft(CompanyA, "purchasing.synthetic-caller.v1", "Purchasing.SyntheticCaller", Guid.NewGuid().ToString("D"),
                "SENTETIK", null, 1, AuditActorTypes.User, run.Actor, "SENTETIK-permission", [], "{}", "{}", "SENTETIK caller", null,
                AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-caller-correlation", null, "SENTETIK-caller-key"));
            var callbacks = 0;
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => caller.Store.ReadMixedServicePriceRevisionPreviewAsync(
                f.Parent.Order.Id, 4, _ => { callbacks++; return Task.CompletedTask; }))).Status);
            Assert.Equal(0, callbacks); Assert.Equal(pending, caller.Writer.HasPendingDrafts);
            if (tx is not null) Assert.Same(tx, caller.Database.Database.CurrentTransaction);
            if (pending) caller.Writer.DiscardPending(); // Only this test's caller releases its draft after the preservation assertions.
        }
    }

    [SqlServerFact]
    public async Task Mixed_price_read_missing_original_approval_integrity_is_unavailable_not_a_fabricated_current_approval()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var count = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        await run.ExecuteAsync("UPDATE audit.AuditEvents SET IntegrityHash=@bad WHERE CompanyId=@c AND EventType='purchasing.purchase-order.approved.v1' AND EntityId=@id",
            ("@bad", new string('0', 64)), ("@c", CompanyA), ("@id", f.Parent.Order.Id.ToString("D")));
        await using var read = MixedApprovalScope(run);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => read.Store.ReadMixedServicePriceRevisionPreviewAsync(
            f.Parent.Order.Id, 4, _ => Task.CompletedTask))).Status);
        Assert.Empty(read.Database.ChangeTracker.Entries()); Assert.False(read.Writer.HasPendingDrafts);
        Assert.Equal(count, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
    }
}
