using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// ALC-S16 (F-027; KLN-D41, KLN-D43, KLN-D45): the bulk goods receipt rules — the kg variance against the Ayarlar tolerance
/// (inside = warning, outside or not set = approver), the label code formats and the conflict suffix suggestion, the
/// preparer ≠ approver decision and the wire shape. No tolerance, format or role is chosen here; the tests pass them in.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseGoodsReceiptBatchTests
{
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";
    private const string Reason = "SENTETIK fark tedarikçiyle doğrulanmadı";
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 6, 0, 0, TimeSpan.Zero);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    // ---- kg variance (KLN-D45) ----------------------------------------------------------------------

    [Theory]
    [InlineData("1000", "1000", "2", GoodsReceiptKgVarianceOutcome.Exact)]
    [InlineData("1000", "1020", "2", GoodsReceiptKgVarianceOutcome.WithinTolerance)]
    [InlineData("1000", "980", "2", GoodsReceiptKgVarianceOutcome.WithinTolerance)]
    [InlineData("1000", "1020.001", "2", GoodsReceiptKgVarianceOutcome.OutsideTolerance)]
    [InlineData("1000", "979.999", "2", GoodsReceiptKgVarianceOutcome.OutsideTolerance)]
    [InlineData("1000", "1000.001", "0", GoodsReceiptKgVarianceOutcome.OutsideTolerance)]
    [InlineData("333.333", "336.666", "1", GoodsReceiptKgVarianceOutcome.WithinTolerance)]
    [InlineData("333.333", "336.667", "1", GoodsReceiptKgVarianceOutcome.OutsideTolerance)]
    public void The_kg_variance_compares_the_total_with_the_expected_kg_exactly_at_the_tolerance_edge(
        string expected, string received, string tolerance, GoodsReceiptKgVarianceOutcome outcome)
    {
        Assert.Equal(outcome, GoodsReceiptKgVariance.Evaluate(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(received, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(tolerance, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Without_an_Ayarlar_tolerance_every_difference_waits_for_an_approver_and_an_exact_total_still_posts()
    {
        Assert.Equal(GoodsReceiptKgVarianceOutcome.ToleranceNotSet, GoodsReceiptKgVariance.Evaluate(1_000m, 1_000.001m, null));
        Assert.Equal(GoodsReceiptKgVarianceOutcome.ToleranceNotSet, GoodsReceiptKgVariance.Evaluate(1_000m, 999m, null));
        Assert.Equal(GoodsReceiptKgVarianceOutcome.Exact, GoodsReceiptKgVariance.Evaluate(1_000m, 1_000m, null));

        Assert.True(GoodsReceiptKgVariance.PostsWithoutApproval(GoodsReceiptKgVarianceOutcome.Exact));
        Assert.True(GoodsReceiptKgVariance.PostsWithoutApproval(GoodsReceiptKgVarianceOutcome.WithinTolerance));
        Assert.False(GoodsReceiptKgVariance.PostsWithoutApproval(GoodsReceiptKgVarianceOutcome.OutsideTolerance));
        Assert.False(GoodsReceiptKgVariance.PostsWithoutApproval(GoodsReceiptKgVarianceOutcome.ToleranceNotSet));
    }

    [Fact]
    public void A_misconfigured_tolerance_or_an_impossible_quantity_is_refused_never_clamped_or_rounded()
    {
        Assert.False(GoodsReceiptKgVariance.IsValidTolerancePercent(-0.000001m));
        Assert.False(GoodsReceiptKgVariance.IsValidTolerancePercent(100.000001m));
        Assert.False(GoodsReceiptKgVariance.IsValidTolerancePercent(1.0000001m));
        Assert.True(GoodsReceiptKgVariance.IsValidTolerancePercent(0m));
        Assert.True(GoodsReceiptKgVariance.IsValidTolerancePercent(100m));
        Assert.True(GoodsReceiptKgVariance.IsValidTolerancePercent(2.123456m));

        Assert.Throws<ArgumentOutOfRangeException>(() => GoodsReceiptKgVariance.Evaluate(1_000m, 1_000m, 101m));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoodsReceiptKgVariance.Evaluate(1_000m, 1_000m, 1.0000001m));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoodsReceiptKgVariance.Evaluate(0m, 10m, 2m));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoodsReceiptKgVariance.Evaluate(1_000m, -1m, 2m));
    }

    // ---- label codes (KLN-D41) ----------------------------------------------------------------------

    [Theory]
    [InlineData("-{n}", true)]
    [InlineData("GR-{n}", true)]
    [InlineData("{n}", true)]
    [InlineData("-{n}-{n}", false)]
    [InlineData("-n", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("-\u0001{n}", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456{n}", false)]
    public void A_label_code_format_from_Ayarlar_holds_exactly_one_number_placeholder_and_stays_bounded(string format, bool valid)
    {
        Assert.Equal(valid, GoodsReceiptLabelCodes.IsValidFormat(format));
    }

    [Fact]
    public void A_format_is_applied_to_a_positive_number_only()
    {
        Assert.Equal("GR-42", GoodsReceiptLabelCodes.Format("GR-{n}", 42));
        Assert.Throws<ArgumentException>(() => GoodsReceiptLabelCodes.Format("GR-", 42));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoodsReceiptLabelCodes.Format("GR-{n}", 0));
    }

    [Fact]
    public void A_taken_code_gets_the_first_free_suffix_suggestion_and_no_suggestion_without_a_valid_format()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-ETK-1", "SENTETIK-ETK-1-2", "SENTETIK-ETK-1-3" };

        Assert.Equal("SENTETIK-ETK-1-4", GoodsReceiptLabelCodes.SuggestSuffix("SENTETIK-ETK-1", "-{n}", taken.Contains));
        Assert.Equal("SENTETIK-ETK-1/2", GoodsReceiptLabelCodes.SuggestSuffix("SENTETIK-ETK-1", "/{n}", taken.Contains));
        Assert.Null(GoodsReceiptLabelCodes.SuggestSuffix("SENTETIK-ETK-1", null, taken.Contains));
        Assert.Null(GoodsReceiptLabelCodes.SuggestSuffix("SENTETIK-ETK-1", "-", taken.Contains));
    }

    [Fact]
    public void The_suffix_suggestion_gives_up_instead_of_exceeding_the_code_bound_or_scanning_forever()
    {
        var longCode = new string('K', GoodsReceiptLabelCodes.MaximumCodeLength - 1);
        Assert.Null(GoodsReceiptLabelCodes.SuggestSuffix(longCode, "-{n}", _ => false));
        Assert.Null(GoodsReceiptLabelCodes.SuggestSuffix("SENTETIK-ETK-1", "-{n}", _ => true));
    }

    // ---- decision (KLN-D45: preparer ≠ approver) ----------------------------------------------------

    private static PurchaseGoodsReceiptBatch Pending(string? preparedBy = Preparer) =>
        new(Guid.NewGuid(), PurchaseGoodsReceiptBatchStatus.PendingApproval, 1, preparedBy);

    [Fact]
    public void An_approver_other_than_the_preparer_approves_a_pending_batch_once_and_the_version_moves()
    {
        var batch = Pending();
        batch.Approve(1, Approver);

        Assert.Equal(PurchaseGoodsReceiptBatchStatus.Posted, batch.Status);
        Assert.Equal(2, batch.Version);
        Assert.Equal(Approver, batch.DecidedBy);
        Assert.Null(batch.Reason);
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.NotPending, CodeOf(() => batch.Approve(2, "SENTETIK-baska-onaylayan")));
    }

    [Fact]
    public void The_preparer_cannot_decide_an_unknown_preparer_fails_closed_and_a_stale_version_conflicts()
    {
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.SameActor, CodeOf(() => Pending().Approve(1, Preparer)));
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.SameActor, CodeOf(() => Pending().Reject(1, Preparer, Reason)));
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.MakerUnknown, CodeOf(() => Pending(preparedBy: null).Approve(1, Approver)));
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.MakerUnknown, CodeOf(() => Pending(preparedBy: " ").Approve(1, Approver)));
        Assert.Throws<PurchasingVersionConflictException>(() => Pending().Approve(2, Approver));
    }

    [Fact]
    public void A_rejection_needs_a_trimmed_bounded_reason_and_releases_nothing_itself()
    {
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonRequired, CodeOf(() => Pending().Reject(1, Approver, "  ")));
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonRequired, CodeOf(() => Pending().Reject(1, Approver, null)));
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonTooLong,
            CodeOf(() => Pending().Reject(1, Approver, new string('x', PurchaseGoodsReceiptBatch.MaximumReasonLength + 1))));

        var batch = Pending();
        batch.Reject(1, Approver, "  " + Reason + "  ");
        Assert.Equal(PurchaseGoodsReceiptBatchStatus.Rejected, batch.Status);
        Assert.Equal(Reason, batch.Reason);
        Assert.Equal(2, batch.Version);
    }

    [Fact]
    public void A_posted_or_rejected_batch_is_not_decided_again()
    {
        var posted = new PurchaseGoodsReceiptBatch(Guid.NewGuid(), PurchaseGoodsReceiptBatchStatus.Posted, 1, Preparer);
        var rejected = new PurchaseGoodsReceiptBatch(Guid.NewGuid(), PurchaseGoodsReceiptBatchStatus.Rejected, 1, Preparer);

        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.NotPending, CodeOf(() => posted.Reject(1, Approver, Reason)));
        Assert.Equal(PurchaseGoodsReceiptBatchRuleCodes.NotPending, CodeOf(() => rejected.Approve(1, Approver)));
    }

    // ---- wire shape ----------------------------------------------------------------------------------

    private static PurchaseGoodsReceiptBatchLabelInputV1 Label(string kg = "250.5", string? supplierNo = "SENTETIK-TED-1", string? code = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), supplierNo, code, kg, null, null);

    private static SubmitPurchaseGoodsReceiptBatchV1 Submit(IReadOnlyList<PurchaseGoodsReceiptBatchLabelInputV1> labels,
        DateTimeOffset? occurredAt = null) =>
        new("3", "SENTETIK-v1", Guid.NewGuid(), Guid.NewGuid(), occurredAt ?? T0, VarianceAcknowledged: false, labels);

    [Fact]
    public void A_bulk_receipt_takes_one_to_the_maximum_labels_each_with_identities_and_an_exact_positive_kg()
    {
        PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit([Label(), Label("0.001", supplierNo: null, code: null)]));
        PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit(
            Enumerable.Range(0, PurchaseGoodsReceiptBatchApiV1.MaximumLabels).Select(_ => Label()).ToArray()));

        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit([])));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit(
            Enumerable.Range(0, PurchaseGoodsReceiptBatchApiV1.MaximumLabels + 1).Select(_ => Label()).ToArray())));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit([Label()], T0.ToOffset(TimeSpan.FromHours(3)))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("1.0001")]
    [InlineData("01")]
    [InlineData("1,5")]
    [InlineData("")]
    public void A_label_kg_that_is_not_a_canonical_positive_decimal_is_refused(string kg)
    {
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit([Label(kg)])));
    }

    [Theory]
    [InlineData(" SENTETIK-1")]
    [InlineData("SENTETIK-1 ")]
    [InlineData("")]
    [InlineData("SENTETIK\u0007")]
    public void A_supplier_label_number_or_label_code_must_be_a_trimmed_bounded_token(string token)
    {
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit([Label(supplierNo: token)])));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit([Label(code: token)])));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(Submit(
            [Label(code: new string('K', GoodsReceiptLabelCodes.MaximumCodeLength + 1))])));
    }

    [Fact]
    public void A_decision_carries_a_canonical_version_and_a_bounded_reason()
    {
        PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(new("1", null));
        PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(new("12", "SENTETIK gerekçe\nikinci satır"));

        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(new("0", null)));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(new("01", null)));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(
            new("1", new string('x', PurchaseGoodsReceiptBatch.MaximumReasonLength + 1))));
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(new("1", "SENTETIK\u0000")));
    }
}
