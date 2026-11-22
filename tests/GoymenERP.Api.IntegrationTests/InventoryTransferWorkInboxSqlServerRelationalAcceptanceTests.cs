using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP6 actual SQL (AI-D31, KLN-D23 (c), KLN-D97): the transfer loss Work inbox source reads the module's own tables under the
/// real model and company filter — an open loss request is listed with the requester as the owner's maker, the destination
/// location as scope and the dispatch kilograms as the rule number; once decided it is gone, and a renewal waits anew. Another
/// company's read sees nothing. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_loss_work_inbox_source_lists_an_open_request_with_its_maker_and_drops_it_once_decided()
    {
        await using var run = await Run.StartAsync();
        var (plan, lineId) = await DispatchedLossTransferAsync(run, "SYN-TRANSFER-LOSS-INBOX");
        var maker = Metadata(run);
        await LossRequestAsync(run, plan.TransferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalExplanation), maker);
        var approver = Guid.NewGuid();

        var open = await ListLossInboxAsync(run, CompanyA, approver);
        var otherCompany = await ListLossInboxAsync(run, CompanyB, approver);

        var item = Assert.Single(open.Items!);
        Assert.Equal((InventoryWorkInboxFence.TransferLineItemId("inventory.transfer-loss.v1", plan.TransferId, lineId),
                maker.ActorId, "/stok/transferler/kayip-onaylari", TimeSpan.Zero),
            (item.ItemId, item.PreparedBy, item.Link, item.WaitingSinceUtc.Offset));
        Assert.Equal(plan.DestinationLocationId, item.Scope!.LocationId);
        var rule = Assert.Single(item.Rules);
        Assert.Equal(("inventory.transfer-loss-request", "kg"), (rule.RuleCode, rule.Unit));
        Assert.True(decimal.Parse(rule.Actual!, System.Globalization.CultureInfo.InvariantCulture) > 0m);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, otherCompany.Outcome);
        Assert.Empty(otherCompany.Items!);

        await LossDecisionAsync(run, plan.TransferId, lineId, new DecideInventoryTransferLossV1("2",
            InventoryTransferDecisionKindV1.Reject, At.AddHours(3), LossRenewalReason), LossChecker(run));
        var decided = await ListLossInboxAsync(run, CompanyA, approver);
        await LossRequestAsync(run, plan.TransferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(4), LossRenewalSecondExplanation), Metadata(run));
        var renewed = await ListLossInboxAsync(run, CompanyA, approver);

        Assert.Empty(decided.Items!);
        Assert.Equal(["inventory.transfer-loss-request", "inventory.transfer-loss-renewed"],
            Assert.Single(renewed.Items!).Rules.Select(candidate => candidate.RuleCode));
    }

    private static async Task<WorkInboxSourceResultV1> ListLossInboxAsync(Run run, Guid company, Guid approver)
    {
        await using var context = run.Context(company);
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        var fence = new InventoryWorkInboxFence(companyContext, new FixedCurrentUser(approver, company),
            new CompanyWideWorkInboxAuthority());
        var source = new InventoryTransferLossWorkInboxSourceV1(context, fence);
        return await source.ListPendingAsync(
            new WorkInboxSourceContextV1(company, approver.ToString("D"), "SENTETIK-wp6-inbox", null), 10,
            CancellationToken.None);
    }

    private sealed class CompanyWideWorkInboxAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
