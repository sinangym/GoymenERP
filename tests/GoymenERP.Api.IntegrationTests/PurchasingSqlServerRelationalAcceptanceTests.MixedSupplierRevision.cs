using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual SQL owned transaction, shared real Audit writer, native immutable revisions and Attempts.
    // Reference preparation/projection/authority/allocation callback are controlled protocols here;
    // this is NOT HTTP, external Inventory release or complete business acceptance evidence.
    [SqlServerFact]
    public async Task Mixed_supplier_revision_atomically_clears_both_owners_prices_and_restores_Draft_without_reexecuting_native_commands()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var beforeAttempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        var beforeChildAttempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts");
        var beforeAudit = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        var body = SupplierRevisionRequest(f); var metadata = AdmissionEnvelope(run, "mixed-supplier-revision");
        PurchaseMixedSupplierRevisionAnswerV1 first;
        await using (var command = MixedApprovalScope(run))
        {
            first = await MixedReviseSupplier(command, f, body, metadata);
            Assert.False(first.IsReplay); Assert.NotNull(first.Receipt); Assert.Equal((4L, 5L), (first.Receipt.ExpectedVersion, first.Receipt.ResultVersion));
            Assert.Equal(("Draft", "5", RevisedSupplier, RevisedSupplierAccount),
                (first.Receipt.Visible.Order.Status, first.Receipt.Visible.Order.Version, first.Receipt.Visible.Order.SupplierPartyId, first.Receipt.Visible.Order.CompanyAccountId));
            var child = Assert.Single(first.Receipt.Children);
            Assert.Equal((2L, 3L, PurchaseServiceOrderStatus.Draft), (child.Original.Version, child.Revised.Version, child.Revised.Status));
            Assert.All(child.Revised.Lines, x => Assert.Null(x.UnitPrice)); Assert.Null(child.Revised.ApprovedBy);
            Assert.Equal(new PurchaseServiceSupplierRevisionOrigin(f.Parent.Order.Id, 5, PurchaseMixedOrderApprovalIntentV1.KeyHash(metadata.IdempotencyKey)), child.Revised.SupplierRevisionOrigin);
            Assert.DoesNotContain(command.Database.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            Assert.False(command.Writer.HasPendingDrafts);
        }
        Assert.Equal(beforeAttempts + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(beforeChildAttempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"));
        Assert.Equal(beforeAudit + 2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        await using var restart = MixedApprovalScope(run);
        var restored = (await restart.Store.FindOrderAsync(f.Parent.Order.Id))!;
        Assert.Equal((5L, PurchaseOrderStatus.Draft, RevisedSupplier, RevisedSupplierAccount),
            (restored.Order.Version, restored.Order.Status, restored.Order.SupplierPartyId, restored.CompanyAccountId));
        Assert.All(restored.Order.Lines, x => Assert.Null(x.Price)); Assert.Single(restored.Order.SupplierRevisions);
        var member = Assert.Single(restored.Order.ServiceMemberships);
        Assert.Equal((f.Service.SupplierPartyId, f.Service.CompanyAccountId, 1L), (member.SupplierPartyId, member.CompanyAccountId, member.PreparedVersion));
        Assert.Equal(beforeAudit + 2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_Draft_price_entry_and_later_reapproval_keep_original_revision_key_and_historical_restore()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run); var body = SupplierRevisionRequest(f);
        var metadata = AdmissionEnvelope(run, "mixed-supplier-original");
        PurchaseMixedSupplierRevisionAnswerV1 original;
        await using (var command = MixedApprovalScope(run)) original = await MixedReviseSupplier(command, f, body, metadata);
        await using (var child = ServiceScope(run, CompanyA))
        {
            var changed = await SetLinkedSupplierPrice(child, f, AdmissionEnvelope(run, "mixed-supplier-child-price"), 0m);
            Assert.Equal((4L, PurchaseServiceOrderStatus.Draft, run.Actor), (changed!.Order!.Version, changed.Order.Status, changed.Order.PreparedBy));
            Assert.Equal(0m, changed.Order.Lines[0].UnitPrice); Assert.Null(changed.Order.ApprovedBy);
        }
        await using (var price = run.Store(CompanyA))
            _ = await ((IPurchasingSupplierRevisionStoreV1)price.Store).SetRevisionPriceAsync(f.Parent.Order.Id, f.Parent.Order.Lines[0].Id, 5,
                new(3m, f.Parent.Order.Lines[0].UnitId), AdmissionEnvelope(run, "mixed-supplier-parent-price"), new string('C', 64), _ => Task.CompletedTask);
        await using (var approve = MixedApprovalScope(run))
        {
            var accepted = await MixedApprove(approve, f, new("6") { MixedServices = [new(f.Service.OrderId, "4", f.Service.Lines.Select(x => x.LineId).ToArray())] },
                run.Envelope("mixed-supplier-reapproved", actor: OrderApprover));
            Assert.Equal("7", accepted.Receipt!.Original.Visible.Order.Version); Assert.Equal(5L, accepted.Receipt.Original.Children[0].After.Version);
        }
        await using (var restart = MixedApprovalScope(run))
        {
            var restored = (await restart.Store.FindOrderAsync(f.Parent.Order.Id))!;
            Assert.Equal((7L, PurchaseOrderStatus.Approved, RevisedSupplier), (restored.Order.Version, restored.Order.Status, restored.Order.SupplierPartyId));
            Assert.Equal(1L, restored.Order.ServiceMemberships[0].PreparedVersion); Assert.Equal(3m, restored.Order.Lines[0].Price!.UnitPrice);
        }
        await using var retry = MixedApprovalScope(run);
        var answer = await MixedReviseSupplier(retry, f, body, metadata with { CorrelationId = "fresh-correlation" });
        Assert.True(answer.IsReplay); Assert.Equal(JsonSerializer.Serialize(original.Receipt, PurchasingJson.Options), JsonSerializer.Serialize(answer.Receipt, PurchasingJson.Options));
        Assert.Equal("5", answer.Receipt!.Visible.Order.Version); Assert.Equal(3L, answer.Receipt.Children[0].Revised.Version);
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_stale_selection_and_existing_child_invoice_refuse_without_partial_mutation()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run); var body = SupplierRevisionRequest(f);
        foreach (var fault in new[] { "version", "line", "foreign" })
        {
            var child = body.MixedServices![0];
            var changed = body with { MixedServices = [fault switch {
                "version" => child with { ExpectedVersion = "1" }, "line" => child with { LineIds = [Guid.NewGuid()] },
                _ => child with { ServiceOrderId = Guid.NewGuid() } }] };
            await using var command = MixedApprovalScope(run);
            Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(command, f, changed,
                AdmissionEnvelope(run, "mixed-supplier-stale-" + fault)))).Status);
            Assert.Empty(command.Database.ChangeTracker.Entries()); Assert.False(command.Writer.HasPendingDrafts);
        }
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
        // Actual native service acceptance + invoice matcher generate the FK-complete immutable binding.
        var acceptance = Guid.NewGuid(); PurchaseServiceOrderState accepted;
        await using (var child = ServiceScope(run, CompanyA))
            accepted = (await child.Store.WriteAsync(f.Service.OrderId, PurchaseServiceOrderCommandsV1.Accept,
                run.Envelope("mixed-revision-before-invoice"), Hash("mixed-revision-before-invoice"), "SENTETIK actual service acceptance",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, acceptance, f.Service.Lines[0].LineId, 4m, Day,
                    run.Actor, "SENTETIK actual acceptance", DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default))!.Order!;
        var invoice = ServiceInvoice(run, accepted); await StoreExpenseInvoiceAsync(run, invoice);
        await using (var matching = run.Store(CompanyA))
            _ = await InvoiceWorkflow(run, matching, new(_ => Task.CompletedTask), InvoiceMatchSource(matching, new())).MatchAsync(invoice.Id,
                new("1", [new(invoice.Lines[0].Id, accepted.OrderId, acceptance, "4")]),
                new("mixed-revision-service-invoice", run.Correlation, null, AcceptanceTime));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceInvoiceBindings WHERE OrderId=@id", ("@id", f.Service.OrderId)));
        await using var rejected = MixedApprovalScope(run);
        var billed = body with { MixedServices = [body.MixedServices![0] with { ExpectedVersion = "3" }] };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(rejected, f, billed,
            AdmissionEnvelope(run, "mixed-supplier-invoice")));
        Assert.Equal((409, PurchaseOrder.SupplierRevisionBlocked), (refusal.Status, refusal.Code));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_accepted_save_and_completed_late_authority_loss_roll_back_both_owners_and_original_key_can_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var accepted in new[] { false, true })
        {
            var f = await SupplierRevisionSourcesAsync(run); var body = SupplierRevisionRequest(f);
            var metadata = AdmissionEnvelope(run, "mixed-supplier-abort-" + f.Parent.Order.Id.ToString("N"));
            var interceptor = new MixedSupplierAcceptedFailure(); var fired = false;
            await using (var command = MixedApprovalScope(run, accepted ? interceptor : null))
            {
                Task Fence(CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    if (!accepted && command.Writer.HasPendingDrafts) { fired = true; throw new PurchasingServiceException(403, "forbidden"); }
                    return Task.CompletedTask;
                }
                if (accepted) await Assert.ThrowsAsync<PurchasingRuleException>(() => MixedReviseSupplier(command, f, body, metadata, Fence));
                else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(command, f, body, metadata, Fence))).Status);
                Assert.True(accepted ? interceptor.Fired && interceptor.Clean : fired);
                Assert.Empty(command.Database.ChangeTracker.Entries()); Assert.False(command.Writer.HasPendingDrafts);
            }
            Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
            Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
            await using var retry = MixedApprovalScope(run); Assert.False((await MixedReviseSupplier(retry, f, body, metadata)).IsReplay);
        }
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_original_artifacts_and_Draft_setter_never_accept_a_forged_or_missing_origin()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run); var metadata = AdmissionEnvelope(run, "mixed-supplier-corrupt-origin");
        await using (var command = MixedApprovalScope(run)) _ = await MixedReviseSupplier(command, f, SupplierRevisionRequest(f), metadata);
        string corrupt;
        await using (var read = ServiceScope(run, CompanyA))
        {
            var row = await read.Database.ServiceOrderRevisions.AsNoTracking().SingleAsync(x => x.OrderId == f.Service.OrderId && x.Version == 3);
            var native = JsonSerializer.Deserialize<PurchaseServiceOrderState>(row.SnapshotJson, PurchaseServiceOrderStoreV1.Json)!;
            corrupt = JsonSerializer.Serialize(native with { SupplierRevisionOrigin = native.SupplierRevisionOrigin! with { ParentCommandKeyHash = new string('0', 64) } }, PurchaseServiceOrderStoreV1.Json);
        }
        // Deliberate database-corruption fixture; normal EF writes reject immutable revision edits.
        await run.ExecuteAsync("UPDATE purchasing.ServiceOrderRevisions SET SnapshotJson=@json WHERE CompanyId=@c AND OrderId=@id AND Version=3",
            ("@json", corrupt), ("@c", CompanyA), ("@id", f.Service.OrderId));
        await using (var setter = ServiceScope(run, CompanyA))
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => SetLinkedSupplierPrice(setter, f,
                AdmissionEnvelope(run, "mixed-supplier-forged-price"), 1m))).Status);
        await using var retry = MixedApprovalScope(run);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(retry, f,
            SupplierRevisionRequest(f), metadata))).Status);
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_postcommit_authority_loss_keeps_effects_and_same_original_key_reconciles_without_second_mutation()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var body = SupplierRevisionRequest(f); var metadata = AdmissionEnvelope(run, "mixed-supplier-postcommit"); var fired = false;
        await using (var command = MixedApprovalScope(run))
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(command, f, body, metadata, _ => {
                if (command.Database.Database.CurrentTransaction is { } tx && tx.GetDbTransaction().Connection is null)
                { fired = true; throw new PurchasingServiceException(403, "forbidden"); }
                return Task.CompletedTask; }))).Status);
            Assert.True(fired); Assert.False(command.Writer.HasPendingDrafts);
        }
        await using var retry = MixedApprovalScope(run); var original = await MixedReviseSupplier(retry, f, body, metadata);
        Assert.True(original.IsReplay); Assert.Equal(5L, original.Receipt!.ResultVersion);
        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_first_await_caller_changes_and_borrowed_transaction_are_preserved_without_adoption()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        await using (var command = MixedApprovalScope(run))
        {
            var caller = new PurchaseOrderRow { CompanyId = CompanyA, Id = Guid.NewGuid(), Version = 1 }; var added = false;
            Task Fence(CancellationToken _) { if (!added) { added = true; command.Database.Orders.Add(caller); } return Task.CompletedTask; }
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(command, f, SupplierRevisionRequest(f),
                AdmissionEnvelope(run, "mixed-supplier-no-adopt"), Fence))).Status);
            Assert.Equal(EntityState.Added, command.Database.Entry(caller).State); Assert.Null(command.Database.Database.CurrentTransaction);
        }
        await using var borrowed = MixedApprovalScope(run); await using var tx = await borrowed.Database.Database.BeginTransactionAsync();
        var calls = 0;
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(borrowed, f, SupplierRevisionRequest(f),
            AdmissionEnvelope(run, "mixed-supplier-borrowed"), _ => { calls++; return Task.CompletedTask; }))).Status);
        Assert.Equal(0, calls); Assert.Same(tx, borrowed.Database.Database.CurrentTransaction);
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_controlled_1205_after_actual_provider_abort_has_one_readonly_original_lookup_and_no_mutating_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var winner in new[] { false, true })
        {
            var f = await SupplierRevisionSourcesAsync(run); var body = SupplierRevisionRequest(f);
            var metadata = AdmissionEnvelope(run, "mixed-supplier-deadlock-" + f.Parent.Order.Id.ToString("N"));
            if (winner) { await using var first = MixedApprovalScope(run); _ = await MixedReviseSupplier(first, f, body, metadata); }
            await using var command = MixedApprovalScope(run); var fired = false;
            async Task Fence(CancellationToken token)
            {
                if (!fired && command.Database.Database.CurrentTransaction is { } tx)
                {
                    fired = true; await tx.GetDbTransaction().RollbackAsync(token);
                    // Controlled injection after actual provider rollback, NOT a simultaneous SQL deadlock reproduction.
                    throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                }
            }
            if (winner) Assert.True((await MixedReviseSupplier(command, f, body, metadata, Fence)).IsReplay);
            else Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedReviseSupplier(command, f, body, metadata, Fence))).Status);
            Assert.True(fired); Assert.Empty(command.Database.ChangeTracker.Entries()); Assert.False(command.Writer.HasPendingDrafts);
            Assert.Equal(winner ? 5L : 4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        }
    }

    [SqlServerFact]
    public async Task Mixed_supplier_revision_original_key_preserves_native_terminal_refusal_and_rejects_changed_actor_or_content()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run); var body = SupplierRevisionRequest(f);
        var metadata = AdmissionEnvelope(run, "mixed-supplier-terminal-key");
        var hash = PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, f.Parent.Order.Id, body);
        await using (var refusal = run.Store(CompanyA))
            await refusal.Store.RecordRefusalAsync(metadata, hash, PurchasingCommands.OrderSupplierReviseV1,
                new(409, PurchaseOrder.SupplierRevisionBlocked, [new("order", PurchaseOrder.SupplierRevisionBlocked)]));
        await using (var retry = MixedApprovalScope(run))
        {
            var terminal = await MixedReviseSupplier(retry, f, body, metadata with { CorrelationId = "fresh-correlation" });
            Assert.True(terminal.IsReplay); Assert.Null(terminal.Receipt); Assert.Equal(metadata.CorrelationId, terminal.OriginalCorrelationId);
            Assert.Equal((409, PurchaseOrder.SupplierRevisionBlocked), (terminal.Refusal!.Status, terminal.Refusal.Code));
        }
        foreach (var actor in new[] { false, true })
        {
            await using var changed = MixedApprovalScope(run);
            var content = actor ? body : body with { Reason = "SENTETIK different frozen reason" };
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, (await Assert.ThrowsAsync<PurchasingStoreException>(() => MixedReviseSupplier(changed, f,
                content, actor ? metadata with { ActorId = OrderApprover } : metadata))).Code);
        }
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
    }

    private static readonly Guid RevisedSupplier = Guid.Parse("b9c7ba2a-f66a-4a0e-a5ea-cea4495fa001");
    private static readonly Guid RevisedSupplierAccount = Guid.Parse("b9c7ba2a-f66a-4a0e-a5ea-cea4495fa002");
    private static async Task<MembershipSources> SupplierRevisionSourcesAsync(Run run)
    {
        var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        await using (var bind = MixedApprovalScope(run)) _ = await AdmitMembership(bind, f, AdmissionRequest(f), AdmissionEnvelope(run, "mixed-revision-bind-" + f.Parent.Order.Id));
        await using (var approve = MixedApprovalScope(run)) _ = await MixedApprove(approve, f, MixedRequest(f), run.Envelope("mixed-revision-approve-" + f.Parent.Order.Id, actor: OrderApprover));
        return f;
    }
    private static RevisePurchaseOrderSupplierRequest SupplierRevisionRequest(MembershipSources f) => new("4", RevisedSupplier,
        RevisedSupplierAccount, "SENTETIK existing ALS-D115 supplier revision") {
            MixedServices = [new(f.Service.OrderId, "2", f.Service.Lines.Select(x => x.LineId).ToArray())] };
    private static Task<PurchaseMixedSupplierRevisionAnswerV1> MixedReviseSupplier(MixedSqlScope command, MembershipSources f,
        RevisePurchaseOrderSupplierRequest body, PurchasingWriteEnvelope metadata, Func<CancellationToken, Task>? fence = null) =>
        command.Store.ReviseMixedSupplierAsync(f.Parent.Order.Id, body, metadata,
            PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, f.Parent.Order.Id, body), true,
            (current, _) => Task.FromResult<IReadOnlyList<PurchaseServiceSupplierRevisionPreparationV1>>(current.Select(x =>
                new PurchaseServiceSupplierRevisionPreparationV1(CompanyA, metadata.ActorId, x, PurchaseServiceOrder.PrepareSupplierRevision(x,
                    x.Version, body.SupplierPartyId, body.CompanyAccountId, "1", "1", metadata.ActorId, body.Reason,
                    DateTimeOffset.UtcNow, PurchaseOrderInvoiceHistory.None))).ToArray()),
            MixedSqlProjection, fence ?? (_ => Task.CompletedTask), (_, _, _) => Task.CompletedTask);
    private static Task<PurchaseServiceOrderWriteV1?> SetLinkedSupplierPrice(ServiceSqlScope child, MembershipSources f, PurchasingWriteEnvelope metadata, decimal price)
    {
        var request = new RevisePurchaseServiceOrderPricesV1("3", [new(f.Service.Lines[0].LineId, price.ToString(CultureInfo.InvariantCulture))], "SENTETIK replacement price");
        var command = PurchaseServiceOrderCommandsV1.SetSupplierRevisionPrices;
        return child.Store.WriteAsync(f.Service.OrderId, command, metadata,
            PurchasingRequestHash.Compute(command, new { orderId = f.Service.OrderId, request }), request.Reason,
            (old, _) => Task.FromResult(PurchaseServiceOrder.SetSupplierRevisionPrices(old!, 3,
                new Dictionary<Guid, decimal> { [f.Service.Lines[0].LineId] = price }, metadata.ActorId, request.Reason, DateTimeOffset.UtcNow)),
            _ => Task.CompletedTask, CancellationToken.None);
    }
    private sealed class MixedSupplierAcceptedFailure : SaveChangesInterceptor
    {
        internal bool Fired, Clean;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (data.Context is PurchasingDbContext db && db.ChangeTracker.Entries<PurchaseAttemptRow>().Any(x => x.Entity.CommandName == PurchasingCommands.OrderSupplierReviseV1))
            { Fired = true; Clean = !db.ChangeTracker.HasChanges(); throw new PurchasingRuleException("SENTETIK accepted revision fault", "SENTETIK after actual SQL SaveChanges/AcceptAllChanges."); }
            return ValueTask.FromResult(result);
        }
    }
}
