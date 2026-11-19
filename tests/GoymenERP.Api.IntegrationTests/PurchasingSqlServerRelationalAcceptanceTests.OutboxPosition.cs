using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Purchasing_outbox_cursor_never_passes_an_earlier_uncommitted_revival()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedApprovedAcceptanceOrderAsync(run);
        var second = await SeedApprovedAcceptanceOrderAsync(run);
        var firstAcceptance = Guid.NewGuid(); var secondAcceptance = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(first, firstAcceptance, "4"), Envelope("horizon-source-a", "source-a"));
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(second, secondAcceptance, "4"), Envelope("horizon-source-b", "source-b"));
        foreach (var seed in new[] { first, second })
        {
            await using var stored = run.Store(CompanyA);
            var key = "horizon-cancel-" + seed.OrderId.ToString("N");
            await stored.Store.MutateOrderAsync(run.Envelope(key, "cancel"), AcceptanceHash(key), PurchasingCommands.OrderCancelV1, seed.OrderId,
                (order, now) => order.Cancel(order.Version, "canceller", "Cancel remainder", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "Cancel remainder"));
        }
        await using var firstScope = run.Factory.Services.CreateAsyncScope();
        firstScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var firstConnection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await firstConnection.OpenAsync();
        await using var firstTransaction = (DbTransaction)await firstConnection.BeginTransactionAsync();
        await using var firstOwner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(firstConnection, firstTransaction, CompanyA,
            firstScope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), firstScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        await firstOwner.StageAsync(new(CompanyA, Guid.NewGuid(), first.OrderId, firstAcceptance, "5", "1", false, false,
            "Earlier open transaction", AcceptanceTime.AddMinutes(10), PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("horizon-a"),
            NotificationSnapshots: [new(CompanyA, first.OrderId, first.LineId, "5", [])]),
            Envelope("horizon-a", "revival-a"));
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            await owner.StageAsync(new(CompanyA, Guid.NewGuid(), second.OrderId, secondAcceptance, "5", "1", false, false,
                "Later committed transaction", AcceptanceTime.AddMinutes(11), PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("horizon-b"),
                NotificationSnapshots: [new(CompanyA, second.OrderId, second.LineId, "5", [])]),
                Envelope("horizon-b", "revival-b"));
            await transaction.CommitAsync();
        }
        // A timestamp or identity cursor could publish B now and lose A permanently when A later commits.
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyA, verifyReadIsolation: true));
        await firstTransaction.CommitAsync();
        var published = await ReadPurchasingOutboxAsync(run, CompanyA);
        Assert.Equal(2, published.Count);
        Assert.Equal(new[] { "revival-a", "revival-b" }, published.Select(item => item.CorrelationId));
        Assert.True(StringComparer.Ordinal.Compare(published[0].Position, published[1].Position) < 0);
        var next = Assert.Single(await ReadPurchasingOutboxAsync(run, CompanyA, published[0].Position));
        Assert.Equal(published[1].EventId, next.EventId);
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyA, published[1].Position));
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyB));
    }
}
