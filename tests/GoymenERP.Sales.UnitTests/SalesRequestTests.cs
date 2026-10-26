using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-20 / API-21 (plan §3.2, S-01; delegation No 160, 161): the sales request aggregate — the order draft's line rules
/// (owner snapshots, Ö2) on a request, the single-approver decision, the reasoned rejection from Submitted or Approved, the
/// version as the only concurrency token, and the restore that refuses a corrupt row. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesRequestTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid PieceUnit = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private const string Preparer = "SENTETIK-preparer";
    private const string Approver = "SENTETIK-approver";

    [Fact]
    public void A_request_is_recorded_submitted_at_version_1_with_its_lines_in_order()
    {
        var first = Line(12.5m, "12.500");
        var second = Line(3m, unit: PieceUnitProfile());

        var request = Create(first, second);

        Assert.Equal((SalesRequestState.Submitted, 1L, Preparer, (SalesRequestDecision?)null),
            (request.State, request.Version, request.CreatedBy, request.Decision));
        Assert.Equal([(1, first.LineId, "12.500", 12.5m), (2, second.LineId, "3", 3m)],
            request.Lines.Select(line => (line.LineNo, line.Id, line.QuantityText, line.Quantity.Value)));
        Assert.Equal((Party, Account, Day), (request.Header.PartyId, request.Header.CompanyAccountId, request.Header.BusinessDate));
    }

    [Fact]
    public void Each_line_holds_the_order_drafts_own_snapshot_and_quantity_rules()
    {
        Assert.Equal(SalesRuleCodes.RequestWithoutLines, CodeOf(() => Create()));
        Assert.Equal(SalesRuleCodes.TooManyLines,
            CodeOf(() => Create([.. Enumerable.Range(0, SalesRequest.MaximumLines + 1).Select(_ => Line(1m))])));
        var line = Line(1m);
        Assert.Equal(SalesRuleCodes.DuplicateLineId, CodeOf(() => Create(line, line with { QuantityText = "1" })));
        // Ö2: decimals beyond min(profile, 3) are refused, never rounded; a count unit takes whole pieces only.
        Assert.Equal(SalesRuleCodes.QuantityScaleExceeded, CodeOf(() => Create(Line(1.2345m))));
        Assert.Equal(SalesRuleCodes.QuantityScaleExceeded, CodeOf(() => Create(Line(1.5m, unit: PieceUnitProfile()))));
        // The owners' snapshots must be this company's, for this business date, and the product's order-unit binding.
        Assert.Equal(SalesRuleCodes.SnapshotCompanyMismatch, CodeOf(() => Create(Line(1m, unit: KgUnitProfile(company: OtherCompany)))));
        Assert.Equal(SalesRuleCodes.SnapshotDateMismatch,
            CodeOf(() => Create(Line(1m) with { Product = Snapshot(KgUnitProfile(), effectiveOn: Day.AddDays(-1)) })));
        Assert.Equal(SalesRuleCodes.ProductUnitMismatch,
            CodeOf(() => Create(Line(1m, unit: PieceUnitProfile()) with { Product = Snapshot(KgUnitProfile()) })));
        Assert.Equal(SalesRuleCodes.UnitProfileNotInEffect, CodeOf(() => Create(Line(1m, unit: KgUnitProfile(isActive: false)))));
        Assert.Equal(SalesRuleCodes.NoteTooLong, CodeOf(() => Create(Line(1m) with { Note = new string('n', 4001) })));
        Assert.Equal(SalesRuleCodes.NoteTooLong,
            CodeOf(() => SalesRequest.Create(Guid.NewGuid(), Company, "SR-2026-000001",
                new SalesRequestHeader(Party, Account, Day, Note: new string('n', 4001)), [Line(1m)], Preparer, T0)));
        // The quantity text must BE the quantity (lossless, the draft's rule): a caller error, not a business rule.
        Assert.Throws<ArgumentException>(() => Create(Line(1.25m, "1.2")));
        Assert.Throws<ArgumentException>(() => SalesRequest.Create(Guid.NewGuid(), Company, "SR-2026-000001",
            new SalesRequestHeader(Guid.Empty, Account, Day), [Line(1m)], Preparer, T0));
    }

    [Fact]
    public void One_approver_approves_a_submitted_request_and_the_version_moves_by_one()
    {
        var request = Create(Line(1m));

        request.Approve(1, Approver, T0.AddMinutes(5));

        Assert.Equal((SalesRequestState.Approved, 2L), (request.State, request.Version));
        Assert.Equal(new SalesRequestDecision(Approver, T0.AddMinutes(5), null), request.Decision);
        // No second approval: an approved request is not approved again.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => request.Approve(2, Approver, T0)));
        Assert.Equal(2L, request.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_submitted_or_approved_request_is_rejected_with_its_reason_and_a_rejected_one_is_final(bool approvedFirst)
    {
        var request = Create(Line(1m));
        if (approvedFirst)
        {
            request.Approve(1, Approver, T0);
        }

        var version = request.Version;
        request.Reject(version, Approver, "Müşteri talebi geri çekti", T0.AddHours(1));

        Assert.Equal((SalesRequestState.Rejected, version + 1), (request.State, request.Version));
        Assert.Equal("Müşteri talebi geri çekti", request.Decision!.RejectionReason);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => request.Reject(version + 1, Approver, "tekrar", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => request.Approve(version + 1, Approver, T0)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_without_a_reason_is_refused_and_nothing_moves(string? reason)
    {
        var request = Create(Line(1m));

        Assert.Equal(SalesRuleCodes.RequestRejectionReasonRequired, CodeOf(() => request.Reject(1, Approver, reason, T0)));
        Assert.Equal(SalesRuleCodes.RequestRejectionReasonTooLong,
            CodeOf(() => request.Reject(1, Approver, new string('r', SalesRequest.MaximumReasonLength + 1), T0)));
        Assert.Equal((SalesRequestState.Submitted, 1L, (SalesRequestDecision?)null), (request.State, request.Version, request.Decision));
    }

    [Fact]
    public void A_stale_version_is_a_version_conflict_and_nothing_moves()
    {
        var request = Create(Line(1m));

        var conflict = Assert.Throws<SalesVersionConflictException>(() => request.Approve(2, Approver, T0));
        Assert.Equal((2L, 1L, SalesRuleCodes.VersionConflict), (conflict.ExpectedVersion, conflict.CurrentVersion, conflict.Code));
        Assert.Throws<SalesVersionConflictException>(() => request.Reject(0, Approver, "neden", T0));
        Assert.Equal((SalesRequestState.Submitted, 1L), (request.State, request.Version));
        Assert.Throws<ArgumentException>(() => request.Approve(1, " ", T0));
    }

    [Fact]
    public void The_restore_accepts_only_facts_that_match_the_state_and_runs_once()
    {
        var decided = new SalesRequestDecision(Approver, T0, null);
        var rejected = new SalesRequestDecision(Approver, T0, "neden");

        var approved = Create(Line(1m));
        approved.Restore(SalesRequestState.Approved, decided, 2, T0);
        Assert.Equal((SalesRequestState.Approved, 2L), (approved.State, approved.Version));
        Assert.Throws<InvalidOperationException>(() => approved.Restore(SalesRequestState.Approved, decided, 2, T0));

        Assert.Throws<InvalidOperationException>(() => Create(Line(1m)).Restore(SalesRequestState.Submitted, decided, 1, T0));
        Assert.Throws<InvalidOperationException>(() => Create(Line(1m)).Restore(SalesRequestState.Approved, rejected, 2, T0));
        Assert.Throws<InvalidOperationException>(() => Create(Line(1m)).Restore(SalesRequestState.Rejected, decided, 2, T0));
        Assert.Throws<InvalidOperationException>(() => Create(Line(1m)).Restore(SalesRequestState.Approved, decided, 1, T0));
        Assert.Throws<InvalidOperationException>(() => Create(Line(1m)).Restore((SalesRequestState)9, null, 1, T0));
        Create(Line(1m)).Restore(SalesRequestState.Rejected, rejected, 3, T0);
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesRequest Create(params SalesRequestLineInput[] lines) =>
        SalesRequest.Create(Guid.NewGuid(), Company, "SR-2026-000001", new SalesRequestHeader(Party, Account, Day), lines, Preparer, T0);

    private static UnitProfileSnapshot KgUnitProfile(Guid? company = null, bool isActive = true) =>
        new(company ?? Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, isActive, From, null);

    private static UnitProfileSnapshot PieceUnitProfile() =>
        new(Company, PieceUnit, "pcs", "count", Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "1", false, 0, 0, true, From, null);

    private static ProductSnapshot Snapshot(UnitProfileSnapshot orderUnit, DateOnly? effectiveOn = null)
    {
        var binding = new UnitBindingSnapshot(orderUnit.CompanyId, orderUnit.UnitId, orderUnit.ProfileId, orderUnit.ProfileVersion);
        return new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", effectiveOn ?? Day, binding, binding, binding);
    }

    private static SalesRequestLineInput Line(decimal quantity, string? text = null, UnitProfileSnapshot? unit = null)
    {
        var orderUnit = unit ?? KgUnitProfile();
        return new SalesRequestLineInput(Guid.NewGuid(), Snapshot(orderUnit), orderUnit,
            text ?? quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), quantity);
    }
}
