using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D2 / API-12 (<c>SINAN_KARARLARI_2026-10-25_SATIS.md</c>) with SAT-D34…D37 and SAT-D42/D43: a price below its list
/// price (before the surcharge) is checked against the HIGHEST limit of the actor's groups — read only from the Settings Sales
/// policy port — and an over-limit (or limit-less, or list-less) price blocks the submission until another person with
/// <c>sales.orders.discount-exception.approve</c> approves it with a reason. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid DiscountGroupA = Guid.Parse("d15c0a00-0000-4000-8000-00000000000a");
    private static readonly Guid DiscountGroupB = Guid.Parse("d15c0b00-0000-4000-8000-00000000000b");

    /// <summary>A line priced from the SENTETIK list (<see cref="FakePriceReferences"/>): its own price, adjustments and the list version.</summary>
    private static SalesDraftLineCommercialV1 FromList(Guid lineId, string price, string? percent = null, string? amount = null,
        string? surcharge = null) =>
        new(lineId, price, KgUnit, percent, new SalesDraftPriceReferenceRequestV1(PriceList, 1), amount, surcharge);

    /// <summary>
    /// SAT-D43: a priced line without a list price goes to approval; tests whose subject is something else approve it first,
    /// as another person, and continue as the original actor. Returns the draft version after the approval.
    /// </summary>
    private static async Task<string> ApproveDiscountChecksAsync(World world, Guid draftId, string version, params Guid[] lineIds)
    {
        var actor = world.User.Id;
        world.User.Id = "synthetic-discount-manager";
        try
        {
            var approved = await world.Service().ApproveDiscountExceptionsAsync(draftId, new SalesDraftDiscountExceptionApprovalRequest(version,
                [.. lineIds.Select(lineId => new SalesDraftDiscountExceptionApprovalLineV1(lineId, "SENTETIK liste dışı fiyat"))]),
                Metadata($"k-discount-checks-{draftId:N}-{version}"));
            return approved.Detail.Draft.Version;
        }
        finally
        {
            world.User.Id = actor;
        }
    }

    private static SalesDraftDiscountExceptionApprovalRequest DiscountApproval(string version, Guid lineId, string reason = "SENTETIK müşteri anlaşması") =>
        new(version, [new SalesDraftDiscountExceptionApprovalLineV1(lineId, reason)]);

    [Fact]
    public async Task Without_a_limit_owner_every_positive_discount_waits_for_a_different_approvers_reasoned_approval()
    {
        // No limit-group source is composed: no limit is defined for anyone (SAT-D2, fail-closed) and Settings is never asked.
        var policies = new FakeDiscountLimitPolicies();
        var world = new World { SalesPolicies = policies };
        var created = (await world.Service().CreateDraftAsync(Priced(discount: "5"), Metadata("k-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        Assert.Empty(policies.Asked);

        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal((Actor, (string?)null, true, (string?)null, (string?)null),
            (check.CheckedBy, check.LimitPercent, check.RequiresApproval, check.ApprovedBy, check.Reason));

        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            Command(created.Draft.Version), Metadata("k-submit-blocked")));
        Assert.Equal((422, "draft", SalesRuleCodes.DiscountExceptionPending),
            (blocked.Status, Assert.Single(blocked.Errors).Field, blocked.Errors[0].Code));

        // Another actor without the dedicated permission is refused before anything is read or recorded.
        world.User.Id = "synthetic-manager";
        world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(created.Draft.Version, lineId), Metadata("k-forbidden")));
        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);

        world.Authority.Denied.Clear();
        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(created.Draft.Version, lineId, "  "), Metadata("k-blank")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionReasonRequired, Assert.Single(blank.Errors).Code);
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval("99", lineId), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));

        var approved = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId), Metadata("k-approve"));
        var approvedCheck = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("synthetic-manager", "SENTETIK müşteri anlaşması", Actor),
            (approvedCheck.ApprovedBy, approvedCheck.Reason, approvedCheck.CheckedBy));
        var transition = Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
        Assert.Equal(("sales.order-draft.discount-exceptions-approved.v1", SalesPermissionManifestV1.OrdersDiscountExceptionApprove),
            (transition.Audit.EventType, transition.Envelope.PermissionCode));

        // The same key, body and actor answers the first result again; another body under it is one 409.
        var replay = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId), Metadata("k-approve"));
        Assert.True(replay.IsReplay);
        Assert.Equal(JsonSerializer.Serialize(approved.Detail, SalesDraftJson.Options), JsonSerializer.Serialize(replay.Detail, SalesDraftJson.Options));
        var changed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(created.Draft.Version, lineId, "SENTETIK başka gerekçe"), Metadata("k-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (changed.Status, changed.Code));
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);

        // A second approval of an approved exception is refused; the draft is submitted by the preparer.
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(approved.Detail.Draft.Version, lineId), Metadata("k-again")));
        Assert.Equal(SalesRuleCodes.RevisionNoChange, Assert.Single(again.Errors).Code);
        world.User.Id = Actor;
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(approved.Detail.Draft.Version), Metadata("k-submit"));
        Assert.Equal("PendingApproval", submitted.Detail.Draft.ApprovalState);
    }

    [Theory]
    [InlineData("12.50", "10", null, null, false)]
    [InlineData("12.50", "10.01", null, null, true)]
    [InlineData("12.50", "9.99", null, null, false)]
    [InlineData("12.50", null, "1.25", null, false)]
    [InlineData("12.50", null, "1.26", null, true)]
    // SAT-D35: a manual price below the list price is a discount with no discount field at all.
    [InlineData("11.25", null, null, null, false)]
    [InlineData("11.24", null, null, null, true)]
    // SAT-D42: the price before the maturity surcharge is measured — the surcharge cannot hide the 15 %.
    [InlineData("12.50", "15", null, "20", true)]
    public async Task The_limit_boundary_is_inclusive_measured_from_the_list_price_before_the_surcharge(
        string price, string? percent, string? amount, string? surcharge, bool requiresApproval)
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } }.AllowDiscountsUpTo(10m);
        world.Clock = new IstanbulDayClock(Day);
        var policies = (FakeDiscountLimitPolicies)world.SalesPolicies!;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        Assert.Empty(policies.Asked);

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            FromList(lineId, price, percent, amount, surcharge)), Metadata("k-revise"));

        Assert.Equal((DiscountGroupA, Day), Assert.Single(policies.Asked));
        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("12.5", "10", requiresApproval, Actor), (check.ListUnitPrice, check.LimitPercent, check.RequiresApproval, check.CheckedBy));
        if (requiresApproval)
        {
            var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
                Command(revised.Detail.Draft.Version), Metadata("k-submit")));
            Assert.Equal(SalesRuleCodes.DiscountExceptionPending, Assert.Single(blocked.Errors).Code);
        }
        else
        {
            var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(revised.Detail.Draft.Version), Metadata("k-submit"));
            Assert.Equal("PendingApproval", submitted.Detail.Draft.ApprovalState);
            // Nothing over the limit: there is no exception to approve.
            world.User.Id = "synthetic-manager";
            var none = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
                created.Draft.Id, DiscountApproval(submitted.Detail.Draft.Version, lineId), Metadata("k-none")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (none.Status, none.Code));
        }
    }

    [Fact]
    public async Task A_price_at_the_list_price_needs_no_check_and_reads_no_limit()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } }.AllowDiscountsUpTo(10m);
        var policies = (FakeDiscountLimitPolicies)world.SalesPolicies!;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            FromList(created.Lines[0].LineId, "12.50", surcharge: "3")), Metadata("k-revise"));

        Assert.Empty(policies.Asked);
        Assert.Null(Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck);
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command(revised.Detail.Draft.Version), Metadata("k-submit"));
    }

    [Fact]
    public async Task Several_limit_groups_apply_the_highest_defined_limit_and_a_group_without_one_contributes_nothing()
    {
        var policies = new FakeDiscountLimitPolicies { Limits = { [DiscountGroupA] = 5m, [DiscountGroupB] = 15m } };
        var world = new World
        {
            PriceReferences = new FakePriceReferences { Price = 12.50m },
            SalesPolicies = policies,
            DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA, DiscountGroupB]),
            Clock = new IstanbulDayClock(Day),
        };

        async Task<SalesDraftDiscountLimitCheckV1> CheckAsync(string key, string percent)
        {
            var created = (await world.Service().CreateDraftAsync(Request(), Metadata($"k-create-{key}"))).Detail;
            await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
                FromList(created.Lines[0].LineId, "12.50", percent)), Metadata($"k-revise-{key}"));
            return Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        }

        // SAT-D34: Satış 5 %, Bölge 15 % → up to 15 % without approval.
        var highest = await CheckAsync("both", "12");
        Assert.Equal(("15", false), (highest.LimitPercent, highest.RequiresApproval));
        Assert.Equal([(DiscountGroupA, Day), (DiscountGroupB, Day)], policies.Asked);

        // A group without an approved limit ("not found") contributes nothing: the other group's limit applies.
        policies.Limits.Remove(DiscountGroupB);
        var one = await CheckAsync("one", "6");
        Assert.Equal(("5", true), (one.LimitPercent, one.RequiresApproval));

        // An inactive policy, or one not in force on the business date, contributes nothing either.
        policies.Limits[DiscountGroupB] = 50m;
        policies.Inactive.Add(DiscountGroupB);
        Assert.Equal("5", (await CheckAsync("inactive", "1")).LimitPercent);
        policies.Inactive.Clear();
        policies.StartsAfterTheBusinessDate.Add(DiscountGroupB);
        Assert.Equal("5", (await CheckAsync("not-yet", "1")).LimitPercent);

        // No group with a defined limit: no limit at all, every discount to approval (SAT-D36).
        policies.Limits.Clear();
        var none = await CheckAsync("none", "1");
        Assert.Equal(((string?)null, true), (none.LimitPercent, none.RequiresApproval));
    }

    [Fact]
    public async Task Sat_d81_the_limit_is_read_on_the_day_the_order_is_entered_or_revised_never_on_its_later_business_date()
    {
        // Seller limit 5 % now; 30 % is scheduled (SAT-D78) from day +10. The order is dated day +20, i.e. after the scheduled
        // change, but it is entered and revised today: today's 5 % applies and a 12 % discount goes to approval.
        var policies = new FakeDiscountLimitPolicies
        {
            Limits = { [DiscountGroupA] = 5m },
            ScheduledFrom = { [DiscountGroupA] = (Day.AddDays(10), 30m) },
        };
        var clock = new IstanbulDayClock(Day);
        var world = new World
        {
            PriceReferences = new FakePriceReferences { Price = 12.50m },
            SalesPolicies = policies,
            DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA]),
            Clock = clock,
        };
        var created = (await world.Service().CreateDraftAsync(Request() with { BusinessDate = Day.AddDays(20) },
            Metadata("k-create-d81"))).Detail;
        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            FromList(created.Lines[0].LineId, "12.50", "12")), Metadata("k-revise-d81"));

        var today = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("5", true), (today.LimitPercent, today.RequiresApproval));
        Assert.Equal([(DiscountGroupA, Day)], policies.Asked);

        // A commercial revision on the day the scheduled limit is in force is checked against it (30 %): no approval.
        clock.Day = Day.AddDays(10);
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(revised.Detail.Draft.Version,
            FromList(created.Lines[0].LineId, "12.50", "11")), Metadata("k-revise-d81-later"));
        var later = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("30", false), (later.LimitPercent, later.RequiresApproval));
        Assert.Equal((DiscountGroupA, Day.AddDays(10)), policies.Asked[^1]);
    }

    [Theory]
    [InlineData("settings_provider_unavailable", 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    [InlineData("settings_profile_invalid", 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    [InlineData("settings_sales_policy_request_invalid", 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    [InlineData("settings_access_denied", 403, "settings_access_denied")]
    public async Task A_limit_the_Settings_owner_cannot_answer_refuses_a_measured_discount_and_records_nothing(
        string settingsCode, int status, string code)
    {
        var policies = new FakeDiscountLimitPolicies { Faults = { [DiscountGroupA] = settingsCode } };
        var world = new World
        {
            PriceReferences = new FakePriceReferences { Price = 12.50m },
            SalesPolicies = policies,
            DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA]),
        };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, FromList(created.Lines[0].LineId, "12.50", "5")), Metadata("k-revise")));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);

        // The same owner state does not stop a line at its list price, nor a manual price (SAT-D43: it goes to approval
        // without a limit): nothing about a limit is read for either.
        policies.Asked.Clear();
        var atList = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, FromList(created.Lines[0].LineId, "12.50")), Metadata("k-revise-list"));
        var manual = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(atList.Detail.Draft.Version, new SalesDraftLineCommercialV1(created.Lines[0].LineId, "12.50", KgUnit, "0")),
            Metadata("k-revise-manual"));
        Assert.Empty(policies.Asked);
        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(((string?)null, true), (check.ListUnitPrice, check.RequiresApproval));
        Assert.NotNull(manual);
    }

    [Theory]
    [InlineData(true, 403, "settings_access_denied")]
    [InlineData(false, 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    public async Task A_binding_owner_that_cannot_answer_is_never_read_as_no_group(bool denied, int status, string code)
    {
        // SAT-D63/D66: the host's adapter over the Settings person → group bindings fails as the port's own exception.
        var policies = new FakeDiscountLimitPolicies { Limits = { [DiscountGroupA] = 50m } };
        var world = new World
        {
            PriceReferences = new FakePriceReferences { Price = 12.50m },
            SalesPolicies = policies,
            DiscountLimitGroups = new FailingDiscountLimitGroups(denied),
        };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, FromList(created.Lines[0].LineId, "12.50", "5")), Metadata("k-revise")));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.Empty(policies.Asked);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    [Fact]
    public async Task Limit_groups_without_a_Settings_source_are_503_and_never_a_guessed_limit()
    {
        var world = new World
        {
            PriceReferences = new FakePriceReferences { Price = 12.50m },
            DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA]),
        };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, FromList(created.Lines[0].LineId, "12.50", "5")), Metadata("k-revise")));

        Assert.Equal((503, SalesDraftProblemCodesV1.DiscountLimitUnavailable), (refused.Status, refused.Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    [Fact]
    public async Task A_manual_price_without_a_list_price_needs_approval_even_with_no_discount()
    {
        var world = new World().AllowDiscountsUpTo(100m);
        var created = (await world.Service().CreateDraftAsync(Priced(discount: null), Metadata("k-create"))).Detail;

        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(((string?)null, (string?)null, true), (check.ListUnitPrice, check.LimitPercent, check.RequiresApproval));
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            Command(created.Draft.Version), Metadata("k-submit")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, Assert.Single(blocked.Errors).Code);
    }

    [Fact]
    public async Task A_no_list_price_may_be_self_approved_but_a_listed_over_limit_discount_needs_another_person()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } }.AllowDiscountsUpTo(10m);

        // SAT-D45: an unlisted line, with or without a discount field, approved by its own writer holding the permission.
        foreach (var (key, discount) in new[] { ("with", (string?)"5"), ("without", null) })
        {
            var unlisted = (await world.Service().CreateDraftAsync(Priced(discount: discount), Metadata($"k-unlisted-{key}"))).Detail;
            var self = await world.Service().ApproveDiscountExceptionsAsync(unlisted.Draft.Id,
                DiscountApproval(unlisted.Draft.Version, unlisted.Lines[0].LineId), Metadata($"k-self-{key}"));
            Assert.Equal(Actor, Assert.Single((await world.Service().GetDraftLinePricingAsync(unlisted.Draft.Id)).Lines).DiscountCheck!.ApprovedBy);
            await world.Service().SubmitDraftAsync(unlisted.Draft.Id, Command(self.Detail.Draft.Version), Metadata($"k-submit-{key}"));
        }

        // SAT-D37: a listed line over the limit is refused to the actor who set the discount ...
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-listed"))).Detail;
        var lineId = created.Lines[0].LineId;
        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            FromList(lineId, "12.50", "15")), Metadata("k-revise"));
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(revised.Detail.Draft.Version, lineId), Metadata("k-self-listed")));
        Assert.Equal((422, "lines[0].reason", SalesRuleCodes.DiscountExceptionSameActor),
            (refused.Status, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));

        // ... and approved by another person.
        world.User.Id = "synthetic-manager";
        var approved = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(revised.Detail.Draft.Version, lineId), Metadata("k-other-listed"));
        world.User.Id = Actor;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command(approved.Detail.Draft.Version), Metadata("k-submit-listed"));
    }

    [Fact]
    public async Task A_priced_line_converted_from_a_quote_has_no_list_price_and_goes_to_approval()
    {
        // SAT-D43: a quote conversion creates the order's lines through the draft's create path, without a list-price reference:
        // every priced line needs approval, whatever its discount; SAT-D45: its converter may approve it.
        var world = new World().AllowDiscountsUpTo(100m);
        var policies = (FakeDiscountLimitPolicies)world.SalesPolicies!;
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        var quote = (await world.Service().CreateQuoteAsync(QuoteOf([request], discount: null), Metadata("q-1"))).Detail.Quote.Id;
        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));

        var order = (await world.Service().ConvertQuoteAsync(quote, new SalesQuoteConversionRequest("1", Day.AddDays(1)),
            Metadata("q-convert"))).Detail;

        Assert.Empty(policies.Asked);
        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(order.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal((Actor, (string?)null, (string?)null, true), (check.CheckedBy, check.ListUnitPrice, check.LimitPercent, check.RequiresApproval));
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(order.Draft.Id,
            Command(order.Draft.Version), Metadata("k-submit-blocked")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, Assert.Single(blocked.Errors).Code);

        var approved = await world.Service().ApproveDiscountExceptionsAsync(order.Draft.Id,
            DiscountApproval(order.Draft.Version, order.Lines[0].LineId), Metadata("k-self"));
        await world.Service().SubmitDraftAsync(order.Draft.Id, Command(approved.Detail.Draft.Version), Metadata("k-submit"));
    }

    [Fact]
    public async Task A_commercial_revision_replaces_an_approved_discount_exception_and_blocks_the_submission_again()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(discount: "5"), Metadata("k-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        world.User.Id = "synthetic-manager";
        var approved = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId), Metadata("k-approve"));
        world.User.Id = "synthetic-pricer";

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(approved.Detail.Draft.Version, new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, "6")), Metadata("k-revise"));

        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("synthetic-pricer", true, (string?)null), (check.CheckedBy, check.RequiresApproval, check.ApprovedBy));
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            Command(revised.Detail.Draft.Version), Metadata("k-submit")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, Assert.Single(blocked.Errors).Code);

        // Removing the price removes the check: nothing waits any more.
        var cleared = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(revised.Detail.Draft.Version, new SalesDraftLineCommercialV1(lineId)), Metadata("k-clear"));
        Assert.Null(Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck);
        world.User.Id = Actor;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command(cleared.Detail.Draft.Version), Metadata("k-submit-2"));
    }

    [Fact]
    public async Task An_approval_names_every_current_over_limit_line_exactly_once()
    {
        var world = new World();
        var request = Request(lines: 2);
        request = request with
        {
            Lines = [request.Lines[0] with { UnitPrice = "12.50", PriceUnitId = KgUnit, DiscountPercent = "5" },
                request.Lines[1] with { UnitPrice = "12.50", PriceUnitId = KgUnit, DiscountPercent = "3" }],
        };
        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-create"))).Detail;
        world.User.Id = "synthetic-manager";

        foreach (var lines in new[]
                 {
                     new[] { created.Lines[0].LineId },
                     new[] { created.Lines[0].LineId, created.Lines[0].LineId },
                     new[] { created.Lines[0].LineId, Guid.NewGuid() },
                 })
        {
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
                created.Draft.Id, new SalesDraftDiscountExceptionApprovalRequest(created.Draft.Version,
                    [.. lines.Select(id => new SalesDraftDiscountExceptionApprovalLineV1(id, "SENTETIK"))]),
                Metadata($"k-mismatch-{lines.Length}-{lines[^1]}")));
            Assert.Equal((422, SalesRuleCodes.DiscountExceptionLinesMismatch), (refused.Status, Assert.Single(refused.Errors).Code));
        }

        var both = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            new SalesDraftDiscountExceptionApprovalRequest(created.Draft.Version,
                [new(created.Lines[1].LineId, "SENTETIK ikinci"), new(created.Lines[0].LineId, "SENTETIK birinci")]), Metadata("k-both"));
        var pricing = (await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines;
        Assert.Equal(["SENTETIK birinci", "SENTETIK ikinci"], pricing.Select(line => line.DiscountCheck!.Reason));
        Assert.Equal(((long)int.Parse(created.Draft.Version) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            both.Detail.Draft.Version);
    }

    // Review B (#834): the discount-exception approval's own race, drift, withheld-replay and revoked-approver lines, the
    // twins of the price-floor approval's tests in SalesDraftServiceTests.LinePricing.cs.

    [Fact]
    public async Task A_lost_same_key_discount_approval_race_answers_the_winners_recorded_result()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(discount: "5"), Metadata("k-race-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        world.User.Id = "synthetic-discount-approver";
        var approved = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId), Metadata("k-race-approve"));
        world.User.Id = Actor;
        // The live draft advances past the approval, so only the winner's immutable result can answer the loser.
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command(approved.Detail.Draft.Version), Metadata("k-race-submit"));
        world.User.Id = "synthetic-discount-approver";

        world.Store.Race = "floor-result-at-refusal";
        var raced = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId), Metadata("k-race-loser"));

        Assert.Equal((true, "winner-floor-correlation", JsonSerializer.Serialize(approved.Detail, SalesDraftJson.Options)),
            (raced.IsReplay, raced.OriginalCorrelationId, JsonSerializer.Serialize(raced.Detail, SalesDraftJson.Options)));
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_discount_result_replay_denies_identity_or_company_drift_during_the_final_price_read(string drift)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(discount: "5"), Metadata("k-drift-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        world.User.Id = "synthetic-drift-approver";
        await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId, "SENTETIK drift anlaşması"), Metadata("k-drift-discount"));

        world.Authority.OnRead = (permission, _) =>
        {
            if (permission != PriceReadPermission) return;
            if (drift == "actor") world.User.Id = "synthetic-swapped-during-price-read";
            else world.User.Company = Guid.NewGuid();
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(created.Draft.Version, lineId, "SENTETIK drift anlaşması"), Metadata("k-drift-discount")));
        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
    }

    [Fact]
    public async Task A_discount_result_replay_refreshes_own_support_restriction_while_price_denial_stays_withheld()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(discount: "5"), Metadata("k-own-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        // SAT-D45: the unlisted line's own writer approves it.
        var approved = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId, "SENTETIK own anlaşması"), Metadata("k-own-discount"));
        Assert.Equal((true, false), (approved.Detail.IsOwnDraft, approved.Detail.IsOwnDecisionRestricted));

        world.ActiveRoleCodes.Codes.Add(GoymenERP.BuildingBlocks.Security.ErpPersonaRoleCodes.SalesSupport);
        world.Authority.Denied.Add(PriceReadPermission);
        var replay = await world.Service().ApproveDiscountExceptionsAsync(created.Draft.Id,
            DiscountApproval(created.Draft.Version, lineId, "SENTETIK own anlaşması"), Metadata("k-own-discount"));

        Assert.Equal((true, true, true), (replay.IsReplay, replay.Detail.IsOwnDraft, replay.Detail.IsOwnDecisionRestricted));
        Assert.All(replay.Detail.Lines, line =>
        {
            Assert.Equal(SalesPriceDisclosure.Withheld, line.PriceDisclosure);
            Assert.Null(line.UnitPrice);
            Assert.Null(line.PriceUnitId);
            Assert.Null(line.DiscountPercent);
        });
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
    }

    [Fact]
    public async Task A_discount_exception_refuses_its_listed_discount_writer_and_an_approver_revoked_after_the_reads_without_an_effect()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } }.AllowDiscountsUpTo(10m);
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-revoke-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            FromList(lineId, "12.50", "15")), Metadata("k-revoke-revise"));
        var request = DiscountApproval(revised.Detail.Draft.Version, lineId, "SENTETIK gerekçe");

        var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, request, Metadata("k-revoke-same")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionSameActor, Assert.Single(same.Errors).Code);

        world.User.Id = "synthetic-discount-approver";
        world.Store.OnStore = () => world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        var revoked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, request, Metadata("k-revoke-approver")));
        Assert.Equal((403, "forbidden"), (revoked.Status, revoked.Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApproveDiscountExceptionsV1);
        Assert.Null(Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!.ApprovedBy);
    }

    /// <summary>A binding owner that cannot answer (denied or unavailable).</summary>
    private sealed class FailingDiscountLimitGroups(bool denied) : ISalesDiscountLimitGroupSourceV1
    {
        public Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<Guid>>(new SalesDiscountLimitGroupSourceException(denied));
    }

    /// <summary>The person → limit group bindings (SAT-D63, the Settings owner's record), as a fixed answer.</summary>
    private sealed class FakeDiscountLimitGroups(IReadOnlyList<Guid> groups) : ISalesDiscountLimitGroupSourceV1
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(groups);
        }
    }

    /// <summary>The Settings owner's Sales policy port: a limit per group, a fault per group, or "not found".</summary>
    private sealed class FakeDiscountLimitPolicies : ISettingsSalesPolicyProviderV1
    {
        public Dictionary<Guid, decimal> Limits { get; } = [];
        public Dictionary<Guid, string> Faults { get; } = [];
        public HashSet<Guid> Inactive { get; } = [];
        public HashSet<Guid> StartsAfterTheBusinessDate { get; } = [];

        /// <summary>SAT-D78: a later record per group, in force from its date (the earlier limit holds until the day before).</summary>
        public Dictionary<Guid, (DateOnly From, decimal Percent)> ScheduledFrom { get; } = [];
        public List<(Guid Group, DateOnly On)> Asked { get; } = [];

        public Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(Guid limitGroupId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((limitGroupId, effectiveOn));
            if (Faults.TryGetValue(limitGroupId, out var fault))
            {
                return Task.FromException<SalesDiscountLimitPolicyV1>(new SettingsPrerequisiteException(fault));
            }

            if (!Limits.TryGetValue(limitGroupId, out var limit))
            {
                return Task.FromException<SalesDiscountLimitPolicyV1>(
                    new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound));
            }

            if (ScheduledFrom.TryGetValue(limitGroupId, out var scheduled))
            {
                return Task.FromResult(effectiveOn >= scheduled.From
                    ? new SalesDiscountLimitPolicyV1(Company, limitGroupId,
                        new ProfileVersionV1(Guid.Parse("d15c0000-0000-4000-8000-000000000002"), 2), scheduled.Percent, true,
                        scheduled.From, null)
                    : new SalesDiscountLimitPolicyV1(Company, limitGroupId,
                        new ProfileVersionV1(Guid.Parse("d15c0000-0000-4000-8000-000000000001"), 1), limit, true,
                        effectiveOn.AddDays(-30), scheduled.From.AddDays(-1)));
            }

            var from = StartsAfterTheBusinessDate.Contains(limitGroupId) ? effectiveOn.AddDays(1) : effectiveOn.AddDays(-30);
            return Task.FromResult(new SalesDiscountLimitPolicyV1(Company, limitGroupId,
                new ProfileVersionV1(Guid.Parse("d15c0000-0000-4000-8000-000000000001"), 1), limit,
                !Inactive.Contains(limitGroupId), from, null));
        }

        public Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(Guid currencyId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>SAT-D81: a clock at noon of a given Europe/Istanbul business day (09:00 UTC).</summary>
    private sealed class IstanbulDayClock(DateOnly day) : TimeProvider
    {
        public DateOnly Day { get; set; } = day;

        public override DateTimeOffset GetUtcNow() => new(Day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
    }
}
