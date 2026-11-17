using System.Data;
using System.Data.Common;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Receipt_cost_host_reads_the_uncommitted_acceptance_and_keeps_unknown_prices_null_without_owning_the_transaction()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "4", await run.LocationOfAsync(CompanyA));
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var services = new ReceiptCostActorServices(scope.ServiceProvider, Company(CompanyA), new FixedCurrentUser(run.Actor, CompanyA));
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using (var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA))
            await purchase.StageAsync(GoodsReceiptPurchaseRequest(seed, acceptanceId, "4"),
                GoodsReceiptPurchaseEnvelope(run, "SENTETIK-cost-scope", run.Correlation));
        await using (var cost = await PurchaseReceiptCostScope.EnlistAsync(connection, transaction, CompanyA, services, default))
        {
            var evidence = await cost.Source.ReadAsync(new(CompanyA, acceptanceId, seed.OrderId, seed.LineId,
                receipt.ProductId, receipt.ExpectedProductVersion, receipt.OccurredAtUtc));
            Assert.Equal((CompanyA, acceptanceId, seed.OrderId, seed.LineId),
                (evidence.CompanyId, evidence.AcceptanceId, evidence.PurchaseOrderId, evidence.PurchaseOrderLineId));
            Assert.Null(evidence.TlUnitCostPerKg);
            Assert.Null(evidence.UsdUnitCostPerKg);
            Assert.False(string.IsNullOrWhiteSpace(evidence.TlUnavailableReason));
            Assert.NotNull(evidence.FirstEntryRate);
            Assert.Contains(acceptanceId.ToString("D"), evidence.EvidenceJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.NotNull(transaction.Connection);
        await transaction.RollbackAsync();
        Assert.Equal(new ReceiptEffects(0, 3L, 0, 0, 0, 0, 0, 0, 0, 0),
            await ReceiptEffectsAsync(run, seed, receipt, run.Correlation));
    }

    // Only request identity is synthetic; the cost scope and Purchasing proof use the real owner connection.
    private sealed class ReceiptCostActorServices(IServiceProvider services, ICompanyContext company, ICurrentUser user) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(ICompanyContext) ? company
            : type == typeof(ICurrentUser) ? user : services.GetService(type);
    }
}
