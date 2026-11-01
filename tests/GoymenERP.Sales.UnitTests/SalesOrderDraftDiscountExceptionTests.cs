using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D2 / API-12 (<c>SINAN_KARARLARI_2026-10-25_SATIS.md</c>) as applied by SAT-D34…D37
/// (<c>SINAN_KARARLARI_2026-10-27_SATIS_EK4.md</c>) and SAT-D42/D43 — the domain half: the discount measured against the LIST
/// price (SAT-D35: (list − discounted) / list; a manual price below list counts even with zero discount fields) on the price
/// BEFORE the maturity surcharge (SAT-D42: a surcharge never hides a discount), the exact limit comparison (exactly at the
/// limit is within it), every priced line without a list reference to approval whatever its discount fields (SAT-D43), the
/// check bound to the line's exact values and reference, the maker-checker approval (hazırlayan ≠ onaylayan, reason required)
/// and the submission gate. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftDiscountExceptionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid PriceList = Guid.Parse("abababab-abab-4bab-8bab-abababababab");
    private static readonly Guid PolicyProfile = Guid.Parse("d15c0000-0000-4000-8000-000000000001");
    private static readonly SalesDiscountLimit TenPercent = SalesDiscountLimit.Defined(10m, PolicyProfile, 1);
    private const string Preparer = "synthetic-preparer";
    private const string Manager = "synthetic-manager";

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static PriceReferenceSnapshot List(string listPrice, int version = 1) =>
        new(PriceList, "SENTETIK liste", version, Day.AddDays(-10), null, "TRY", KgUnit, listPrice, null, "ExcludingVat", "0", null);

    /// <summary>A line priced from a list: its own unit price, the list price as reference and the optional adjustments.</summary>
    private static CommercialDraftFields Listed(string listPrice, string price, string? percent = null, string? amount = null,
        string? surchargePercent = null) =>
        new(price, KgUnit, percent, List(listPrice), amount, surchargePercent);

    /// <summary>A manually priced line without any list reference.</summary>
    private static CommercialDraftFields Manual(string price, string? percent = null, string? amount = null) =>
        new(price, KgUnit, percent, DiscountAmount: amount);

    private static SalesOrderDraft Draft(params CommercialDraftFields[] lines)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000042",
            new SalesDraftHeader(Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("44444444-4444-4444-8444-444444444444"),
                Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, DueTerms: TestDueTerms.PointDue30),
            new MoneyProfileSnapshot(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"), SalesOrderDraft.MoneyOperation,
                Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2), T0);
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day,
            binding, binding, binding);
        foreach (var commercial in lines)
        {
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1", 1m, Commercial: commercial), T0);
        }

        return draft;
    }

    [Theory]
    // (list − net) / list against 10 %: exactly at the limit is within it.
    [InlineData("100", "100", "10", null, null, false)]
    [InlineData("100", "100", "10.01", null, null, true)]
    [InlineData("100", "100", null, "10", null, false)]
    [InlineData("100", "100", null, "10.01", null, true)]
    // SAT-D35: a manual price below the list price is a discount even with zero discount fields.
    [InlineData("100", "90", null, null, null, false)]
    [InlineData("100", "89.99", null, null, null, true)]
    [InlineData("100", "89.99", "0", null, null, true)]
    // The base is the LIST price: 5 % off a price already below list adds up (95 × 0.95 = 90.25 → 9.75 % off 100).
    [InlineData("100", "95", "5", null, null, false)]
    [InlineData("100", "94", "5", null, null, true)]
    // SAT-D42: the price BEFORE the maturity surcharge is measured — a surcharge never hides a discount (net 93.5, but 15 % off).
    [InlineData("100", "100", "15", null, "10", true)]
    [InlineData("100", "100", "10", null, "50", false)]
    // Uneven list prices compare exactly, never through a rounded percent.
    [InlineData("3", "2.7", null, null, null, false)]
    [InlineData("3", "2.69", null, null, null, true)]
    public void The_discount_is_measured_from_the_list_price_to_the_price_before_surcharge_and_exactly_at_the_limit_is_within(
        string listPrice, string price, string? percent, string? amount, string? surchargePercent, bool exceeds)
    {
        var commercial = Listed(listPrice, price, percent, amount, surchargePercent);
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(commercial));
        Assert.Equal(exceeds, SalesLinePriceCalculator.DiscountExceedsListPercent(commercial, 10m));
    }

    [Fact]
    public void A_price_at_or_above_list_needs_no_check_and_every_priced_line_without_a_list_price_does()
    {
        Assert.False(SalesLinePriceCalculator.NeedsDiscountCheck(Listed("100", "100")));
        Assert.False(SalesLinePriceCalculator.NeedsDiscountCheck(Listed("100", "110", "5")));  // 104.5 is above list.
        Assert.False(SalesLinePriceCalculator.DiscountExceedsListPercent(Listed("100", "110", "5"), 0m));
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(Listed("100", "110", "10")));  // 99 is below list.
        // SAT-D42: a surcharge never lifts a discounted price back to "no discount".
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(Listed("100", "99", surchargePercent: "5")));
        Assert.Equal(("99", "103.95"), (SalesLinePriceCalculator.DiscountedUnitPriceText(Listed("100", "99", surchargePercent: "5")),
            SalesLinePriceCalculator.NetUnitPriceText(Listed("100", "99", surchargePercent: "5"))));
        // SAT-D43: without a list reference every priced line needs the check, whatever its discount fields.
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(Manual("100")));
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(Manual("100", "0")));
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(Manual("100", null, "0.00")));
        Assert.True(SalesLinePriceCalculator.NeedsDiscountCheck(Manual("100", "0.01")));
        Assert.False(SalesLinePriceCalculator.NeedsDiscountCheck(new CommercialDraftFields(null, null, null)));
        Assert.Throws<InvalidOperationException>(() => SalesLinePriceCalculator.DiscountExceedsListPercent(Manual("100", "5"), 10m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesLinePriceCalculator.DiscountExceedsListPercent(Listed("100", "90"), 100.01m));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(-1m, PolicyProfile, 1));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(101m, PolicyProfile, 1));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(10m, Guid.Empty, 1));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(10m, PolicyProfile, 0));
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.WithDiscountLimitCheck(Manual("1", "1"),
            new SalesDiscountLimit(null, PolicyProfile, 1), Preparer, T0));
        Assert.True(SalesOrderDraft.IsMeasurablyDiscounted(Listed("100", "90")));
        Assert.False(SalesOrderDraft.IsMeasurablyDiscounted(Manual("100", "5")));
        Assert.False(SalesOrderDraft.IsMeasurablyDiscounted(Listed("100", "100")));
        Assert.False(SalesOrderDraft.IsMeasurablyDiscounted(Manual("100")));
    }

    [Fact]
    public void The_check_records_who_the_discounted_price_the_list_reference_the_limit_and_whether_approval_is_required()
    {
        var within = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "90"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal((Preparer, T0.ToUniversalTime(), "90", (string?)null, (string?)null, "90", PriceList, 1, "100"),
            (within.CheckedBy, within.CheckedAtUtc, within.UnitPrice, within.DiscountPercent, within.DiscountAmount,
                within.DiscountedUnitPrice, within.PriceListId, within.PriceListVersionNo, within.ListUnitPrice));
        Assert.Equal(("10", PolicyProfile, 1L, false, true, false),
            (within.LimitPercent, within.LimitProfileId, within.LimitProfileVersion, within.RequiresApproval, within.IsMeasured,
                within.IsApproved));

        var over = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "100", amount: "10.01"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(("10.01", "89.99", true), (over.DiscountAmount, over.DiscountedUnitPrice, over.RequiresApproval));
        // SAT-D42: the surcharge is not part of what is measured.
        var surcharged = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "85", surchargePercent: "20"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(("85", true), (surcharged.DiscountedUnitPrice, surcharged.RequiresApproval));

        // "Limiti tanımlı olmayan rol için her iskonto onaya düşer" (SAT-D36): no limit, no profile, approval required.
        var undefined = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "99.5"), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck!;
        Assert.Equal(((string?)null, (Guid?)null, (long?)null, true),
            (undefined.LimitPercent, undefined.LimitProfileId, undefined.LimitProfileVersion, undefined.RequiresApproval));

        // SAT-D43: no list reference — approval required whatever the limit and the discount fields (here: none at all).
        var unmeasured = SalesOrderDraft.WithDiscountLimitCheck(Manual("100"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(((Guid?)null, (int?)null, (string?)null, false, true),
            (unmeasured.PriceListId, unmeasured.PriceListVersionNo, unmeasured.ListUnitPrice, unmeasured.IsMeasured, unmeasured.RequiresApproval));

        // At or above the list price, or no price: no check at all.
        Assert.Null(SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "100"), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck);
        Assert.Null(SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "100", surchargePercent: "3"), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck);
        Assert.Null(SalesOrderDraft.WithDiscountLimitCheck(new CommercialDraftFields(null, null, null), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck);
    }

    [Fact]
    public void An_over_limit_discount_blocks_the_submission_until_another_actor_approves_it_with_a_reason()
    {
        var draft = Draft(SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "85"), TenPercent, Preparer, T0),
            SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "100", "5"), TenPercent, Preparer, T0));
        var over = draft.Lines[0];
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.Submit(draft.Version, Preparer, T0)));

        // Only the over-limit line is named; the within-limit line is not an exception.
        Assert.Equal(SalesRuleCodes.DiscountExceptionLinesMismatch, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(over.Id, "SENTETIK"), new(draft.Lines[1].Id, "SENTETIK")], Manager, T0)));
        Assert.Equal(SalesRuleCodes.DiscountExceptionLinesMismatch, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version, [], Manager, T0)));
        Assert.Equal(SalesRuleCodes.DiscountExceptionSameActor, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(over.Id, "SENTETIK")], Preparer, T0)));
        Assert.Equal(SalesRuleCodes.DiscountExceptionReasonRequired, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(over.Id, " ")], Manager, T0)));
        Assert.Equal(SalesRuleCodes.ApprovalReasonTooLong, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(over.Id, new string('x', SalesOrderDraft.MaximumRejectionReasonLength + 1))], Manager, T0)));
        Assert.Throws<SalesVersionConflictException>(() => draft.ApproveDiscountExceptions(draft.Version + 1,
            [new(over.Id, "SENTETIK")], Manager, T0));
        var versionBefore = draft.Version;
        var commercialBefore = over.Commercial with { DiscountCheck = null };

        draft.ApproveDiscountExceptions(versionBefore, [new(over.Id, "  SENTETIK müşteri anlaşması  ")], Manager, T0.AddMinutes(5));

        var approved = over.Commercial.DiscountCheck!;
        Assert.Equal((Manager, T0.AddMinutes(5).ToUniversalTime(), "SENTETIK müşteri anlaşması", true),
            (approved.ApprovedBy, approved.ApprovedAtUtc, approved.Reason, approved.IsApproved));
        Assert.Equal(commercialBefore, over.Commercial with { DiscountCheck = null });
        Assert.Equal(versionBefore + 1, draft.Version);
        Assert.Equal(SalesRuleCodes.RevisionNoChange, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(over.Id, "SENTETIK")], "synthetic-other-manager", T0)));

        draft.Submit(draft.Version, Preparer, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(over.Id, "SENTETIK")], Manager, T0)));
        draft.Approve(draft.Version, Manager, T0);
        Assert.Equal(InternalApprovalState.Approved, draft.ApprovalState);
    }

    [Fact]
    public void A_priced_line_without_a_list_price_needs_approval_even_with_a_zero_discount_and_is_approvable()
    {
        var draft = Draft(SalesOrderDraft.WithDiscountLimitCheck(Manual("100", "0"), TenPercent, Preparer, T0));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.Submit(draft.Version, Preparer, T0)));
        draft.ApproveDiscountExceptions(draft.Version, [new(draft.Lines[0].Id, "SENTETIK liste dışı fiyat")], Manager, T0);
        draft.Submit(draft.Version, Preparer, T0);
    }

    [Fact]
    public void A_no_list_line_may_be_self_approved_but_an_over_limit_listed_line_never_by_its_discount_writer()
    {
        // SAT-D45: without a list price the line's only reason is SAT-D43; its own writer may approve the whole line,
        // with or without a discount field.
        foreach (var commercial in new[] { Manual("100"), Manual("100", "5"), Manual("100", null, "1") })
        {
            var draft = Draft(SalesOrderDraft.WithDiscountLimitCheck(commercial, TenPercent, Preparer, T0));
            draft.ApproveDiscountExceptions(draft.Version, [new(draft.Lines[0].Id, "SENTETIK kendi fiyatım")], Preparer, T0);
            Assert.Equal(Preparer, draft.Lines[0].Commercial.DiscountCheck!.ApprovedBy);
            draft.Submit(draft.Version, Preparer, T0);
        }

        // SAT-D37: a listed line over the limit (here a manual price below list, SAT-D35) is refused to its own writer ...
        var listed = Draft(SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "85"), TenPercent, Preparer, T0));
        Assert.Equal(SalesRuleCodes.DiscountExceptionSameActor, CodeOf(() => listed.ApproveDiscountExceptions(
            listed.Version, [new(listed.Lines[0].Id, "SENTETIK")], Preparer, T0)));
        // ... and approved by another person.
        listed.ApproveDiscountExceptions(listed.Version, [new(listed.Lines[0].Id, "SENTETIK")], Manager, T0);
        listed.Submit(listed.Version, Preparer, T0);

        // Stored facts follow the same rule: a self-approved no-list line restores; a self-approved listed discount is corrupt.
        var check = SalesOrderDraft.WithDiscountLimitCheck(Manual("100", "5"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.True(Draft(Manual("100", "5") with { DiscountCheck = check with { ApprovedBy = Preparer, ApprovedAtUtc = T0, Reason = "x" } })
            .Lines[0].Commercial.DiscountCheck!.IsApproved);
        var listedCheck = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "85"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => Draft(Listed("100", "85") with
        {
            DiscountCheck = listedCheck with { ApprovedBy = Preparer, ApprovedAtUtc = T0, Reason = "x" },
        })));
    }

    [Fact]
    public void A_commercial_revision_checks_against_the_revisers_limit_and_replaces_any_approval()
    {
        var draft = Draft(SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "85"), TenPercent, Preparer, T0));
        var line = draft.Lines[0];
        draft.ApproveDiscountExceptions(draft.Version, [new(line.Id, "SENTETIK")], Manager, T0);

        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Listed("100", "88"))], "synthetic-pricer", T0,
            SalesDiscountLimit.Defined(12m, PolicyProfile, 2));
        Assert.Equal(("synthetic-pricer", "12", 2L, false, false), (line.Commercial.DiscountCheck!.CheckedBy, line.Commercial.DiscountCheck.LimitPercent,
            line.Commercial.DiscountCheck.LimitProfileVersion, line.Commercial.DiscountCheck.RequiresApproval, line.Commercial.DiscountCheck.IsApproved));
        draft.Submit(draft.Version, Preparer, T0);
        draft.Reject(draft.Version, Manager, null, T0);

        // Without a limit given, none is defined: a smaller discount now waits for approval again.
        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Listed("100", "89"))], "synthetic-pricer", T0);
        Assert.True(line.Commercial.DiscountCheck!.RequiresApproval);
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.Submit(draft.Version, Preparer, T0)));

        // The same values again are no change, whatever the new check's time (the check is the revision's record).
        Assert.Equal(SalesRuleCodes.RevisionNoChange, CodeOf(() => draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, Listed("100", "89"))], "synthetic-pricer", T0.AddMinutes(1))));

        // Back at the list price: no discount, no check, nothing waits.
        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Listed("100", "100"))], "synthetic-pricer", T0);
        Assert.Null(line.Commercial.DiscountCheck);
        draft.Submit(draft.Version, Preparer, T0);
    }

    [Fact]
    public void A_priced_line_stored_without_a_check_is_kept_but_never_submitted_or_approved_as_an_exception()
    {
        var draft = Draft(Listed("100", "90"), Manual("100"));
        Assert.All(draft.Lines, line => Assert.Null(line.Commercial.DiscountCheck));

        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.Submit(draft.Version, Preparer, T0)));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(draft.Lines[0].Id, "SENTETIK"), new(draft.Lines[1].Id, "SENTETIK")], Manager, T0)));
    }

    [Fact]
    public void A_check_that_does_not_match_its_line_or_its_list_reference_is_an_internal_invariant_never_repaired()
    {
        var check = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "85"), TenPercent, Preparer, T0).DiscountCheck!;

        foreach (var commercial in new[]
                 {
                     Listed("100", "84") with { DiscountCheck = check },
                     Listed("101", "85") with { DiscountCheck = check },
                     (Listed("100", "85") with { Reference = List("100", version: 2) }) with { DiscountCheck = check },
                     Manual("85", "1") with { DiscountCheck = check },
                     Listed("100", "100") with { DiscountCheck = check },
                     Listed("100", "85") with { DiscountCheck = check with { RequiresApproval = false } },
                     Listed("100", "85") with { DiscountCheck = check with { LimitProfileId = null } },
                     Listed("100", "85") with { DiscountCheck = check with { LimitPercent = "101", RequiresApproval = false } },
                     Listed("100", "85") with { DiscountCheck = check with { ApprovedBy = Preparer, ApprovedAtUtc = T0, Reason = "x" } },
                     Listed("100", "85") with { DiscountCheck = check with { ApprovedBy = Manager, ApprovedAtUtc = null, Reason = "x" } },
                     Listed("100", "85") with { DiscountCheck = check with { CheckedBy = " " } },
                 })
        {
            Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => Draft(commercial)));
        }

        // A surcharge added later does not unbind the check (SAT-D42: it is not measured) — the line keeps its check.
        Assert.NotNull(Draft(Listed("100", "85", surchargePercent: "1") with { DiscountCheck = check }).Lines[0].Commercial.DiscountCheck);

        // An unmeasured check must require approval; a within-limit check cannot carry an approval.
        var unmeasured = SalesOrderDraft.WithDiscountLimitCheck(Manual("100"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => Draft(Manual("100") with
        {
            DiscountCheck = unmeasured with { RequiresApproval = false },
        })));
        var within = SalesOrderDraft.WithDiscountLimitCheck(Listed("100", "95"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => Draft(Listed("100", "95") with
        {
            DiscountCheck = within with { ApprovedBy = Manager, ApprovedAtUtc = T0, Reason = "x" },
        })));

        // A stored approved check of a different approver restores as it was.
        var restored = Draft(Listed("100", "85") with { DiscountCheck = check with { ApprovedBy = Manager, ApprovedAtUtc = T0, Reason = "x" } });
        Assert.True(restored.Lines[0].Commercial.DiscountCheck!.IsApproved);
        restored.Submit(restored.Version, Preparer, T0);
    }
}
