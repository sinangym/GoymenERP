using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual relational owned TX/shared Audit writer/immutable native Attempt+revision sources.
    // Reference preparation/price disclosure callbacks below are controlled protocols, not HTTP or full first-use acceptance.
    [SqlServerFact]
    public async Task Mixed_service_price_revision_preserves_partial_service_and_reapproves_only_changed_child_with_one_parent_decision()
    {
        await using var run = await Run.StartAsync(); var f = await PriceSiblingSourcesAsync(run);
        var originalApprovalKey = run.Envelope("mixed-price-initial-" + f.Source.Parent.Order.Id.ToString("N"), actor: OrderApprover);
        var childAttempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts");
        PurchaseServiceOrderState accepted;
        await using (var service = ServiceScope(run, CompanyA))
            accepted = (await service.Store.WriteAsync(f.Source.Service.OrderId, PurchaseServiceOrderCommandsV1.Accept,
                run.Envelope("mixed-price-partial-service"), Hash("mixed-price-partial-service"), "SENTETIK partial native service",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, Guid.NewGuid(), old!.Lines[0].LineId, 1m,
                    old.BusinessDate, run.Actor, "SENTETIK partial service", DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default))!.Order!;
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        var attempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        var body = SiblingPriceRequest(f, "4", accepted.Version.ToString(CultureInfo.InvariantCulture), "2", "0");
        var envelope = AdmissionEnvelope(run, "mixed-price-parent-native"); PurchaseMixedServicePriceAnswerV1 original;
        await using (var price = MixedApprovalScope(run)) original = await MixedPriceRevise(price, f.Source, body, envelope);
        var receipt = Assert.IsType<PurchaseMixedServicePriceReceiptV1>(original.Receipt);
        Assert.Equal((4L, 5L), (receipt.ExpectedVersion, receipt.ResultVersion));
        var changed = receipt.Children.Single(x => x.Before.OrderId == accepted.OrderId);
        var held = receipt.Children.Single(x => x.Before.OrderId == f.Second.OrderId);
        Assert.Equal((3L, 4L, 0m), (changed.Before.Version, changed.After.Version, changed.After.Lines[0].UnitPrice!.Value));
        Assert.Single(changed.After.Acceptances); Assert.Equal(1m, changed.After.Lines[0].AcceptedQuantity);
        Assert.True(PurchaseMixedOrderApprovalFactsV1.SameChild(held.Before, held.After));
        Assert.Equal(2L, held.After.Version); Assert.Equal(OrderApprover, held.After.ApprovedBy);
        Assert.Equal(attempts + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(childAttempts + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts")); // Only native acceptance.
        Assert.Equal(audits + 2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents")); // Parent + changed child, never held child.
        var approval = new ApprovePurchaseOrderRequest("5") { MixedServices = [
            new(accepted.OrderId, "4", accepted.Lines.Select(x => x.LineId).ToArray()),
            new(f.Second.OrderId, "2", f.Second.Lines.Select(x => x.LineId).ToArray())] };
        await using (var reapprove = MixedApprovalScope(run))
        {
            var result = await MixedApprove(reapprove, f.Source, approval, run.Envelope("mixed-price-one-reapproval", actor: OrderApprover));
            Assert.Equal((5L, 6L), (result.Receipt!.ExpectedVersion, result.Receipt.ResultVersion));
            Assert.Equal(5L, result.Receipt.Original.Children.Single(x => x.After.OrderId == accepted.OrderId).After.Version);
            Assert.Equal(2L, result.Receipt.Original.Children.Single(x => x.After.OrderId == f.Second.OrderId).After.Version);
            Assert.Equal(5L, result.Receipt.Original.HeldFromPriceRevisionVersion);
        }
        Assert.Equal(audits + 4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        await using (var restart = MixedApprovalScope(run))
        {
            var restored = (await restart.Store.FindOrderAsync(f.Source.Parent.Order.Id))!.Order;
            Assert.Equal((6L, PurchaseOrderStatus.Approved), (restored.Version, restored.Status));
            Assert.Equal(2, restored.ServiceMemberships.Count); Assert.All(restored.ServiceMemberships, x => Assert.Equal(1L, x.PreparedVersion));
            var replay = await MixedPriceRevise(restart, f.Source, body, envelope with { CorrelationId = "fresh-price-replay" });
            Assert.True(replay.IsReplay); Assert.Equal(envelope.CorrelationId, replay.Receipt!.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(original.Receipt, PurchasingJson.Options), JsonSerializer.Serialize(replay.Receipt, PurchasingJson.Options));
        }
        await using var oldApproval = MixedApprovalScope(run);
        var first = await MixedApprove(oldApproval, f.Source, InitialSiblingApproval(f), originalApprovalKey);
        Assert.True(first.IsReplay); Assert.Equal(4L, first.Receipt!.ResultVersion);
        Assert.All(first.Receipt.Original.Children, x => Assert.Equal(2L, x.After.Version));
    }

    [SqlServerFact]
    public async Task Mixed_Approved_parent_blocks_standalone_child_price_bypass_but_unbound_legacy_native_revision_remains_available()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var count = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var bound = ServiceScope(run, CompanyA))
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => NativePrice(bound, f.Service.OrderId, 2,
                run.Envelope("mixed-price-forbidden-bypass"), f.Service.Lines[0].LineId, 3m))).Status);
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(count, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        await using var legacy = ServiceScope(run, CompanyA); var draft = ServiceDraft(run) with { Money = f.Service.Money };
        await legacy.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("price-legacy-create"), Hash("price-legacy-create"),
            "SENTETIK unbound native", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        await legacy.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("price-legacy-approve", actor: OrderApprover), Hash("price-legacy-approve"),
            "SENTETIK native", (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, OrderApprover, DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default);
        var changed = await NativePrice(legacy, draft.OrderId, 2, run.Envelope("price-legacy-revise"), draft.Lines[0].LineId, 0m);
        Assert.Equal((3L, PurchaseServiceOrderStatus.Draft, 0m), (changed!.Order!.Version, changed.Order.Status, changed.Order.Lines[0].UnitPrice!.Value));
    }

    [SqlServerFact]
    public async Task Mixed_price_revision_rolls_back_changed_child_parent_and_attempt_after_accepted_save_or_completed_authority_loss()
    {
        await using var run = await Run.StartAsync();
        foreach (var afterSave in new[] { false, true })
        {
            var f = await SupplierRevisionSourcesAsync(run); var body = PriceRequest(f, "4", "2", "3");
            var envelope = AdmissionEnvelope(run, "mixed-price-abort-" + f.Parent.Order.Id.ToString("N"));
            var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"); var fired = false;
            var fault = new PriceAcceptedDomainFailure();
            await using (var scope = MixedApprovalScope(run, afterSave ? fault : null))
            {
                Task Fence(CancellationToken ct)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!afterSave && scope.Writer.HasPendingDrafts) { fired = true; throw new PurchasingServiceException(403, "forbidden"); }
                    return Task.CompletedTask;
                }
                var error = await Record.ExceptionAsync(() => MixedPriceRevise(scope, f, body, envelope, Fence));
                if (afterSave) { Assert.IsType<PurchasingRuleException>(error); Assert.True(fault.Fired); Assert.True(fault.AcceptedClean); }
                else { Assert.Equal(403, Assert.IsType<PurchasingServiceException>(error).Status); Assert.True(fired); }
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
                Assert.Null(scope.Database.Database.CurrentTransaction);
            }
            Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
            Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", f.Service.OrderId)));
            Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName=@cmd AND IdempotencyKeyHash=@key",
                ("@cmd", PurchaseMixedServicePriceIntentV1.Command), ("@key", PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey))));
            await using var retry = MixedApprovalScope(run); Assert.False((await MixedPriceRevise(retry, f, body, envelope)).IsReplay);
        }
    }

    [SqlServerFact]
    public async Task Mixed_price_old_key_reconciles_after_postcommit_authority_loss_and_rejects_changed_actor_content_or_corrupt_original_proof()
    {
        await using var run = await Run.StartAsync(); var f = await SupplierRevisionSourcesAsync(run);
        var body = PriceRequest(f, "4", "2", "3"); var envelope = AdmissionEnvelope(run, "mixed-price-unknown"); var fired = false;
        await using (var scope = MixedApprovalScope(run))
            Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPriceRevise(scope, f, body, envelope, _ => {
                if (scope.Database.Database.CurrentTransaction is { } tx && tx.GetDbTransaction().Connection is null)
                { fired = true; throw new PurchasingServiceException(403, "forbidden"); }
                return Task.CompletedTask;
            }))).Status);
        Assert.True(fired); Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        await using (var retry = MixedApprovalScope(run)) Assert.True((await MixedPriceRevise(retry, f, body, envelope)).IsReplay);
        foreach (var actor in new[] { false, true })
        {
            await using var changed = MixedApprovalScope(run);
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, (await Assert.ThrowsAsync<PurchasingStoreException>(() => MixedPriceRevise(changed, f,
                actor ? body : body with { Reason = "SENTETIK other content" }, actor ? envelope with { ActorId = OrderApprover } : envelope))).Code);
        }
        await run.ExecuteAsync("UPDATE audit.AuditEvents SET IntegrityHash=@bad WHERE CompanyId=@c AND EventType=@event AND EntityId=@id",
            ("@bad", new string('0', 64)), ("@c", CompanyA), ("@event", PurchaseMixedServicePriceIntentV1.Event), ("@id", f.Parent.Order.Id.ToString("D")));
        await using var corrupt = MixedApprovalScope(run);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPriceRevise(corrupt, f, body, envelope))).Status);
    }

    [SqlServerFact]
    public async Task Mixed_price_precommit_controlled_1205_performs_only_one_original_readonly_pass_and_preserves_terminal_refusal()
    {
        await using var run = await Run.StartAsync();
        foreach (var winner in new[] { false, true })
        {
            var f = await SupplierRevisionSourcesAsync(run); var body = PriceRequest(f, "4", "2", "3");
            var envelope = AdmissionEnvelope(run, "mixed-price-1205-" + f.Parent.Order.Id.ToString("N"));
            if (winner) { await using var first = MixedApprovalScope(run); _ = await MixedPriceRevise(first, f, body, envelope); }
            await using var command = MixedApprovalScope(run); var fired = false;
            async Task Fence(CancellationToken ct)
            {
                if (!fired && command.Database.Database.CurrentTransaction is { } tx)
                { fired = true; await tx.GetDbTransaction().RollbackAsync(ct); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
            }
            if (winner) Assert.True((await MixedPriceRevise(command, f, body, envelope, Fence)).IsReplay);
            else Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPriceRevise(command, f, body, envelope, Fence))).Status);
            Assert.True(fired); Assert.Empty(command.Database.ChangeTracker.Entries()); Assert.False(command.Writer.HasPendingDrafts);
            Assert.Equal(winner ? 5L : 4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        }
        // Controlled error after an actual provider rollback; NOT a simultaneous deadlock reproduction.
        var original = await SupplierRevisionSourcesAsync(run); var request = PriceRequest(original, "4", "2", "3");
        var metadata = AdmissionEnvelope(run, "mixed-price-terminal");
        await using (var record = run.Store(CompanyA)) await record.Store.RecordRefusalAsync(metadata,
            PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, original.Parent.Order.Id, request), PurchaseMixedServicePriceIntentV1.Command,
            new(409, PurchasingProblemCodesV1.VersionConflict, [new("order", "version_conflict")]));
        await using var replay = MixedApprovalScope(run); var refusal = await MixedPriceRevise(replay, original, request, metadata);
        Assert.True(refusal.IsReplay); Assert.Null(refusal.Receipt); Assert.Equal(409, refusal.Refusal!.Status);
        Assert.Equal(metadata.CorrelationId, refusal.OriginalCorrelationId);
    }

    [SqlServerFact]
    public async Task Mixed_price_Draft_parent_preserves_native_held_sibling_revision_and_reapproval_freezes_actual_current_child_again()
    {
        await using var run = await Run.StartAsync(); var f = await PriceSiblingSourcesAsync(run);
        var body = SiblingPriceRequest(f, "4", "2", "2", "3");
        await using (var price = MixedApprovalScope(run)) _ = await MixedPriceRevise(price, f.Source, body, AdmissionEnvelope(run, "mixed-price-first-child"));
        await using (var second = ServiceScope(run, CompanyA))
            Assert.Equal(3L, (await NativePrice(second, f.Second.OrderId, 2, run.Envelope("mixed-price-existing-held-edit"), f.Second.Lines[0].LineId, 4m))!.Order!.Version);
        var selected = new ApprovePurchaseOrderRequest("5") { MixedServices = [new(f.Source.Service.OrderId, "3", f.Source.Service.Lines.Select(x => x.LineId).ToArray()),
            new(f.Second.OrderId, "3", f.Second.Lines.Select(x => x.LineId).ToArray())] };
        await using (var approve = MixedApprovalScope(run))
        {
            var result = await MixedApprove(approve, f.Source, selected, run.Envelope("mixed-price-both-native-reapprove", actor: OrderApprover));
            Assert.All(result.Receipt!.Original.Children, x => Assert.Equal(4L, x.After.Version));
        }
        await using var restart = MixedApprovalScope(run); Assert.Equal(6L, (await restart.Store.FindOrderAsync(f.Source.Parent.Order.Id))!.Order.Version);
    }

    private static RevisePurchaseMixedServicePricesRequest PriceRequest(MembershipSources f, string parent, string child, string price) =>
        new(parent, [new(f.Service.OrderId, child, f.Service.Lines.Select(x => x.LineId).ToArray())],
            [new(f.Service.OrderId, [new(f.Service.Lines[0].LineId, price)])], "SENTETIK ALS-D9/D18 parent price revision");
    private static Task<PurchaseMixedServicePriceAnswerV1> MixedPriceRevise(MixedSqlScope scope, MembershipSources f,
        RevisePurchaseMixedServicePricesRequest body, PurchasingWriteEnvelope envelope, Func<CancellationToken, Task>? fence = null) =>
        scope.Store.ReviseMixedServicePricesAsync(f.Parent.Order.Id, body, envelope,
            PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, f.Parent.Order.Id, body), true, MixedSqlProjection, fence ?? (_ => Task.CompletedTask));
    private static Task<PurchaseServiceOrderWriteV1?> NativePrice(ServiceSqlScope scope, Guid id, long version,
        PurchasingWriteEnvelope envelope, Guid lineId, decimal price) => scope.Store.WriteAsync(id, PurchaseServiceOrderCommandsV1.RevisePrices, envelope,
            Hash(envelope.IdempotencyKey), "SENTETIK existing native price", (old, _) => Task.FromResult(PurchaseServiceOrder.RevisePrices(old!, version,
                new Dictionary<Guid, decimal> { [lineId] = price }, envelope.ActorId, "SENTETIK native price", DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default);
    private sealed record PriceSqlSiblings(MembershipSources Source, PurchaseServiceOrderState Second);
    private static ApprovePurchaseOrderRequest InitialSiblingApproval(PriceSqlSiblings f) => new("3") { MixedServices = [
        new(f.Source.Service.OrderId, "1", f.Source.Service.Lines.Select(x => x.LineId).ToArray()),
        new(f.Second.OrderId, "1", f.Second.Lines.Select(x => x.LineId).ToArray())] };
    private static RevisePurchaseMixedServicePricesRequest SiblingPriceRequest(PriceSqlSiblings f, string parent, string first, string second, string price) =>
        new(parent, [new(f.Source.Service.OrderId, first, f.Source.Service.Lines.Select(x => x.LineId).ToArray()),
            new(f.Second.OrderId, second, f.Second.Lines.Select(x => x.LineId).ToArray())],
            [new(f.Source.Service.OrderId, [new(f.Source.Service.Lines[0].LineId, price)])], "SENTETIK one changed service price");
    private static async Task<PriceSqlSiblings> PriceSiblingSourcesAsync(Run run)
    {
        var source = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true); PurchaseServiceOrderState extra;
        await using (var service = ServiceScope(run, CompanyA))
        {
            var draft = ServiceDraft(run) with { Money = source.Service.Money, SupplierPartyId = source.Service.SupplierPartyId, CompanyAccountId = source.Service.CompanyAccountId };
            extra = (await service.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("mixed-price-extra-" + draft.OrderId.ToString("N")),
                Hash("mixed-price-extra-" + draft.OrderId.ToString("N")), "SENTETIK second native service", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default))!.Order!;
        }
        var f = new PriceSqlSiblings(source, extra);
        var bind = new BindPurchaseOrderServicesRequest("2", [new(source.Service.OrderId, "1", source.Service.Lines.Select(x => x.LineId).ToArray()),
            new(extra.OrderId, "1", extra.Lines.Select(x => x.LineId).ToArray())]);
        await using (var admission = MixedApprovalScope(run))
            _ = await admission.Store.BindServicesAsync(source.Parent.Order.Id, bind, AdmissionEnvelope(run, "mixed-price-bind-" + source.Parent.Order.Id.ToString("N")),
                PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, source.Parent.Order.Id, bind), true, MixedSqlProjection, _ => Task.CompletedTask);
        await using (var approve = MixedApprovalScope(run)) _ = await MixedApprove(approve, source, InitialSiblingApproval(f),
            run.Envelope("mixed-price-initial-" + source.Parent.Order.Id.ToString("N"), actor: OrderApprover));
        return f;
    }
    private sealed class PriceAcceptedDomainFailure : SaveChangesInterceptor
    {
        internal bool Fired; internal bool AcceptedClean;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (data.Context is PurchasingDbContext db && db.ChangeTracker.Entries<PurchaseAttemptRow>().Any(x => x.Entity.CommandName == PurchaseMixedServicePriceIntentV1.Command))
            { Fired = true; AcceptedClean = !db.ChangeTracker.HasChanges(); throw new PurchasingRuleException("SENTETIK-price-accepted-save", "SENTETIK after actual accepted save"); }
            return ValueTask.FromResult(result);
        }
    }
}
