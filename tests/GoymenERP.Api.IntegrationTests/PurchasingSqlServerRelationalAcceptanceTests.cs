using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// PUR-4a on real SQL Server (RCSI, the disposable database of the run): a request and an order with their
/// lines are stored, submitted and approved in ONE save each (header, lines, attempt, audit); the same key
/// replays with the first correlation and another content is one 409; the Σ-shares rule (Sinan #66
/// 5738113623 §1) refuses the approval that would over-source a request line, writes nothing, and is
/// recorded / replayed as a terminal refusal (§7) while the supply port answers only the APPROVED line's
/// remaining commitment as expected supply, never stock; a stale version is terminal and a same-key race
/// stores one transition, one attempt and one audit (§8); company B sees nothing and the create-only
/// filtered index refuses a second create attempt on a stored order. Nothing physical is written (T09).
/// SENTETIK owners: the supplier and the product are identities here — the owners' answers are the service's
/// (unit tests) evidence.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new DateOnly(2026, 9, 19);
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = Guid.Parse("8a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
    private static readonly Guid Product = Guid.Parse("9b2e3d4c-5b6a-4798-8b1c-2d3e4f5a6b04");
    private static readonly PurchasingMutationAudit Submitted = new PurchasingMutationAudit("purchasing.purchase-request.submitted.v1", "Submit", "SENTETIK gönderildi");
    private static readonly PurchasingMutationAudit Approved = new PurchasingMutationAudit("purchasing.purchase-order.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task A_request_and_an_order_are_stored_in_one_save_each_and_the_same_key_replays_with_the_first_correlation()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        StoredPurchaseRequest request;
        StoredPurchaseOrder order;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseRequest built = Request(owners, 100m);
            request = (await a.Store.StoreRequestAsync(run.Envelope("k-req"), Hash("req-1"), Day, () => built, VersionsOf(built))).Request;
            var replay = await a.Store.StoreRequestAsync(run.Envelope("k-req", correlation: "SENTETIK-second"), Hash("req-1"), Day,
                () => throw new InvalidOperationException("a replay never builds"), NoVersions);
            Assert.Equal((PurchasingWriteOutcome.Replayed, request.Request.Id, run.Correlation), (replay.Outcome, replay.Request.Request.Id, replay.OriginalCorrelationId));
            var other = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.StoreRequestAsync(run.Envelope("k-req"), Hash("req-9"), Day,
                () => throw new InvalidOperationException("never built"), NoVersions));
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, other.Code);

            var submitted = await a.Store.MutateRequestAsync(run.Envelope("k-submit"), Hash("submit-2"), PurchasingCommands.RequestSubmitV1, request.Request.Id,
                (purchaseRequest, now) => purchaseRequest.Submit(2, now), Submitted);
            Assert.Equal((PurchaseRequestStatus.Submitted, 3L), (submitted!.Request.Request.Status, submitted.Request.Request.Version));
        }

        Assert.Equal((1, 1, 2, 2, 3L), await run.RequestCountsAsync(request.Request.Id));

        await using (StoreScope a = run.Store(CompanyA))
        {
            Guid requestLineId = request.Request.Lines[0].Id;
            PurchaseOrder builtOrder = Order(owners, request.Request.Id, requestLineId, 60m);
            order = (await a.Store.StoreOrderAsync(run.Envelope("k-order"), Hash("order-1"), owners.Account, Day, () => builtOrder, VersionsOf(builtOrder))).Order;
            Assert.Equal((PurchaseOrderStatus.Draft, 2L, owners.Account, Day), (order.Order.Status, order.Order.Version, order.CompanyAccountId, order.EffectiveOn));
            var replay = await a.Store.StoreOrderAsync(run.Envelope("k-order", correlation: "SENTETIK-second"), Hash("order-1"), owners.Account, Day,
                () => throw new InvalidOperationException("a replay never builds"), NoVersions);
            Assert.Equal((PurchasingWriteOutcome.Replayed, order.Order.Id, run.Correlation), (replay.Outcome, replay.Order.Order.Id, replay.OriginalCorrelationId));
            var keyed = await a.Store.FindReplayAsync(run.Envelope("k-order", correlation: "SENTETIK-third"), Hash("order-1"), PurchasingCommands.OrderCreateV1);
            Assert.Equal((order.Order.Id, run.Correlation), (keyed!.Order!.Order.Id, keyed.OriginalCorrelationId));
            Assert.Null(keyed.Request);
            Assert.Null(keyed.Refusal);
        }

        Assert.Equal((1, 1, 3, 1, 2L), await run.OrderCountsAsync(order.Order.Id));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Rebuilt through the domain in recording order (lines, then the transition): the same version, the same binding and shares.
            StoredPurchaseOrder? rebuilt = await a.Store.FindOrderAsync(order.Order.Id);
            Assert.NotNull(rebuilt);
            Assert.Equal((PurchaseOrderStatus.Draft, 2L, 1, request.Request.Id, request.Request.Lines[0].Id, 60m, 0m, 60m, "7"),
                (rebuilt.Order.Status, rebuilt.Order.Version, rebuilt.Order.Lines.Count, rebuilt.Order.SourceRequestId, rebuilt.Order.Lines[0].SourceRequestLineId,
                    rebuilt.Order.Lines[0].Quantity.Value, rebuilt.Order.Lines[0].AcceptedQuantity.Value, rebuilt.Order.Lines[0].RemainingQuantity.Value,
                    rebuilt.ProductVersionsByLineId[rebuilt.Order.Lines[0].Id]));
            StoredPurchaseRequest? rebuiltRequest = await a.Store.FindRequestAsync(request.Request.Id);
            Assert.Equal((PurchaseRequestStatus.Submitted, 3L, 100m), (rebuiltRequest!.Request.Status, rebuiltRequest.Request.Version, rebuiltRequest.Request.Lines[0].Quantity.Value));
            var drafts = await a.Store.ListOrdersAsync(1, null, PurchaseOrderStatus.Draft);
            Assert.Equal(order.Order.Id, Assert.Single(drafts.Items).Order.Id);
            Assert.Null(drafts.NextCursor);
            var submittedPage = await a.Store.ListRequestsAsync(10, null, PurchaseRequestStatus.Submitted);
            Assert.Equal(request.Request.Id, Assert.Single(submittedPage.Items).Request.Id);
        }
    }

    [SqlServerFact]
    public async Task Approval_enforces_the_sum_of_shares_across_orders_and_records_the_refusal_as_terminal()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId;
        Guid requestLineId;
        Guid orderA;
        Guid orderB;
        Guid orderC;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseRequest built = Request(owners, 100m);
            PurchaseRequest request = (await a.Store.StoreRequestAsync(run.Envelope("k-req"), Hash("req-1"), Day, () => built, VersionsOf(built))).Request.Request;
            requestId = request.Id;
            requestLineId = request.Lines[0].Id;
            await a.Store.MutateRequestAsync(run.Envelope("k-submit"), Hash("submit-2"), PurchasingCommands.RequestSubmitV1, requestId, (r, now) => r.Submit(2, now), Submitted);
            // Multi-sourcing (Sinan 5738113623 §1): 60 + 50 > 100 is refused at the second approval; 60 + 40 = 100 is not.
            PurchaseOrder builtA = Order(owners, requestId, requestLineId, 60m);
            orderA = (await a.Store.StoreOrderAsync(run.Envelope("k-order-a"), Hash("order-a"), owners.Account, Day, () => builtA, VersionsOf(builtA))).Order.Order.Id;
            PurchaseOrder builtB = Order(owners, requestId, requestLineId, 50m);
            orderB = (await a.Store.StoreOrderAsync(run.Envelope("k-order-b"), Hash("order-b"), owners.Account, Day, () => builtB, VersionsOf(builtB))).Order.Order.Id;
            PurchaseOrder builtC = Order(owners, requestId, requestLineId, 40m);
            orderC = (await a.Store.StoreOrderAsync(run.Envelope("k-order-c"), Hash("order-c"), owners.Account, Day, () => builtC, VersionsOf(builtC))).Order.Order.Id;
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Draft orders reserve nothing (ASSUMPTION-PUR4-2): the supply port answers NotApproved for every line, whatever their sum.
            PurchaseSupplyReferencePort port = new PurchaseSupplyReferencePort(a.Store);
            Guid lineA = (await a.Store.FindOrderAsync(orderA))!.Order.Lines[0].Id;
            var draft = await port.GetApprovedLineAsync(orderA, lineA);
            Assert.Equal((PurchaseSupplyReferenceOutcomeV1.NotApproved, "Draft"), (draft.Outcome, draft.Status));
            Assert.Null(draft.Line);

            var approvedA = await a.Store.MutateOrderAsync(run.Envelope("k-approve-a"), Hash("approve-a"), PurchasingCommands.OrderApproveV1, orderA, (o, now) => o.Approve(2, now), Approved);
            Assert.Equal((PurchaseOrderStatus.Approved, 3L), (approvedA!.Order.Order.Status, approvedA.Order.Order.Version));
            Assert.Equal(1L, await run.ScalarAsync<long>("SELECT SourcingVersion FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));

            var supply = await port.GetApprovedLineAsync(orderA, lineA);
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.Approved, supply.Outcome);
            Assert.Equal((CompanyA, orderA, lineA, 1, owners.Party, owners.Account, requestId, requestLineId, Product, "7", KgUnit, "General", "60", "0", "60", 3L),
                (supply.Line!.CompanyId, supply.Line.OrderId, supply.Line.LineId, supply.Line.LineNo, supply.Line.SupplierPartyId, supply.Line.CompanyAccountId,
                    supply.Line.SourceRequestId, supply.Line.SourceRequestLineId, supply.Line.ProductId, supply.Line.ProductVersion, supply.Line.UnitId, supply.Line.Kind,
                    supply.Line.CommittedText, supply.Line.AcceptedText, supply.Line.RemainingText, supply.Line.Version));
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(orderA, Guid.NewGuid())).Outcome);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            // 60 approved + 50 = 110 > 100: the store refuses inside the save, names the line, and writes nothing.
            PurchasingWriteEnvelope envelope = run.Envelope("k-approve-b");
            var over = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.MutateOrderAsync(envelope, Hash("approve-b"), PurchasingCommands.OrderApproveV1, orderB,
                (o, now) => o.Approve(2, now), Approved));
            Assert.Equal((PurchasingStoreCodes.RequestLineOverSourced, "lines[0].sourceRequestLineId", PurchasingStoreCodes.RequestLineOverSourced),
                (over.Code, over.Errors[0].Field, over.Errors[0].Code));
            // §7: the service records the refusal under the key on the same context; the same key answers it again without touching the domain.
            await a.Store.RecordRefusalAsync(envelope, Hash("approve-b"), PurchasingCommands.OrderApproveV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.RequestLineOverSourced, over.Errors));
            var replay = await a.Store.FindReplayAsync(run.Envelope("k-approve-b", correlation: "SENTETIK-retry"), Hash("approve-b"), PurchasingCommands.OrderApproveV1);
            Assert.Equal((409, PurchasingProblemCodesV1.RequestLineOverSourced, run.Correlation), (replay!.Refusal!.Status, replay.Refusal.Code, replay.OriginalCorrelationId));
            Assert.Null(replay.Order);
        }

        Assert.Equal((1, 1, 7, 1, 2L), await run.OrderCountsAsync(orderB));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT SourcingVersion FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // 60 + 40 = 100: exactly the need is not over-sourcing; the request's sourcing version moves again.
            var approvedC = await a.Store.MutateOrderAsync(run.Envelope("k-approve-c"), Hash("approve-c"), PurchasingCommands.OrderApproveV1, orderC, (o, now) => o.Approve(2, now), Approved);
            Assert.Equal(PurchaseOrderStatus.Approved, approvedC!.Order.Order.Status);
        }

        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT SourcingVersion FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Status = 2"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE Outcome = 2 AND ISJSON(RefusalJson) = 1"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.approved.v1'"));
    }

    [SqlServerFact]
    public async Task Two_orders_on_one_request_line_approved_concurrently_yield_one_approval_and_the_losers_retry_is_refused_as_over_sourced()
    {
        // FİNANS #352 P3-1: the concurrency proof of PUR-2 = A (Sinan 5738113623 §1). Need 10, two orders of 6 approved at the same
        // moment under two keys: both read "0 approved so far", both pass the sum in memory, both move the request's SourcingVersion —
        // only one save wins its row version; the other is a ConcurrentChange with nothing stored, and its retry measures again.
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId;
        Guid orderA;
        Guid orderB;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseRequest built = Request(owners, 10m);
            PurchaseRequest request = (await a.Store.StoreRequestAsync(run.Envelope("k-req"), Hash("req-1"), Day, () => built, VersionsOf(built))).Request.Request;
            requestId = request.Id;
            Guid requestLineId = request.Lines[0].Id;
            await a.Store.MutateRequestAsync(run.Envelope("k-submit"), Hash("submit-2"), PurchasingCommands.RequestSubmitV1, requestId, (r, now) => r.Submit(2, now), Submitted);
            PurchaseOrder builtA = Order(owners, requestId, requestLineId, 6m);
            orderA = (await a.Store.StoreOrderAsync(run.Envelope("k-order-a"), Hash("order-a"), owners.Account, Day, () => builtA, VersionsOf(builtA))).Order.Order.Id;
            PurchaseOrder builtB = Order(owners, requestId, requestLineId, 6m);
            orderB = (await a.Store.StoreOrderAsync(run.Envelope("k-order-b"), Hash("order-b"), owners.Account, Day, () => builtB, VersionsOf(builtB))).Order.Order.Id;
        }

        Barrier barrier = new Barrier(2);
        async Task<object> ApproveAsync(Guid orderId, string key)
        {
            await using StoreScope writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.MutateOrderAsync(run.Envelope(key), Hash(key), PurchasingCommands.OrderApproveV1, orderId, (o, now) => o.Approve(2, now), Approved);
                return result!.Outcome;
            }
            catch (PurchasingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync(orderA, "k-approve-a")), Task.Run(() => ApproveAsync(orderB, "k-approve-b")));
        Assert.Single(results, result => result is PurchasingWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == PurchasingStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Status = 2"));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT SourcingVersion FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.approved.v1'"));
        // The loser's key holds nothing: the race left no record, no refusal.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE Outcome = 2"));
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"));

        Guid loser = results[0] is PurchasingWriteOutcome.Recorded ? orderB : orderA;
        string loserKey = loser == orderA ? "k-approve-a" : "k-approve-b";
        await using (StoreScope a = run.Store(CompanyA))
        {
            // The retry under the same key now sees the winner's 6: 6 + 6 > 10 is refused inside the save, named on the line, nothing written;
            // the service records it as terminal (§7) and the key answers it again.
            PurchasingWriteEnvelope envelope = run.Envelope(loserKey, correlation: "SENTETIK-retry");
            var over = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.MutateOrderAsync(envelope, Hash(loserKey), PurchasingCommands.OrderApproveV1, loser,
                (o, now) => o.Approve(2, now), Approved));
            Assert.Equal((PurchasingStoreCodes.RequestLineOverSourced, "lines[0].sourceRequestLineId"), (over.Code, over.Errors[0].Field));
            await a.Store.RecordRefusalAsync(envelope, Hash(loserKey), PurchasingCommands.OrderApproveV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.RequestLineOverSourced, over.Errors));
            var replay = await a.Store.FindReplayAsync(run.Envelope(loserKey, correlation: "SENTETIK-again"), Hash(loserKey), PurchasingCommands.OrderApproveV1);
            Assert.Equal((409, PurchasingProblemCodesV1.RequestLineOverSourced, "SENTETIK-retry"), (replay!.Refusal!.Status, replay.Refusal.Code, replay.OriginalCorrelationId));
            Assert.Null(replay.Order);
        }

        Assert.Equal((1, 1, 6, 1, 2L), await run.OrderCountsAsync(loser));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Status = 2"));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT SourcingVersion FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE Outcome = 2 AND ISJSON(RefusalJson) = 1"));
    }

    [SqlServerFact]
    public async Task A_stale_version_is_terminal_and_a_same_key_race_leaves_one_record()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid id;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = Order(owners, null, null, 6m);
            id = (await a.Store.StoreOrderAsync(run.Envelope("k-create"), Hash("create-1"), owners.Account, Day, () => built, VersionsOf(built))).Order.Order.Id;
            PurchasingWriteEnvelope envelope = run.Envelope("k-approve-stale");
            var stale = await Assert.ThrowsAsync<PurchasingVersionConflictException>(() => a.Store.MutateOrderAsync(envelope, Hash("approve-1"), PurchasingCommands.OrderApproveV1, id,
                (o, now) => o.Approve(1, now), Approved));
            Assert.Equal((1L, 2L), (stale.ExpectedVersion, stale.CurrentVersion));
            await a.Store.RecordRefusalAsync(envelope, Hash("approve-1"), PurchasingCommands.OrderApproveV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.VersionConflict, [new PurchasingFieldError("expectedVersion", "version_conflict")]));
            var replay = await a.Store.FindReplayAsync(run.Envelope("k-approve-stale", correlation: "SENTETIK-retry"), Hash("approve-1"), PurchasingCommands.OrderApproveV1);
            Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, run.Correlation), (replay!.Refusal!.Status, replay.Refusal.Code, replay.OriginalCorrelationId));
        }

        Assert.Equal((1, 1, 2, 1, 2L), await run.OrderCountsAsync(id));

        Barrier barrier = new Barrier(2);
        async Task<object> ApproveAsync()
        {
            await using StoreScope writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.MutateOrderAsync(run.Envelope("k-race"), Hash("race"), PurchasingCommands.OrderApproveV1, id, (o, now) => o.Approve(2, now), Approved);
                return result!.Outcome;
            }
            catch (PurchasingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(ApproveAsync), Task.Run(ApproveAsync));
        Assert.Single(results, result => result is PurchasingWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == PurchasingStoreCodes.ConcurrentChange);
        Assert.Equal((1, 1, 3, 2, 3L), await run.OrderCountsAsync(id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.approved.v1'"));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // §8: the loser re-reads the key and answers the winner's record.
            var retry = await a.Store.FindReplayAsync(run.Envelope("k-race", correlation: "SENTETIK-loser"), Hash("race"), PurchasingCommands.OrderApproveV1);
            Assert.Equal((PurchaseOrderStatus.Approved, 3L, run.Correlation), (retry!.Order!.Order.Status, retry.Order.Order.Version, retry.OriginalCorrelationId));
            var replayed = await a.Store.MutateOrderAsync(run.Envelope("k-race", correlation: "SENTETIK-again"), Hash("race"), PurchasingCommands.OrderApproveV1, id,
                (_, _) => throw new InvalidOperationException("a replay never mutates"), Approved);
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation), (replayed!.Outcome, replayed.OriginalCorrelationId));
        }
    }

    [SqlServerFact]
    public async Task Company_B_sees_nothing_of_company_A_and_a_second_create_attempt_on_the_same_order_is_refused_by_the_filtered_index()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid id;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder built = Order(owners, null, null, 6m);
            id = (await a.Store.StoreOrderAsync(run.Envelope("k-create"), Hash("create-1"), owners.Account, Day, () => built, VersionsOf(built))).Order.Order.Id;
            // The same order id under another key is refused before the index (the store's own check), nothing written.
            PurchaseOrder sameId = Order(owners, null, null, 6m, id);
            var duplicate = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.StoreOrderAsync(run.Envelope("k-create-2"), Hash("create-2"), owners.Account, Day,
                () => sameId, VersionsOf(sameId)));
            Assert.Equal(PurchasingStoreCodes.ConcurrentChange, duplicate.Code);
        }

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindOrderAsync(id));
            Assert.Empty((await b.Store.ListOrdersAsync(10, null)).Items);
            Assert.Empty((await b.Store.ListRequestsAsync(10, null)).Items);
            Assert.Null(await b.Store.FindReplayAsync(run.Envelope("k-create"), Hash("create-1"), PurchasingCommands.OrderCreateV1));
            Assert.Null(await b.Store.MutateOrderAsync(run.Envelope("k-b-approve"), Hash("approve-b"), PurchasingCommands.OrderApproveV1, id, (o, now) => o.Approve(2, now), Approved));
            PurchaseSupplyReferencePort port = new PurchaseSupplyReferencePort(b.Store);
            Assert.Equal(PurchaseSupplyReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(id, Guid.NewGuid())).Outcome);
            // Company B's same key is its own attempt: a different company, a different record.
            PurchaseOrder ownOrder = Order(Owners.For(CompanyB), null, null, 6m);
            var own = await b.Store.StoreOrderAsync(run.Envelope("k-create"), Hash("create-1"), owners.Account, Day, () => ownOrder, VersionsOf(ownOrder));
            Assert.Equal((PurchasingWriteOutcome.Recorded, CompanyB), (own.Outcome, own.Order.Order.CompanyId));
        }

        Assert.Equal((1, 1, 2, 1, 2L), await run.OrderCountsAsync(id));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders"));

        // The create-only filtered unique index (the Sales #338 lesson): a second CREATE attempt row for the stored order is 2601 at the index
        // itself, while an APPROVE attempt row sharing the order id passes the filter.
        const string insert = "INSERT INTO purchasing.PurchaseAttempts (CompanyId, CommandName, IdempotencyKeyHash, RequestHash, ActorId, Outcome, RequestId, OrderId, RefusalJson, CorrelationId, RecordedAtUtc) "
            + "VALUES (@company, @command, @keyHash, @requestHash, N'SENTETIK', 1, NULL, @order, NULL, N'SENTETIK', SYSDATETIMEOFFSET())";
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(insert, ("@company", CompanyA), ("@command", PurchasingCommands.OrderCreateV1),
            ("@keyHash", Hash("second-create").ToUpperInvariant()), ("@requestHash", Hash("create-2")), ("@order", id)));
        Assert.Equal(2601, refused.Number);
        await run.ExecuteAsync(insert, ("@company", CompanyA), ("@command", PurchasingCommands.OrderApproveV1),
            ("@keyHash", Hash("an-approve").ToUpperInvariant()), ("@requestHash", Hash("approve-9")), ("@order", id));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE OrderId = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE OrderId = @id AND CommandName = @command",
            ("@id", id), ("@command", PurchasingCommands.OrderCreateV1)));
    }

    [SqlServerFact]
    public async Task Supplier_filter_is_company_scoped_and_keeps_opaque_cursor_paging_inside_the_supplier()
    {
        await using Run run = await Run.StartAsync();
        Owners supplierA = Owners.For(CompanyA);
        Owners supplierB = Owners.For(CompanyA);
        Guid firstA;
        Guid secondA;
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrder builtA1 = Order(supplierA, null, null, 6m);
            firstA = (await a.Store.StoreOrderAsync(run.Envelope("k-supplier-a-1"), Hash("supplier-a-1"), supplierA.Account, Day,
                () => builtA1, VersionsOf(builtA1))).Order.Order.Id;
            PurchaseOrder builtB = Order(supplierB, null, null, 7m);
            await a.Store.StoreOrderAsync(run.Envelope("k-supplier-b"), Hash("supplier-b"), supplierB.Account, Day,
                () => builtB, VersionsOf(builtB));
            PurchaseOrder builtA2 = Order(supplierA, null, null, 8m);
            secondA = (await a.Store.StoreOrderAsync(run.Envelope("k-supplier-a-2"), Hash("supplier-a-2"), supplierA.Account, Day,
                () => builtA2, VersionsOf(builtA2))).Order.Order.Id;

            PurchasingOrderPage page1 = await a.Store.ListOrdersAsync(1, null, supplierPartyId: supplierA.Party);
            Assert.Equal(secondA, Assert.Single(page1.Items).Order.Id);
            Assert.NotNull(page1.NextCursor);
            PurchasingOrderPage page2 = await a.Store.ListOrdersAsync(1, page1.NextCursor, supplierPartyId: supplierA.Party);
            Assert.Equal(firstA, Assert.Single(page2.Items).Order.Id);
            Assert.Null(page2.NextCursor);
            Assert.Empty((await a.Store.ListOrdersAsync(10, null, supplierPartyId: Guid.NewGuid())).Items);
        }

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Empty((await b.Store.ListOrdersAsync(10, null, supplierPartyId: supplierA.Party)).Items);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>The Product owner's answered version per line; the store keeps it beside the line. Every line of this test is version "7".</summary>
    private static IReadOnlyDictionary<Guid, string> VersionsOf(PurchaseRequest request) => request.Lines.ToDictionary(line => line.Id, _ => "7");

    private static IReadOnlyDictionary<Guid, string> VersionsOf(PurchaseOrder order) => order.Lines.ToDictionary(line => line.Id, _ => "7");

    private static IReadOnlyDictionary<Guid, string> NoVersions => new Dictionary<Guid, string>();

    /// <summary>A Draft request of the company with one kg line of the given quantity.</summary>
    private static PurchaseRequest Request(Owners owners, decimal quantity)
    {
        PurchaseRequest request = PurchaseRequest.Create(Guid.NewGuid(), owners.Company, T0);
        request.AddLine(request.Version, Guid.NewGuid(), Product, KgUnit, Kg(quantity), T0);
        return request;
    }

    /// <summary>A Draft order of the company towards SYNTHETIC supplier identities, one general kg line, bound to the request line when given.</summary>
    private static PurchaseOrder Order(Owners owners, Guid? requestId, Guid? requestLineId, decimal quantity, Guid? id = null)
    {
        PurchaseOrder order = requestId is { } sourced
            ? PurchaseOrder.CreateForRequest(id ?? Guid.NewGuid(), owners.Company, owners.Party, sourced, T0)
            : PurchaseOrder.Create(id ?? Guid.NewGuid(), owners.Company, owners.Party, T0);
        order.AddLine(order.Version, Guid.NewGuid(), Product, KgUnit, Kg(quantity), requestLineId, T0);
        return order;
    }

    private sealed record Owners(Guid Company, Guid Party, Guid Account)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StoreScope(PurchasingDbContext dbContext, PurchasingStore store) : IAsyncDisposable
    {
        public IPurchasingStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"pur4-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            string baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            SqlServerRelationalApiFactory factory = new SqlServerRelationalApiFactory(baseConnectionString);
            Run run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // The host applies the module's migration after Organization's once it is wired (central); applied here idempotently either way.
            CompanyContext companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using PurchasingDbContext context = new PurchasingDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public PurchasingWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-purchasing-permission", ["SENTETIK-PURCHASING"], correlation ?? Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            CompanyContext companyContext = new CompanyContext();
            companyContext.Set(company);
            AuditEventCollector collector = new AuditEventCollector();
            DbContextOptionsBuilder<PurchasingDbContext> builder = new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            PurchasingDbContext dbContext = new PurchasingDbContext(builder.Options, companyContext);
            return new StoreScope(dbContext, new PurchasingStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        /// <summary>(requests, request lines, attempts, audit events of the request, version) as the real SQL holds them.</summary>
        public async Task<(int Requests, int Lines, int Attempts, int AuditEvents, long Version)> RequestCountsAsync(Guid id) =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseRequestLines WHERE RequestId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE RequestId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", id.ToString("D"))),
                await ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", id)));

        /// <summary>(orders, order lines, attempts of the run, audit events of the order, version) as the real SQL holds them.</summary>
        public async Task<(int Orders, int Lines, int Attempts, int AuditEvents, long Version)> OrderCountsAsync(Guid id) =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", id.ToString("D"))),
                await ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", id)));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqlConnection connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqlConnection connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using SqlConnection connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<PurchasingDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
                .Options;
    }
}
