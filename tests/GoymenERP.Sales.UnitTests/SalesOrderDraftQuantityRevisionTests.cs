using System.Globalization;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-002 / AT-023, the domain half of the quantity revision: an APPROVED order archives its approved revision (the
/// approval facts, every line as it was and the reason) BEFORE the quantity changes, and goes on as the next revision back in
/// Preparing — Rev.0 stays readable and the new revision needs the existing approval again. The price revision's rules hold:
/// every line exactly once, a no-change refused, a pending/withdrawn/cancelled order refused; Ö2 in the line's own unit; the
/// delivery plan is never re-split. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftQuantityRevisionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 28);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid PieceUnit = Guid.Parse("68686868-6868-4868-8868-686868686868");
    private static readonly Guid PieceProfile = Guid.Parse("78787878-7878-4878-8878-787878787878");
    private const string Reviser = "synthetic-reviser";

    [Fact]
    public void An_approved_order_archives_rev0_with_the_reason_and_re_enters_approval_at_rev1()
    {
        var draft = ApprovedDraft(out var line, out var second);
        var approval = draft.Approval;

        var archived = draft.ReviseQuantities(draft.Version,
            [Input(line.Id, "2.5"), Input(second.Id, "4")], $" {Reviser} ", "  SENTETIK müşteri miktarı artırdı  ", T0.AddHours(1));

        Assert.NotNull(archived);
        Assert.Equal((0, approval, "SENTETIK müşteri miktarı artırdı", Reviser), (archived.Revision, archived.Approval, archived.Reason, archived.ArchivedBy));
        // Rev.0 as it WAS: the approved quantities, the commercial values untouched.
        Assert.Equal(["1.25", "4"], archived.Lines.Select(archivedLine => archivedLine.QuantityText));
        Assert.Equal(1, draft.Revision);
        Assert.Equal((InternalApprovalState.Preparing, ApprovalFacts.None, 6L), (draft.ApprovalState, draft.Approval, draft.Version));
        Assert.Same(archived, Assert.Single(draft.ArchivedRevisions));
        Assert.Equal(("2.5", 2.5m), (line.QuantityText, line.Quantity.Value));
        // A value-equal line keeps its stored text; its slice, date and price are untouched.
        Assert.Equal(("4", 4m, CommercialDraftFields.None), (second.QuantityText, second.Quantity.Value, second.Commercial));
        Assert.Single(line.RequestedDeliverySlices);

        // The new revision is approved through the existing flow; Rev.0 stays archived.
        draft.Submit(draft.Version, "synthetic-preparer", T0.AddHours(2));
        draft.Approve(draft.Version, "synthetic-approver", T0.AddHours(3));
        Assert.Equal((InternalApprovalState.Approved, 1, 8L), (draft.ApprovalState, draft.Revision, draft.Version));
        Assert.Equal("1.25", Assert.Single(draft.ArchivedRevisions).Lines[0].QuantityText);
    }

    [Fact]
    public void A_preparing_or_rejected_draft_changes_its_quantities_without_an_archive()
    {
        var draft = Draft(out var line, out var second);
        Assert.Null(draft.ReviseQuantities(draft.Version, [Input(line.Id, "3"), Input(second.Id, "4")], Reviser, "düzeltme", T0));
        Assert.Equal((0, InternalApprovalState.Preparing, "3"), (draft.Revision, draft.ApprovalState, line.QuantityText));

        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Reject(draft.Version, "synthetic-approver", "miktar yanlış", T0);
        Assert.Null(draft.ReviseQuantities(draft.Version, [Input(line.Id, "3.5"), Input(second.Id, "4")], Reviser, "düzeltme", T0));
        Assert.Equal((0, InternalApprovalState.Rejected, "3.5"), (draft.Revision, draft.ApprovalState, line.QuantityText));
        Assert.Empty(draft.ArchivedRevisions);
    }

    [Theory]
    [InlineData("PendingApproval")]
    [InlineData("Withdrawn")]
    [InlineData("Cancelled")]
    public void A_pending_withdrawn_or_cancelled_order_is_a_state_conflict_and_moves_nothing(string state)
    {
        var draft = Draft(out var line, out var second);
        switch (state)
        {
            case "PendingApproval":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                break;
            case "Withdrawn":
                draft.Withdraw(draft.Version, "synthetic-preparer", null, T0);
                break;
            case "Cancelled":
                draft.Submit(draft.Version, "synthetic-preparer", T0);
                draft.Approve(draft.Version, "synthetic-approver", T0);
                draft.Cancel(draft.Version, "synthetic-canceller", "neden",
                    new CancellationImpactSnapshot(new string('c', 64), [new("inventory.active-allocations", "None")]), T0);
                break;
        }

        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.StateConflict,
            CodeOf(() => draft.ReviseQuantities(draft.Version, [Input(line.Id, "9"), Input(second.Id, "4")], Reviser, "neden", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequireQuantityRevisable(draft.Version, "neden")));
        Assert.Equal((version, "1.25", 0), (draft.Version, line.QuantityText, draft.Revision));
    }

    [Fact]
    public void Every_rule_refuses_before_anything_moves()
    {
        var draft = ApprovedDraft(out var line, out var second);
        var version = draft.Version;

        // Reason: required, bounded; version: current; lines: every current line exactly once.
        Assert.Equal(SalesRuleCodes.RevisionReasonRequired, CodeOf(() => draft.ReviseQuantities(version, [Input(line.Id, "2"), Input(second.Id, "4")], Reviser, "  ", T0)));
        Assert.Equal(SalesRuleCodes.RevisionReasonTooLong, CodeOf(() => draft.ReviseQuantities(version, [Input(line.Id, "2"), Input(second.Id, "4")], Reviser,
            new string('x', SalesOrderDraft.MaximumRevisionReasonLength + 1), T0)));
        Assert.Throws<SalesVersionConflictException>(() => draft.ReviseQuantities(version - 1, [Input(line.Id, "2"), Input(second.Id, "4")], Reviser, "neden", T0));
        Assert.Equal(SalesRuleCodes.RevisionLinesMismatch, CodeOf(() => draft.ReviseQuantities(version, [Input(line.Id, "2")], Reviser, "neden", T0)));
        Assert.Equal(SalesRuleCodes.RevisionLinesMismatch, CodeOf(() => draft.ReviseQuantities(version,
            [Input(line.Id, "2"), Input(line.Id, "3")], Reviser, "neden", T0)));
        Assert.Equal(SalesRuleCodes.RevisionLinesMismatch, CodeOf(() => draft.ReviseQuantities(version,
            [Input(line.Id, "2"), Input(Guid.NewGuid(), "4")], Reviser, "neden", T0)));

        // Ö2 in the line's own unit: kg at most 3 decimals, a piece whole — refused, never rounded; each names its line.
        var kg = Assert.Throws<SalesRuleException>(() => draft.ReviseQuantities(version, [Input(line.Id, "2.0001"), Input(second.Id, "4")], Reviser, "neden", T0));
        Assert.Equal((SalesRuleCodes.QuantityScaleExceeded, (Guid?)line.Id), (kg.Code, kg.LineId));
        var piece = Assert.Throws<SalesRuleException>(() => draft.ReviseQuantities(version, [Input(line.Id, "2"), Input(second.Id, "4.5")], Reviser, "neden", T0));
        Assert.Equal((SalesRuleCodes.QuantityScaleExceeded, (Guid?)second.Id), (piece.Code, piece.LineId));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.ReviseQuantities(version, [Input(line.Id, "0"), Input(second.Id, "4")], Reviser, "neden", T0));
        Assert.Throws<ArgumentException>(() => draft.ReviseQuantities(version,
            [new LineQuantityInput(line.Id, "2.5", 2.6m), Input(second.Id, "4")], Reviser, "neden", T0));

        // The plan is not re-split: the first line plans 1 kg, so 0.5 kg is below its slices.
        var plan = Assert.Throws<SalesRuleException>(() => draft.ReviseQuantities(version, [Input(line.Id, "0.5"), Input(second.Id, "4")], Reviser, "neden", T0));
        Assert.Equal((SalesRuleCodes.SlicesExceedQuantity, (Guid?)line.Id), (plan.Code, plan.LineId));

        // No change (a value-equal text included) is refused: an approval is never discarded for nothing.
        Assert.Equal(SalesRuleCodes.RevisionNoChange, CodeOf(() => draft.ReviseQuantities(version,
            [Input(line.Id, "1.250"), Input(second.Id, "4")], Reviser, "neden", T0)));

        Assert.Equal((version, 0, InternalApprovalState.Approved, "1.25", "4"),
            (draft.Version, draft.Revision, draft.ApprovalState, line.QuantityText, second.QuantityText));
        Assert.Empty(draft.ArchivedRevisions);
        // Exactly the planned kilogram is still allowed (the slice fits).
        Assert.NotNull(draft.ReviseQuantities(version, [Input(line.Id, "1"), Input(second.Id, "4")], Reviser, "neden", T0));
    }

    [Fact]
    public void An_archived_reason_restores_and_a_blank_or_oversized_one_is_refused()
    {
        var source = ApprovedDraft(out var line, out var second);
        var archived = source.ReviseQuantities(source.Version, [Input(line.Id, "2"), Input(second.Id, "4")], Reviser, "neden", T0)!;

        var rebuilt = Draft(out _, out _, line.Id, second.Id);
        rebuilt.RestoreRevision(1);
        rebuilt.RestoreArchivedRevision(archived);
        rebuilt.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, source.Version, T0);
        Assert.Equal("neden", Assert.Single(rebuilt.ArchivedRevisions).Reason);

        // A price revision's archive has no reason and restores as before.
        var priced = Draft(out _, out _, line.Id, second.Id);
        priced.RestoreRevision(1);
        priced.RestoreArchivedRevision(archived with { Reason = null });

        foreach (var bad in new[] { " ", new string('x', SalesOrderDraft.MaximumRevisionReasonLength + 1) })
        {
            var refused = Draft(out _, out _, line.Id, second.Id);
            refused.RestoreRevision(1);
            Assert.Throws<InvalidOperationException>(() => refused.RestoreArchivedRevision(archived with { Reason = bad }));
        }
    }

    [Fact]
    public void The_price_revision_after_a_quantity_revision_stays_in_the_same_revision_and_keeps_the_quantities()
    {
        var draft = ApprovedDraft(out var line, out var second);
        draft.ReviseQuantities(draft.Version, [Input(line.Id, "2"), Input(second.Id, "4")], Reviser, "neden", T0);
        Assert.Null(draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, new CommercialDraftFields("10", KgUnit, null)), new LineCommercialInput(second.Id, CommercialDraftFields.None)],
            "synthetic-pricer", T0));
        Assert.Equal((1, "2", "10"), (draft.Revision, line.QuantityText, line.Commercial.UnitPrice));
    }

    [Fact]
    public void Quantity_revision_keeps_a_reference_inside_its_tier_and_refuses_upper_boundary_or_gap_before_archive()
    {
        var within = PricedApprovedDraft(out var withinLine, out var withinSecond);
        Assert.NotNull(within.ReviseQuantities(within.Version, [Input(withinLine.Id, "1"), Input(withinSecond.Id, "4")],
            Reviser, "alt sınır", T0.AddHours(4)));
        Assert.Equal("1", withinLine.Commercial.Reference!.LowerQuantity);

        var boundary = PricedApprovedDraft(out var boundaryLine, out var boundarySecond);
        var version = boundary.Version;
        var archiveCount = boundary.ArchivedRevisions.Count;
        var exception = boundaryLine.Commercial.FloorException;
        var refused = Assert.Throws<SalesRuleException>(() => boundary.ReviseQuantities(boundary.Version,
            [Input(boundaryLine.Id, "2"), Input(boundarySecond.Id, "4")], Reviser, "üst sınır", T0.AddHours(4)));
        Assert.Equal((SalesRuleCodes.PriceReferenceQuantityMismatch, (Guid?)boundaryLine.Id), (refused.Code, refused.LineId));
        Assert.Equal((version, archiveCount, InternalApprovalState.Approved, "1.25", exception),
            (boundary.Version, boundary.ArchivedRevisions.Count, boundary.ApprovalState, boundaryLine.QuantityText,
                boundaryLine.Commercial.FloorException));

        var gap = PricedApprovedDraft(out var gapLine, out var gapSecond);
        Assert.Equal(SalesRuleCodes.PriceReferenceQuantityMismatch, CodeOf(() => gap.ReviseQuantities(gap.Version,
            [Input(gapLine.Id, "2.5"), Input(gapSecond.Id, "4")], Reviser, "kademe boşluğu", T0.AddHours(4))));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static LineQuantityInput Input(Guid lineId, string text) =>
        new(lineId, text, decimal.Parse(text, CultureInfo.InvariantCulture));

    private static SalesDraftHeader Header() => new(Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day, Day.AddDays(10));

    private static MoneyProfileSnapshot Money() => new(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"),
        SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private static SalesDraftLineInput KgLine(Guid id, string quantity)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity, decimal.Parse(quantity, CultureInfo.InvariantCulture), Day.AddDays(3),
            RequestedDeliverySlices: [new DeliverySliceInput(Guid.Parse("12121212-1212-4212-8212-121212121212"), KgUnit, "1", 1m, Day.AddDays(4))]);
    }

    private static SalesDraftLineInput PieceLine(Guid id, string quantity)
    {
        var unit = new UnitProfileSnapshot(Company, PieceUnit, "adet", "count", PieceProfile, "1", false, 0, 0, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, PieceUnit, PieceProfile, "1");
        var product = new ProductSnapshot(Guid.Parse("98989898-9898-4898-8898-989898989898"), "2", "GDC-002", "Sentetik makara", Day, binding, binding, binding);
        return new SalesDraftLineInput(id, product, unit, quantity, decimal.Parse(quantity, CultureInfo.InvariantCulture));
    }

    /// <summary>A two-line draft (1.25 kg with a 1 kg slice; 4 pieces) at version 3.</summary>
    private static SalesOrderDraft Draft(out SalesOrderDraftLine line, out SalesOrderDraftLine second, Guid? lineId = null, Guid? secondId = null)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);
        line = draft.AddLine(draft.Version, KgLine(lineId ?? Guid.NewGuid(), "1.25"), T0);
        second = draft.AddLine(draft.Version, PieceLine(secondId ?? Guid.NewGuid(), "4"), T0);
        return draft;
    }

    /// <summary>The two-line draft submitted and approved: version 5.</summary>
    private static SalesOrderDraft ApprovedDraft(out SalesOrderDraftLine line, out SalesOrderDraftLine second)
    {
        var draft = Draft(out line, out second);
        draft.Submit(draft.Version, "synthetic-preparer", T0);
        draft.Approve(draft.Version, "synthetic-approver", T0);
        return draft;
    }

    private static SalesOrderDraft PricedApprovedDraft(out SalesOrderDraftLine line, out SalesOrderDraftLine second)
    {
        var draft = ApprovedDraft(out line, out second);
        var reference = new PriceReferenceSnapshot(Guid.NewGuid(), "SENTETIK", 1, Day, null, "TRY", KgUnit,
            "100", "95", "ExcludingVat", "1", "2");
        draft.ReviseCommercial(draft.Version,
            [new LineCommercialInput(line.Id, new CommercialDraftFields("90", KgUnit, null, reference)),
                new LineCommercialInput(second.Id, CommercialDraftFields.None)], Reviser, T0.AddHours(1));
        draft.ApprovePriceFloorExceptions(draft.Version,
            [new LinePriceFloorExceptionApprovalInput(line.Id, "SENTETIK gerekçe")], "synthetic-checker", T0.AddHours(2));
        draft.Submit(draft.Version, Reviser, T0.AddHours(2));
        draft.Approve(draft.Version, "synthetic-approver", T0.AddHours(3));
        return draft;
    }
}
