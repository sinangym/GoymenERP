using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Purchase_notification_reads_the_recorded_preparer_and_approver_and_refuses_foreign_or_legacy_missing_actors()
    {
        await using Run run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var order = Order(owners, null, null, 10m);
        var approver = Guid.NewGuid();
        var lineId = order.Lines.Single().Id;
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.StoreOrderAsync(run.Envelope("notification-actors-create"), Hash("notification-actors-create"),
                owners.Account, Day, () => order, VersionsOf(order));
            await scope.Store.MutateOrderAsync(run.Envelope("notification-actors-approve", actor: approver.ToString("D")),
                Hash("notification-actors-approve"), PurchasingCommands.OrderApproveV1, order.Id,
                (candidate, now) => candidate.Approve(candidate.Version, approver.ToString("D"), now), Approved);
        }
        var before = await InvoiceCostRateWriteCountsAsync(run);
        var answer = await ReadNotificationRecipientsAsync(run, CompanyA, order.Id, lineId);
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.Found, answer.Outcome);
        Assert.Equal(new[] { Guid.Parse(run.Actor), approver }.Order(), answer.UserIds);
        Assert.Equal(3L, answer.ObservedOrderVersion);
        var foreign = await ReadNotificationRecipientsAsync(run, CompanyB, order.Id, lineId);
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.NotFound, foreign.Outcome);
        Assert.Empty(foreign.UserIds);
        Assert.Null(foreign.ObservedOrderVersion);
        var wrongLine = await ReadNotificationRecipientsAsync(run, CompanyA, order.Id, Guid.NewGuid());
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.NotFound, wrongLine.Outcome);
        Assert.Equal(before, await InvoiceCostRateWriteCountsAsync(run));

        // A pre-approval-history row is valid legacy storage, but its missing approver is never invented.
        await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET ApprovedBy = NULL, ApprovedAtUtc = NULL WHERE CompanyId = @company AND Id = @order",
            ("@company", CompanyA), ("@order", order.Id));
        var legacy = await ReadNotificationRecipientsAsync(run, CompanyA, order.Id, lineId);
        Assert.Equal(PurchaseNotificationRecipientsOutcomeV1.Unassigned, legacy.Outcome);
        Assert.Empty(legacy.UserIds);
    }

    private static async Task<PurchaseNotificationRecipientsV1> ReadNotificationRecipientsAsync(Run run, Guid companyId,
        Guid orderId, Guid lineId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
        return await new PurchaseNotificationRecipients(database, company).ReadAsync(companyId, orderId, lineId);
    }
}
