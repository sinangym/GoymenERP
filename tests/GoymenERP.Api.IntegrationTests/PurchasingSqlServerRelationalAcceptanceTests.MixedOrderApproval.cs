using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual SQL parent TX/native participant/real scoped Audit writer and genuine immutable artifacts.
    // Reference preparation and the projection callback are explicitly controlled inputs, not external owner/HTTP/UI acceptance.
    [SqlServerFact]
    public async Task Mixed_owned_approval_records_parent_and_child_once_and_restarts_only_from_genuine_original_receipt()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run);
        var attempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        var serviceAttempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        var request = MixedRequest(f); var envelope = run.Envelope("mixed-owned-approval", actor: OrderApprover);
        PurchaseMixedOrderApprovalStoreAnswerV1 result;
        await using (var scope = MixedApprovalScope(run))
        {
            result = await MixedApprove(scope, f, request, envelope);
            Assert.False(result.IsReplay); Assert.NotNull(result.Receipt);
            Assert.Equal((3L, 4L), (result.Receipt.ExpectedVersion, result.Receipt.ResultVersion));
            var child = Assert.Single(result.Receipt.Original.Children);
            Assert.Equal((1L, 2L, PurchaseServiceOrderStatus.Approved), (child.OriginalBefore.Version, child.After.Version, child.After.Status));
            Assert.False(scope.Writer.HasPendingDrafts); Assert.Null(scope.Database.Database.CurrentTransaction);
        }
        Assert.Equal(attempts + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(serviceAttempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"));
        Assert.Equal(audits + 2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        await using (var restart = run.Store(CompanyA))
        {
            var parent = (await restart.Store.FindOrderAsync(f.Parent.Order.Id))!.Order;
            Assert.Equal((4L, PurchaseOrderStatus.Approved, OrderApprover), (parent.Version, parent.Status, parent.Approval!.ApprovedBy));
            Assert.Equal(1L, Assert.Single(parent.ServiceMemberships).PreparedVersion);
        }
        await using (var child = ServiceScope(run, CompanyA))
            await child.Store.WriteAsync(f.Service.OrderId, PurchaseServiceOrderCommandsV1.RevisePrices,
                run.Envelope("mixed-later-child-native-revision"), Hash("mixed-later-child-native-revision"), "SENTETIK existing native price revision",
                (old, _) => Task.FromResult(PurchaseServiceOrder.RevisePrices(old!, 2,
                    new Dictionary<Guid, decimal> { [f.Service.Lines[0].LineId] = 3m }, run.Actor, "SENTETIK native revision", DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default);
        await using (var retry = MixedApprovalScope(run))
        {
            var replay = await retry.Store.ApproveMixedOrderAsync(f.Parent.Order.Id, request, envelope with { CorrelationId = "new-replay-correlation" },
                PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, f.Parent.Order.Id, request), true,
                (_, _, _) => throw new InvalidOperationException("Replay must not prepare a current child."), MixedSqlProjection, _ => Task.CompletedTask);
            Assert.True(replay.IsReplay); Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(result.Receipt, PurchasingJson.Options), JsonSerializer.Serialize(replay.Receipt, PurchasingJson.Options));
            Assert.Equal(2L, Assert.Single(replay.Receipt!.Original.Children).After.Version);
        }
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
        Assert.Equal(attempts + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
    }

    [SqlServerFact]
    public async Task Mixed_stale_or_incomplete_current_child_intent_and_new_standalone_child_approval_have_no_effects()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var valid = MixedRequest(f);
        var attempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        foreach (var request in new[] { valid with { MixedServices = [valid.MixedServices![0] with { ExpectedVersion = "2" }] },
            valid with { MixedServices = [valid.MixedServices![0] with { LineIds = [Guid.NewGuid()] }] } })
        {
            await using var scope = MixedApprovalScope(run);
            Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedApprove(scope, f, request,
                run.Envelope("mixed-stale-" + Guid.NewGuid().ToString("N"), actor: OrderApprover)))).Status);
            Assert.False(scope.Writer.HasPendingDrafts); Assert.Empty(scope.Database.ChangeTracker.Entries());
        }
        await using (var child = ServiceScope(run, CompanyA))
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => child.Store.WriteAsync(f.Service.OrderId,
                PurchaseServiceOrderCommandsV1.Approve, run.Envelope("mixed-no-standalone-child", actor: OrderApprover), Hash("mixed-no-standalone-child"),
                "SENTETIK standalone bypass", (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, OrderApprover, DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default))).Status);
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_completed_authority_revocation_and_accepted_save_domain_fault_roll_back_both_owners_and_leave_original_key_available()
    {
        await using var run = await Run.StartAsync();
        foreach (var afterAcceptedSave in new[] { false, true })
        {
            var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f); var envelope = run.Envelope("mixed-rollback-" + f.Parent.Order.Id.ToString("N"), actor: OrderApprover);
            var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
            var interceptor = new MixedAcceptedDomainFailure(); var fired = false;
            await using (var scope = MixedApprovalScope(run, afterAcceptedSave ? interceptor : null))
            {
                async Task Fence(CancellationToken ct)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!afterAcceptedSave && scope.Writer.HasPendingDrafts)
                    { fired = true; await Task.Yield(); throw new PurchasingServiceException(403, "forbidden"); }
                }
                var failure = await Record.ExceptionAsync(() => MixedApprove(scope, f, request, envelope, Fence));
                Assert.NotNull(failure);
                if (afterAcceptedSave) { Assert.IsType<PurchasingRuleException>(failure); Assert.True(interceptor.Fired); Assert.True(interceptor.AcceptedClean); }
                else { Assert.Equal(403, Assert.IsType<PurchasingServiceException>(failure).Status); Assert.True(fired); }
                Assert.False(scope.Writer.HasPendingDrafts); Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.Null(scope.Database.Database.CurrentTransaction);
            }
            Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
            Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
            await using var retry = MixedApprovalScope(run); Assert.False((await MixedApprove(retry, f, request, envelope)).IsReplay);
        }
    }

    [SqlServerFact]
    public async Task Mixed_refuses_caller_work_even_when_first_authority_await_completed_normally_without_clearing_it()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        foreach (var preexisting in new[] { false, true })
        {
            await using var scope = MixedApprovalScope(run); var caller = new PurchaseOrderRow { CompanyId = CompanyA, Id = Guid.NewGuid(), Version = 1 };
            if (preexisting) scope.Database.Orders.Add(caller);
            var calls = 0;
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedApprove(scope, f, request,
                run.Envelope("mixed-caller-" + Guid.NewGuid().ToString("N"), actor: OrderApprover), _ =>
                { calls++; scope.Database.Orders.Add(caller); return Task.CompletedTask; }))).Status);
            Assert.Equal(preexisting ? 0 : 1, calls); Assert.Same(caller, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            Assert.Equal(EntityState.Added, scope.Database.Entry(caller).State); Assert.False(scope.Writer.HasPendingDrafts);
        }
    }

    [SqlServerFact]
    public async Task Mixed_missing_or_tampered_original_child_revision_is_unknown_not_a_current_row_success()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        var envelope = run.Envelope("mixed-corrupt-artifact", actor: OrderApprover);
        await using (var scope = MixedApprovalScope(run)) _ = await MixedApprove(scope, f, request, envelope);
        // SQL fixture corruption only: immutable row guards are not disabled in the production model.
        await run.ExecuteAsync("UPDATE purchasing.ServiceOrderRevisions SET SnapshotJson=JSON_MODIFY(SnapshotJson,'$.approvedBy','SENTETIK-foreign') WHERE CompanyId=@c AND OrderId=@id AND Version=2",
            ("@c", CompanyA), ("@id", f.Service.OrderId));
        await using var retry = MixedApprovalScope(run);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedApprove(retry, f, request, envelope))).Status);
        Assert.False(retry.Writer.HasPendingDrafts); Assert.Null(retry.Database.Database.CurrentTransaction);
    }

    [SqlServerFact]
    public async Task Mixed_controlled_1205_has_one_readonly_original_key_pass_and_no_mutating_retry_including_already_aborted_provider_transaction()
    {
        await using var run = await Run.StartAsync();
        foreach (var winner in new[] { false, true }) foreach (var wrapped in new[] { false, true }) foreach (var autoAborted in new[] { false, true })
        {
            var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
            var envelope = run.Envelope("mixed-controlled-1205-" + f.Parent.Order.Id.ToString("N"), actor: OrderApprover);
            if (winner) { await using var first = MixedApprovalScope(run); _ = await MixedApprove(first, f, request, envelope); }
            await using var scope = MixedApprovalScope(run); var fired = false; var preparations = 0;
            async Task Fence(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (!fired && scope.Database.Database.CurrentTransaction is not null && (winner || scope.Writer.HasPendingDrafts))
                {
                    fired = true;
                    // Real provider rollback -> Connection null. The subsequent SQL 1205 is explicitly injected, not a simultaneous deadlock reproduction.
                    if (autoAborted) await scope.Database.Database.CurrentTransaction.GetDbTransaction().RollbackAsync(ct);
                    var sql = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (wrapped) throw new DbUpdateException("SENTETIK controlled wrapped deadlock", sql);
                    throw sql;
                }
            }
            var task = scope.Store.ApproveMixedOrderAsync(f.Parent.Order.Id, request, envelope,
                PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, f.Parent.Order.Id, request), true,
                (_, current, _) => { preparations++; return Task.FromResult(new PurchaseMixedOrderApprovalPreparationV1(current, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>())); },
                MixedSqlProjection, Fence);
            if (winner) { var replay = await task; Assert.True(replay.IsReplay); Assert.NotNull(replay.Receipt); }
            else Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => task)).Status);
            Assert.True(fired); Assert.Equal(winner ? 0 : 1, preparations); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.Null(scope.Database.Database.CurrentTransaction);
            Assert.Equal(winner ? 4L : 3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
            Assert.Equal(winner ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
        }
    }

    [SqlServerFact]
    public async Task Mixed_two_actual_competing_parent_transactions_have_one_approval_and_one_conflict_without_partial_child_effects()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        await using var left = MixedApprovalScope(run); await using var right = MixedApprovalScope(run);
        var tasks = new[] { MixedApprove(left, f, request, run.Envelope("mixed-race-left", actor: OrderApprover)),
            MixedApprove(right, f, request, run.Envelope("mixed-race-right", actor: OrderApprover)) };
        var results = await Task.WhenAll(tasks.Select(async task =>
        {
            try { return (Answer: (PurchaseMixedOrderApprovalStoreAnswerV1?)await task, Failure: (Exception?)null); }
            catch (Exception failure) { return (Answer: (PurchaseMixedOrderApprovalStoreAnswerV1?)null, Failure: failure); }
        }));
        Assert.Single(results, x => x.Answer is { IsReplay: false, Receipt: not null });
        var rejected = Assert.Single(results, x => x.Failure is not null).Failure;
        Assert.True(rejected is PurchasingVersionConflictException or PurchasingServiceException { Status: 409 }, rejected?.ToString());
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE OrderId=@id AND CommandName=@command",
            ("@id", f.Parent.Order.Id), ("@command", PurchasingCommands.OrderApproveV1)));
    }

    [SqlServerFact]
    public async Task Mixed_same_key_changed_child_intent_is_never_relabelled_as_the_original_approval()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        var envelope = run.Envelope("mixed-original-content", actor: OrderApprover);
        await using (var first = MixedApprovalScope(run)) _ = await MixedApprove(first, f, request, envelope);
        await using var retry = MixedApprovalScope(run);
        var changed = request with { MixedServices = [request.MixedServices![0] with { ExpectedVersion = "2" }] };
        Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch,
            (await Assert.ThrowsAsync<PurchasingStoreException>(() => MixedApprove(retry, f, changed, envelope))).Code);
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
    }

    [SqlServerFact]
    public async Task Mixed_receipt_binds_the_actual_attempt_integrity_audits_exact_revisions_and_original_complete_body()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        var envelope = run.Envelope("mixed-artifact-bindings", actor: OrderApprover);
        await using (var command = MixedApprovalScope(run)) _ = await MixedApprove(command, f, request, envelope);
        await using var read = MixedApprovalScope(run); var id = f.Parent.Order.Id; var entity = id.ToString("D");
        var attempt = await read.Database.Attempts.AsNoTracking().SingleAsync(x => x.OrderId == id && x.CommandName == PurchasingCommands.OrderApproveV1);
        var parentAudit = await read.Database.Set<AuditEvent>().AsNoTracking().SingleAsync(x => x.EntityId == entity && x.EventType == "purchasing.purchase-order.approved.v1");
        var childAudit = await read.Database.Set<AuditEvent>().AsNoTracking().SingleAsync(x => x.EntityId == f.Service.OrderId.ToString("D") && x.EventType == "purchasing.service-order.approve.recorded.v1");
        var revisions = await read.Database.ServiceOrderRevisions.AsNoTracking().Where(x => x.OrderId == f.Service.OrderId).OrderBy(x => x.Version).ToArrayAsync();
        var hash = PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, id, request);
        Assert.NotNull(PurchasingStore.RestoreMixedOrderReceipt(attempt, [parentAudit], revisions, [childAudit], id, request, envelope, hash));
        foreach (var fault in new[] { "actor", "version", "unknown", "prepared", "frozen", "hidden", "headerCompany", "time", "membership", "childCorrelation", "revision", "key", "beforeStatus", "frozenCanonicalHash" })
        {
            var root = JsonNode.Parse(parentAudit.AfterJson)!.AsObject(); var parentEnvelope = parentAudit.ToIntegrityEnvelope();
            var childEnvelope = childAudit.ToIntegrityEnvelope(); var requestRead = request; var copied = revisions.Select(x => new PurchaseServiceOrderRevisionRow {
                CompanyId = x.CompanyId, OrderId = x.OrderId, Version = x.Version, SnapshotJson = x.SnapshotJson }).ToArray();
            if (fault == "actor") parentEnvelope = parentEnvelope with { ActorId = run.Actor };
            if (fault == "version") parentEnvelope = parentEnvelope with { EntityVersionAfter = 5 };
            if (fault == "unknown") root["unknownReceipt"] = true;
            if (fault == "prepared") root["mixedApproval"]!["children"]![0]!["prepared"]!["partyVersion"] = "changed";
            if (fault == "frozen") root["mixedApproval"]!["frozenRequest"]!["mixedServices"]![0]!["expectedVersion"] = "2";
            if (fault == "hidden") root["mixedApproval"]!["withheld"]!["lines"]![0]!["productId"] = Guid.NewGuid().ToString("D");
            if (fault == "headerCompany") root["mixedApproval"]!["visible"]!["order"]!["companyId"] = CompanyB.ToString("D");
            if (fault == "time") root["nativeOrder"]!["approval"]!["approvedAtUtc"] = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
            if (fault == "membership") root["mixedApproval"]!["membership"] = new JsonArray();
            if (fault == "beforeStatus")
            {
                var before = JsonNode.Parse(parentEnvelope.BeforeJson)!; before["status"] = "Approved";
                parentEnvelope = parentEnvelope with { BeforeJson = before.ToJsonString() };
            }
            if (fault == "frozenCanonicalHash")
            {
                requestRead = request with { ExpectedVersion = "03" };
                root["mixedApproval"]!["frozenRequest"]!["expectedVersion"] = "03";
            }
            if (fault == "childCorrelation") childEnvelope = childEnvelope with { CorrelationId = "SENTETIK-other-command" };
            if (fault == "revision") copied[1].SnapshotJson = JsonSerializer.Serialize(f.Service with { Version = 2 }, PurchaseServiceOrderStoreV1.Json);
            parentEnvelope = parentEnvelope with { AfterJson = root.ToJsonString() };
            var changedParent = AuditEvent.Create(new(parentEnvelope, AuditEventIntegrity.Compute(parentEnvelope)));
            var changedChild = AuditEvent.Create(new(childEnvelope, AuditEventIntegrity.Compute(childEnvelope)));
            Assert.Null(PurchasingStore.RestoreMixedOrderReceipt(attempt, [changedParent], copied, [changedChild], id, requestRead,
                fault == "key" ? envelope with { IdempotencyKey = "SENTETIK-other-key" } : envelope, hash));
        }
        Assert.False(read.Writer.HasPendingDrafts); Assert.Empty(read.Database.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task Mixed_borrowed_transaction_and_caller_pending_audit_remain_owned_by_the_caller()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        foreach (var pending in new[] { false, true })
        {
            await using var scope = MixedApprovalScope(run); await using var tx = pending ? null : await scope.Database.Database.BeginTransactionAsync();
            if (pending) scope.Collector.Enqueue(new AuditEventDraft(CompanyA, "purchasing.synthetic-caller.v1", "Purchasing.SyntheticCaller", Guid.NewGuid().ToString("D"),
                "SENTETIK", null, 1, AuditActorTypes.User, run.Actor, "SENTETIK-purchasing-permission", [], "{}", "{}", "SENTETIK caller-owned draft", null,
                AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-caller-correlation", null, "SENTETIK-caller-key"));
            var fences = 0;
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedApprove(scope, f, request,
                run.Envelope("mixed-no-adoption", actor: OrderApprover), _ => { fences++; return Task.CompletedTask; }))).Status);
            Assert.Equal(0, fences); Assert.Equal(pending, scope.Writer.HasPendingDrafts);
            if (tx is not null) Assert.Same(tx, scope.Database.Database.CurrentTransaction);
            if (pending) scope.Writer.DiscardPending(); // Only this test's caller releases its own draft, after preservation assertions.
        }
    }

    [SqlServerFact]
    public async Task Mixed_cleanup_fault_is_visible_and_never_turns_an_aborted_command_into_readonly_or_mutating_retry()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var original = new InvalidOperationException("SENTETIK-original-authority-fault");
        MixedDiscardFailure? faultWriter = null;
        await using var scope = new MixedSqlScope(run, null, writer => faultWriter = new MixedDiscardFailure(writer));
        var fired = false;
        var failure = await Assert.ThrowsAsync<AggregateException>(() => MixedApprove(scope, f, MixedRequest(f),
            run.Envelope("mixed-cleanup-fault", actor: OrderApprover), _ =>
            { if (scope.Writer.HasPendingDrafts) { fired = true; throw original; } if (fired) throw new InvalidOperationException("Unexpected retry fence."); return Task.CompletedTask; }));
        Assert.Contains(original, failure.InnerExceptions); Assert.Equal(1, faultWriter!.Discards); Assert.True(fired);
        Assert.Contains(failure.InnerExceptions, x => x.Message == "SENTETIK-discard-fault");
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
    }

    [SqlServerFact]
    public async Task Mixed_postcommit_authority_loss_does_not_claim_rollback_and_fresh_same_key_reconciles_original_receipt()
    {
        await using var run = await Run.StartAsync(); var f = await MixedApprovalSourcesAsync(run); var request = MixedRequest(f);
        var envelope = run.Envelope("mixed-postcommit-loss", actor: OrderApprover); var fired = false;
        await using (var scope = MixedApprovalScope(run))
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedApprove(scope, f, request, envelope, _ =>
            {
                if (scope.Database.Database.CurrentTransaction is { } tx && tx.GetDbTransaction().Connection is null)
                { fired = true; throw new PurchasingServiceException(403, "forbidden"); }
                return Task.CompletedTask;
            }))).Status);
            Assert.True(fired); Assert.False(scope.Writer.HasPendingDrafts); Assert.Null(scope.Database.Database.CurrentTransaction);
        }
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        await using var retry = MixedApprovalScope(run);
        var reconciled = await MixedApprove(retry, f, request, envelope with { CorrelationId = "fresh-same-key-correlation" });
        Assert.True(reconciled.IsReplay); Assert.Equal(envelope.CorrelationId, reconciled.OriginalCorrelationId); Assert.Equal(2L, reconciled.Receipt!.Original.Children[0].After.Version);
    }

    private static async Task<MembershipSources> MixedApprovalSourcesAsync(Run run)
    { var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true); await StageMembershipFixtureAsync(run, f); return f; }
    private static ApprovePurchaseOrderRequest MixedRequest(MembershipSources f) => new("3") {
        MixedServices = [new(f.Service.OrderId, "1", f.Service.Lines.Select(x => x.LineId).ToArray())] };
    private static Task<PurchaseMixedOrderApprovalStoreAnswerV1> MixedApprove(MixedSqlScope scope, MembershipSources f,
        ApprovePurchaseOrderRequest request, PurchasingWriteEnvelope envelope, Func<CancellationToken, Task>? fence = null) =>
        scope.Store.ApproveMixedOrderAsync(f.Parent.Order.Id, request, envelope,
            PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, f.Parent.Order.Id, request), true,
            (_, current, _) => Task.FromResult(new PurchaseMixedOrderApprovalPreparationV1(current, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>())),
            MixedSqlProjection, fence ?? (_ => Task.CompletedTask));
    private static PurchaseOrderDetailView MixedSqlProjection(StoredPurchaseOrder stored, string disclosure) => new(
        new(stored.Order.Id, stored.Order.CompanyId, stored.Order.Status.ToString(), stored.Order.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stored.Order.SupplierPartyId, stored.CompanyAccountId, stored.Order.SourceRequestId, stored.EffectiveOn, stored.Order.CreatedAtUtc, stored.Order.UpdatedAtUtc, stored.Order.Lines.Count),
        stored.Order.Lines.Select((line, i) => new PurchaseOrderLineView(line.Id, i + 1, line.Kind.ToString(), line.ParentLineId, line.SourceRequestLineId,
            line.ProductId, stored.ProductVersionsByLineId[line.Id], line.UnitId, line.Quantity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "0", "4", "0", "4")).ToArray())
        { PriceDisclosure = disclosure, PricePending = disclosure == PurchasingPriceDisclosureV1.Visible ? true : null,
            Currency = new(stored.Order.Money!.CurrencyId, stored.Order.Money.CurrencyCode, stored.Order.Money.Scale) };
    private static MixedSqlScope MixedApprovalScope(Run run, IInterceptor? extra = null) => new(run, extra);
    private sealed class MixedSqlScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        public PurchasingDbContext Database { get; }
        public IAuditTransactionWriterV1 Writer { get; }
        public IAuditEventCollector Collector { get; }
        public PurchasingStore Store { get; }
        internal MixedSqlScope(Run run, IInterceptor? extra, Func<IAuditTransactionWriterV1, IAuditTransactionWriterV1>? decorate = null)
        {
            scope = run.Factory.Services.CreateAsyncScope(); var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var actualWriter = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            Writer = decorate?.Invoke(actualWriter) ?? actualWriter;
            Collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
            var options = new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema));
            if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, company); Store = new(Database, company, Collector, TimeProvider.System, decisionAuditWriter: Writer);
        }
        public async ValueTask DisposeAsync() { await Database.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class MixedDiscardFailure(IAuditTransactionWriterV1 inner) : IAuditTransactionWriterV1
    {
        internal int Discards;
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken ct = default) => inner.WritePendingAsync(transaction, ct);
        public void DiscardPending() { inner.DiscardPending(); Discards++; throw new InvalidOperationException("SENTETIK-discard-fault"); }
    }

    private sealed class MixedAcceptedDomainFailure : SaveChangesInterceptor
    {
        internal bool Fired; internal bool AcceptedClean;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (data.Context is PurchasingDbContext db && db.ChangeTracker.Entries<PurchaseAttemptRow>().Any(x => x.Entity.CommandName == PurchasingCommands.OrderApproveV1))
            { Fired = true; AcceptedClean = !db.ChangeTracker.HasChanges(); throw new PurchasingRuleException("SENTETIK-after-accepted-save", "SENTETIK native domain refusal after accepted SQL save."); }
            return ValueTask.FromResult(result);
        }
    }
}
