using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesDiscountExceptionWorkInboxSourceV1Tests
{
    private static readonly Guid LimitProfile = Guid.Parse("5a1e0000-0000-0000-0000-0000000002a1");

    [Fact]
    public async Task Only_drafts_whose_every_over_limit_line_waits_for_its_approval_are_listed()
    {
        var store = new Store
        {
            Drafts =
            [
                Draft(1, [Line(1, OverLimit(T0)), Line(2, AtListPrice())]),
                Draft(2, [Line(1, OverLimit(T0) with { DiscountCheck = Check(T0) with
                {
                    ApprovedBy = Approver, ApprovedAtUtc = T0, Reason = "SENTETIK gerekce",
                } })]),
                Draft(3, [Line(1, AtListPrice())]),
                Draft(4, [Line(1, OverLimit(T0) with { DiscountCheck = null })]),
                Draft(5, [Line(1, OverLimit(T0))], subcontract: true),
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
        Assert.False(result.HasMore);
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task The_store_is_asked_for_the_callers_company_and_a_foreign_row_fails_closed()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, OverLimit(T0))], company: OtherCompany)] };

        var result = await Source(store).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Equal(Company, Assert.Single(store.Calls).CompanyId);
    }

    [Fact]
    public async Task Without_the_company_wide_approver_permission_the_source_is_denied_and_reads_nothing()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, OverLimit(T0))])] };
        var source = Source(store, granted: permission => permission != SalesPermissionManifestV1.OrdersDiscountExceptionApprove);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task The_makers_own_draft_is_returned_with_the_checked_actor_as_prepared_by()
    {
        var store = new Store { Drafts = [Draft(1, [Line(1, OverLimit(T0))])] };

        var result = await Source(store, user: Maker).ListPendingAsync(Context(Maker), 10, default);

        Assert.Equal(Maker, Assert.Single(result.Items!).PreparedBy);
    }

    [Fact]
    public async Task Oldest_first_and_has_more_is_set_when_more_drafts_wait_than_asked()
    {
        var store = new Store
        {
            Drafts =
            [
                Draft(1, [Line(1, OverLimit(T0.AddHours(2)))]),
                Draft(2, [Line(1, OverLimit(T0))]),
            ],
        };

        var result = await Source(store).ListPendingAsync(Context(), 1, default);

        Assert.True(result.HasMore);
        Assert.Equal(Draft(2, []).Id, Assert.Single(result.Items!).ItemId);
    }

    [Fact]
    public async Task Rule_numbers_come_from_the_stored_check_and_lines_without_a_list_price_or_limit_carry_none()
    {
        var store = new Store
        {
            Drafts =
            [
                Draft(1,
                [
                    Line(1, OverLimit(T0)),
                    Line(2, ManualBelowList(T0)),
                    Line(3, OverLimit(T0) with { DiscountCheck = Check(T0) with { LimitPercent = null, LimitProfileId = null, LimitProfileVersion = null } }),
                    Line(4, WithoutListPrice(T0)),
                ]),
            ],
        };
        var source = Source(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var rules = Assert.Single(result.Items!).Rules;
        Assert.Equal(new WorkApprovalRuleResultV1(SalesDiscountExceptionWorkInboxSourceV1.OverLimitRule, "10", "12.5", "2.5", "%"), rules[0]);
        // A manual price below list: the stored percent is not the measured discount, so only the stored limit is shown.
        Assert.Equal(new WorkApprovalRuleResultV1(SalesDiscountExceptionWorkInboxSourceV1.OverLimitRule, "10", null, null, "%"), rules[1]);
        Assert.Equal(new WorkApprovalRuleResultV1(SalesDiscountExceptionWorkInboxSourceV1.LimitUndefinedRule, null, "12.5", null, "%"), rules[2]);
        Assert.Equal(new WorkApprovalRuleResultV1(SalesDiscountExceptionWorkInboxSourceV1.WithoutListPriceRule, null, null, null, null), rules[3]);
        AssertContractShape(source, result);

        var withheld = await Source(store, granted: permission => permission != SalesPermissionManifestV1.OrdersPriceRead)
            .ListPendingAsync(Context(), 10, default);
        var hidden = Assert.Single(withheld.Items!).Rules;
        Assert.Equal(new WorkApprovalRuleResultV1(SalesDiscountExceptionWorkInboxSourceV1.OverLimitRule, "10", null, null, "%"), hidden[0]);
        Assert.Equal(new WorkApprovalRuleResultV1(SalesDiscountExceptionWorkInboxSourceV1.LimitUndefinedRule, null, null, null, null), hidden[2]);
    }

    [Fact]
    public async Task A_failing_owner_read_is_unavailable()
    {
        var result = await Source(new Store { Failure = new InvalidOperationException("SENTETIK") })
            .ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static SalesDiscountExceptionWorkInboxSourceV1 Source(Store store, Func<string, bool>? granted = null,
        string user = Approver) =>
        new(store, new CompanyContext(Company), new User(user, Company), new Authority(granted ?? (_ => true)));

    private static DiscountLimitCheckSnapshot Check(DateTimeOffset checkedAt) =>
        new(Maker, checkedAt, "100", "12.5", null, "87.5", PriceList, 3, "100", "10", LimitProfile, 1, true);

    /// <summary>List 100, 12.5 % off: over the 10 % limit the check recorded.</summary>
    private static CommercialDraftFields OverLimit(DateTimeOffset checkedAt) =>
        new("100", PriceUnit, "12.5", Reference("100", null), DiscountCheck: Check(checkedAt));

    /// <summary>A manual price of 95 against list 100 with a 10 % discount: the measured discount is not the stored percent.</summary>
    private static CommercialDraftFields ManualBelowList(DateTimeOffset checkedAt) =>
        new("95", PriceUnit, "10", Reference("100", null),
            DiscountCheck: new DiscountLimitCheckSnapshot(Maker, checkedAt, "95", "10", null, "85.5", PriceList, 3, "100", "10",
                LimitProfile, 1, true));

    private static CommercialDraftFields WithoutListPrice(DateTimeOffset checkedAt) =>
        new("80", PriceUnit, null,
            DiscountCheck: new DiscountLimitCheckSnapshot(Maker, checkedAt, "80", null, null, "80", null, null, null, "10",
                LimitProfile, 1, true));

    private static CommercialDraftFields AtListPrice() => new("100", PriceUnit, null, Reference("100", null));
}
