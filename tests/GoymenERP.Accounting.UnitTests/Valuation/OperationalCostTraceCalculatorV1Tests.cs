using System.Collections;
using GoymenERP.Modules.Accounting.Application.Valuation;

namespace GoymenERP.Accounting.UnitTests.Valuation;

/// <summary>Only synthetic, caller-proven snapshots; no actual Settings profile, stock fact or posting.</summary>
public sealed class OperationalCostTraceCalculatorV1Tests
{
    private static readonly Guid Company = Id(1);
    private static readonly Guid Owner = Id(2);
    private static readonly Guid Product = Id(3);
    private static readonly Guid Currency = Id(4);
    private static readonly Guid OriginId = Id(5);
    private static readonly Guid AdjustmentId = Id(6);
    private static readonly Guid ProfileId = Id(7);
    private static readonly OperationalCostQuantityBasisV1 QuantityBasis =
        new(Id(12), Id(13), "synthetic-unit-profile-v1");

    [Fact]
    public void Late_positive_cost_follows_exact_historical_lot_label_shares_once()
    {
        var request = Standard();

        var result = OperationalCostTraceCalculatorV1.Preview(request);

        Assert.Equal(OperationalCostTraceStatusV1.Ready, result.Status);
        Assert.Equal(OperationalCostTraceCodesV1.Ready, result.Code);
        Assert.False(result.Replayed);
        Assert.Equal("110000.00", result.NewTotalAmountText);
        Assert.Equal(3, result.Shares.Count);
        Assert.Equal((OperationalCostShareKindV1.OnHand, "4000.00", "44000.00"),
            (result.Shares[0].Kind, result.Shares[0].DeltaAmountText, result.Shares[0].NewAmountText));
        Assert.Equal((OperationalCostShareKindV1.Wip, "2000.00", "22000.00"),
            (result.Shares[1].Kind, result.Shares[1].DeltaAmountText, result.Shares[1].NewAmountText));
        Assert.Equal((OperationalCostShareKindV1.SoldOrConsumed, "4000.00", "44000.00"),
            (result.Shares[2].Kind, result.Shares[2].DeltaAmountText, result.Shares[2].NewAmountText));
        Assert.NotNull(result.Fingerprint);
        Assert.Equal(64, result.Fingerprint.Length);
        Assert.Equal(request.Origin, Standard().Origin); // the origin was not changed by the preview
    }

    [Fact]
    public void Late_negative_price_delta_retains_the_same_origin_and_exact_shares()
    {
        var result = OperationalCostTraceCalculatorV1.Preview(Standard() with { AdjustmentAmountText = "-5000" });

        Assert.Equal(OperationalCostTraceStatusV1.Ready, result.Status);
        Assert.Equal("95000.00", result.NewTotalAmountText);
        Assert.Equal(["-2000.00", "-1000.00", "-2000.00"],
            result.Shares.Select(share => share.DeltaAmountText).ToArray());
    }

    [Fact]
    public void Same_id_and_fingerprint_replay_emits_no_second_delta()
    {
        var request = Standard();
        var first = OperationalCostTraceCalculatorV1.Preview(request);
        var replay = OperationalCostTraceCalculatorV1.Preview(request with
        {
            PriorApplications = [new OperationalCostApplicationV1(AdjustmentId, first.Fingerprint!)]
        });

        Assert.Equal(OperationalCostTraceStatusV1.Ready, replay.Status);
        Assert.Equal(OperationalCostTraceCodesV1.Replayed, replay.Code);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Fingerprint, replay.Fingerprint);
        Assert.Null(replay.NewTotalAmountText);
        Assert.Empty(replay.Shares);
    }

    [Fact]
    public void Same_id_with_changed_payload_conflicts_instead_of_reapplying()
    {
        var request = Standard();
        var first = OperationalCostTraceCalculatorV1.Preview(request);
        var changed = request with
        {
            AdjustmentAmountText = "9000",
            PriorApplications = [new OperationalCostApplicationV1(AdjustmentId, first.Fingerprint!)]
        };

        Refused(changed, OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AdjustmentConflict);
    }

    [Fact]
    public void Unknown_value_is_not_silently_treated_as_zero()
    {
        Refused(Standard() with { CurrentTotalAmountText = null },
            OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.UnknownAmount);
        var request = Standard();
        Refused(request with { Shares = [request.Shares![0] with { CurrentAmountText = null },
            request.Shares[1], request.Shares[2]] },
            OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.UnknownAmount);
    }

    [Fact]
    public void Customer_owned_material_is_not_company_inventory_value()
    {
        var request = Standard();
        Refused(request with { Origin = request.Origin! with { AssetKind = OperationalCostAssetKindV1.CustomerOwned } },
            OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.CustomerMaterial);
    }

    [Fact]
    public void Missing_approved_scale_has_no_default_or_rounding_fallback()
    {
        Refused(Standard() with { ApprovedScale = null },
            OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.ProfileUnavailable);
    }

    [Fact]
    public void Non_integral_minor_share_requires_a_separate_rounding_decision()
    {
        var request = Standard();
        request = request with
        {
            AdjustmentAmountText = "0.01",
            TotalQuantityText = "2",
            CurrentTotalAmountText = "0",
            Shares = [Share(101, OperationalCostShareKindV1.OnHand, "1", "0"),
                Share(102, OperationalCostShareKindV1.Wip, "1", "0")]
        };

        Refused(request, OperationalCostTraceStatusV1.RoundingRequired, OperationalCostTraceCodesV1.RoundingRequired);
    }

    [Fact]
    public void Closed_period_returns_forward_correction_requirement_not_a_backwrite()
    {
        Refused(Standard() with { SourcePeriodOpen = false },
            OperationalCostTraceStatusV1.ForwardCorrectionRequired,
            OperationalCostTraceCodesV1.ForwardCorrectionRequired);
    }

    [Fact]
    public void Company_currency_origin_and_evidence_mismatch_fail_closed()
    {
        var request = Standard();
        var shares = request.Shares!;
        Refused(request with { Shares = [shares[0] with { CompanyId = Id(900) }, shares[1], shares[2]] },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.SourceMismatch);
        Refused(request with { Shares = [shares[0] with { CurrencyId = Id(901) }, shares[1], shares[2]] },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.SourceMismatch);
        Refused(request with { Shares = [shares[0] with { CostOriginId = Id(902) }, shares[1], shares[2]] },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.SourceMismatch);
        Refused(request with { Shares = [shares[0] with { EvidenceFactId = Guid.Empty }, shares[1], shares[2]] },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
    }

    [Fact]
    public void Share_quantity_and_current_amount_must_conserve_the_proven_totals()
    {
        Refused(Standard() with { TotalQuantityText = "101" },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.ShareTotalMismatch);
        Refused(Standard() with { CurrentTotalAmountText = "99999" },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AmountMismatch);
    }

    [Fact]
    public void Negative_result_is_refused_without_partial_share_output()
    {
        Refused(Standard() with { AdjustmentAmountText = "-110000" },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NegativeValue);
    }

    [Fact]
    public void Unapproved_extra_input_decimal_is_refused_not_rounded()
    {
        Refused(Standard() with { AdjustmentAmountText = "0.001" },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AmountScaleExceeded);
    }

    [Fact]
    public void Every_share_must_use_the_totals_exact_unit_and_approved_profile_snapshot()
    {
        var request = Standard();
        var shares = request.Shares!;
        foreach (var mismatched in new[]
        {
            QuantityBasis with { UnitId = Id(14) },
            QuantityBasis with { ApprovedProfileId = Id(15) },
            QuantityBasis with { ApprovedProfileVersion = "synthetic-unit-profile-v2" },
        })
        {
            Refused(request with { Shares = [shares[0] with { QuantityBasis = mismatched }, shares[1], shares[2]] },
                OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.QuantityBasisMismatch);
        }

        Refused(request with { QuantityBasis = QuantityBasis with { UnitId = Id(16) } },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.QuantityBasisMismatch);
        Refused(request with { QuantityBasis = QuantityBasis with { ApprovedProfileVersion = "" } },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
    }

    [Fact]
    public void Evidence_producer_and_fact_kind_are_bound_into_the_replay_fingerprint()
    {
        var request = Standard();
        var first = OperationalCostTraceCalculatorV1.Preview(request);
        var shares = request.Shares!;
        var otherKind = request with { Shares = [shares[0] with
            { EvidenceFactKind = "synthetic.manufacturing-share.v1" }, shares[1], shares[2]] };
        var otherProducer = request with { Shares = [shares[0] with
            { EvidenceProducer = "synthetic.manufacturing" }, shares[1], shares[2]] };

        Assert.NotEqual(first.Fingerprint, OperationalCostTraceCalculatorV1.Preview(otherKind).Fingerprint);
        Assert.NotEqual(first.Fingerprint, OperationalCostTraceCalculatorV1.Preview(otherProducer).Fingerprint);
        Refused(otherKind with { PriorApplications =
                [new OperationalCostApplicationV1(AdjustmentId, first.Fingerprint!)] },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AdjustmentConflict);
        Refused(request with { Shares = [shares[0] with { EvidenceFactKind = "" }, shares[1], shares[2]] },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
    }

    [Fact]
    public void Extreme_scale_and_numeric_text_refuse_before_unbounded_arithmetic()
    {
        Refused(Standard() with { ApprovedScale = int.MaxValue },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NumericLimitExceeded);
        Refused(Standard() with { AdjustmentAmountText = new string('9',
                OperationalCostTraceSerializationLimitsV1.MaxNumericTextLength + 1) },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NumericLimitExceeded);
        Refused(Standard() with { TotalQuantityText = new string('9',
                OperationalCostTraceSerializationLimitsV1.MaxNumericTextLength + 1) },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NumericLimitExceeded);
        Refused(Standard() with { AdjustmentAmountText = "1e1000000" },
            OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
    }

    [Fact]
    public void Caller_owned_share_and_history_collections_are_enumerated_once_then_frozen()
    {
        var request = Standard();
        var firstShares = request.Shares!;
        var changedShares = new[] { firstShares[0] with { CompanyId = Id(900) },
            firstShares[1], firstShares[2] };
        var volatileShares = new ChangingReadOnlyList<OperationalCostShareV1>(firstShares, changedShares);
        var firstHistory = new[] { new OperationalCostApplicationV1(Id(99), new string('A', 64)) };
        var changedHistory = new[] { new OperationalCostApplicationV1(AdjustmentId, new string('B', 64)) };
        var volatileHistory = new ChangingReadOnlyList<OperationalCostApplicationV1>(firstHistory, changedHistory);

        var result = OperationalCostTraceCalculatorV1.Preview(request with
        {
            Shares = volatileShares,
            PriorApplications = volatileHistory,
        });

        Assert.Equal(OperationalCostTraceStatusV1.Ready, result.Status);
        Assert.False(result.Replayed);
        Assert.Equal(1, volatileShares.EnumerationCount);
        Assert.Equal(1, volatileHistory.EnumerationCount);
    }

    private static void Refused(OperationalCostTraceRequestV1 request,
        OperationalCostTraceStatusV1 status, string code)
    {
        var result = OperationalCostTraceCalculatorV1.Preview(request);
        Assert.Equal(status, result.Status);
        Assert.Equal(code, result.Code);
        Assert.False(result.Replayed);
        Assert.Null(result.NewTotalAmountText);
        Assert.Empty(result.Shares);
    }

    private static OperationalCostTraceRequestV1 Standard() => new(
        new OperationalCostOriginV1(OriginId, Company, Owner, Product, "synthetic-product-v1",
            Id(8), Id(9), Currency, "synthetic.approved-cost.v1", Id(10), "source-v1",
            OperationalCostAssetKindV1.CompanyOwned),
        AdjustmentId, "synthetic.late-cost.v1", Id(11), "adjustment-v1", new DateOnly(2026, 10, 19),
        "10000", ProfileId, "synthetic-profile-v1", 2, true, QuantityBasis, "100", "100000",
        [Share(101, OperationalCostShareKindV1.OnHand, "40", "40000"),
            Share(102, OperationalCostShareKindV1.Wip, "20", "20000"),
            Share(103, OperationalCostShareKindV1.SoldOrConsumed, "40", "40000")],
        []);

    private static OperationalCostShareV1 Share(int id, OperationalCostShareKindV1 kind,
        string quantity, string? currentAmount) => new(Id(id), OriginId, Company, Owner, Product,
        "synthetic-product-v1", Id(id + 1000), Id(id + 2000), Currency,
        "synthetic.inventory", "synthetic.inventory-share.v1", Id(id + 3000),
        "synthetic-evidence-v1", kind, QuantityBasis, quantity, currentAmount);

    private static Guid Id(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:D12}");

    private sealed class ChangingReadOnlyList<T>(IReadOnlyList<T> first, IReadOnlyList<T> later)
        : IReadOnlyList<T>
    {
        public int EnumerationCount { get; private set; }
        public int Count => first.Count;
        public T this[int index] => first[index];

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return (EnumerationCount == 1 ? first : later).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
