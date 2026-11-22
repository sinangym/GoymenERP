using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesPriceFloorExceptionWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_drafts_whose_every_below_floor_line_waits_for_its_exception_are_listed()
    {
        var store = new Store
        {
            Drafts =
            [
                Draft(1, [Line(1, BelowFloor(T0)), Line(2, AboveFloor())]),
                Draft(2, [Line(1, BelowFloor(T0) with { FloorException = Exception(T0) with
                {
                    ApprovedBy = Approver, ApprovedAtUtc = T0, Reason = "SENTETIK gerekce",
                } })]),
                Draft(3, [Line(1, AboveFloor())]),
                Draft(4, [Line(1, BelowFloor(T0) with { FloorException = null })]),
                Draft(5, [Line(1, BelowFloor(T0)), Line(2, BelowFloor(T0) with { FloorException = null })]),
            ],
        };
        var source = Source(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        var item = Assert.Single(result.Items!);
        Assert.Equal(Draft(1, []).Id, item.ItemId);
        Assert.Equal("SO-SNT-0001", item.DocumentLabel);
        Assert.Equal($"/satis/siparisler/{Draft(1, []).Id:D}", item.Link);
        Assert.Equal(T0.ToUniversalTime(), item.WaitingSinceUtc);
        Assert.Single(item.Rules);
        Assert.False(result.HasMore);
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task The_store_is_asked_for_the_callers_company_and_a_foreign_row_fails_closed()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, BelowFloor(T0))], company: OtherCompany)] };

        var result = await Source(store).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
        Assert.Equal(Company, Assert.Single(store.Calls).CompanyId);
    }

    [Fact]
    public async Task Without_the_company_wide_approver_permission_the_source_is_denied_and_reads_nothing()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, BelowFloor(T0))])] };
        var source = Source(store, granted: permission => permission != SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_caller_who_is_not_the_current_user_or_another_company_is_denied()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, BelowFloor(T0))])] };

        var otherUser = await Source(store).ListPendingAsync(Context("SENTETIK-someone-else"), 10, default);
        var otherCompany = await Source(store, company: new CompanyContext(OtherCompany)).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, otherUser.Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, otherCompany.Outcome);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task The_makers_own_draft_is_returned_with_the_requester_as_prepared_by()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, BelowFloor(T0))])] };

        var result = await Source(store, user: Maker).ListPendingAsync(Context(Maker), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal(Maker, item.PreparedBy);
    }

    [Fact]
    public async Task Oldest_first_and_has_more_is_set_when_more_drafts_wait_than_asked()
    {
        var store = new Store
        {
            Drafts =
            [
                Draft(1, [Line(1, BelowFloor(T0.AddHours(2)))]),
                Draft(2, [Line(1, BelowFloor(T0))]),
                Draft(3, [Line(1, BelowFloor(T0.AddHours(1)))]),
            ],
        };

        var result = await Source(store).ListPendingAsync(Context(), 2, default);

        Assert.True(result.HasMore);
        Assert.Equal(new[] { Draft(2, []).Id, Draft(3, []).Id }, result.Items!.Select(item => item.ItemId).ToArray());
    }

    [Fact]
    public async Task Rule_numbers_come_from_the_stored_floor_snapshot_and_are_withheld_without_price_read()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, BelowFloor(T0)), Line(2, BelowFloor(T0.AddMinutes(5)))])] };

        var visible = await Source(store).ListPendingAsync(Context(), 10, default);
        var withheld = await Source(store, granted: permission => permission != SalesPermissionManifestV1.OrdersPriceRead)
            .ListPendingAsync(Context(), 10, default);

        var rules = Assert.Single(visible.Items!).Rules;
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.Equal(SalesPriceFloorExceptionWorkInboxSourceV1.BelowFloorRule, rule.RuleCode);
            Assert.Equal("95", rule.Threshold);
            Assert.Equal("90.5", rule.Actual);
            Assert.Equal("4.5", rule.Deviation);
            Assert.Equal("TRY", rule.Unit);
        });
        var hidden = Assert.Single(Assert.Single(withheld.Items!).Rules.DistinctBy(rule => rule.RuleCode));
        Assert.Equal(new WorkApprovalRuleResultV1(SalesPriceFloorExceptionWorkInboxSourceV1.BelowFloorRule, null, null, null, null),
            hidden);
        AssertContractShape(Source(store), visible);
    }

    [Fact]
    public async Task A_failing_owner_read_is_unavailable()
    {
        var store = new Store { Failure = new InvalidOperationException("SENTETIK") };

        var result = await Source(store).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static SalesPriceFloorExceptionWorkInboxSourceV1 Source(Store store, Func<string, bool>? granted = null,
        CompanyContext? company = null, string user = Approver) =>
        new(store, company ?? new CompanyContext(Company), new User(user, Company), new Authority(granted ?? (_ => true)));

    private static PriceFloorExceptionSnapshot Exception(DateTimeOffset requestedAt) =>
        new(Maker, requestedAt, PriceList, 3, PriceUnit, "90.5", "90.5", "95");

    /// <summary>List 100, floor 95, own price 90.5: below the floor, with a pending exception snapshot.</summary>
    private static CommercialDraftFields BelowFloor(DateTimeOffset requestedAt) =>
        new("90.5", PriceUnit, null, Reference("100", "95"), FloorException: Exception(requestedAt));

    private static CommercialDraftFields AboveFloor() => new("100", PriceUnit, null, Reference("100", "95"));
}
