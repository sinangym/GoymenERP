using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts;
using Microsoft.Data.SqlClient;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Receipt_cost_proof_reads_uncommitted_owner_acceptance_and_preserves_pending_price_without_committing()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var request = AcceptanceRequest(seed, acceptanceId, "4");
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using (var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA))
            await participant.StageAsync(request, Envelope("cost-proof-rollback", "cost-proof-rollback"));
        await using (var proof = await PurchaseReceiptCostProof.EnlistAsync(connection, transaction, CompanyA))
        {
            var actual = Assert.IsType<PurchaseReceiptCostProofV1>(await proof.ReadAsync(seed.OrderId, seed.LineId, acceptanceId));
            Assert.Equal((CompanyA, seed.OrderId, seed.LineId, acceptanceId, AcceptanceProduct, "product-v1", "4", "4", request.OccurredAtUtc),
                (actual.CompanyId, actual.PurchaseOrderId, actual.PurchaseOrderLineId, actual.AcceptanceId, actual.ProductId,
                    actual.ProductVersion, actual.AcceptedQuantity, actual.AcceptanceOrderVersion, actual.ReceivedAtUtc));
            Assert.Equal(PurchaseOrderLineCostReasonsV1.PricePending, actual.Price.UnreadableReason);
            Assert.Null(actual.Price.Price);
            Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded, actual.InvoiceRate.UnavailableReason);
            var orderRate = Assert.IsType<PurchaseOrderExchangeRateAnswerV1>(actual.ApprovedOrderRate);
            Assert.Equal(PurchaseOrderExchangeRateReasonsV1.RateNotSpecified, orderRate.UnavailableReason);
            Assert.Equal(actual.Price.OrderVersion, orderRate.OrderVersion);
            Assert.Null(await proof.ReadAsync(seed.OrderId, Guid.NewGuid(), acceptanceId));
            Assert.Null(await proof.ReadAsync(Guid.NewGuid(), seed.LineId, acceptanceId));
        }
        await using (var foreign = await PurchaseReceiptCostProof.EnlistAsync(connection, transaction, CompanyB))
            Assert.Null(await foreign.ReadAsync(seed.OrderId, seed.LineId, acceptanceId));
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.NotNull(transaction.Connection);
        await transaction.RollbackAsync();
        await using var scope = run.Store(CompanyA);
        var after = Assert.IsType<StoredPurchaseOrder>(await scope.Store.FindOrderAsync(seed.OrderId));
        Assert.Empty(after.Order.Acceptances);
        Assert.Equal(seed.Version, after.Order.Version);
    }

    [SqlServerFact]
    public async Task Receipt_cost_proof_keeps_discounted_owner_unit_price_and_does_not_replace_missing_invoice_fx()
    {
        await using var run = await Run.StartAsync();
        var orderId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var acceptanceId = Guid.NewGuid();
        long approvedVersion;
        await using (var scope = run.Store(CompanyA))
        {
            var order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier,
                PurchaseOrderMoney.Of(OrderLineCostCurrency, "USD", OrderLineCostProfile, 1, 2, 4), AcceptanceTime);
            var day = DateOnly.FromDateTime(AcceptanceTime.UtcDateTime);
            order.AssignDefaultExchangeRate(new PurchaseOrderExchangeRate(34.5m, SettingsCurrencyIdentitiesV1.TurkishLira,
                PurchaseOrderExchangeRateKind.TcmbForexSelling, day, PurchaseOrderExchangeRateSource.Automatic,
                day.AddDays(-1)), run.Actor, AcceptanceTime);
            order.AddLine(order.Version, lineId, AcceptanceProduct, AcceptanceUnit, Quantity.Of(10m, QuantityUnit.Kilogram),
                null, new PurchaseOrderLinePrice(12.3456m, AcceptanceUnit, 10m, null, 2m), AcceptanceTime);
            await scope.Store.StoreOrderAsync(run.Envelope("cost-proof-create"), Hash("cost-proof-create"), AcceptanceAccount,
                DateOnly.FromDateTime(AcceptanceTime.UtcDateTime), () => order, new Dictionary<Guid, string> { [lineId] = "product-v1" });
            var approved = await scope.Store.MutateOrderAsync(run.Envelope("cost-proof-approve"), Hash("cost-proof-approve"),
                PurchasingCommands.OrderApproveV1, orderId, (candidate, now) => candidate.Approve(candidate.Version, OrderApprover, now,
                    SettingsCurrencyIdentitiesV1.TurkishLira), Approved);
            approvedVersion = approved!.Order.Order.Version;
        }
        var seed = new AcceptanceSeed(orderId, lineId, AcceptanceUnit, approvedVersion);
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, acceptanceId, "4"), Envelope("cost-proof-accept", "cost-proof-accept"));
        var counts = await InvoiceCostRateWriteCountsAsync(run);
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var proof = await PurchaseReceiptCostProof.EnlistAsync(connection, transaction, CompanyA);
            var actual = Assert.IsType<PurchaseReceiptCostProofV1>(await proof.ReadAsync(orderId, lineId, acceptanceId));
            var price = Assert.IsType<PurchaseOrderLineCostPriceV1>(actual.Price.Price);
            Assert.Equal(11.11104m, price.MovingAverageUnitCost);
            Assert.Equal(11.3332608m, price.NetUnitPrice);
            Assert.Equal((OrderLineCostCurrency, "USD"), (actual.InvoiceRate.OrderCurrencyId, actual.InvoiceRate.OrderCurrencyCode));
            Assert.Equal(PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded, actual.InvoiceRate.UnavailableReason);
            var orderRate = Assert.IsType<PurchaseOrderExchangeRateAnswerV1>(actual.ApprovedOrderRate);
            Assert.Equal(PurchaseOrderExchangeRateOutcomeV1.Rate, orderRate.Outcome);
            Assert.Equal((CompanyA, orderId, actual.Price.OrderVersion), (orderRate.CompanyId, orderRate.PurchaseOrderId, orderRate.OrderVersion));
            Assert.Equal(34.5m, orderRate.Rate!.Value.Rate);
            await transaction.RollbackAsync();
        }
        Assert.Equal(counts, await InvoiceCostRateWriteCountsAsync(run));
    }
}
