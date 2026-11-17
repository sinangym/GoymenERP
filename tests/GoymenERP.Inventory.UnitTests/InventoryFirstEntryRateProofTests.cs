using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>SENTETIK immutable receipt envelopes only. These codec and contract tests are not SQL or provider evidence.</summary>
public sealed class InventoryFirstEntryRateProofTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid CompanyId = Id(1);
    private static readonly Guid OtherCompanyId = Id(2);
    private static readonly Guid OrderId = Id(3);
    private static readonly Guid OtherOrderId = Id(4);
    private static readonly Guid AskedLabelId = Id(5);
    private static readonly Guid Usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
    private static readonly Guid Eur = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("EUR");
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly DateOnly EntryDay = new(2026, 11, 18);
    private static readonly DateTimeOffset EnteredAtUtc = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_matching_hashed_owner_snapshot_is_admitted_and_round_trips_through_the_real_codec(bool entryDaySource)
    {
        var snapshot = RatedSnapshot(1, 0.2m, entryDaySource);

        Assert.True(InventoryFirstEntryRateProof.IsValid(snapshot, CompanyId, Acceptance(1), OrderId));
        Assert.True(InventoryFirstEntryRateAdmissionV1.IsValid(snapshot, CompanyId, Acceptance(1), OrderId));
        Assert.Equal(snapshot, InventoryFirstEntryRateProof.Read(Encode(snapshot), CompanyId, Acceptance(1)));
    }

    [Fact]
    public void A_legacy_scalar_only_snapshot_remains_readable_without_an_invented_ratio()
    {
        var snapshot = Rehash(RatedSnapshot(1, 0.2m) with { RateRatio = null });

        var read = InventoryFirstEntryRateProof.Read(Encode(snapshot), CompanyId, Acceptance(1));

        Assert.Equal(snapshot, read);
        Assert.Null(read!.RateRatio);
        Assert.Equal(0.2m, read.Rate);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("acceptance")]
    [InlineData("order")]
    public void The_requested_owner_frame_must_match_the_snapshot(string field)
    {
        var snapshot = RatedSnapshot(1, 0.2m);
        var company = field == "company" ? OtherCompanyId : CompanyId;
        var acceptance = field == "acceptance" ? Acceptance(2) : Acceptance(1);
        var order = field == "order" ? OtherOrderId : OrderId;

        Assert.False(InventoryFirstEntryRateProof.IsValid(snapshot, company, acceptance, order));
        if (field != "order") Assert.Null(InventoryFirstEntryRateProof.Read(Encode(snapshot), company, acceptance));
    }

    [Theory]
    [InlineData("companyId")]
    [InlineData("acceptanceId")]
    [InlineData("purchaseOrderId")]
    [InlineData("entryDay")]
    public void A_rehashed_but_mismatched_evidence_frame_is_not_admitted(string field)
    {
        var snapshot = Reframe(RatedSnapshot(1, 0.2m), field);

        AssertRejected(snapshot);
    }

    [Fact]
    public void Evidence_changed_after_the_source_hash_was_recorded_is_not_admitted()
    {
        var original = RatedSnapshot(1, 0.2m);
        var changed = original with { EvidenceJson = original.EvidenceJson + " " };

        AssertRejected(changed);
        Assert.True(InventoryFirstEntryRateProof.IsValid(original, CompanyId, Acceptance(1)));
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("currency")]
    [InlineData("order_version")]
    public void Changing_an_outer_owner_fact_does_not_reuse_the_original_source_digest(string field)
    {
        var original = RatedSnapshot(1, 0.2m);
        var changed = field switch
        {
            "rate" => original with { Rate = 0.9m, RateRatio = new("9", "10") },
            "currency" => original with { SourceCurrencyId = Eur },
            "order_version" => original with { PurchaseOrderVersion = 10 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Assert.Equal(original.SourceVersion, changed.SourceVersion);
        AssertRejected(changed);
        Assert.True(InventoryFirstEntryRateProof.IsValid(original, CompanyId, Acceptance(1), OrderId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("{}")]
    public void Malformed_or_incomplete_evidence_is_refused_even_with_its_own_matching_hash(string? evidence)
    {
        var snapshot = Rehash(RatedSnapshot(1, 0.2m) with { EvidenceJson = evidence! });

        AssertRejected(snapshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("{}")]
    public void Malformed_or_missing_serialized_snapshot_is_not_a_receipt_proof(string? serialized)
    {
        Assert.Null(InventoryFirstEntryRateProof.Read(serialized, CompanyId, Acceptance(1)));
    }

    [Theory]
    [InlineData("scalar_ratio")]
    [InlineData("zero_rate")]
    [InlineData("negative_rate")]
    [InlineData("missing_value")]
    [InlineData("ratio_not_canonical")]
    [InlineData("ratio_denominator_zero")]
    [InlineData("same_currency")]
    [InlineData("target_currency")]
    [InlineData("source_currency_empty")]
    [InlineData("source_currency_missing")]
    [InlineData("source_kind")]
    [InlineData("reason")]
    [InlineData("version_missing")]
    [InlineData("version_zero")]
    [InlineData("unknown_state")]
    public void Rated_requires_a_positive_consistent_value_and_the_recorded_foreign_order_frame(string fault)
    {
        var snapshot = RatedSnapshot(1, 0.2m);
        snapshot = fault switch
        {
            "scalar_ratio" => snapshot with { RateRatio = new("1", "3") },
            "zero_rate" => snapshot with { Rate = 0m, RateRatio = new("0", "1") },
            "negative_rate" => snapshot with { Rate = -1m, RateRatio = new("-1", "1") },
            "missing_value" => snapshot with { Rate = null, RateRatio = null },
            "ratio_not_canonical" => snapshot with { RateRatio = new("2", "10") },
            "ratio_denominator_zero" => snapshot with { RateRatio = new("1", "0") },
            "same_currency" => snapshot with { SourceCurrencyId = Lira },
            "target_currency" => snapshot with { TargetCurrencyId = Eur },
            "source_currency_empty" => snapshot with { SourceCurrencyId = Guid.Empty },
            "source_currency_missing" => snapshot with { SourceCurrencyId = null },
            "source_kind" => Rehash(snapshot with { SourceKind = "SENTETIK-unrecognized-rate-owner" }),
            "reason" => snapshot with { Reason = "SENTETIK-unavailable" },
            "version_missing" => snapshot with { PurchaseOrderVersion = null },
            "version_zero" => snapshot with { PurchaseOrderVersion = 0 },
            "unknown_state" => snapshot with { State = (InventoryFirstEntryRateStateV1)99 },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        AssertRejected(Rehash(snapshot));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("too_long")]
    [InlineData("different_hash")]
    public void A_missing_or_different_source_version_cannot_authorize_a_snapshot(string fault)
    {
        var snapshot = RatedSnapshot(1, 0.2m);
        snapshot = snapshot with
        {
            SourceVersion = fault switch
            {
                "missing" => null!,
                "blank" => " ",
                "too_long" => new string('x', 161),
                "different_hash" => snapshot.SourceKind + "/" + new string('0', 64),
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            },
        };

        AssertRejected(snapshot);
    }

    [Fact]
    public void NotRequired_is_a_readable_no_conversion_fact_without_a_scalar_or_ratio_of_one()
    {
        var snapshot = NotRequiredSnapshot(1);

        Assert.True(InventoryFirstEntryRateProof.IsValid(snapshot, CompanyId, Acceptance(1), OrderId));
        var read = InventoryFirstEntryRateProof.Read(Encode(snapshot), CompanyId, Acceptance(1));
        Assert.Equal(snapshot, read);
        Assert.Null(read!.Rate);
        Assert.Null(read.RateRatio);
        Assert.Equal(Lira, read.SourceCurrencyId);
        Assert.Equal(Lira, read.TargetCurrencyId);
    }

    [Theory]
    [InlineData("scalar_one")]
    [InlineData("ratio_one")]
    [InlineData("foreign_currency")]
    [InlineData("reason")]
    [InlineData("order_version")]
    [InlineData("source_kind")]
    public void NotRequired_cannot_be_used_as_a_default_FX_rate(string fault)
    {
        var snapshot = NotRequiredSnapshot(1);
        snapshot = fault switch
        {
            "scalar_one" => snapshot with { Rate = 1m },
            "ratio_one" => snapshot with { RateRatio = new("1", "1") },
            "foreign_currency" => snapshot with { SourceCurrencyId = Usd },
            "reason" => snapshot with { Reason = "SENTETIK-unavailable" },
            "order_version" => snapshot with { PurchaseOrderVersion = null },
            "source_kind" => Rehash(snapshot with { SourceKind = InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder }),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        AssertRejected(Rehash(snapshot));
    }

    [Fact]
    public void Unavailable_is_a_readable_explicit_refusal_and_carries_no_convertible_value()
    {
        var snapshot = UnavailableSnapshot(1);

        Assert.True(InventoryFirstEntryRateProof.IsValid(snapshot, CompanyId, Acceptance(1), OrderId));
        Assert.Equal(snapshot, InventoryFirstEntryRateProof.Read(Encode(snapshot), CompanyId, Acceptance(1)));
        Assert.Null(snapshot.Rate);
        Assert.Null(snapshot.RateRatio);
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("ratio")]
    [InlineData("missing_reason")]
    [InlineData("blank_reason")]
    [InlineData("source_kind")]
    public void Unavailable_cannot_smuggle_a_value_or_omit_its_reason(string fault)
    {
        var snapshot = UnavailableSnapshot(1);
        snapshot = fault switch
        {
            "scalar" => snapshot with { Rate = 1m },
            "ratio" => snapshot with { RateRatio = new("1", "1") },
            "missing_reason" => snapshot with { Reason = null },
            "blank_reason" => snapshot with { Reason = " " },
            "source_kind" => Rehash(snapshot with { SourceKind = InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder }),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        AssertRejected(Rehash(snapshot));
    }

    [Fact]
    public void A_merge_preserves_an_exact_one_third_rate_and_both_actual_receipts_with_their_material_shares()
    {
        var sources = MergeSources();

        var result = Merge(sources);

        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Rated, result.Status);
        Assert.Equal((CompanyId, AskedLabelId, (Guid?)Usd, (Guid?)Lira),
            (result.CompanyId, result.LabelId, result.SourceCurrencyId, result.TargetCurrencyId));
        Assert.Equal(new InventoryExactCostRatioV1("1", "3"), result.RateRatio);
        Assert.Null(result.Rate);
        Assert.Null(result.Entry);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(new InventoryExactCostRatioV1("1", "3"), result.Sources[0].MaterialShare);
        Assert.Equal(new InventoryExactCostRatioV1("2", "3"), result.Sources[1].MaterialShare);
        Assert.Equal(Acceptance(1), result.Sources[0].Entry.GoodsAcceptanceId);
        Assert.Equal(Acceptance(2), result.Sources[1].Entry.GoodsAcceptanceId);
        Assert.All(result.Sources, source => Assert.True(InventoryFirstEntryRateProof.IsValid(source.Snapshot,
            CompanyId, source.Entry.GoodsAcceptanceId!.Value, OrderId)));
    }

    [Fact]
    public void Reordering_actual_sources_does_not_change_the_exact_merge_or_detach_their_material_shares()
    {
        var sources = MergeSources();

        var forward = Merge(sources);
        var backward = Merge(sources.Reverse().ToArray());

        Assert.Equal(forward.RateRatio, backward.RateRatio);
        Assert.Equal(forward.SourceVersion, backward.SourceVersion);
        Assert.Equal(forward.Sources.OrderBy(source => source.Entry.MovementId).ToArray(),
            backward.Sources.OrderBy(source => source.Entry.MovementId).ToArray());
    }

    [Fact]
    public void A_duplicate_receipt_movement_is_refused_even_when_its_duplicate_shares_sum_to_one()
    {
        var source = Source(1, 0.2m, new("1", "2"));

        Assert.ThrowsAny<ArgumentException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("1", "5"), LineageVersion(), [source, source]));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("acceptance")]
    [InlineData("currency_pair")]
    [InlineData("source_kind")]
    [InlineData("evidence_hash")]
    [InlineData("evidence_company")]
    [InlineData("evidence_acceptance")]
    [InlineData("evidence_order")]
    [InlineData("evidence_day")]
    [InlineData("scalar_ratio")]
    public void A_foreign_or_inconsistent_source_frame_cannot_authorize_a_merged_rate(string fault)
    {
        var sources = MergeSources();
        var snapshot = sources[1].Snapshot;
        snapshot = fault switch
        {
            "company" => Rehash(snapshot with { CompanyId = OtherCompanyId }),
            "acceptance" => Rehash(snapshot with { AcceptanceId = Acceptance(1) }),
            "currency_pair" => snapshot with { SourceCurrencyId = Eur },
            "source_kind" => Rehash(snapshot with { SourceKind = "SENTETIK-other-owner" }),
            "evidence_hash" => snapshot with { EvidenceJson = snapshot.EvidenceJson + " " },
            "evidence_company" => Reframe(snapshot, "companyId"),
            "evidence_acceptance" => Reframe(snapshot, "acceptanceId"),
            "evidence_order" => Reframe(snapshot, "purchaseOrderId"),
            "evidence_day" => Reframe(snapshot, "entryDay"),
            "scalar_ratio" => snapshot with { RateRatio = new("1", "3") },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        if (fault != "evidence_hash") snapshot = Rehash(snapshot);
        sources[1] = sources[1] with { Snapshot = snapshot };

        Assert.ThrowsAny<ArgumentException>(() => Merge(sources));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_nonrated_actual_receipt_never_becomes_a_valued_merge_source(bool notRequired)
    {
        var snapshot = notRequired ? NotRequiredSnapshot(1) : UnavailableSnapshot(1);
        var source = new InventoryFirstReceiptRateSourceV1(Entry(1), new("1", "1"), snapshot);

        Assert.ThrowsAny<ArgumentException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("1", "1"), LineageVersion(), [source]));
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "3")]
    [InlineData("2", "1")]
    [InlineData("1", "4")]
    public void Nonpositive_or_nonunit_total_material_shares_are_refused(string numerator, string denominator)
    {
        var source = Source(1, 0.2m, new(numerator, denominator));

        Assert.ThrowsAny<ArgumentException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("1", "5"), LineageVersion(), [source]));
    }

    [Theory]
    [InlineData("2", "6")]
    [InlineData("1", "0")]
    [InlineData("01", "3")]
    public void A_malformed_exact_material_share_is_not_normalized_into_a_valid_owner_proof(string numerator, string denominator)
    {
        var source = Source(1, 0.2m, new(numerator, denominator));

        Assert.Throws<InvalidOperationException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("1", "5"), LineageVersion(), [source]));
    }

    [Fact]
    public void A_claimed_rate_different_from_the_exact_weighted_sources_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("3333", "10000"), LineageVersion(), MergeSources()));
    }

    [Fact]
    public void An_exact_rate_requires_at_least_one_actual_receipt_source()
    {
        Assert.ThrowsAny<ArgumentException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("1", "3"), LineageVersion(), []));
        Assert.Throws<ArgumentNullException>(() => InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId,
            Usd, Lira, new("1", "3"), LineageVersion(), null!));
    }

    [Fact]
    public void A_single_exact_source_keeps_the_actual_entry_and_a_finite_scalar_when_exactly_representable()
    {
        var source = Source(1, 0.2m, new("1", "1"));

        var result = InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId, Usd, Lira, new("1", "5"),
            source.Snapshot.SourceVersion, [source]);

        Assert.Equal(source.Entry, result.Entry);
        Assert.Equal(ExchangeRate.Of(Usd, Lira, 0.2m), result.Rate);
        Assert.Equal(new InventoryExactCostRatioV1("1", "5"), result.RateRatio);
        Assert.Equal(source, Assert.Single(result.Sources));
    }

    [Fact]
    public void The_legacy_Rated_factory_preserves_the_finite_rate_and_original_entry_shape()
    {
        var entry = Entry(1);
        var rate = ExchangeRate.Of(Usd, Lira, 0.2m);

        var result = InventoryFirstReceiptExchangeRateV1.Rated(CompanyId, AskedLabelId, entry, rate);

        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.Rated, result.Status);
        Assert.Equal((CompanyId, AskedLabelId), (result.CompanyId, result.LabelId));
        Assert.Equal(entry, result.Entry);
        Assert.Equal(rate, result.Rate);
        Assert.Equal(new InventoryExactCostRatioV1("1", "5"), result.RateRatio);
        Assert.Equal(Usd, result.SourceCurrencyId);
        Assert.Equal(Lira, result.TargetCurrencyId);
        Assert.Null(result.UnavailableReason);
        Assert.Null(result.SourceVersion);
        Assert.Empty(result.Sources);
    }

    private static InventoryFirstReceiptExchangeRateV1 Merge(IReadOnlyList<InventoryFirstReceiptRateSourceV1> sources) =>
        InventoryFirstReceiptExchangeRateV1.RatedExact(CompanyId, AskedLabelId, Usd, Lira, new("1", "3"), LineageVersion(), sources);

    private static InventoryFirstReceiptRateSourceV1[] MergeSources() =>
        [Source(1, 0.2m, new("1", "3")), Source(2, 0.4m, new("2", "3"))];

    private static InventoryFirstReceiptRateSourceV1 Source(int index, decimal rate, InventoryExactCostRatioV1 share) =>
        new(Entry(index), share, RatedSnapshot(index, rate));

    private static InventoryFirstReceiptEntryV1 Entry(int index) => new(Id(100 + index), Id(200 + index), EnteredAtUtc,
        Acceptance(index), null);

    private static InventoryFirstEntryRateV1 RatedSnapshot(int index, decimal rate, bool entryDaySource = false)
    {
        var source = entryDaySource ? InventoryFirstEntryRateSourcesV1.EntryDayTcmb : InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder;
        return Snapshot(index, InventoryFirstEntryRateStateV1.Rated, Usd, rate,
            InventoryExactCostRatioV1.From(ExactCostRatio.Of(rate)), source, null, 9);
    }

    private static InventoryFirstEntryRateV1 NotRequiredSnapshot(int index) => Snapshot(index,
        InventoryFirstEntryRateStateV1.NotRequired, Lira, null, null, InventoryFirstEntryRateSourcesV1.FunctionalCurrency, null, 9);

    private static InventoryFirstEntryRateV1 UnavailableSnapshot(int index) => Snapshot(index,
        InventoryFirstEntryRateStateV1.Unavailable, Usd, null, null, InventoryFirstEntryRateSourcesV1.Unavailable,
        "SENTETIK-first-entry-unavailable", null);

    private static InventoryFirstEntryRateV1 Snapshot(int index, InventoryFirstEntryRateStateV1 state, Guid currency,
        decimal? rate, InventoryExactCostRatioV1? ratio, string source, string? reason, long? orderVersion)
    {
        var usesDayRate = source == InventoryFirstEntryRateSourcesV1.EntryDayTcmb;
        var usesOrderRate = source == InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder;
        var evidence = JsonSerializer.Serialize(new
        {
            companyId = CompanyId,
            acceptanceId = Acceptance(index),
            purchaseOrderId = OrderId,
            entryDay = EntryDay,
            order = orderVersion is null ? null : new
            {
                outcome = usesOrderRate ? 1 : 2,
                companyId = CompanyId,
                purchaseOrderId = OrderId,
                orderVersion,
                orderCurrencyId = currency,
                orderCurrencyCode = currency == Lira ? "TRY" : "USD",
                rate = usesOrderRate ? new { sourceCurrencyId = currency, targetCurrencyId = Lira, rate } : null,
                unavailableReason = usesOrderRate ? null : "purchase_order_exchange_rate_not_specified",
            },
            dayRate = usesDayRate ? new
            {
                rateDay = EntryDay,
                sourceCurrencyId = currency,
                targetCurrencyId = Lira,
                currencyCode = "USD",
                state = ExchangeRateResolutionStatesV1.Resolved,
                source = ExchangeRateSourcesV1.Tcmb,
                halfDayRuleApplied = false,
                previousFullBusinessDay = (DateOnly?)null,
                bulletinDate = EntryDay.AddDays(-1),
                sellingRate = rate?.ToString(CultureInfo.InvariantCulture),
                provenance = ExchangeRateProvenancesV1.Official,
                recordId = Id(300 + index),
                recordVersion = "4",
                code = (string?)null,
                stoppedAt = (DateOnly?)null,
            } : null,
            reason,
        }, Json);
        return Rehash(new(CompanyId, Acceptance(index), OrderId, EntryDay, state, currency, Lira, rate, ratio,
            source, "", evidence, reason, orderVersion));
    }

    private static InventoryFirstEntryRateV1 Reframe(InventoryFirstEntryRateV1 snapshot, string field)
    {
        var root = JsonNode.Parse(snapshot.EvidenceJson)!.AsObject();
        root[field] = field switch
        {
            "companyId" => JsonValue.Create(OtherCompanyId),
            "acceptanceId" => JsonValue.Create(Id(999)),
            "purchaseOrderId" => JsonValue.Create(OtherOrderId),
            "entryDay" => JsonValue.Create(EntryDay.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        return Rehash(snapshot with { EvidenceJson = root.ToJsonString(Json) });
    }

    private static InventoryFirstEntryRateV1 Rehash(InventoryFirstEntryRateV1 snapshot) => snapshot with
    {
        SourceVersion = InventoryFirstEntryRateAdmissionV1.SourceVersionOf(snapshot),
    };

    private static void AssertRejected(InventoryFirstEntryRateV1 snapshot)
    {
        Assert.False(InventoryFirstEntryRateProof.IsValid(snapshot, CompanyId, Acceptance(1), OrderId));
        Assert.Null(InventoryFirstEntryRateProof.Read(Encode(snapshot), CompanyId, Acceptance(1)));
    }

    private static string Encode(InventoryFirstEntryRateV1 snapshot) => JsonSerializer.Serialize(snapshot, Json);
    private static string LineageVersion() => "inventory.first-entry-lineage.v1/" + Hash("SENTETIK-merge-two-receipts");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Guid Acceptance(int index) => Id(400 + index);
    private static Guid Id(int value) => Guid.Parse($"8c230000-0000-4000-8000-{value:D12}");
}
