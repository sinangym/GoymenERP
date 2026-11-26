using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)) over SYNTHETIC owners: a submission and an approval evaluate the customer's risk on the order as
/// it stands before the transition. "Over" or "Unavailable" rides on the same save as a limit warning (row + audit); the order is
/// never blocked and no separate approval is asked. A replay, a stale version or a transition the approval axis refuses asks no
/// owner and leaves no warning; a lost authority stays a 403 and nothing is written. The "Over" / "Within" mapping itself is the
/// domain's (<see cref="SalesOrderRiskWarningTests"/>).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task F018_an_order_without_a_composed_finance_owner_moves_on_with_a_source_unavailable_warning_on_each_transition()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-rw-create"))).Detail.Draft;

        var submitted = await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-rw-submit"));
        var approved = await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-rw-approve")));

        // The warning never blocks: the order is submitted and approved as on main.
        Assert.Equal(("PendingApproval", "Approved"), (submitted.Detail.Draft.ApprovalState, approved.Detail.Draft.ApprovalState));
        Assert.Equal(
            [
                (SalesDraftCommands.SubmitV1, created.Id, 3L, SalesOrderRiskWarnings.SourceUnavailable(Day), CreatePermission),
                (SalesDraftCommands.ApproveV1, created.Id, 4L, SalesOrderRiskWarnings.SourceUnavailable(Day), ApprovePermission),
            ],
            world.Store.RiskWarnings.Select(warning =>
                (warning.Command, warning.DraftId, warning.DraftVersion, warning.Warning, warning.Envelope.PermissionCode)));
        // The warning rides on the transition's own save and envelope; there is no extra transition or approval.
        Assert.Equal(2, world.Store.Transitions.Count);
        Assert.Equal(world.Store.Transitions.Select(transition => transition.Envelope), world.Store.RiskWarnings.Select(warning => warning.Envelope));

        // The same key answers the stored transition again and adds no second warning.
        var replay = await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-rw-submit"));
        Assert.True(replay.IsReplay);
        Assert.Equal(2, world.Store.RiskWarnings.Count);
    }

    [Fact]
    public async Task F018_a_customer_without_a_credit_limit_is_the_credit_limit_not_set_warning_and_a_failing_owner_is_source_unavailable()
    {
        var world = new World { Deliveries = new FakeDeliveries(order => new(Company, order, [])) };
        var partyRisk = new FakePartyRisk(FinanceLike([]));
        world.PartyRisk = partyRisk;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-rw-limit"))).Detail.Draft;

        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-rw-limit-submit"));

        var notSet = Assert.Single(world.Store.RiskWarnings);
        Assert.Equal(
            new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet, Day,
                SalesOrderRiskWarnings.PartyRiskContractV1),
            notSet.Warning);
        Assert.Single(partyRisk.Asked);

        // The finance owner failing at the approval: still approved, the warning names the source, never "within".
        partyRisk.Answer = (_, _) => throw new InvalidOperationException("SENTETIK finance outage");
        var approved = await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-rw-limit-approve")));
        Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);
        Assert.Equal((SalesDraftCommands.ApproveV1, SalesOrderRiskWarningCategory.SourceUnavailable),
            (world.Store.RiskWarnings[1].Command, world.Store.RiskWarnings[1].Warning.Category));
    }

    [Fact]
    public async Task F018_a_stale_version_or_a_refused_transition_asks_no_owner_and_a_rejection_records_no_warning()
    {
        var world = new World { Deliveries = new FakeDeliveries(order => new(Company, order, [])) };
        var partyRisk = new FakePartyRisk(FinanceLike([]));
        world.PartyRisk = partyRisk;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-rw-stale"))).Detail.Draft;

        // A stale submission and an approval of a draft not yet submitted end in their own refusals.
        Assert.Equal(409, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().SubmitDraftAsync(created.Id, Command("9"), Metadata("k-rw-stale-submit")))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Id, Command("2"), Metadata("k-rw-early-approve"))))).Status);
        Assert.Empty(partyRisk.Asked);
        Assert.Empty(world.Store.RiskWarnings);

        // Submitted (one warning), then rejected: the rejection evaluates nothing and records nothing.
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-rw-submit-1"));
        await WithIndependentApproverAsync(world, () => world.Service().RejectDraftAsync(created.Id, Command("3", "SENTETIK ret"), Metadata("k-rw-reject")));
        Assert.Single(partyRisk.Asked);
        Assert.Equal([SalesDraftCommands.SubmitV1], world.Store.RiskWarnings.Select(warning => warning.Command));

        // A resubmission after the rejection is a new submission and is evaluated again.
        await world.Service().SubmitDraftAsync(created.Id, Command("4"), Metadata("k-rw-submit-2"));
        Assert.Equal(2, partyRisk.Asked.Count);
        Assert.Equal([SalesDraftCommands.SubmitV1, SalesDraftCommands.SubmitV1], world.Store.RiskWarnings.Select(warning => warning.Command));
    }

    [Fact]
    public async Task F018_an_authority_lost_during_the_evaluation_stays_403_and_neither_the_transition_nor_a_warning_is_written()
    {
        var world = new World { Deliveries = new FakeDeliveries(order => new(Company, order, [])) };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-rw-lost"))).Detail.Draft;
        var answer = FinanceLike([]);
        world.PartyRisk = new FakePartyRisk((party, account) =>
        {
            // The submission's own name is withdrawn while the finance owner answers; the next fence measures it.
            world.Authority.Denied.Add(CreatePermission);
            return answer(party, account);
        });

        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-rw-lost-submit")));

        Assert.Equal(403, lost.Status);
        Assert.Empty(world.Store.Transitions);
        Assert.Empty(world.Store.RiskWarnings);
    }

    [Fact]
    public async Task F018_recorded_warnings_are_read_newest_first_under_the_draft_read_and_an_unknown_draft_is_404()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-rw-read"))).Detail.Draft;
        var recordedAt = new DateTimeOffset(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);
        world.Store.StoredRiskWarnings.Add((created.Id, new SalesOrderRiskWarningRecord(0, 3, SalesDraftCommands.SubmitV1,
            SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.OverLimit, Actor, recordedAt, Day, SalesOrderRiskWarnings.PartyRiskContractV1)));
        world.Store.StoredRiskWarnings.Add((created.Id, new SalesOrderRiskWarningRecord(0, 4, SalesDraftCommands.ApproveV1,
            SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet, Actor, recordedAt.AddMinutes(5), Day,
            SalesOrderRiskWarnings.PartyRiskContractV1)));
        world.Store.StoredRiskWarnings.Add((Guid.NewGuid(), new SalesOrderRiskWarningRecord(0, 3, SalesDraftCommands.SubmitV1,
            SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.SourceUnavailable, Actor, recordedAt, Day,
            SalesOrderRiskWarnings.PartyRiskContractV1)));
        var attempts = world.Store.Attempts;
        world.Authority.Measured.Clear();

        var read = await world.Service().GetDraftRiskWarningsAsync(created.Id);

        Assert.False(read.Truncated);
        Assert.Equal(
            [
                new SalesOrderDraftRiskWarningV1(0, "4", SalesDraftCommands.ApproveV1, "Unavailable", "CreditLimitNotSet", Actor,
                    recordedAt.AddMinutes(5), Day, "sales.party-risk-read.v1"),
                new SalesOrderDraftRiskWarningV1(0, "3", SalesDraftCommands.SubmitV1, "Over", "OverLimit", Actor, recordedAt, Day,
                    "sales.party-risk-read.v1"),
            ],
            read.Warnings);
        // ASSUMPTION-SAT-RISK-3: the draft read alone (no new name), measured before and after the store; read-only.
        Assert.All(world.Authority.Measured, permission => Assert.Equal(ReadPermission, permission));
        Assert.Equal(attempts, world.Store.Attempts);

        // More rows than one read answers: the newest page and "truncated".
        for (var version = 5; version < 5 + SalesDraftRiskWarningsApiV1.MaximumListed; version++)
        {
            world.Store.StoredRiskWarnings.Add((created.Id, new SalesOrderRiskWarningRecord(0, version, SalesDraftCommands.SubmitV1,
                SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.OverLimit, Actor, recordedAt, Day,
                SalesOrderRiskWarnings.PartyRiskContractV1)));
        }

        var page = await world.Service().GetDraftRiskWarningsAsync(created.Id);
        Assert.True(page.Truncated);
        Assert.Equal(SalesDraftRiskWarningsApiV1.MaximumListed, page.Warnings.Count);
        Assert.Equal((4 + SalesDraftRiskWarningsApiV1.MaximumListed).ToString(System.Globalization.CultureInfo.InvariantCulture),
            page.Warnings[0].DraftVersion);

        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskWarningsAsync(Guid.Empty))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskWarningsAsync(Guid.NewGuid()))).Status);

        world.Authority.Denied.Add(ReadPermission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskWarningsAsync(created.Id))).Status);
        world.Authority.Denied.Clear();

        world.Store.Fault = "synthetic_store_outage";
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRiskWarningsAsync(created.Id))).Status);
    }
}
