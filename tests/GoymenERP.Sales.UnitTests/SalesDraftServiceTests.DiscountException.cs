using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D2 / API-12 (<c>SINAN_KARARLARI_2026-10-25_SATIS.md</c>): a positive discount is checked against the limit of the actor
/// who set it — read only from the Settings Sales policy port, for the actor's limit groups — and an over-limit (or
/// limit-less) discount blocks the submission until another person with <c>sales.orders.discount-exception.approve</c>
/// approves it with a reason. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid DiscountGroupA = Guid.Parse("d15c0a00-0000-4000-8000-00000000000a");
    private static readonly Guid DiscountGroupB = Guid.Parse("d15c0b00-0000-4000-8000-00000000000b");

    private static SalesDraftDiscountExceptionApprovalRequest DiscountApproval(string version, Guid lineId, string reason = "SENTETIK müşteri anlaşması") =>
        new(version, [new SalesDraftDiscountExceptionApprovalLineV1(lineId, reason)]);

    [Fact]
    public async Task Without_a_limit_owner_every_positive_discount_waits_for_a_different_approvers_reasoned_approval()
    {
        // No limit-group source is composed: no limit is defined for anyone (SAT-D2, fail-closed) and Settings is never asked.
        var policies = new FakeSalesPolicies();
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

        // Hazırlayan ≠ onaylayan: the actor who set the discount cannot approve it, even holding the permission.
        var self = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(created.Draft.Version, lineId), Metadata("k-self")));
        Assert.Equal((422, "lines[0].reason", SalesRuleCodes.DiscountExceptionSameActor),
            (self.Status, Assert.Single(self.Errors).Field, self.Errors[0].Code));

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
    [InlineData("10", null, false)]
    [InlineData("10.01", null, true)]
    [InlineData("9.99", null, false)]
    [InlineData(null, "1.25", false)]
    [InlineData(null, "1.26", true)]
    public async Task The_limit_boundary_is_inclusive_for_a_percent_and_an_exact_amount_of_the_unit_price(
        string? percent, string? amount, bool requiresApproval)
    {
        var world = new World().AllowDiscountsUpTo(10m);
        var policies = (FakeSalesPolicies)world.SalesPolicies!;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        Assert.Empty(policies.Asked);

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, percent, DiscountAmount: amount)), Metadata("k-revise"));

        Assert.Equal((DiscountGroupA, Day), Assert.Single(policies.Asked));
        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("10", requiresApproval, Actor), (check.LimitPercent, check.RequiresApproval, check.CheckedBy));
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
    public async Task Several_limit_groups_apply_the_strictest_limit_and_a_group_without_a_limit_leaves_none_defined()
    {
        var policies = new FakeSalesPolicies { Limits = { [DiscountGroupA] = 15m, [DiscountGroupB] = 5m } };
        var world = new World { SalesPolicies = policies, DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA, DiscountGroupB]) };
        var created = (await world.Service().CreateDraftAsync(Priced(discount: "6"), Metadata("k-create"))).Detail;

        var check = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(("5", true), (check.LimitPercent, check.RequiresApproval));
        Assert.Equal([(DiscountGroupA, Day), (DiscountGroupB, Day)], policies.Asked);

        // Group B has no approved limit ("not found"): the actor's limit is undefined, even though group A allows 15 %.
        policies.Limits.Remove(DiscountGroupB);
        var undefined = (await world.Service().CreateDraftAsync(Priced(discount: "1"), Metadata("k-create-2"))).Detail;
        var undefinedCheck = Assert.Single((await world.Service().GetDraftLinePricingAsync(undefined.Draft.Id)).Lines).DiscountCheck!;
        Assert.Equal(((string?)null, true), (undefinedCheck.LimitPercent, undefinedCheck.RequiresApproval));

        // An inactive policy, or one not in force on the business date, is no approved limit either.
        policies.Limits[DiscountGroupB] = 50m;
        policies.Inactive.Add(DiscountGroupB);
        var inactive = (await world.Service().CreateDraftAsync(Priced(discount: "1"), Metadata("k-create-3"))).Detail;
        Assert.True(Assert.Single((await world.Service().GetDraftLinePricingAsync(inactive.Draft.Id)).Lines).DiscountCheck!.RequiresApproval);
        policies.Inactive.Clear();
        policies.StartsAfterTheBusinessDate.Add(DiscountGroupA);
        var notYet = (await world.Service().CreateDraftAsync(Priced(discount: "1"), Metadata("k-create-4"))).Detail;
        Assert.True(Assert.Single((await world.Service().GetDraftLinePricingAsync(notYet.Draft.Id)).Lines).DiscountCheck!.RequiresApproval);
    }

    [Theory]
    [InlineData("settings_provider_unavailable", 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    [InlineData("settings_profile_invalid", 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    [InlineData("settings_sales_policy_request_invalid", 503, SalesDraftProblemCodesV1.DiscountLimitUnavailable)]
    [InlineData("settings_access_denied", 403, "settings_access_denied")]
    public async Task A_limit_the_Settings_owner_cannot_answer_refuses_a_positive_discount_and_records_nothing(
        string settingsCode, int status, string code)
    {
        var policies = new FakeSalesPolicies { Faults = { [DiscountGroupA] = settingsCode } };
        var world = new World { SalesPolicies = policies, DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA]) };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(Priced(discount: "5"), Metadata("k-create")));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.Equal(0, world.Store.Attempts);

        // The same owner state does not stop an order without a discount: nothing about a limit is read for it.
        policies.Asked.Clear();
        var plain = await world.Service().CreateDraftAsync(Priced(discount: "0"), Metadata("k-create-zero"));
        Assert.Null(Assert.Single((await world.Service().GetDraftLinePricingAsync(plain.Detail.Draft.Id)).Lines).DiscountCheck);
        Assert.Empty(policies.Asked);
    }

    [Fact]
    public async Task Limit_groups_without_a_Settings_source_are_503_and_never_a_guessed_limit()
    {
        var world = new World { DiscountLimitGroups = new FakeDiscountLimitGroups([DiscountGroupA]) };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(created.Lines[0].LineId, "12.50", KgUnit, "5")), Metadata("k-revise")));

        Assert.Equal((503, SalesDraftProblemCodesV1.DiscountLimitUnavailable), (refused.Status, refused.Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
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

        // The approver of the earlier exception may approve the new one; its own reviser may not.
        var self = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDiscountExceptionsAsync(
            created.Draft.Id, DiscountApproval(revised.Detail.Draft.Version, lineId), Metadata("k-self")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionSameActor, Assert.Single(self.Errors).Code);

        // Removing the discount removes the check: nothing waits any more.
        var cleared = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(revised.Detail.Draft.Version, new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, null)), Metadata("k-clear"));
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

    /// <summary>The Identity owner's role → limit group mapping, as a fixed answer.</summary>
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
    private sealed class FakeSalesPolicies : ISettingsSalesPolicyProviderV1
    {
        public Dictionary<Guid, decimal> Limits { get; } = [];
        public Dictionary<Guid, string> Faults { get; } = [];
        public HashSet<Guid> Inactive { get; } = [];
        public HashSet<Guid> StartsAfterTheBusinessDate { get; } = [];
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
}
