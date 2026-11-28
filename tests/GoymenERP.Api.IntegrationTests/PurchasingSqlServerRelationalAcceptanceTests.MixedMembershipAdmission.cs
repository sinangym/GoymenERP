using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    // Actual SQL native parent transaction/membership/immutable revisions/scoped Audit writer.
    // Only the existing projection callback and authority fence are controlled; not HTTP or complete business acceptance.
    [SqlServerFact]
    public async Task Mixed_membership_native_admission_records_one_parent_edit_and_replays_original_Draft_after_actual_later_approval()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        var request = AdmissionRequest(f); var envelope = AdmissionEnvelope(run, "membership-native-admit");
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        PurchaseMixedOrderMembershipAnswerV1 first;
        await using (var command = MixedApprovalScope(run))
        {
            first = await AdmitMembership(command, f, request, envelope);
            Assert.False(first.IsReplay); Assert.Equal(("Draft", "3"), (first.Receipt.Visible.Order.Status, first.Receipt.Visible.Order.Version));
            Assert.False(command.Writer.HasPendingDrafts); Assert.Null(command.Database.Database.CurrentTransaction);
        }
        Assert.Equal(before + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));
        Assert.Equal(audits + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
        await using (var restart = run.Store(CompanyA))
        {
            var restored = (await restart.Store.FindOrderAsync(f.Parent.Order.Id))!.Order;
            Assert.Equal((3L, PurchaseOrderStatus.Draft), (restored.Version, restored.Status));
            var membership = Assert.Single(restored.ServiceMemberships);
            Assert.Equal((f.Service.OrderId, 1L), (membership.ServiceOrderId, membership.PreparedVersion));
            Assert.Equal(f.Service.Lines.Select(x => x.LineId), membership.LineIds);
        }
        await using (var approval = MixedApprovalScope(run))
            _ = await MixedApprove(approval, f, MixedRequest(f), run.Envelope("membership-later-parent-approval", actor: OrderApprover));
        await using (var retry = MixedApprovalScope(run))
        {
            var original = await AdmitMembership(retry, f, request, envelope with { CorrelationId = "later-correlation" });
            Assert.True(original.IsReplay); Assert.Equal(first.Receipt.OriginalCorrelationId, original.Receipt.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(first.Receipt, PurchasingJson.Options), JsonSerializer.Serialize(original.Receipt, PurchasingJson.Options));
            Assert.Equal(("Draft", "3"), (original.Receipt.Visible.Order.Status, original.Receipt.Visible.Order.Version));
        }
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
    }

    [SqlServerFact]
    public async Task Mixed_membership_native_admission_rejects_stale_incomplete_foreign_and_already_bound_child_without_partial_parent_edit()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        var foreign = await MembershipSourcesAsync(run, CompanyB, functionalCurrency: true);
        var valid = AdmissionRequest(f);
        foreach (var (request, status) in new[] { (valid with { ExpectedVersion = "1" }, 409),
            (valid with { Services = [valid.Services[0] with { ExpectedVersion = "2" }] }, 409),
            (valid with { Services = [valid.Services[0] with { LineIds = [Guid.NewGuid()] }] }, 409),
            (valid with { Services = [new(foreign.Service.OrderId, "1", foreign.Service.Lines.Select(x => x.LineId).ToArray())] }, 503) })
        {
            await using var attempt = MixedApprovalScope(run);
            var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => AdmitMembership(attempt, f, request,
                AdmissionEnvelope(run, "membership-refused-" + Guid.NewGuid().ToString("N"))));
            Assert.Equal(status, error.Status);
            Assert.Empty(attempt.Database.ChangeTracker.Entries()); Assert.False(attempt.Writer.HasPendingDrafts);
        }
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships"));
        await using (var first = MixedApprovalScope(run)) _ = await AdmitMembership(first, f, valid, AdmissionEnvelope(run, "membership-first-parent"));
        var other = await MembershipSourcesAsync(run, CompanyA, sharedService: f.Service);
        await using var second = MixedApprovalScope(run);
        Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => AdmitMembership(second, other,
            AdmissionRequest(other), AdmissionEnvelope(run, "membership-second-parent")))).Status);
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", other.Parent.Order.Id)));
    }

    [SqlServerFact]
    public async Task Mixed_membership_accepted_save_fault_and_completed_authority_loss_abort_exact_owned_work_and_leave_key_available()
    {
        await using var run = await Run.StartAsync();
        foreach (var accepted in new[] { false, true })
        {
            var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true); var request = AdmissionRequest(f);
            var envelope = AdmissionEnvelope(run, "membership-abort-" + f.Parent.Order.Id.ToString("N")); var interceptor = new MembershipAcceptedSaveFailure();
            await using (var command = MixedApprovalScope(run, accepted ? interceptor : null))
            {
                var revoked = false;
                async Task Fence(CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    if (!accepted && await command.Database.OrderServiceMemberships.AsNoTracking().AnyAsync(x => x.OrderId == f.Parent.Order.Id, token)) revoked = true;
                    if (revoked) throw new PurchasingServiceException(403, "forbidden");
                }
                if (accepted) await Assert.ThrowsAsync<PurchasingRuleException>(() => AdmitMembership(command, f, request, envelope, Fence));
                else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => AdmitMembership(command, f, request, envelope, Fence))).Status);
                if (accepted) { Assert.True(interceptor.Fired); Assert.True(interceptor.AcceptedClean); } else Assert.True(revoked);
                Assert.Empty(command.Database.ChangeTracker.Entries()); Assert.False(command.Writer.HasPendingDrafts);
            }
            Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships WHERE OrderId=@id", ("@id", f.Parent.Order.Id)));
            await using var retry = MixedApprovalScope(run); Assert.False((await AdmitMembership(retry, f, request, envelope)).IsReplay);
        }
    }

    [SqlServerFact]
    public async Task Mixed_membership_first_completed_authority_read_cannot_adopt_or_clear_caller_work_or_borrow_a_transaction()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        await using (var command = MixedApprovalScope(run))
        {
            var caller = new PurchaseOrderRow { CompanyId = CompanyA, Id = Guid.NewGuid(), Version = 1 }; var added = false;
            Task Fence(CancellationToken token)
            { token.ThrowIfCancellationRequested(); if (!added) { command.Database.Orders.Add(caller); added = true; } return Task.CompletedTask; }
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => AdmitMembership(command, f, AdmissionRequest(f),
                AdmissionEnvelope(run, "membership-caller"), Fence))).Status);
            Assert.True(added); Assert.Equal(EntityState.Added, command.Database.Entry(caller).State); Assert.Null(command.Database.Database.CurrentTransaction);
        }
        await using (var command = MixedApprovalScope(run))
        {
            await using var borrowed = await command.Database.Database.BeginTransactionAsync();
            Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => AdmitMembership(command, f, AdmissionRequest(f),
                AdmissionEnvelope(run, "membership-borrowed")))).Status);
            Assert.Same(borrowed, command.Database.Database.CurrentTransaction);
        }
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
    }

    [SqlServerFact]
    public async Task Mixed_membership_genuine_receipt_rejects_tampered_audit_intent_identity_and_missing_prepared_revision_without_current_fallback()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        var request = AdmissionRequest(f); var envelope = AdmissionEnvelope(run, "membership-artifacts");
        await using (var command = MixedApprovalScope(run)) _ = await AdmitMembership(command, f, request, envelope);
        await using var read = MixedApprovalScope(run);
        var key = PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey);
        var attempt = await read.Database.Attempts.AsNoTracking().SingleAsync(x => x.CommandName == PurchaseMixedOrderMembershipV1.Command && x.IdempotencyKeyHash == key);
        var audit = await read.Database.Set<AuditEvent>().AsNoTracking().SingleAsync(x => x.EventType == PurchaseMixedOrderMembershipV1.Event && x.IdempotencyKeyHash == key);
        var members = await read.Database.OrderServiceMemberships.AsNoTracking().Where(x => x.OrderId == f.Parent.Order.Id).ToArrayAsync();
        var revisions = await read.Database.ServiceOrderRevisions.AsNoTracking().Where(x => x.OrderId == f.Service.OrderId).ToArrayAsync();
        var hash = PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, f.Parent.Order.Id, request);
        Assert.NotNull(PurchasingStore.RestoreMembershipReceipt(attempt, audit, members, revisions, f.Parent.Order.Id, request, envelope, hash));
        foreach (var fault in new[] { "unknown", "actor", "selection", "missingNative", "beforeStatus", "childVersion", "hidden" })
        {
            var after = JsonNode.Parse(audit.AfterJson)!.AsObject(); var actual = audit.ToIntegrityEnvelope();
            if (fault == "unknown") after["unknown"] = true;
            if (fault == "actor") after["membership"]!["actorId"] = "other";
            if (fault == "selection") after["membership"]!["frozenRequest"]!["services"]![0]!["expectedVersion"] = "2";
            if (fault == "missingNative") after.Remove("nativeOrder");
            if (fault == "beforeStatus") { var before = JsonNode.Parse(actual.BeforeJson)!; before["status"] = "Approved"; actual = actual with { BeforeJson = before.ToJsonString() }; }
            if (fault == "hidden") after["membership"]!["withheld"]!["order"]!["version"] = "4";
            if (fault == "childVersion") after["added"]![0]!["preparedVersion"] = 2;
            actual = actual with { AfterJson = after.ToJsonString() };
            var tampered = AuditEvent.Create(new(actual, AuditEventIntegrity.Compute(actual)));
            Assert.Null(PurchasingStore.RestoreMembershipReceipt(attempt, tampered, members, revisions, f.Parent.Order.Id, request, envelope, hash));
        }
        Assert.Null(PurchasingStore.RestoreMembershipReceipt(attempt, audit, members, [], f.Parent.Order.Id, request, envelope, hash));

    }
    [SqlServerFact]
    public async Task Mixed_membership_same_original_key_rejects_changed_content_or_actor_without_a_second_binding()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        var body = AdmissionRequest(f); var envelope = AdmissionEnvelope(run, "membership-exact-key");
        await using (var command = MixedApprovalScope(run)) _ = await AdmitMembership(command, f, body, envelope);
        foreach (var changeActor in new[] { false, true })
        {
            await using var retry = MixedApprovalScope(run);
            var request = changeActor ? body : body with { Services = [body.Services[0] with { ExpectedVersion = "2" }] };
            var metadata = changeActor ? envelope with { ActorId = OrderApprover } : envelope;
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch,
                (await Assert.ThrowsAsync<PurchasingStoreException>(() => AdmitMembership(retry, f, request, metadata))).Code);
            Assert.Empty(retry.Database.ChangeTracker.Entries()); Assert.False(retry.Writer.HasPendingDrafts);
        }
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderServiceMemberships WHERE OrderId=@id", ("@id", f.Parent.Order.Id)));
    }
    [SqlServerFact]
    public async Task Mixed_membership_postcommit_authority_loss_keeps_actual_binding_and_same_key_reconciles_original_answer()
    {
        await using var run = await Run.StartAsync(); var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true);
        var body = AdmissionRequest(f); var envelope = AdmissionEnvelope(run, "membership-postcommit"); var fired = false;
        await using (var command = MixedApprovalScope(run))
        {
            async Task Fence(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (command.Database.Database.CurrentTransaction is null
                    && await command.Database.OrderServiceMemberships.AsNoTracking().AnyAsync(x => x.OrderId == f.Parent.Order.Id, token))
                { fired = true; throw new PurchasingServiceException(403, "forbidden"); }
            }
            Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => AdmitMembership(command, f, body, envelope, Fence))).Status);
            Assert.True(fired); Assert.False(command.Writer.HasPendingDrafts);
        }
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
        await using var retry = MixedApprovalScope(run);
        var original = await AdmitMembership(retry, f, body, envelope);
        Assert.True(original.IsReplay); Assert.Equal("3", original.Receipt.Visible.Order.Version);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName=@cmd AND IdempotencyKeyHash=@key",
            ("@cmd", PurchaseMixedOrderMembershipV1.Command), ("@key", PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey))));
    }
    [SqlServerFact]
    public async Task Mixed_membership_controlled_1205_uses_one_readonly_original_key_pass_without_mutating_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var winner in new[] { false, true }) foreach (var wrapped in new[] { false, true }) foreach (var autoAborted in new[] { false, true })
        {
            var f = await MembershipSourcesAsync(run, CompanyA, functionalCurrency: true); var body = AdmissionRequest(f);
            var envelope = AdmissionEnvelope(run, "membership-controlled-deadlock-" + f.Parent.Order.Id.ToString("N"));
            if (winner) { await using var original = MixedApprovalScope(run); _ = await AdmitMembership(original, f, body, envelope); }
            await using var command = MixedApprovalScope(run); var fired = false;
            async Task Fence(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!fired && command.Database.Database.CurrentTransaction is not null && (winner || command.Writer.HasPendingDrafts))
                {
                    fired = true;
                    // Actual provider rollback followed by injected1205; NOT an actual simultaneous deadlock reproduction.
                    if (autoAborted) await command.Database.Database.CurrentTransaction.GetDbTransaction().RollbackAsync(token);
                    var failure = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (wrapped) throw new DbUpdateException("SENTETIK membership wrapped1205", failure);
                    throw failure;
                }
            }
            var task = AdmitMembership(command, f, body, envelope, Fence);
            if (winner) Assert.True((await task).IsReplay);
            else Assert.Equal(409, (await Assert.ThrowsAsync<PurchasingServiceException>(() => task)).Status);
            Assert.True(fired); Assert.Empty(command.Database.ChangeTracker.Entries()); Assert.False(command.Writer.HasPendingDrafts);
            Assert.Null(command.Database.Database.CurrentTransaction);
            Assert.Equal(winner ? 3L : 2L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", f.Parent.Order.Id)));
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE CompanyId=@c AND OrderId=@id", ("@c", CompanyA), ("@id", f.Service.OrderId)));
            Assert.Equal(winner ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName=@cmd AND IdempotencyKeyHash=@key",
                ("@cmd", PurchaseMixedOrderMembershipV1.Command), ("@key", PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey))));
        }
    }
    private static BindPurchaseOrderServicesRequest AdmissionRequest(MembershipSources f) => new("2",
        [new(f.Service.OrderId, "1", f.Service.Lines.Select(x => x.LineId).ToArray())]);
    private static PurchasingWriteEnvelope AdmissionEnvelope(Run run, string key) => run.Envelope(key) with { PermissionCode = ErpPermissions.PurchasingOrdersRevise };
    private static Task<PurchaseMixedOrderMembershipAnswerV1> AdmitMembership(MixedSqlScope command, MembershipSources f,
        BindPurchaseOrderServicesRequest request, PurchasingWriteEnvelope envelope, Func<CancellationToken, Task>? fence = null) =>
        command.Store.BindServicesAsync(f.Parent.Order.Id, request, envelope,
            PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, f.Parent.Order.Id, request), true, MixedSqlProjection, fence ?? (_ => Task.CompletedTask));
    private sealed class MembershipAcceptedSaveFailure : SaveChangesInterceptor
    {
        internal bool Fired; internal bool AcceptedClean;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (data.Context is PurchasingDbContext db && db.ChangeTracker.Entries<PurchaseAttemptRow>().Any(x => x.Entity.CommandName == PurchaseMixedOrderMembershipV1.Command))
            { Fired = true; AcceptedClean = !db.ChangeTracker.HasChanges(); throw new PurchasingRuleException("SENTETIK-accepted-membership-save", "SENTETIK after actual accepted SQL save."); }
            return ValueTask.FromResult(result);
        }
    }
}
