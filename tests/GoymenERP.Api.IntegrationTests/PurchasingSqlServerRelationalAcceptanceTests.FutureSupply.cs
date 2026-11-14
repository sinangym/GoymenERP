using System.Data.Common;
using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-017 F17-1 (ALIS_F017 v1 §3.1/§3.3/§3.4, STOK_F017 §10 F017-a) on real SQL Server: the future-supply read port answers an
/// Approved general line's allocatable amount with its technical slices folded in and never counted twice; a technical slice, a
/// Draft order's line and a Cancelled order's line are NotAllocatable; company B sees NotFound and an empty list; the list walks the
/// Approved general lines of one product above zero by business date with its cursor; reading writes nothing. The purchase line
/// lock really serializes: while another transaction holds a general line's lock, the remainder cancel and the acceptance share are
/// refused with <c>purchasing_line_busy</c> and write nothing, and both succeed once it is released. The technical slice is written
/// with direct SQL because no production command stores a breakdown yet. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid FutureSupplyTechnicalProduct = Guid.Parse("74000000-0000-0000-0000-000000000017");

    [SqlServerFact]
    public async Task Future_supply_answers_an_approved_general_line_with_technical_slices_folded_and_follows_acceptance()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, version) = await FutureSupplyOrderAsync(run, 10m, DateOnly.FromDateTime(AcceptanceTime.UtcDateTime));

        var plain = await ReadFutureSupplyLineAsync(run, CompanyA, orderId, lineId);
        Assert.Equal(PurchaseFutureSupplyOutcomeV1.Allocatable, plain.Outcome);
        var line = Assert.IsType<PurchaseFutureSupplyLineV1>(plain.Line);
        Assert.Equal((CompanyA, orderId, lineId, 1, AcceptanceSupplier, AcceptanceAccount, AcceptanceProduct, "product-v1", AcceptanceUnit, "General"),
            (line.CompanyId, line.OrderId, line.LineId, line.LineNo, line.SupplierPartyId, line.CompanyAccountId, line.ProductId, line.ProductVersion,
                line.UnitId, line.Kind));
        Assert.Equal(("10", "0", "0", "10", version), (line.CommittedText, line.AcceptedText, line.CancelledText, line.AllocatableText,
            line.ObservedOrderVersion));
        Assert.Null(line.SalesSourceOrderId);
        Assert.Null(line.SalesSourceOrderLineId);
        Assert.Null(plain.Status);
        Assert.Null(plain.ParentLineId);

        // An acceptance share on the general line lowers the allocatable amount; the stage result names the general line it counts on.
        PurchaseAcceptanceStageResultV1 accepted = await StageAndCommitAsync(run, CompanyA,
            AcceptanceRequest(new AcceptanceSeed(orderId, lineId, AcceptanceUnit, version), Guid.NewGuid(), "4"),
            Envelope($"future-accept-{orderId:N}", $"correlation-future-accept-{orderId:N}"));
        Assert.Equal(lineId, accepted.PurchaseOrderGeneralLineId);
        var afterAcceptance = Assert.IsType<PurchaseFutureSupplyLineV1>((await ReadFutureSupplyLineAsync(run, CompanyA, orderId, lineId)).Line);
        Assert.Equal(("10", "4", "0", "6"), (afterAcceptance.CommittedText, afterAcceptance.AcceptedText, afterAcceptance.CancelledText,
            afterAcceptance.AllocatableText));
        Assert.Equal(long.Parse(accepted.PurchaseOrderVersion, CultureInfo.InvariantCulture), afterAcceptance.ObservedOrderVersion);

        // A technical slice of 3 (1 of it accepted) is folded into its general line: committed stays 10, the slice's share counts once.
        var (slicedOrder, generalLine, _) = await FutureSupplyOrderAsync(run, 10m, DateOnly.FromDateTime(AcceptanceTime.UtcDateTime));
        Guid technicalLine = await AddTechnicalSliceAsync(run, slicedOrder, generalLine, slice: 3m, sliceAccepted: 1m);
        var folded = Assert.IsType<PurchaseFutureSupplyLineV1>((await ReadFutureSupplyLineAsync(run, CompanyA, slicedOrder, generalLine)).Line);
        Assert.Equal(("10", "1", "0", "9"), (folded.CommittedText, folded.AcceptedText, folded.CancelledText, folded.AllocatableText));

        // The slice itself is never allocatable: the answer points at its general line.
        var slice = await ReadFutureSupplyLineAsync(run, CompanyA, slicedOrder, technicalLine);
        Assert.Equal((PurchaseFutureSupplyOutcomeV1.NotAllocatable, PurchaseFutureSupplyStatusesV1.TechnicalLine, (Guid?)generalLine),
            (slice.Outcome, slice.Status, slice.ParentLineId));
        Assert.Null(slice.Line);

        // The list names only the general lines, each once, with the same folded amounts.
        var listed = await ReadFutureSupplyPageAsync(run, CompanyA, AcceptanceProduct, PurchaseFutureSupplyPageV1.MaximumPageSize, null);
        Assert.Null(listed.NextCursor);
        Assert.Equal(new[] { lineId, generalLine }.Order(), listed.Items.Select(item => item.LineId).Order());
        Assert.Equal("9", Assert.Single(listed.Items, item => item.LineId == generalLine).AllocatableText);
        Assert.Equal("6", Assert.Single(listed.Items, item => item.LineId == lineId).AllocatableText);
        Assert.Empty((await ReadFutureSupplyPageAsync(run, CompanyA, FutureSupplyTechnicalProduct, 10, null)).Items);
    }

    [SqlServerFact]
    public async Task Future_supply_answers_draft_and_cancelled_lines_not_allocatable_and_another_company_not_found_without_writing()
    {
        await using Run run = await Run.StartAsync();
        var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
        var (draftOrder, draftLine) = await FutureSupplyDraftOrderAsync(run, 5m, day);
        var (cancelledOrder, cancelledLine, cancelledVersion) = await FutureSupplyOrderAsync(run, 5m, day);
        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-future-cancel"), Hash("future-cancel"), PurchasingCommands.OrderCancelV1, cancelledOrder,
                (order, now) => order.Cancel(cancelledVersion, run.Actor, "SENTETIK", now), OrderRemainderCancelled);
        }

        var (approvedOrder, approvedLine, _) = await FutureSupplyOrderAsync(run, 5m, day);
        var before = await FutureSupplyWriteCountsAsync(run);

        var draft = await ReadFutureSupplyLineAsync(run, CompanyA, draftOrder, draftLine);
        Assert.Equal((PurchaseFutureSupplyOutcomeV1.NotAllocatable, "Draft", (Guid?)null), (draft.Outcome, draft.Status, draft.ParentLineId));
        Assert.Null(draft.Line);
        var cancelled = await ReadFutureSupplyLineAsync(run, CompanyA, cancelledOrder, cancelledLine);
        Assert.Equal((PurchaseFutureSupplyOutcomeV1.NotAllocatable, "Cancelled", (Guid?)null),
            (cancelled.Outcome, cancelled.Status, cancelled.ParentLineId));
        Assert.Null(cancelled.Line);

        // Only the approved line is listed; the draft and the cancelled lines are not choices.
        var listed = await ReadFutureSupplyPageAsync(run, CompanyA, AcceptanceProduct, 10, null);
        Assert.Equal(approvedLine, Assert.Single(listed.Items).LineId);

        // Company B sees none of company A's lines: NotFound, an empty list, never 403.
        Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, (await ReadFutureSupplyLineAsync(run, CompanyB, approvedOrder, approvedLine)).Outcome);
        Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, (await ReadFutureSupplyLineAsync(run, CompanyB, draftOrder, draftLine)).Outcome);
        Assert.Empty((await ReadFutureSupplyPageAsync(run, CompanyB, AcceptanceProduct, 10, null)).Items);

        // An unknown line, or a line asked under another order, is NotFound.
        Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, (await ReadFutureSupplyLineAsync(run, CompanyA, approvedOrder, Guid.NewGuid())).Outcome);
        Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, (await ReadFutureSupplyLineAsync(run, CompanyA, draftOrder, approvedLine)).Outcome);

        // Reading creates nothing: no attempt, no audit event and no version moved.
        Assert.Equal(before, await FutureSupplyWriteCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Future_supply_list_walks_the_product_lines_above_zero_by_business_date_with_its_cursor()
    {
        await using Run run = await Run.StartAsync();
        var early = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
        var (late1, late1Line, _) = await FutureSupplyOrderAsync(run, 5m, early.AddDays(2));
        var (first, firstLine, _) = await FutureSupplyOrderAsync(run, 5m, early);
        var (late2, late2Line, _) = await FutureSupplyOrderAsync(run, 5m, early.AddDays(2));
        var (full, fullLine, fullVersion) = await FutureSupplyOrderAsync(run, 5m, early.AddDays(1));
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(new AcceptanceSeed(full, fullLine, AcceptanceUnit, fullVersion), Guid.NewGuid(), "5"),
            Envelope($"future-full-{full:N}", $"correlation-future-full-{full:N}"));

        // The fully accepted line answers Allocatable with zero on the single-line read, but it is not a list choice.
        var zero = await ReadFutureSupplyLineAsync(run, CompanyA, full, fullLine);
        Assert.Equal((PurchaseFutureSupplyOutcomeV1.Allocatable, "0"), (zero.Outcome, zero.Line!.AllocatableText));

        var walked = new List<PurchaseFutureSupplyLineV1>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await ReadFutureSupplyPageAsync(run, CompanyA, AcceptanceProduct, 1, cursor);
            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
            Assert.True(pages <= 4, "The cursor walk must end.");
        }
        while (cursor is not null);

        // The earliest business date comes first; the two later orders follow in the store's order id order, each line once.
        Assert.Equal(3, walked.Count);
        Assert.Equal((first, firstLine), (walked[0].OrderId, walked[0].LineId));
        Assert.Equal(new[] { late1Line, late2Line }.Order(), walked.Skip(1).Select(line => line.LineId).Order());
        Assert.Equal(new[] { late1, late2 }.Order(), walked.Skip(1).Select(line => line.OrderId).Order());
        Assert.DoesNotContain(walked, line => line.LineId == fullLine);
        var whole = await ReadFutureSupplyPageAsync(run, CompanyA, AcceptanceProduct, PurchaseFutureSupplyPageV1.MaximumPageSize, null);
        Assert.Equal(walked.Select(line => line.LineId), whole.Items.Select(line => line.LineId));

        // The cursor is company A's and this product's: company B and another product refuse it.
        var firstPage = await ReadFutureSupplyPageAsync(run, CompanyA, AcceptanceProduct, 1, null);
        Assert.NotNull(firstPage.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ReadFutureSupplyPageAsync(run, CompanyB, AcceptanceProduct, 1, firstPage.NextCursor));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ReadFutureSupplyPageAsync(run, CompanyA, FutureSupplyTechnicalProduct, 1, firstPage.NextCursor));
    }

    [SqlServerFact]
    public async Task A_held_purchase_line_lock_refuses_the_cancel_and_the_acceptance_as_busy_without_writing_until_it_is_released()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, version) = await FutureSupplyOrderAsync(run, 10m, DateOnly.FromDateTime(AcceptanceTime.UtcDateTime));
        var request = AcceptanceRequest(new AcceptanceSeed(orderId, lineId, AcceptanceUnit, version), Guid.NewGuid(), "4");
        var acceptanceEnvelope = Envelope($"future-busy-accept-{orderId:N}", $"correlation-future-busy-accept-{orderId:N}");

        await using (var holder = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await holder.OpenAsync();
            await using var holderTransaction = (DbTransaction)await holder.BeginTransactionAsync();
            await PurchaseLineSerializationLock.Bind(holder, holderTransaction, CompanyA).AcquireAsync(lineId);

            await using (StoreScope a = run.Store(CompanyA))
            {
                var busy = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.MutateOrderAsync(run.Envelope("k-future-busy-cancel"),
                    Hash("future-busy-cancel"), PurchasingCommands.OrderCancelV1, orderId,
                    (order, now) => order.Cancel(version, run.Actor, "SENTETIK", now), OrderRemainderCancelled));
                Assert.Equal(PurchaseLineSerializationLockContractV1.BusyCode, busy.Code);
            }

            await AssertRefusedAsync(run, CompanyA, request, acceptanceEnvelope, PurchaseAcceptanceParticipantCodes.LineBusy);

            // The lock is the company's: the same line identity under company B is another resource and is granted at once.
            await using (var other = new SqlConnection(run.Factory.DatabaseConnectionString))
            {
                await other.OpenAsync();
                await using var otherTransaction = (DbTransaction)await other.BeginTransactionAsync();
                await PurchaseLineSerializationLock.Bind(other, otherTransaction, CompanyB).AcquireAsync(lineId);
                await otherTransaction.RollbackAsync();
            }

            // Nothing was written by either refused write.
            Assert.Equal((2, 0m, 0m, 0), (
                await run.ScalarAsync<int>("SELECT Status FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
                await run.ScalarAsync<decimal>("SELECT Accepted FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
                await run.ScalarAsync<decimal>("SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
                await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId = @id", ("@id", orderId))));
            Assert.Equal(version, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command",
                ("@command", PurchasingCommands.OrderCancelV1)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.remainder-cancelled.v1'"));

            await holderTransaction.RollbackAsync();
        }

        // Released: the acceptance share and then the remainder cancel go through on the same line.
        PurchaseAcceptanceStageResultV1 accepted = await StageAndCommitAsync(run, CompanyA, request, acceptanceEnvelope);
        Assert.Equal(lineId, accepted.PurchaseOrderGeneralLineId);
        await using (StoreScope a = run.Store(CompanyA))
        {
            var cancelled = await a.Store.MutateOrderAsync(run.Envelope("k-future-cancel-after"), Hash("future-cancel-after"),
                PurchasingCommands.OrderCancelV1, orderId,
                (order, now) => order.Cancel(long.Parse(accepted.PurchaseOrderVersion, CultureInfo.InvariantCulture), run.Actor, "SENTETIK", now),
                OrderRemainderCancelled);
            Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled!.Order.Order.Status);
        }

        Assert.Equal((4m, 6m), (
            await run.ScalarAsync<decimal>("SELECT Accepted FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
            await run.ScalarAsync<decimal>("SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId))));
    }

    /// <summary>An approved, unbound order of company A with one general line on the acceptance identities, on the given business date.</summary>
    private static async Task<(Guid Order, Guid Line, long Version)> FutureSupplyOrderAsync(Run run, decimal quantity, DateOnly effectiveOn)
    {
        var (orderId, lineId) = await FutureSupplyDraftOrderAsync(run, quantity, effectiveOn);
        await using StoreScope scope = run.Store(CompanyA);
        string approveKey = $"future-approve-{orderId:N}";
        var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1, orderId,
            (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now), Approved);
        return (orderId, lineId, approved!.Order.Order.Version);
    }

    private static async Task<(Guid Order, Guid Line)> FutureSupplyDraftOrderAsync(Run run, decimal quantity, DateOnly effectiveOn)
    {
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        await using StoreScope scope = run.Store(CompanyA);
        PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier, AcceptanceTime);
        order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(quantity, QuantityUnit.Kilogram), sourceRequestLineId: null,
            AcceptanceTime);
        string createKey = $"future-create-{orderId:N}";
        await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, effectiveOn, () => order,
            new Dictionary<Guid, string> { [lineId] = "product-v1" });
        return (orderId, lineId);
    }

    /// <summary>
    /// Writes one technical slice row under the general line and the general line's technical total, as a breakdown would leave them
    /// (no production command stores a breakdown yet). Only reads follow on this order.
    /// </summary>
    private static async Task<Guid> AddTechnicalSliceAsync(Run run, Guid orderId, Guid generalLineId, decimal slice, decimal sliceAccepted)
    {
        Guid technicalLineId = Guid.NewGuid();
        await run.ExecuteAsync(
            "INSERT INTO purchasing.PurchaseOrderLines (CompanyId, OrderId, LineNo, LineId, Kind, ParentLineId, SourceRequestLineId, ProductId, "
            + "ProductVersion, UnitId, UnitIsKilogram, Ordered, Technical, Accepted, Cancelled) "
            + "VALUES (@company, @order, 2, @line, 2, @parent, NULL, @product, N'product-v1', @unit, 1, @slice, 0, @accepted, 0); "
            + "UPDATE purchasing.PurchaseOrderLines SET Technical = @slice WHERE CompanyId = @company AND OrderId = @order AND LineId = @parent;",
            ("@company", CompanyA), ("@order", orderId), ("@line", technicalLineId), ("@parent", generalLineId),
            ("@product", FutureSupplyTechnicalProduct), ("@unit", AcceptanceUnit), ("@slice", slice), ("@accepted", sliceAccepted));
        return technicalLineId;
    }

    private static async Task<PurchaseFutureSupplyAnswerV1> ReadFutureSupplyLineAsync(Run run, Guid company, Guid orderId, Guid lineId)
    {
        var (context, port) = FutureSupplyPort(run, company);
        await using (context)
        {
            return await port.GetAllocatableLineAsync(orderId, lineId);
        }
    }

    private static async Task<PurchaseFutureSupplyPageV1> ReadFutureSupplyPageAsync(Run run, Guid company, Guid productId, int pageSize, string? cursor)
    {
        var (context, port) = FutureSupplyPort(run, company);
        await using (context)
        {
            return await port.ListAllocatableLinesAsync(productId, pageSize, cursor);
        }
    }

    private static (PurchasingDbContext Context, IPurchaseFutureSupplyReadPortV1 Port) FutureSupplyPort(Run run, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options, companyContext);
        return (context, new PurchaseFutureSupplyReadPort(context, companyContext));
    }

    private static async Task<(int Attempts, int AuditEvents, long Versions)> FutureSupplyWriteCountsAsync(Run run) => (
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"),
        await run.ScalarAsync<long>("SELECT COALESCE(SUM(Version), 0) FROM purchasing.PurchaseOrders"));
}
