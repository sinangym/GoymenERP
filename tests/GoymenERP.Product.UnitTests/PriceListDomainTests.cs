using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;

namespace GoymenERP.Product.UnitTests;

/// <summary>AT-018 price list aggregate: context, versions, validity, publication and resolution. SENTETIK.</summary>
public sealed class PriceListDomainTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ProductA = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ProductB = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Kg = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 30);

    [Fact]
    public void A_new_list_is_version_1_with_one_draft_version_and_keeps_its_context()
    {
        var list = NewList();

        Assert.Equal(1, list.Version);
        Assert.Equal("Yurt içi satış", list.Name);
        Assert.Equal("TRY", list.CurrencyCode);
        Assert.Equal(PriceListChannel.Sales, list.Channel);
        Assert.Equal(PriceListVatBasis.ExcludingVat, list.VatBasis);
        var version = Assert.Single(list.Versions);
        Assert.Equal((1, PriceListVersionState.Draft), (version.VersionNo, version.State));
        Assert.Equal(42000.50m, version.Lines[0].UnitPrice);
        // The written 42000.50 is kept as its canonical value (the scale check ran on the written text first).
        Assert.Equal((1, "42000.5"), (PriceList.Scale(version.Lines[0].UnitPrice), PriceList.Format(version.Lines[0].UnitPrice)));
        Assert.Null(list.ResolveOn(Today));
        Assert.Equal(PriceListVersionStatus.Draft, list.StatusOf(version, Today));
    }

    [Theory]
    [InlineData("", "TRY", PriceListRuleCodes.InvalidName)]
    [InlineData("A\u0001", "TRY", PriceListRuleCodes.InvalidName)]
    [InlineData("Liste", "try", PriceListRuleCodes.InvalidCurrency)]
    [InlineData("Liste", "TRYY", PriceListRuleCodes.InvalidCurrency)]
    public void The_list_context_is_refused_when_malformed(string name, string currency, string code)
    {
        var exception = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, name, currency,
            PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null, [], "user-1", Now));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Only_the_sales_channel_and_prices_excluding_vat_are_accepted_fail_closed()
    {
        Assert.Equal(PriceListRuleCodes.UnsupportedChannel, Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company,
            "L", "TRY", (PriceListChannel)2, PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null, [], "u", Now)).Code);
        Assert.Equal(PriceListRuleCodes.UnsupportedVatBasis, Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company,
            "L", "TRY", PriceListChannel.Sales, (PriceListVatBasis)2, Guid.NewGuid(), Today, null, [], "u", Now)).Code);
    }

    [Theory]
    [InlineData("0", null, PriceListRuleCodes.InvalidPrice)]
    [InlineData("1.12345", null, PriceListRuleCodes.PriceScaleExceeded)]
    [InlineData("100", "100.01", PriceListRuleCodes.FloorAbovePrice)]
    [InlineData("100", "0", PriceListRuleCodes.InvalidPrice)]
    public void Prices_are_positive_exact_up_to_four_decimals_and_the_floor_never_exceeds_the_price(string price, string? floor, string code)
    {
        var line = new PriceListLineInput(Guid.NewGuid(), ProductA, Kg, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
            floor is null ? null : decimal.Parse(floor, System.Globalization.CultureInfo.InvariantCulture));
        var exception = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, "L", "TRY", PriceListChannel.Sales,
            PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null, [line], "u", Now));
        Assert.Equal(code, exception.Code);
        Assert.StartsWith("lines[0].", exception.Field, StringComparison.Ordinal);
    }

    [Fact]
    public void A_product_is_priced_once_per_unit_in_a_version_and_validity_must_not_end_before_it_starts()
    {
        var duplicate = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, "L", "TRY", PriceListChannel.Sales,
            PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null,
            [Line(ProductA, 10m), Line(ProductA, 11m)], "u", Now));
        Assert.Equal((PriceListRuleCodes.QuantityTierOverlap, "lines[1].lowerQuantity"), (duplicate.Code, duplicate.Field));

        var validity = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, "L", "TRY", PriceListChannel.Sales,
            PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, Today.AddDays(-1), [], "u", Now));
        Assert.Equal((PriceListRuleCodes.InvalidValidity, "validThrough"), (validity.Code, validity.Field));
    }

    [Fact]
    public void A_draft_is_revised_with_the_expected_version_and_a_stale_version_is_a_conflict()
    {
        var list = NewList();
        var lineId = list.Versions[0].Lines[0].LineId;

        list.ReviseDraft(1, "  Yeni ad ", Today.AddDays(1), Today.AddDays(30), [new PriceListLineInput(lineId, ProductA, Kg, 41000m, 39000m),
            Line(ProductB, 9.5m)], "editor-1", Now.AddMinutes(1));

        Assert.Equal(2, list.Version);
        Assert.Equal("Yeni ad", list.Name);
        var draft = list.DraftVersion!;
        Assert.Equal((Today.AddDays(1), Today.AddDays(30)), (draft.ValidFrom, draft.ValidThrough));
        Assert.Equal([1, 2], draft.Lines.Select(line => line.LineNo));
        Assert.Equal((lineId, 41000m, 39000m), (draft.Lines[0].LineId, draft.Lines[0].UnitPrice, draft.Lines[0].FloorUnitPrice));
        Assert.Equal(["user-1", "editor-1"], draft.PreparerActorIds);
        var conflict = Assert.Throws<PriceListVersionConflictException>(() => list.ReviseDraft(1, "x", Today, null, [], "editor-2", Now));
        Assert.Equal((1, 2), (conflict.ExpectedVersion, conflict.CurrentVersion));
    }

    [Fact]
    public void Publishing_needs_lines_a_start_not_in_the_past_and_makes_the_version_immutable()
    {
        var empty = PriceList.Create(Guid.NewGuid(), Company, "Boş", "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(),
            Today, null, [], "u", Now);
        Assert.Equal(PriceListRuleCodes.VersionWithoutLines, Assert.Throws<PriceListRuleException>(() => empty.Publish(1, 1, Today, "p", Now)).Code);

        var past = PriceList.Create(Guid.NewGuid(), Company, "Geçmiş", "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(),
            Today.AddDays(-1), null, [Line(ProductA, 1m)], "u", Now);
        Assert.Equal(PriceListRuleCodes.ValidFromInPast, Assert.Throws<PriceListRuleException>(() => past.Publish(1, 1, Today, "p", Now)).Code);

        var list = NewList();
        list.Publish(1, 1, Today, "publisher-1", Now.AddMinutes(5));

        var published = list.Versions[0];
        Assert.Equal((PriceListVersionState.Published, "publisher-1", Now.AddMinutes(5)), (published.State, published.PublishedBy, published.PublishedAtUtc));
        Assert.Null(list.DraftVersion);
        Assert.Equal(2, list.Version);
        Assert.Same(published, list.ResolveOn(Today));
        Assert.Equal(PriceListVersionStatus.Active, list.StatusOf(published, Today));
        Assert.Equal(PriceListRuleCodes.NoDraftVersion,
            Assert.Throws<PriceListRuleException>(() => list.ReviseDraft(2, "x", Today, null, [], "u", Now)).Code);
        Assert.Equal(PriceListRuleCodes.VersionNotDraft, Assert.Throws<PriceListRuleException>(() => list.Publish(2, 1, Today, "p", Now)).Code);
    }

    [Fact]
    public void A_new_version_carries_the_published_prices_and_supersedes_from_its_start_without_fallback()
    {
        var list = NewList();
        list.Publish(1, 1, Today, "p", Now);
        var next = list.StartNextVersion(2, Guid.NewGuid(), Today.AddDays(10), null, "u", Now);
        Assert.Equal((2, PriceListVersionState.Draft), (next.VersionNo, next.State));
        Assert.Equal(list.Versions[0].Lines.Select(line => (line.ProductId, line.PriceUnitId, line.UnitPrice)),
            next.Lines.Select(line => (line.ProductId, line.PriceUnitId, line.UnitPrice)));
        Assert.Equal(PriceListRuleCodes.DraftVersionOpen,
            Assert.Throws<PriceListRuleException>(() => list.StartNextVersion(3, Guid.NewGuid(), Today.AddDays(20), null, "u", Now)).Code);

        // Version 2 prices only product B: from its start product A has no list price (no fallback to version 1).
        list.ReviseDraft(3, list.Name, Today.AddDays(10), null, [Line(ProductB, 7m)], "editor-2", Now);
        list.Publish(4, 2, Today, "p", Now);

        Assert.Equal(1, list.ResolveOn(Today.AddDays(9))!.VersionNo);
        Assert.Equal(2, list.ResolveOn(Today.AddDays(10))!.VersionNo);
        Assert.Null(list.ResolveOn(Today.AddDays(10))!.LineFor(ProductA, Kg));
        Assert.Equal(PriceListVersionStatus.Planned, list.StatusOf(list.Versions[1], Today));
        Assert.Equal(PriceListVersionStatus.Ended, list.StatusOf(list.Versions[0], Today.AddDays(10)));
    }

    [Fact]
    public void Every_creator_or_editor_of_the_exact_version_is_barred_from_publishing_and_another_actor_may_publish()
    {
        var list = NewList();
        var line = list.DraftVersion!.Lines[0];
        list.ReviseDraft(1, list.Name, Today, null,
            [new PriceListLineInput(line.LineId, line.ProductId, line.PriceUnitId, 41000m, null)], "editor-1", Now.AddMinutes(1));
        list.ReviseDraft(2, list.Name, Today, null,
            [new PriceListLineInput(line.LineId, line.ProductId, line.PriceUnitId, 40000m, null)], "editor-1", Now.AddMinutes(2));

        Assert.Equal(["user-1", "editor-1"], list.DraftVersion!.PreparerActorIds);
        Assert.Equal(PriceListRuleCodes.PublisherPreparedVersion,
            Assert.Throws<PriceListRuleException>(() => list.Publish(3, 1, Today, "user-1", Now.AddMinutes(3))).Code);
        Assert.Equal(PriceListRuleCodes.PublisherPreparedVersion,
            Assert.Throws<PriceListRuleException>(() => list.Publish(3, 1, Today, "editor-1", Now.AddMinutes(3))).Code);

        list.Publish(3, 1, Today, "publisher-1", Now.AddMinutes(3));
        Assert.Equal("publisher-1", list.Versions[0].PublishedBy);
    }

    [Fact]
    public void A_later_version_must_start_after_every_published_one_and_an_ended_validity_leaves_a_gap()
    {
        var list = PriceList.Create(Guid.NewGuid(), Company, "Kısa", "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(),
            Today.AddDays(5), Today.AddDays(6), [Line(ProductA, 1m)], "u", Now);
        list.Publish(1, 1, Today, "p", Now);
        Assert.Null(list.ResolveOn(Today.AddDays(4)));
        Assert.NotNull(list.ResolveOn(Today.AddDays(6)));
        Assert.Null(list.ResolveOn(Today.AddDays(7)));

        list.StartNextVersion(2, Guid.NewGuid(), Today.AddDays(5), null, "u", Now);
        Assert.Equal(PriceListRuleCodes.ValidFromNotAfterPublished,
            Assert.Throws<PriceListRuleException>(() => list.Publish(3, 2, Today, "p", Now)).Code);
    }

    [Fact]
    public void Quantity_tiers_are_lower_inclusive_upper_exclusive_reject_overlap_and_leave_gaps_empty()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var otherUnit = Guid.NewGuid();
        var otherUnitLine = Guid.NewGuid();
        var list = PriceList.Create(Guid.NewGuid(), Company, "Kademeli", "TRY", PriceListChannel.Sales,
            PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null,
            [
                new PriceListLineInput(first, ProductA, Kg, 100m, null, 0m, 10m),
                new PriceListLineInput(second, ProductA, Kg, 90m, null, 10m, 20m),
                new PriceListLineInput(third, ProductA, Kg, 80m, null, 25m, null),
                new PriceListLineInput(otherUnitLine, ProductA, otherUnit, 70m, null, 0m, 20m),
            ], "u", Now);
        var version = list.Versions[0];

        Assert.Equal(first, version.LineFor(ProductA, Kg, 9.999m)!.LineId);
        Assert.Equal(second, version.LineFor(ProductA, Kg, 10m)!.LineId);
        Assert.Null(version.LineFor(ProductA, Kg, 20m));
        Assert.Equal(third, version.LineFor(ProductA, Kg, 25m)!.LineId);
        Assert.Equal(otherUnitLine, version.LineFor(ProductA, otherUnit, 10m)!.LineId);
        Assert.Null(version.LineFor(ProductA, Kg));

        var overlap = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, "Çakışan", "TRY",
            PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null,
            [
                new PriceListLineInput(Guid.NewGuid(), ProductA, Kg, 100m, null, 0m, 10m),
                new PriceListLineInput(Guid.NewGuid(), ProductA, Kg, 90m, null, 9m, 20m),
            ], "u", Now));
        Assert.Equal(PriceListRuleCodes.QuantityTierOverlap, overlap.Code);

        var empty = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, "Boş", "TRY",
            PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null,
            [new PriceListLineInput(Guid.NewGuid(), ProductA, Kg, 100m, null, 10m, 10m)], "u", Now));
        Assert.Equal(PriceListRuleCodes.InvalidQuantityTier, empty.Code);

        var scale = Assert.Throws<PriceListRuleException>(() => PriceList.Create(Guid.NewGuid(), Company, "Ölçek", "TRY",
            PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null,
            [new PriceListLineInput(Guid.NewGuid(), ProductA, Kg, 100m, null, 0m, 10.0001m)], "u", Now));
        Assert.Equal(PriceListRuleCodes.QuantityScaleExceeded, scale.Code);
    }

    [Fact]
    public void Restore_refuses_gaps_two_drafts_and_unordered_published_starts()
    {
        var version1 = PriceListVersion.Restore(Guid.NewGuid(), 1, Today, null, PriceListVersionState.Published,
            [new PriceListLine(Guid.NewGuid(), 1, ProductA, Kg, 5m, null)], "u", Now, "p", Now);
        var draft2 = PriceListVersion.Restore(Guid.NewGuid(), 2, Today.AddDays(1), null, PriceListVersionState.Draft, [], "u", Now, null, null);
        var draft3 = PriceListVersion.Restore(Guid.NewGuid(), 3, Today.AddDays(2), null, PriceListVersionState.Draft, [], "u", Now, null, null);

        var restored = PriceList.Restore(Guid.NewGuid(), Company, "L", "TRY", PriceListChannel.Sales, PriceListVatBasis.ExcludingVat, 3, "u", Now, Now,
            [draft2, version1]);
        Assert.Equal([1, 2], restored.Versions.Select(version => version.VersionNo));
        Assert.Throws<InvalidOperationException>(() => PriceList.Restore(Guid.NewGuid(), Company, "L", "TRY", PriceListChannel.Sales,
            PriceListVatBasis.ExcludingVat, 3, "u", Now, Now, [version1, draft3]));
        Assert.Throws<InvalidOperationException>(() => PriceList.Restore(Guid.NewGuid(), Company, "L", "TRY", PriceListChannel.Sales,
            PriceListVatBasis.ExcludingVat, 3, "u", Now, Now, [version1, draft2, draft3]));
        Assert.Throws<InvalidOperationException>(() => PriceListVersion.Restore(Guid.NewGuid(), 1, Today, null, PriceListVersionState.Published,
            [], "u", Now, null, null));
    }

    [Theory]
    [InlineData("42000", true)]
    [InlineData("42000.5", true)]
    [InlineData("0.0001", true)]
    [InlineData("1.00000", false)]
    [InlineData("01", false)]
    [InlineData("-1", false)]
    [InlineData("1,5", false)]
    [InlineData("1e3", false)]
    [InlineData("1000000000000000", false)]
    public void The_wire_carries_exact_decimal_text_never_a_number(string text, bool accepted)
    {
        Assert.Equal(accepted, ProductPriceListWire.ParsePrice(text) is not null);
    }

    [Fact]
    public void The_wire_dates_and_versions_are_strict()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), ProductPriceListWire.ParseDate("2026-10-01"));
        Assert.Null(ProductPriceListWire.ParseDate("2026-02-30"));
        Assert.Null(ProductPriceListWire.ParseDate("01.10.2026"));
        Assert.Equal(12L, ProductPriceListWire.ParseVersion("12"));
        Assert.Null(ProductPriceListWire.ParseVersion("012"));
        Assert.Null(ProductPriceListWire.ParseVersion("0"));
        Assert.Equal("42000.50", ProductPriceListWire.FormatPrice(ProductPriceListWire.ParsePrice("42000.50")!.Value));
        Assert.Equal("40000", PriceList.Format(PriceList.Normalize(40000.0000m)));
        Assert.Equal(10m, ProductPriceListWire.ParseQuantity("10.000"));
        Assert.Null(ProductPriceListWire.ParseQuantity("10.0001"));
        Assert.Null(ProductPriceListWire.ParseQuantity("-1"));
    }

    private static PriceList NewList() => PriceList.Create(Guid.NewGuid(), Company, " Yurt içi satış ", "TRY", PriceListChannel.Sales,
        PriceListVatBasis.ExcludingVat, Guid.NewGuid(), Today, null,
        [new PriceListLineInput(Guid.NewGuid(), ProductA, Kg, 42000.50m, 40000m)], "user-1", Now);

    private static PriceListLineInput Line(Guid product, decimal price) => new(Guid.NewGuid(), product, Kg, price, null);
}
