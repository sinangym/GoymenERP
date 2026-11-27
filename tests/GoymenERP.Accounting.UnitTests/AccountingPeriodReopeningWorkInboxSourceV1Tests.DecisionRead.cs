using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Audit.Application.Contracts;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingPeriodReopeningWorkInboxSourceV1Tests
{
    [Theory] [InlineData("none")] [InlineData("same-company")] [InlineData("foreign-company")] [InlineData("already-reopened")]
    [InlineData("cancel-after-max")]
    public async Task Owner_scalar_later_closed_flag_matches_existing_inbox_rule_without_broad_history_or_cross_company_rows(string scenario)
    {
        await using var kit = new Kit();
        var (period, close) = Closed(new(2026, 10, 1), 3); var proposal = Proposal(period, close, Kit.Start, Kit.Maker);
        await kit.SeedAsync(Kit.Company, db => { db.Periods.Add(period); db.PeriodClosures.Add(close); db.PeriodReopenings.Add(proposal); });
        if (scenario != "none" && scenario != "cancel-after-max")
        {
            var company = scenario == "foreign-company" ? Kit.OtherCompany : Kit.Company;
            var (later, laterClose) = Closed(new(2026, 11, 1), 3, company);
            if (scenario == "already-reopened")
            {
                laterClose.ReopeningId = Guid.NewGuid(); laterClose.ReopenedBy = Kit.Caller;
                laterClose.ReopenedAtUtc = Kit.Start.ToUniversalTime(); laterClose.Version = 2;
            }
            await kit.SeedAsync(company, db => { db.Periods.Add(later); db.PeriodClosures.Add(laterClose); });
        }
        var inbox = await Source(kit).ListPendingAsync(Kit.Context(), 10, default);
        var item = Assert.Single(inbox.Items!); var expected = scenario == "same-company";
        Assert.Equal(expected, item.Rules.Any(rule => rule.RuleCode == AccountingPeriodReopeningWorkInboxSourceV1.LaterPeriodClosedRule));
        var companyContext = new CompanyContext(); companyContext.Set(Kit.Company);
        var core = new AccountingStore(kit.Db, companyContext, new NoDecisionWriteCollector(), TimeProvider.System,
            new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Kit.Company, 2)));
        using var cancellation = new CancellationTokenSource(); var callbacks = 0;
        Task Fence(CancellationToken _)
        {
            callbacks++;
            if (scenario == "cancel-after-max" && callbacks == 5) cancellation.Cancel();
            return Task.CompletedTask;
        }
        if (scenario == "cancel-after-max")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.ReadReopeningDecisionAsync(proposal.Id, Fence, cancellation.Token));
        else
        {
            var source = await core.ReadReopeningDecisionAsync(proposal.Id, Fence);
            Assert.NotNull(source); Assert.Equal(expected, source.LaterPeriodClosed);
            Assert.DoesNotContain("LaterPeriodClosed", JsonSerializer.Serialize(source), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(5, callbacks); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }
    private sealed class NoDecisionWriteCollector : IAuditEventCollector
    { public void Enqueue(AuditEventDraft draft) => throw new InvalidOperationException("A decision read cannot write Audit."); }
}
