using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-3b on real SQL Server: the bulk receipt's piece facts and its Down guard (52042), the piece comparison recorded with an
/// acceptance and replayed unchanged, the kilogram facts the invoice match freezes (ALS-D121) and the order realization read
/// (SAT-D436 TK6). SENTETIK owners; no cost or stock value is written by these reads.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private const decimal PieceKilogramsPerUnit = 2.5m;

    /// <summary>An approved, priced piece order of 10 pieces with its kg per piece recorded at approval (USD, like the rate facts).</summary>
    private static async Task<AcceptanceSeed> PieceOrderSeedAsync(Run run, decimal unitPrice = 10m)
    {
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        await using StoreScope scope = run.Store(CompanyA);
        PurchaseOrder order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier,
            PurchaseOrderMoney.Of(InvoiceCostRateCurrency, "USD", InvoiceCostRateProfile, 1, 2), AcceptanceTime);
        order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Piece), sourceRequestLineId: null,
            new PurchaseOrderLinePrice(unitPrice, AcceptanceUnit), AcceptanceTime);
        string createKey = $"piece-realization-create-{orderId:N}";
        await scope.Store.StoreOrderAsync(run.Envelope(createKey), Hash(createKey), AcceptanceAccount, Day, () => order,
            new Dictionary<Guid, string> { [lineId] = "product-v1" });
        string approveKey = $"piece-realization-approve-{orderId:N}";
        var approved = await scope.Store.MutateOrderAsync(run.Envelope(approveKey), Hash(approveKey), PurchasingCommands.OrderApproveV1, orderId,
            (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now, InvoiceCostRateCurrency,
                new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>
                {
                    [lineId] = new(PieceKilogramsPerUnit, "product.kg-per-unit.v1/SENTETIK"),
                }), Approved);
        return new AcceptanceSeed(orderId, lineId, AcceptanceUnit, approved!.Order.Order.Version);
    }

    private static PurchaseGoodsReceiptPieceKilogramsV1 PieceComparison(int pieces, string measuredKg, string theoreticalKg, string differenceKg) =>
        new(pieces, "2.5", theoreticalKg, measuredKg, differenceKg, nameof(GoodsReceiptKgVarianceOutcome.OutsideTolerance), "1");

    [SqlServerFact]
    public async Task A_piece_acceptance_stores_its_comparison_and_a_replay_answers_the_stored_one()
    {
        await using Run run = await Run.StartAsync();
        AcceptanceSeed seed = await PieceOrderSeedAsync(run);
        Guid acceptanceId = Guid.NewGuid();
        PurchaseGoodsReceiptPieceKilogramsV1 comparison = PieceComparison(4, "9.8", "10", "-0.2");
        PurchaseAcceptanceStageRequestV1 request = AcceptanceRequest(seed, acceptanceId, "4") with
        {
            MeasuredQuantityKg = "9.8", PieceKilograms = comparison,
        };
        PurchasingWriteEnvelope envelope = Envelope("SENTETIK-piece-comparison", "SENTETIK-piece-comparison");

        PurchaseAcceptanceStageResultV1 first = await StageAndCommitAsync(run, CompanyA, request, envelope);

        Assert.False(first.IsReplay);
        Assert.Equal(comparison, first.PieceKilograms);
        Assert.Equal(9.8m, await run.ScalarAsync<decimal>(
            "SELECT MeasuredQuantityKg FROM purchasing.PurchaseAcceptanceShares WHERE AcceptanceId = @id", ("@id", acceptanceId)));

        // The replay carries no comparison of its own (the service sends none) and answers the one stored with the acceptance;
        // the comparison is not part of the request hash, so the same key and content still replay.
        PurchaseAcceptanceStageResultV1 replay = await StageAndCommitAsync(run, CompanyA, request with { PieceKilograms = null }, envelope);

        Assert.True(replay.IsReplay);
        Assert.Equal(comparison, replay.PieceKilograms);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId = @id", ("@id", seed.OrderId)));
    }

    [SqlServerFact]
    public async Task A_comparison_that_does_not_describe_its_piece_acceptance_is_refused_and_nothing_is_staged()
    {
        await using Run run = await Run.StartAsync();
        AcceptanceSeed seed = await PieceOrderSeedAsync(run);
        PurchaseAcceptanceStageRequestV1 request = AcceptanceRequest(seed, Guid.NewGuid(), "4") with
        {
            MeasuredQuantityKg = "9.8", PieceKilograms = PieceComparison(3, "9.8", "7.5", "2.3"),
        };

        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA);
        var refusal = await Assert.ThrowsAsync<PurchaseAcceptanceParticipantException>(() =>
            participant.StageAsync(request, Envelope("SENTETIK-piece-comparison-mismatch", "SENTETIK-piece-comparison-mismatch")));
        await transaction.RollbackAsync();

        Assert.Equal((400, PurchaseAcceptanceParticipantCodes.InvalidRequest), (refusal.StatusCode, refusal.Code));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE OrderId = @id", ("@id", seed.OrderId)));
    }

    [SqlServerFact]
    public async Task The_bulk_receipt_piece_fact_migrations_Down_passes_with_only_kilogram_batches_and_refuses_while_a_piece_batch_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        string guardSql = Assert.IsType<SqlOperation>(new AddPurchaseGoodsReceiptBatchPieceFacts().DownOperations[0]).Sql;
        PurchaseOrder kgOrder = Order(owners, null, null, 10m);
        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.StoreOrderAsync(run.Envelope("k-batch-kg-order"), Hash("batch-kg-order"), owners.Account, Day, () => kgOrder, VersionsOf(kgOrder));
        }

        AcceptanceSeed piece = await PieceOrderSeedAsync(run);
        Guid kgBatchId = Guid.NewGuid();
        Guid pieceBatchId = Guid.NewGuid();
        var kgBatch = new PurchaseGoodsReceiptBatchDraft(kgBatchId, kgOrder.Id, kgOrder.Lines[0].Id, Product, "7", KgUnit, Guid.NewGuid(), T0,
            PurchaseGoodsReceiptBatchStatus.PendingApproval, 10m, 9m, GoodsReceiptKgVarianceOutcome.ToleranceNotSet, null, false,
            [new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-KG-1", GoodsReceiptLabelCodeSource.Entered, null, null, 9m, null, null)]);
        // 4 + 6 pieces x 2,5 kg = 25,000 kg theoretical; weighed 9,8 + 15,4 = 25,2 kg: outside a 0,5 % tolerance, still Posted.
        var pieceBatch = new PurchaseGoodsReceiptBatchDraft(pieceBatchId, piece.OrderId, piece.LineId, AcceptanceProduct, "product-v1", AcceptanceUnit,
            Guid.NewGuid(), T0, PurchaseGoodsReceiptBatchStatus.Posted, 25m, 25.2m, GoodsReceiptKgVarianceOutcome.OutsideTolerance, 0.5m, false,
            [
                new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-ADET-1", GoodsReceiptLabelCodeSource.Entered, null, null, 9.8m, null, null, 4),
                new(2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-ADET-2", GoodsReceiptLabelCodeSource.Entered, null, null, 15.4m, null, null, 6),
            ], PieceKilogramsPerUnit);

        async Task WithBatchStore(Func<PurchaseGoodsReceiptBatchStore, Task> action)
        {
            var contextCompany = new CompanyContext();
            contextCompany.Set(CompanyA);
            var collector = new AuditEventCollector();
            await using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
                .UseSqlServer(run.Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, contextCompany)).Options, contextCompany);
            await action(new(context, contextCompany, collector, TimeProvider.System));
        }

        await WithBatchStore(batches => batches.SaveNewAsync(kgBatch, run.Envelope("k-batch-kg"), Hash("batch-kg"), PurchaseGoodsReceiptBatchCommandsV1.SubmitV1));

        // Only a kilogram batch exists: the rollback guard passes.
        await run.ExecuteAsync(guardSql);

        // A kilogram batch keeps KLN-D45: it cannot be Posted outside the tolerance without a decider.
        var kgPosted = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseGoodsReceiptBatches SET Status = 2, VarianceOutcome = 3, TolerancePercent = 1 WHERE BatchId = @id", ("@id", kgBatchId)));
        Assert.Equal(547, kgPosted.Number);
        Assert.Contains("CK_PurchaseGoodsReceiptBatches_Variance", kgPosted.Message, StringComparison.Ordinal);

        await WithBatchStore(batches => batches.SaveNewAsync(pieceBatch, run.Envelope("k-batch-piece"), Hash("batch-piece"), PurchaseGoodsReceiptBatchCommandsV1.SubmitV1));
        await WithBatchStore(async batches =>
        {
            StoredPurchaseGoodsReceiptBatch stored = (await batches.FindAsync(piece.OrderId, pieceBatchId))!;
            Assert.Equal((PurchaseGoodsReceiptBatchStatus.Posted, (decimal?)PieceKilogramsPerUnit, 25m, 25.2m),
                (stored.Batch.Status, stored.Batch.KilogramsPerUnit, stored.Batch.ExpectedQuantityKg, stored.Batch.ReceivedQuantityKg));
            Assert.Equal([4, 6], stored.Batch.Labels.Select(label => label.PieceCount));
            Assert.Null((await batches.FindAsync(kgOrder.Id, kgBatchId))!.Batch.Labels[0].PieceCount);
        });

        // A piece batch is always Posted without a decider; a piece count is positive.
        var piecePending = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseGoodsReceiptBatches SET Status = 1 WHERE BatchId = @id", ("@id", pieceBatchId)));
        Assert.Equal(547, piecePending.Number);
        var zeroPieces = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseGoodsReceiptBatchLabels SET PieceCount = 0 WHERE BatchId = @id AND LineNo = 1", ("@id", pieceBatchId)));
        Assert.Equal(547, zeroPieces.Number);
        Assert.Contains("CK_PurchaseGoodsReceiptBatchLabels_PieceCount", zeroPieces.Message, StringComparison.Ordinal);

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(52042, guard.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseGoodsReceiptBatchLabels WHERE PieceCount IS NOT NULL"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseGoodsReceiptBatches WHERE KilogramsPerUnit IS NOT NULL"));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_matching_freezes_the_piece_kilogram_facts_and_refuses_a_fraction_of_a_piece()
    {
        await using var run = await Run.StartAsync();
        AcceptanceSeed seed = await PieceOrderSeedAsync(run);
        Guid acceptanceId = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, acceptanceId, "4") with { MeasuredQuantityKg = "9.8" },
            Envelope("SENTETIK-piece-match-receipt", "SENTETIK-piece-match-receipt"));
        long orderVersion = await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id=@id", ("@id", seed.OrderId));
        var receipt = new PurchaseInvoiceReceiptMatch(CompanyA, Guid.Empty, acceptanceId, seed.OrderId, seed.LineId, orderVersion, AcceptanceSupplier,
            AcceptanceAccount, InvoiceCostRateCurrency, AcceptanceUnit, 4m, 40m, 35m, Day);
        await using var scope = run.Store(CompanyA);
        var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new InvoiceMatchSettingsFixture()));

        // A fraction of a piece: refused as a recorded terminal refusal, nothing bound.
        var fractional = await StoreRateInvoiceAsync(run, receipt, 2.5m, 35m, finalize: false, recordedOnly: true);
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.MatchAsync(fractional.Id,
            new PurchaseIncomingInvoiceMatchRequestV1("1", [new(fractional.Lines.Single().Id, seed.OrderId, acceptanceId, "2.5")]),
            new PurchasingCommandMetadata("SENTETIK-piece-match-fraction", run.Correlation, null, AcceptanceTime)));
        Assert.Equal((409, PurchaseIncomingInvoice.PieceQuantityInvalid), (refusal.Status, refusal.Code));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", fractional.Id)));

        // 3 of the 4 received pieces invoiced: the match freezes the weighed kg of the whole acceptance and the recorded kg/piece;
        // the kilogram share is 9,8 x 3 / 4 = 7,35 kg (ALS-D121) and the theoretical 3 x 2,5 = 7,5 kg.
        var invoice = await StoreRateInvoiceAsync(run, receipt, 3m, 35m, finalize: false, recordedOnly: true);
        var matched = await service.MatchAsync(invoice.Id,
            new PurchaseIncomingInvoiceMatchRequestV1("1", [new(invoice.Lines.Single().Id, seed.OrderId, acceptanceId, "3")]),
            new PurchasingCommandMetadata("SENTETIK-piece-match", run.Correlation, null, AcceptanceTime));
        var match = matched.Invoice.Restore().Matches.Single();

        Assert.Equal(new PurchaseInvoiceMatchKilograms(false, 9.8m, PieceKilogramsPerUnit), match.Kilograms);
        Assert.Equal((3m, (decimal?)4m), (match.Quantity, match.AcceptedQuantity));
        Assert.Equal(7.35m, PurchaseInvoiceKilograms.Weighed(match)!.Value.Round(3));
        Assert.Equal(7.5m, PurchaseInvoiceKilograms.Theoretical(match));
        // 30,00 USD over 7,5 kg theoretical = 4,00; over 7,35 kg weighed = 4,08 (USD/kg, no rate: SAT-D439).
        var view = PurchaseIncomingInvoiceWireV1.View(matched.Invoice);
        Assert.Equal(new PurchaseKgPriceView("4.00", null, "4.08", null), view.Lines.Single().KgPrice);
        Assert.Equal(new PurchaseKgPriceView("4.00", null, "4.08", null), view.KgPrice);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_matching_on_a_kilogram_line_records_that_its_quantity_is_kilograms()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run, new PurchaseOrderLinePrice(10m, AcceptanceUnit));
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false, recordedOnly: true);
        await using var scope = run.Store(CompanyA);
        var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new InvoiceMatchSettingsFixture()));

        var matched = await service.MatchAsync(invoice.Id,
            new PurchaseIncomingInvoiceMatchRequestV1("1", [new(invoice.Lines.Single().Id, receipt.OrderId, receipt.AcceptanceId, "4")]),
            new PurchasingCommandMetadata("SENTETIK-kg-match-facts", run.Correlation, null, AcceptanceTime));

        var match = matched.Invoice.Restore().Matches.Single();
        Assert.Equal(new PurchaseInvoiceMatchKilograms(true), match.Kilograms);
        Assert.Equal(4m, PurchaseInvoiceKilograms.Theoretical(match));
        // 40,00 over 4 kg: the kg price of a kilogram line is its own unit price.
        Assert.Equal(new PurchaseKgPriceView("10.00", null, "10.00", null), PurchaseIncomingInvoiceWireV1.View(matched.Invoice).Lines.Single().KgPrice);
    }

    [SqlServerFact]
    public async Task Order_realization_reads_only_finalised_invoice_shares_of_its_company_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);

        // A matched but not finalised invoice does not count (the single named rule; NEEDS-SINAN).
        await StoreRateInvoiceAsync(run, receipt, 1m, 35m, finalize: false);
        await using (StoreScope a = run.Store(CompanyA))
        {
            Assert.Empty((await a.Store.ReadOrderInvoiceSharesAsync(receipt.OrderId))!);
        }

        var finalised = await StoreRateInvoiceAsync(run, receipt, 3m, 35m, finalize: true);
        var before = await InvoiceRateSourceCountsAsync(run);
        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseOrderInvoiceShare share = Assert.Single((await a.Store.ReadOrderInvoiceSharesAsync(receipt.OrderId))!);
            Assert.Equal((finalised.Id, receipt.OrderLineId, receipt.AcceptanceId, 3m),
                (share.InvoiceId, share.OrderLineId, share.AcceptanceId, share.Quantity));
            // The invoice's own currency amount (30,00 USD), never the functional amount: SAT-D439.
            Assert.Equal(30m, share.Amount!.Value.Round(2));
            // This match was stored without kilogram facts: no kg figure is invented for it.
            Assert.Null(share.WeighedKg);
            Assert.Null(share.TheoreticalKg);
            Assert.Empty((await a.Store.ReadOrderInvoiceSharesAsync(Guid.NewGuid()))!);
        }

        Assert.Equal(before, await InvoiceRateSourceCountsAsync(run));

        // Another company reads nothing of this order.
        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Empty((await b.Store.ReadOrderInvoiceSharesAsync(receipt.OrderId))!);
        }
    }
}
