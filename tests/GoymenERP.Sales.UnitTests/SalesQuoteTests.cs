using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-22…24 (plan §3.2, S-01; delegation No 162, 163; master §7.2): the sales quote aggregate — the order draft's line and
/// commercial rules on a quote, the order money profile it is checked under, the revision as the next Rev., the loss with its
/// reason, the ONE conversion, and the restore that refuses a corrupt row. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesQuoteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private const string Actor = "SENTETIK-seller";
    private const string Reviser = "SENTETIK-reviser";
    private const string Approver = "SENTETIK-manager";
    private static readonly SalesQuoteLossReason ListReason = new(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "SENTETIK-RAKIP",
        "Sentetik rakip", Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), 1);

    [Fact]
    public void A_quote_is_born_open_at_rev0_with_its_priced_lines_and_sorted_requests()
    {
        var first = Guid.Parse("00000000-0000-4000-8000-000000000002");
        var second = Guid.Parse("00000000-0000-4000-8000-000000000001");

        var quote = Create([Line("12.50", "5")], requests: [first, second], terms: new SalesQuoteTerms(Day.AddDays(15), Note: "not"));

        Assert.Equal((SalesQuoteState.Open, 0, 1L, (DateOnly?)Day.AddDays(15)), (quote.State, quote.Revision, quote.Version, quote.Terms.ValidUntil));
        Assert.Equal([second, first], quote.RequestIds);
        var line = Assert.Single(quote.Lines);
        Assert.Equal(("12.50", (Guid?)KgUnit, "5", "1.25"), (line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.QuantityText));
    }

    [Fact]
    public void Lines_hold_the_order_lines_snapshot_quantity_and_commercial_rules()
    {
        Assert.Equal(SalesRuleCodes.QuoteWithoutLines, CodeOf(() => Create([])));
        Assert.Equal(SalesRuleCodes.PriceScaleExceeded, CodeOf(() => Create([Line("12.505")])));
        Assert.Equal(SalesRuleCodes.PriceUnitPairing, CodeOf(() => Create([Line("12.50") with { PriceUnitId = null }])));
        Assert.Equal(SalesRuleCodes.PriceUnitUnsupported, CodeOf(() => Create([Line("12.50") with { PriceUnitId = Guid.NewGuid() }])));
        Assert.Equal(SalesRuleCodes.DiscountWithoutPrice, CodeOf(() => Create([Line(null) with { DiscountPercent = "5" }])));
        Assert.Equal(SalesRuleCodes.DiscountOutOfRange, CodeOf(() => Create([Line("12.50", "100.5")])));
        Assert.Equal(SalesRuleCodes.QuantityScaleExceeded, CodeOf(() => Create([Line("1") with { Quantity = 1.2345m, QuantityText = "1.2345" }])));
        Assert.Equal(SalesRuleCodes.QuoteValidUntilBeforeDate, CodeOf(() => Create([Line("1")], terms: new SalesQuoteTerms(Day.AddDays(-1)))));
        Assert.Equal(SalesRuleCodes.QuoteRequestsInvalid, CodeOf(() => Create([Line("1")], requests: [Product, Product])));
        // The money profile must be the ORDER operation's, for this company and date, in effect.
        Assert.Equal(SalesRuleCodes.SnapshotOperationMismatch, CodeOf(() => Create([Line("1")], money: Money() with { Operation = "sales.quote.v1" })));
        Assert.Equal(SalesRuleCodes.SnapshotDateMismatch, CodeOf(() => Create([Line("1")], money: Money() with { EffectiveOn = Day.AddDays(1) })));
        Assert.Equal(SalesRuleCodes.MoneyProfileNotInEffect, CodeOf(() => Create([Line("1")], money: Money() with { IsActive = false })));
        // An unpriced line is a quoted quantity, not a free one: allowed, and the conversion's draft path decides the rest.
        Assert.Null(Create([Line(null)]).Lines[0].UnitPrice);
    }

    [Fact]
    public void A_revision_replaces_lines_and_terms_as_the_next_revision_and_a_refused_one_moves_nothing()
    {
        var quote = Create([Line("12.50")]);

        Assert.Equal(SalesRuleCodes.PriceScaleExceeded, CodeOf(() => quote.Revise(1, Actor, new SalesQuoteTerms(), [Line("1.001")], T0)));
        Assert.Equal((0, 1L, "12.50"), (quote.Revision, quote.Version, quote.Lines[0].UnitPrice));

        quote.Revise(1, Reviser, new SalesQuoteTerms(Day.AddDays(3)), [Line("13.00"), Line("1")], T0.AddMinutes(1));
        Assert.Equal((1, 2L, 2, "13.00", (DateOnly?)Day.AddDays(3)),
            (quote.Revision, quote.Version, quote.Lines.Count, quote.Lines[0].UnitPrice, quote.Terms.ValidUntil));
        // SAT-D44 (1): who created (Rev.0) and who revised each revision are kept on the quote.
        Assert.Equal(
            [new SalesQuoteRevisionRecord(0, null, Actor, T0), new SalesQuoteRevisionRecord(1, Day.AddDays(3), Reviser, T0.AddMinutes(1))],
            quote.Revisions);
        Assert.Throws<SalesVersionConflictException>(() => quote.Revise(1, Actor, new SalesQuoteTerms(), [Line("1")], T0));
    }

    [Fact]
    public void A_loss_needs_its_reason_and_closes_the_quote_and_a_conversion_is_recorded_once()
    {
        var lost = Create([Line("1")]);
        // SAT-D25: the entry's own flag decides whether the explanation is required; a blank one is refused, never defaulted.
        Assert.Equal(SalesRuleCodes.QuoteLossReasonRequired, CodeOf(() => lost.RecordLoss(1, Actor, ListReason, true, " ", T0)));
        Assert.Equal(SalesRuleCodes.QuoteLossReasonTooLong, CodeOf(() => lost.RecordLoss(1, Actor, ListReason, false, new string('x', 4001), T0)));
        Assert.Throws<ArgumentException>(() => lost.RecordLoss(1, Actor, ListReason with { Code = " " }, false, null, T0));
        Assert.Throws<ArgumentException>(() => lost.RecordLoss(1, Actor, ListReason with { ProfileVersion = 0 }, false, null, T0));
        Assert.Equal((SalesQuoteState.Open, 1L), (lost.State, lost.Version));
        lost.RecordLoss(1, Actor, ListReason, true, "Rakip daha ucuz", T0);
        Assert.Equal((SalesQuoteState.Lost, 2L, "Rakip daha ucuz", ListReason), (lost.State, lost.Version, lost.Loss!.Reason, lost.Loss.ListReason));
        var withoutExplanation = Create([Line("1")]);
        withoutExplanation.RecordLoss(1, Actor, ListReason, false, "  ", T0);
        Assert.Null(withoutExplanation.Loss!.Reason);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => lost.Convert(2, Guid.NewGuid(), Actor, Day, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => lost.Revise(2, Actor, new SalesQuoteTerms(), [Line("1")], T0)));

        var converted = Create([Line("1")]);
        var draft = Guid.NewGuid();
        Assert.Throws<SalesVersionConflictException>(() => converted.RequireConvertible(2));
        converted.Convert(1, draft, Actor, Day, T0);
        Assert.Equal((SalesQuoteState.Converted, draft, 2L), (converted.State, converted.Conversion!.DraftId, converted.Version));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => converted.Convert(2, Guid.NewGuid(), Actor, Day, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => converted.RecordLoss(2, Actor, ListReason, false, "geç", T0)));
    }

    [Fact]
    public void An_expired_quote_converts_only_with_an_approval_for_its_current_revision_and_the_validity_day_itself_still_converts()
    {
        // SAT-D24: the validity date is the last valid day; on a later (server) business day the conversion is refused.
        var expired = Create([Line("1")], terms: new SalesQuoteTerms(Day.AddDays(3)));
        expired.RequireValidOn(Day.AddDays(3));
        Assert.False(expired.IsExpiredOn(Day.AddDays(3)));
        Assert.True(expired.IsExpiredOn(Day.AddDays(4)));
        Assert.Equal(SalesRuleCodes.QuoteExpired, CodeOf(() => expired.RequireValidOn(Day.AddDays(4))));
        Assert.Equal(SalesRuleCodes.QuoteExpired, CodeOf(() => expired.Convert(1, Guid.NewGuid(), Actor, Day.AddDays(4), T0)));
        Assert.Equal((SalesQuoteState.Open, 1L, (SalesQuoteConversion?)null), (expired.State, expired.Version, expired.Conversion));
        // A stale version or a closed quote answers first (the recorded refusals), the expiry only on a convertible quote.
        Assert.Throws<SalesVersionConflictException>(() => expired.Convert(2, Guid.NewGuid(), Actor, Day.AddDays(4), T0));

        expired.Convert(1, Guid.NewGuid(), Actor, Day.AddDays(3), T0);
        Assert.Equal(SalesQuoteState.Converted, expired.State);

        // A quote without a validity date never expires (SAT-D39: the date is not mandatory).
        var open = Create([Line("1")]);
        open.RequireValidOn(Day.AddYears(5));
        Assert.Equal(SalesRuleCodes.QuoteNotExpired, CodeOf(() => open.ApproveExpiredConversion(1, Approver, Day.AddYears(5), null, T0)));
        open.Convert(1, Guid.NewGuid(), Actor, Day.AddYears(5), T0);
        Assert.Equal(SalesQuoteState.Converted, open.State);
    }

    [Fact]
    public void The_expired_quote_approval_binds_one_revision_any_holder_even_the_creator_may_give_it_and_a_revision_needs_a_new_one()
    {
        var quote = Create([Line("1")], terms: new SalesQuoteTerms(Day.AddDays(3)));
        var today = Day.AddDays(10);

        // Not expired on the business day: nothing to approve, nothing moves.
        Assert.Equal(SalesRuleCodes.QuoteNotExpired, CodeOf(() => quote.ApproveExpiredConversion(1, Approver, Day.AddDays(3), null, T0)));
        Assert.Throws<SalesVersionConflictException>(() => quote.ApproveExpiredConversion(2, Approver, today, null, T0));
        Assert.Equal(SalesRuleCodes.NoteTooLong, CodeOf(() => quote.ApproveExpiredConversion(1, Approver, today, new string('n', 4001), T0)));
        Assert.Equal((1L, 0), (quote.Version, quote.ExpiryApprovals.Count));

        // SAT-D44 (2): the quote's own creator holds the approval permission and approves it (the P6 exception of this approval).
        quote.ApproveExpiredConversion(1, Actor, today, "Müşteri fiyatı kabul etti", T0.AddMinutes(5));
        Assert.Equal((2L, SalesQuoteState.Open), (quote.Version, quote.State));
        Assert.Equal(new SalesQuoteExpiryApproval(0, Day.AddDays(3), today, Actor, T0.AddMinutes(5), "Müşteri fiyatı kabul etti"),
            quote.CurrentExpiryApproval);
        Assert.Equal(SalesRuleCodes.QuoteExpiryAlreadyApproved, CodeOf(() => quote.ApproveExpiredConversion(2, Approver, today, null, T0)));
        quote.RequireValidOn(today);

        // A revision is a new offer: the approval stays with Rev.0 and Rev.1 is expired again until approved.
        quote.Revise(2, Reviser, new SalesQuoteTerms(Day.AddDays(4)), [Line("2")], T0.AddMinutes(6));
        Assert.Null(quote.CurrentExpiryApproval);
        Assert.Equal(SalesRuleCodes.QuoteExpired, CodeOf(() => quote.Convert(3, Guid.NewGuid(), Actor, today, T0)));
        quote.ApproveExpiredConversion(3, Reviser, today, "  ", T0.AddMinutes(7));
        Assert.Null(quote.CurrentExpiryApproval!.Note);
        Assert.Equal([0, 1], quote.ExpiryApprovals.Select(approval => approval.Revision));
        quote.Convert(4, Guid.NewGuid(), Actor, today, T0.AddMinutes(8));
        Assert.Equal((SalesQuoteState.Converted, 5L), (quote.State, quote.Version));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => quote.ApproveExpiredConversion(5, Approver, today, null, T0)));
    }

    [Fact]
    public void The_restore_accepts_only_facts_that_match_the_state_and_the_recorded_steps()
    {
        var conversion = new SalesQuoteConversion(Guid.NewGuid(), Actor, T0);
        var loss = new SalesQuoteLoss("Fiyat", Actor, T0);
        var listLoss = new SalesQuoteLoss(null, Actor, T0, ListReason);
        var revised = Create([Line("1")]);
        revised.Restore(SalesQuoteState.Converted, 2, null, conversion, 4, T0);
        Assert.Equal((2, 4L), (revised.Revision, revised.Version));
        Assert.Throws<InvalidOperationException>(() => revised.Restore(SalesQuoteState.Open, 0, null, null, 1, T0));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")]).Restore(SalesQuoteState.Converted, 2, null, conversion, 3, T0));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")]).Restore(SalesQuoteState.Lost, 0, null, null, 2, T0));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")]).Restore(SalesQuoteState.Open, 0, loss, null, 1, T0));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")]).Restore(SalesQuoteState.Lost, 0, loss, conversion, 2, T0));
        Create([Line("1")]).Restore(SalesQuoteState.Lost, 1, loss, null, 3, T0);
        // SAT-D25: a list entry needs its complete snapshot; the explanation may be absent but never blank.
        Create([Line("1")]).Restore(SalesQuoteState.Lost, 0, listLoss, null, 2, T0);
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")]).Restore(SalesQuoteState.Lost, 0, listLoss with { Reason = "" }, null, 2, T0));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")])
            .Restore(SalesQuoteState.Lost, 0, listLoss with { ListReason = ListReason with { ProfileId = Guid.Empty } }, null, 2, T0));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")]).Restore(SalesQuoteState.Lost, 0, loss with { Reason = null }, null, 2, T0));

        // SAT-D24 / SAT-D44: approvals count as steps, bind one revision each and the current one's validity date.
        var expiring = new SalesQuoteTerms(Day.AddDays(1));
        var approval = new SalesQuoteExpiryApproval(0, Day.AddDays(1), Day.AddDays(5), Approver, T0, null);
        var history = new[] { new SalesQuoteRevisionRecord(0, Day.AddDays(1), Actor, T0) };
        var approved = Create([Line("1")], terms: expiring);
        approved.Restore(SalesQuoteState.Open, 0, null, null, 2, T0, history, [approval]);
        Assert.Equal(approval, approved.CurrentExpiryApproval);
        approved.RequireValidOn(Day.AddDays(9));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")], terms: expiring).Restore(SalesQuoteState.Open, 0, null, null, 1, T0, history, [approval]));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")], terms: expiring)
            .Restore(SalesQuoteState.Open, 0, null, null, 3, T0, history, [approval, approval]));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")], terms: expiring)
            .Restore(SalesQuoteState.Open, 0, null, null, 2, T0, history, [approval with { ValidUntil = Day.AddDays(2) }]));
        Assert.Throws<InvalidOperationException>(() => Create([Line("1")], terms: expiring)
            .Restore(SalesQuoteState.Open, 0, null, null, 2, T0, [], [approval]));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesQuote Create(IReadOnlyList<SalesQuoteLineInput> lines, IReadOnlyList<Guid>? requests = null, SalesQuoteTerms? terms = null,
        MoneyProfileSnapshot? money = null) =>
        SalesQuote.Create(Guid.NewGuid(), Company, "SQ-2026-000001", new SalesQuoteHeader(Party, Account, Currency, Day), money ?? Money(),
            requests ?? [], terms ?? new SalesQuoteTerms(), lines, Actor, T0);

    private static MoneyProfileSnapshot Money() =>
        new(Company, Currency, SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, From, null, 2);

    private static SalesQuoteLineInput Line(string? price, string? discount = null)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesQuoteLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m, price, price is null ? null : KgUnit, discount);
    }
}
