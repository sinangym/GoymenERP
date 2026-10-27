using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D2 / API-12 (<c>SINAN_KARARLARI_2026-10-25_SATIS.md</c>): "limiti aşan iskonto Müdür onayına gider. Limiti tanımlı
/// olmayan rol için her iskonto onaya düşer (fail-closed)" — the domain half: the deterministic limit comparison (exactly at
/// the limit is within it), the check bound to the line's exact values, the maker-checker approval (hazırlayan ≠ onaylayan,
/// reason required) and the submission gate. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftDiscountExceptionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid PolicyProfile = Guid.Parse("d15c0000-0000-4000-8000-000000000001");
    private static readonly SalesDiscountLimit TenPercent = SalesDiscountLimit.Defined(10m, PolicyProfile, 1);
    private const string Preparer = "synthetic-preparer";
    private const string Manager = "synthetic-manager";

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static CommercialDraftFields Percent(string price, string? percent) => new(price, KgUnit, percent);

    private static CommercialDraftFields Amount(string price, string amount) => new(price, KgUnit, null, DiscountAmount: amount);

    private static SalesOrderDraft Draft(params CommercialDraftFields[] lines)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000042",
            new SalesDraftHeader(Guid.Parse("33333333-3333-4333-8333-333333333333"), Guid.Parse("44444444-4444-4444-8444-444444444444"),
                Guid.Parse("55555555-5555-4555-8555-555555555555"), Day),
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
    [InlineData("12.50", "10", null, false)]
    [InlineData("12.50", "10.0001", null, true)]
    [InlineData("12.50", "9.9999", null, false)]
    [InlineData("12.50", null, "1.25", false)]
    [InlineData("12.50", null, "1.26", true)]
    [InlineData("3", null, "0.3", false)]
    [InlineData("3", null, "0.31", true)]
    [InlineData("12.50", "0", null, false)]
    public void The_limit_is_a_percent_of_the_unit_price_and_exactly_at_it_is_within(string price, string? percent, string? amount, bool exceeds)
    {
        var commercial = amount is null ? Percent(price, percent) : Amount(price, amount);
        Assert.Equal(exceeds, SalesLinePriceCalculator.DiscountExceedsPercent(commercial, 10m));
    }

    [Fact]
    public void A_zero_discount_is_no_discount_and_a_limit_outside_0_to_100_is_refused()
    {
        Assert.False(SalesLinePriceCalculator.HasPositiveDiscount(Percent("12.50", "0")));
        Assert.False(SalesLinePriceCalculator.HasPositiveDiscount(Amount("12.50", "0.00")));
        Assert.False(SalesLinePriceCalculator.HasPositiveDiscount(Percent("12.50", null)));
        Assert.True(SalesLinePriceCalculator.HasPositiveDiscount(Percent("12.50", "0.01")));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesLinePriceCalculator.DiscountExceedsPercent(Percent("1", "1"), 100.01m));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(-1m, PolicyProfile, 1));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(101m, PolicyProfile, 1));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(10m, Guid.Empty, 1));
        Assert.Throws<ArgumentException>(() => SalesDiscountLimit.Defined(10m, PolicyProfile, 0));
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.WithDiscountLimitCheck(Percent("1", "1"),
            new SalesDiscountLimit(null, PolicyProfile, 1), Preparer, T0));
    }

    [Fact]
    public void The_check_records_who_set_the_discount_the_limit_and_whether_it_exceeds_it()
    {
        var within = SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "10"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal((Preparer, T0.ToUniversalTime(), "12.50", "10", (string?)null, "10", PolicyProfile, 1L, false, false),
            (within.CheckedBy, within.CheckedAtUtc, within.UnitPrice, within.DiscountPercent, within.DiscountAmount, within.LimitPercent,
                within.LimitProfileId, within.LimitProfileVersion, within.RequiresApproval, within.IsApproved));

        var over = SalesOrderDraft.WithDiscountLimitCheck(Amount("12.50", "1.26"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(("1.26", true), (over.DiscountAmount, over.RequiresApproval));

        // "Limiti tanımlı olmayan rol için her iskonto onaya düşer": no limit, no profile, approval required.
        var undefined = SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "0.5"), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck!;
        Assert.Equal(((string?)null, (Guid?)null, (long?)null, true),
            (undefined.LimitPercent, undefined.LimitProfileId, undefined.LimitProfileVersion, undefined.RequiresApproval));

        // No positive discount, no price: no check at all.
        Assert.Null(SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "0"), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck);
        Assert.Null(SalesOrderDraft.WithDiscountLimitCheck(new CommercialDraftFields(null, null, null), SalesDiscountLimit.Undefined, Preparer, T0).DiscountCheck);
    }

    [Fact]
    public void An_over_limit_discount_blocks_the_submission_until_another_actor_approves_it_with_a_reason()
    {
        var draft = Draft(SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "15"), TenPercent, Preparer, T0),
            SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "5"), TenPercent, Preparer, T0));
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
    public void A_commercial_revision_checks_against_the_revisers_limit_and_replaces_any_approval()
    {
        var draft = Draft(SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "15"), TenPercent, Preparer, T0));
        var line = draft.Lines[0];
        draft.ApproveDiscountExceptions(draft.Version, [new(line.Id, "SENTETIK")], Manager, T0);

        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Percent("12.50", "12"))], "synthetic-pricer", T0,
            SalesDiscountLimit.Defined(12m, PolicyProfile, 2));
        Assert.Equal(("synthetic-pricer", "12", 2L, false, false), (line.Commercial.DiscountCheck!.CheckedBy, line.Commercial.DiscountCheck.LimitPercent,
            line.Commercial.DiscountCheck.LimitProfileVersion, line.Commercial.DiscountCheck.RequiresApproval, line.Commercial.DiscountCheck.IsApproved));
        draft.Submit(draft.Version, Preparer, T0);
        draft.Reject(draft.Version, Manager, null, T0);

        // Without a limit given, none is defined: the same discount now waits for approval again.
        draft.ReviseCommercial(draft.Version, [new LineCommercialInput(line.Id, Percent("12.50", "11"))], "synthetic-pricer", T0);
        Assert.True(line.Commercial.DiscountCheck!.RequiresApproval);
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.Submit(draft.Version, Preparer, T0)));

        // The same values again are no change, whatever the new check's time (the check is the revision's record).
        Assert.Equal(SalesRuleCodes.RevisionNoChange, CodeOf(() => draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, Percent("12.50", "11"))], "synthetic-pricer", T0.AddMinutes(1))));
    }

    [Fact]
    public void A_positive_discount_stored_without_a_check_is_kept_but_never_submitted_or_approved_as_an_exception()
    {
        var draft = Draft(Percent("12.50", "5"));
        var line = draft.Lines[0];
        Assert.Null(line.Commercial.DiscountCheck);

        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.Submit(draft.Version, Preparer, T0)));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, CodeOf(() => draft.ApproveDiscountExceptions(draft.Version,
            [new(line.Id, "SENTETIK")], Manager, T0)));
    }

    [Fact]
    public void A_check_that_does_not_match_its_line_is_an_internal_invariant_never_repaired()
    {
        var check = SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "15"), TenPercent, Preparer, T0).DiscountCheck!;

        foreach (var commercial in new[]
                 {
                     Percent("12.50", "16") with { DiscountCheck = check },
                     Percent("13", "15") with { DiscountCheck = check },
                     Percent("12.50", null) with { DiscountCheck = check },
                     Percent("12.50", "15") with { DiscountCheck = check with { RequiresApproval = false } },
                     Percent("12.50", "15") with { DiscountCheck = check with { LimitProfileId = null } },
                     Percent("12.50", "15") with { DiscountCheck = check with { LimitPercent = "101", RequiresApproval = false } },
                     Percent("12.50", "15") with { DiscountCheck = check with { ApprovedBy = Preparer, ApprovedAtUtc = T0, Reason = "x" } },
                     Percent("12.50", "15") with { DiscountCheck = check with { ApprovedBy = Manager, ApprovedAtUtc = null, Reason = "x" } },
                     Percent("12.50", "15") with { DiscountCheck = check with { CheckedBy = " " } },
                 })
        {
            Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => Draft(commercial)));
        }

        var within = SalesOrderDraft.WithDiscountLimitCheck(Percent("12.50", "5"), TenPercent, Preparer, T0).DiscountCheck!;
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => Draft(Percent("12.50", "5") with
        {
            DiscountCheck = within with { ApprovedBy = Manager, ApprovedAtUtc = T0, Reason = "x" },
        })));

        // A stored approved check of a different approver restores as it was.
        var restored = Draft(Percent("12.50", "15") with { DiscountCheck = check with { ApprovedBy = Manager, ApprovedAtUtc = T0, Reason = "x" } });
        Assert.True(restored.Lines[0].Commercial.DiscountCheck!.IsApproved);
        restored.Submit(restored.Version, Preparer, T0);
    }
}
