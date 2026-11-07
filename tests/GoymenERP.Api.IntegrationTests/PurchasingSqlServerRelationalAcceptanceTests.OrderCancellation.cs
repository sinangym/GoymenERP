using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A1-3 (plan API-10, TEK-09; No.161 §2) on real SQL Server: after a partial receipt the cancellation of an approved order's whole
/// unaccepted remainder is stored in ONE save (header facts, line shares, attempt, audit) and the same key replays; the remainder
/// leaves the future supply on both Purchasing read ports (INV-006) while the accepted share, the stock receipt and the request
/// binding stay; no further receipt is staged on a cancelled order; the checks hold the facts and the shares on a direct write; a
/// cancelled order keeps counting in its request line's Σ-shares (ALS-D68: a new request is needed) and no longer blocks the request's
/// rejection (No.161 §1); company B neither sees nor cancels company A's order; the migration's Down refuses while a cancelled order
/// exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly PurchasingMutationAudit OrderRemainderCancelled =
        new PurchasingMutationAudit("purchasing.purchase-order.remainder-cancelled.v1", "Cancel", "SENTETIK sipariş kalanı iptal edildi");

    [SqlServerFact]
    public async Task After_a_partial_receipt_the_whole_remainder_is_cancelled_in_one_save_and_leaves_the_future_supply_only()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 100m);
        var (otherOrderId, _, _) = await ApprovedAcceptanceOrderAsync(run, 30m);
        var seed = new AcceptanceSeed(orderId, lineId, AcceptanceUnit, approvedVersion);
        PurchaseAcceptanceStageResultV1 accepted = await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, Guid.NewGuid(), "40"),
            Envelope($"cancel-accept-{orderId:N}", $"correlation-cancel-accept-{orderId:N}"));
        Assert.Equal(("4", "60"), (accepted.PurchaseOrderVersion, accepted.RemainingQuantity));
        int acceptanceAuditsBefore = await ExactAcceptanceAuditCountAsync(run, orderId);

        await using (StoreScope a = run.Store(CompanyA))
        {
            // A stale version (the approval's, before the receipt) is refused and writes nothing.
            await Assert.ThrowsAsync<PurchasingVersionConflictException>(() => a.Store.MutateOrderAsync(run.Envelope("k-stale"), Hash("stale"),
                PurchasingCommands.OrderCancelV1, orderId, (order, now) => order.Cancel(approvedVersion, run.Actor, "SENTETIK", now), OrderRemainderCancelled));
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            var cancelled = await a.Store.MutateOrderAsync(run.Envelope("k-cancel"), Hash("k-cancel"), PurchasingCommands.OrderCancelV1, orderId,
                (order, now) => order.Cancel(4, run.Actor, "SENTETIK tedarikçi kalanı gönderemiyor", now), OrderRemainderCancelled);
            Assert.Equal((PurchaseOrderStatus.Cancelled, 5L, 40m, 60m, 0m), (cancelled!.Order.Order.Status, cancelled.Order.Order.Version,
                cancelled.Order.Order.Lines[0].AcceptedQuantity.Value, cancelled.Order.Order.Lines[0].CancelledQuantity.Value,
                cancelled.Order.Order.Lines[0].RemainingQuantity.Value));
            var replay = await a.Store.MutateOrderAsync(run.Envelope("k-cancel", correlation: "SENTETIK-second"), Hash("k-cancel"),
                PurchasingCommands.OrderCancelV1, orderId, (_, _) => throw new InvalidOperationException("a replay never mutates"), OrderRemainderCancelled);
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation, 5L), (replay!.Outcome, replay.OriginalCorrelationId, replay.Order.Order.Version));
        }

        // One save: the header facts, the line's cancelled share, one attempt and one audit; the accepted share and its audit stay.
        Assert.Equal((3, run.Actor, "SENTETIK tedarikçi kalanı gönderemiyor", 5L),
            (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
                await run.ScalarAsync<string>("SELECT CancelledBy FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
                await run.ScalarAsync<string>("SELECT CancellationReason FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
                await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId))));
        Assert.Equal((100m, 0m, 40m, 60m),
            (await run.ScalarAsync<decimal>("SELECT Ordered FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
                await run.ScalarAsync<decimal>("SELECT Technical FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
                await run.ScalarAsync<decimal>("SELECT Accepted FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
                await run.ScalarAsync<decimal>("SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command AND OrderId = @id",
            ("@command", PurchasingCommands.OrderCancelV1), ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.remainder-cancelled.v1' AND Operation = N'Cancel' "
            + "AND ActorId = @actor AND EntityId = @id AND EntityVersionBefore = 4 AND EntityVersionAfter = 5",
            ("@actor", run.Actor), ("@id", orderId.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(acceptanceAuditsBefore, await ExactAcceptanceAuditCountAsync(run, orderId));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Rebuilt through the domain (lines, approval, the acceptance, the cancellation): the same version, facts and shares.
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((PurchaseOrderStatus.Cancelled, 5L, run.Actor, 60m, 0m), (rebuilt.Status, rebuilt.Version, rebuilt.Cancellation!.CancelledBy,
                rebuilt.Lines[0].CancelledQuantity.Value, rebuilt.Lines[0].RemainingQuantity.Value));
            Assert.Equal(orderId, Assert.Single((await a.Store.ListOrdersAsync(10, null, PurchaseOrderStatus.Cancelled)).Items).Order.Id);

            // INV-006: the supply reference port answers no supply for the cancelled order ("Cancelled"), the other order keeps its own.
            var supply = new PurchaseSupplyReferencePort(a.Store);
            var answer = await supply.GetApprovedLineAsync(orderId, lineId);
            Assert.Equal((PurchaseSupplyReferenceOutcomeV1.NotApproved, "Cancelled"), (answer.Outcome, answer.Status));
            Assert.Null(answer.Line);
        }

        // The open-line port (Lojistik/Stok readers) lists the other approved order's line and nothing of the cancelled one.
        CompanyContext company = new CompanyContext();
        company.Set(CompanyA);
        await using (PurchasingDbContext context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
                         .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
                         .Options, company))
        {
            var open = await new PurchaseOpenLineReadPort(context, company).ListOpenApprovedLinesAsync(100, null);
            Assert.DoesNotContain(open.Items, item => item.OrderId == orderId);
            Assert.Equal("30", Assert.Single(open.Items, item => item.OrderId == otherOrderId).RemainingText);
        }

        // No receipt is staged on a cancelled order (the participant's state refusal; nothing is written).
        await AssertRefusedAsync(run, CompanyA, AcceptanceRequest(seed with { Version = 5 }, Guid.NewGuid(), "1"),
            Envelope($"cancel-accept-after-{orderId:N}", $"correlation-cancel-accept-after-{orderId:N}"),
            PurchaseAcceptanceParticipantCodes.OrderStateConflict);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId = @id", ("@id", orderId)));

        // The checks refuse what the domain refuses, on a direct write too (547): a cancelled share over the commitment, a cancelled
        // order without its canceller, a blank reason, and cancellation facts on a non-cancelled order.
        foreach (var (sql, check) in new[]
                 {
                     ("UPDATE purchasing.PurchaseOrderLines SET Cancelled = 61 WHERE OrderId = @id", "CK_PurchaseOrderLines_Shares"),
                     ("UPDATE purchasing.PurchaseOrderLines SET Cancelled = -1 WHERE OrderId = @id", "CK_PurchaseOrderLines_Shares"),
                     ("UPDATE purchasing.PurchaseOrders SET CancelledBy = NULL WHERE Id = @id", "CK_PurchaseOrders_Cancellation"),
                     ("UPDATE purchasing.PurchaseOrders SET CancellationReason = N'   ' WHERE Id = @id", "CK_PurchaseOrders_Cancellation"),
                     ("UPDATE purchasing.PurchaseOrders SET Status = 2 WHERE Id = @id", "CK_PurchaseOrders_Cancellation"),
                 })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", orderId)));
            Assert.Equal(547, refused.Number);
            Assert.Contains(check, refused.Message, StringComparison.Ordinal);
        }

        var facts = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrders SET CancelledBy = N'SENTETIK' WHERE Id = @id", ("@id", otherOrderId)));
        Assert.Contains("CK_PurchaseOrders_Cancellation", facts.Message, StringComparison.Ordinal);
        var unknown = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrders SET Status = 4 WHERE Id = @id", ("@id", otherOrderId)));
        Assert.Contains("CK_PurchaseOrders_Status", unknown.Message, StringComparison.Ordinal);
        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
    }

    [SqlServerFact]
    public async Task A_draft_and_a_fully_accepted_order_are_refused_and_nothing_is_written()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid draftId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = Order(owners, null, null, 5m);
            draftId = (await a.Store.StoreOrderAsync(run.Envelope("k-draft"), Hash("draft"), owners.Account, Day, () => built, VersionsOf(built))).Order.Order.Id;
            var draft = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateOrderAsync(run.Envelope("k-cancel-draft"), Hash("cancel-draft"),
                PurchasingCommands.OrderCancelV1, draftId, (order, now) => order.Cancel(2, run.Actor, "SENTETIK", now), OrderRemainderCancelled));
            Assert.Equal(PurchasingRuleCodes.OrderNotCancellable, draft.Code);
        }

        var (fullId, fullLine, fullVersion) = await ApprovedAcceptanceOrderAsync(run, 10m);
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(new AcceptanceSeed(fullId, fullLine, AcceptanceUnit, fullVersion), Guid.NewGuid(), "10"),
            Envelope($"cancel-full-{fullId:N}", $"correlation-cancel-full-{fullId:N}"));
        await using (StoreScope a = run.Store(CompanyA))
        {
            var full = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateOrderAsync(run.Envelope("k-cancel-full"), Hash("cancel-full"),
                PurchasingCommands.OrderCancelV1, fullId, (order, now) => order.Cancel(4, run.Actor, "SENTETIK", now), OrderRemainderCancelled));
            Assert.Equal(PurchasingRuleCodes.OrderNothingToCancel, full.Code);
        }

        Assert.Equal((1, 2), (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", draftId)),
            await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", fullId))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command",
            ("@command", PurchasingCommands.OrderCancelV1)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.remainder-cancelled.v1'"));
    }

    [SqlServerFact]
    public async Task A_cancelled_order_keeps_its_share_of_the_request_line_and_no_longer_blocks_the_requests_rejection()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId = await SubmittedRequestAsync(run, owners, 100m);
        Guid first;
        Guid second;
        await using (StoreScope a = run.Store(CompanyA))
        {
            await ApproveRequestAsync(run, a, requestId, 3);
            Guid requestLine = (await a.Store.FindRequestAsync(requestId))!.Request.Lines[0].Id;
            PurchaseOrder builtFirst = Order(owners, requestId, requestLine, 60m);
            first = (await a.Store.StoreOrderAsync(run.Envelope("k-first"), Hash("first"), owners.Account, Day, () => builtFirst, VersionsOf(builtFirst)))
                .Order.Order.Id;
            PurchaseOrder builtSecond = Order(owners, requestId, requestLine, 50m);
            second = (await a.Store.StoreOrderAsync(run.Envelope("k-second"), Hash("second"), owners.Account, Day, () => builtSecond, VersionsOf(builtSecond)))
                .Order.Order.Id;
            await a.Store.MutateOrderAsync(run.Envelope("k-first-approve", actor: Manager), Hash("first-approve"), PurchasingCommands.OrderApproveV1, first,
                (order, now) => order.Approve(2, now), Approved);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            // No.161 §1: the bound approved order blocks the rejection until it is cancelled.
            var blocked = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-reject-1", actor: Manager),
                Hash("reject-1"), PurchasingCommands.RequestRejectV1, requestId,
                (r, bindings, now) => r.Reject(4, Manager, "SENTETIK", bindings.HasApprovedOrder, now), RequestRejected));
            Assert.Equal(PurchasingRuleCodes.RequestHasApprovedOrder, blocked.Code);
            await a.Store.MutateOrderAsync(run.Envelope("k-cancel-first"), Hash("cancel-first"), PurchasingCommands.OrderCancelV1, first,
                (order, now) => order.Cancel(3, run.Actor, "SENTETIK", now), OrderRemainderCancelled);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            // ALS-D68 (EK10): the cancelled order still holds its 60 of the request line's 100; 60 + 50 is over-sourced.
            var over = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.MutateOrderAsync(run.Envelope("k-second-approve", actor: Manager),
                Hash("second-approve"), PurchasingCommands.OrderApproveV1, second, (order, now) => order.Approve(2, now), Approved));
            Assert.Equal(PurchasingStoreCodes.RequestLineOverSourced, over.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", second)));
        await using (StoreScope a = run.Store(CompanyA))
        {
            // The binding stays on the cancelled order; with no APPROVED order left the request is rejected.
            var cancelled = (await a.Store.FindOrderAsync(first))!.Order;
            Assert.Equal((requestId, PurchaseOrderStatus.Cancelled), (cancelled.SourceRequestId, cancelled.Status));
            var rejected = await a.Store.MutateRequestAsync(run.Envelope("k-reject-2", actor: Manager), Hash("reject-2"), PurchasingCommands.RequestRejectV1,
                requestId, (r, bindings, now) => r.Reject(4, Manager, "SENTETIK ihtiyaç kalktı", bindings.HasApprovedOrder, now), RequestRejected);
            Assert.Equal(PurchaseRequestStatus.Rejected, rejected!.Request.Request.Status);
        }
    }

    [SqlServerFact]
    public async Task Company_B_neither_sees_nor_cancels_company_As_order()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, _, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 5m);

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindOrderAsync(orderId));
            Assert.Null(await b.Store.MutateOrderAsync(run.Envelope("k-b-cancel"), Hash("b-cancel"), PurchasingCommands.OrderCancelV1, orderId,
                (order, now) => order.Cancel(approvedVersion, run.Actor, "SENTETIK", now), OrderRemainderCancelled));
        }

        Assert.Equal((2, approvedVersion), (await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
            await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command",
            ("@command", PurchasingCommands.OrderCancelV1)));
    }

    [SqlServerFact]
    public async Task The_cancellation_migrations_Down_refuses_while_a_cancelled_order_exists()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, _, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 5m);
        var migration = new AddPurchaseOrderCancellation();
        string guardSql = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;

        // Nothing cancelled yet: the guard passes (and changes nothing).
        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-cancel"), Hash("cancel"), PurchasingCommands.OrderCancelV1, orderId,
                (order, now) => order.Cancel(approvedVersion, run.Actor, "SENTETIK", now), OrderRemainderCancelled);
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51168, guard.Number);
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(5m, await run.ScalarAsync<decimal>("SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)));
    }

    /// <summary>An unbound approved order of company A on the acceptance participant's SENTETIK identities (product version "product-v1").</summary>
    private static async Task<(Guid Order, Guid Line, long Version)> ApprovedAcceptanceOrderAsync(Run run, decimal quantity)
    {
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        await using StoreScope scope = run.Store(CompanyA);
        PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier, AcceptanceTime);
        order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(quantity, QuantityUnit.Kilogram), sourceRequestLineId: null,
            AcceptanceTime);
        string createKey = $"cancel-create-{orderId:N}";
        await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, DateOnly.FromDateTime(AcceptanceTime.UtcDateTime),
            () => order, new Dictionary<Guid, string> { [lineId] = "product-v1" });
        string approveKey = $"cancel-approve-{orderId:N}";
        var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1, orderId,
            (candidate, now) => candidate.Approve(candidate.Version, now), Approved);
        return (orderId, lineId, approved!.Order.Order.Version);
    }
}
