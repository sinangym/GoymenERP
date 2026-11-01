using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-008 / SAT-D129 / SAT-D131 / SAT-D124 / SAT-D125 (EK18): the order's due terms (value object) and the ONE deterministic
/// calculator — ONLY the two due types ("nokta vade" = order date + N days, "sevk vadesi" = each shipment's date + N days; no
/// invoice-date type), the invoice-first flow where a "sevk vadesi" counts from the invoice date, instalments each with its own
/// type and days, golden instalment amounts (rounded to the money scale, the remainder on the LAST instalment), no
/// weekend/holiday shift, and a missing base date unresolved (fail-closed).
/// </summary>
public sealed class SalesDueTermsTests
{
    private static readonly DateOnly OrderDate = new(2026, 11, 2);
    private static readonly DateOnly ShipmentDate = new(2026, 11, 18);
    private static readonly DateOnly InvoiceDate = new(2026, 11, 20);
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static SalesDueInstalment Instalment(int sequence, SalesDueBasis basis, int days, string share) => new(sequence, basis, days, share);

    [Fact]
    public void A_point_due_is_the_order_date_plus_the_days_whatever_the_shipment()
    {
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 30, "100")]);

        var withShipment = SalesDueDateCalculator.Compute(terms, OrderDate, ShipmentDate);
        var withoutShipment = SalesDueDateCalculator.Compute(terms, OrderDate, null);

        Assert.True(withShipment.IsResolved);
        Assert.Equal([new SalesDueDate(1, new DateOnly(2026, 12, 2), "100")], withShipment.DueDates);
        Assert.Equal(withShipment.DueDates, withoutShipment.DueDates);
    }

    [Fact]
    public void A_shipment_due_is_each_shipments_day_plus_the_days_so_two_shipments_get_two_due_dates()
    {
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.ShipmentDate, 45, "100")]);

        Assert.Equal(new DateOnly(2027, 1, 2), SalesDueDateCalculator.Compute(terms, OrderDate, ShipmentDate).DueDates[0].DueDate);
        Assert.Equal(new DateOnly(2027, 1, 5), SalesDueDateCalculator.Compute(terms, OrderDate, ShipmentDate.AddDays(3)).DueDates[0].DueDate);
    }

    [Fact]
    public void SAT_D129_there_are_exactly_two_due_types_and_no_invoice_date_type()
    {
        Assert.Equal([SalesDueBasis.OrderDate, SalesDueBasis.ShipmentDate], Enum.GetValues<SalesDueBasis>());
        Assert.DoesNotContain(Enum.GetNames<SalesDueBasis>(), name => name.Contains("Invoice", StringComparison.Ordinal));
        Assert.Equal([SalesDueDateUnresolvedReason.NoTerms, SalesDueDateUnresolvedReason.ShipmentDateMissing],
            Enum.GetValues<SalesDueDateUnresolvedReason>());
    }

    [Fact]
    public void SAT_D131_in_the_invoice_first_flow_a_shipment_due_counts_from_the_invoice_date_and_a_point_due_from_the_order_date()
    {
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 10, "40"), Instalment(2, SalesDueBasis.ShipmentDate, 30, "60")]);

        var invoiceFirst = SalesDueDateCalculator.ComputeInvoiceFirst(terms, OrderDate, InvoiceDate);

        Assert.True(invoiceFirst.IsResolved);
        Assert.Equal([new SalesDueDate(1, new DateOnly(2026, 11, 12), "40"), new SalesDueDate(2, new DateOnly(2026, 12, 20), "60")],
            invoiceFirst.DueDates);
        // The same terms in the shipment-first flow: the "sevk vadesi" counts from the shipment day instead.
        Assert.Equal(new DateOnly(2026, 12, 18), SalesDueDateCalculator.Compute(terms, OrderDate, ShipmentDate).DueDates[1].DueDate);
        Assert.Equal((SalesDueDateUnresolvedReason.NoTerms, 0),
            (SalesDueDateCalculator.ComputeInvoiceFirst(null, OrderDate, InvoiceDate).Unresolved!.Value,
             SalesDueDateCalculator.ComputeInvoiceFirst(null, OrderDate, InvoiceDate).DueDates.Count));
    }

    [Fact]
    public void Instalments_each_carry_their_own_type_and_days()
    {
        var terms = SalesDueTerms.Create([
            Instalment(1, SalesDueBasis.OrderDate, 0, "25"),
            Instalment(2, SalesDueBasis.ShipmentDate, 15, "25.5"),
            Instalment(3, SalesDueBasis.ShipmentDate, 60, "49.50"),
        ]);

        var result = SalesDueDateCalculator.Compute(terms, OrderDate, ShipmentDate);

        Assert.Equal([
            new SalesDueDate(1, OrderDate, "25"),
            new SalesDueDate(2, new DateOnly(2026, 12, 3), "25.5"),
            new SalesDueDate(3, new DateOnly(2027, 1, 17), "49.50"),
        ], result.DueDates);
    }

    [Fact]
    public void A_shipment_due_without_a_shipment_day_or_an_order_without_terms_is_unresolved_never_a_guess()
    {
        var shipment = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 10, "50"), Instalment(2, SalesDueBasis.ShipmentDate, 10, "50")]);

        var pending = SalesDueDateCalculator.Compute(shipment, OrderDate, null);
        // A point due needs no shipment.
        Assert.True(SalesDueDateCalculator.Compute(SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 1, "100")]), OrderDate, null).IsResolved);
        Assert.Equal((SalesDueDateUnresolvedReason.ShipmentDateMissing, 0), (pending.Unresolved!.Value, pending.DueDates.Count));
        var none = SalesDueDateCalculator.Compute(null, OrderDate, ShipmentDate);
        Assert.Equal((SalesDueDateUnresolvedReason.NoTerms, 0), (none.Unresolved!.Value, none.DueDates.Count));
    }

    [Fact]
    public void SAT_D125_a_due_date_on_a_weekend_or_a_holiday_does_not_move()
    {
        // 2026-11-02 + 5 days = Saturday 2026-11-07; 2026-10-28 + 1 day = 29 October (Cumhuriyet Bayramı). Neither moves.
        var saturday = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 5, "100")]);
        Assert.Equal(new DateOnly(2026, 11, 7), SalesDueDateCalculator.Compute(saturday, OrderDate, null).DueDates[0].DueDate);
        var holiday = SalesDueTerms.Create([Instalment(1, SalesDueBasis.ShipmentDate, 1, "100")]);
        Assert.Equal(new DateOnly(2026, 10, 29), SalesDueDateCalculator.Compute(holiday, OrderDate, new DateOnly(2026, 10, 28)).DueDates[0].DueDate);
    }

    /// <summary>SAT-D124 golden cases: every instalment but the last rounded half away from zero to the scale; the last the remainder.</summary>
    [Theory]
    [InlineData("100.00", 2, new[] { "33.33", "33.33", "33.34" }, new[] { "33.33", "33.33", "33.34" })]
    [InlineData("100.00", 2, new[] { "33.34", "33.33", "33.33" }, new[] { "33.34", "33.33", "33.33" })]
    [InlineData("1000.00", 2, new[] { "33.33", "33.33", "33.34" }, new[] { "333.30", "333.30", "333.40" })]
    [InlineData("100.01", 2, new[] { "50", "50" }, new[] { "50.01", "50.00" })]
    [InlineData("10.00", 2, new[] { "33.335", "33.335", "33.33" }, new[] { "3.33", "3.33", "3.34" })]
    [InlineData("0.05", 2, new[] { "30", "30", "40" }, new[] { "0.02", "0.02", "0.01" })]
    [InlineData("7", 0, new[] { "50", "50" }, new[] { "4", "3" })]
    [InlineData("1234.56", 2, new[] { "100" }, new[] { "1234.56" })]
    [InlineData("10.000", 3, new[] { "33.33", "33.33", "33.34" }, new[] { "3.333", "3.333", "3.334" })]
    [InlineData("0.01", 3, new[] { "50", "50" }, new[] { "0.005", "0.005" })]
    [InlineData("0.00", 2, new[] { "50", "50" }, new[] { "0.00", "0.00" })]
    [InlineData("0", 0, new[] { "25", "25", "50" }, new[] { "0", "0", "0" })]
    public void SAT_D124_instalment_amounts_are_rounded_to_the_scale_and_the_remainder_goes_to_the_last(string total, int scale,
        string[] shares, string[] expected)
    {
        var terms = SalesDueTerms.Create([.. shares.Select((share, index) => Instalment(index + 1, SalesDueBasis.OrderDate, 30 * (index + 1), share))]);
        var money = Money.Of(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture), Try, scale);

        var amounts = SalesDueDateCalculator.SplitAmount(terms, money);

        Assert.Equal(expected.Select(value => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
            amounts.Select(amount => amount.Amount.Amount));
        Assert.Equal(Enumerable.Range(1, shares.Length), amounts.Select(amount => amount.SequenceNo));
        Assert.Equal(money.Amount, amounts.Sum(amount => amount.Amount.Amount));
        Assert.All(amounts, amount => Assert.Equal((Try, scale), (amount.Amount.CurrencyId, amount.Amount.Scale)));
    }

    [Fact]
    public void SAT_D124_a_split_that_would_leave_the_last_instalment_negative_is_refused_never_repaired()
    {
        // Many small shares rounded UP: 0.03 over five 16.67 % instalments rounds each to 0.01 (0.005001), leaving 0.03 − 0.05 < 0
        // for the 16.65 % last one.
        var terms = SalesDueTerms.Create([
            Instalment(1, SalesDueBasis.OrderDate, 1, "16.67"), Instalment(2, SalesDueBasis.OrderDate, 2, "16.67"),
            Instalment(3, SalesDueBasis.OrderDate, 3, "16.67"), Instalment(4, SalesDueBasis.OrderDate, 4, "16.67"),
            Instalment(5, SalesDueBasis.OrderDate, 5, "16.67"), Instalment(6, SalesDueBasis.OrderDate, 6, "16.65"),
        ]);

        var refused = Assert.Throws<SalesDueTermsException>(() => SalesDueDateCalculator.SplitAmount(terms, Money.Of(0.03m, Try, 2)));

        Assert.Equal(SalesDueTermsCodes.AmountNotSplittable, refused.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesDueDateCalculator.SplitAmount(terms, Money.Of(-1m, Try, 2)));
    }

    [Theory]
    [InlineData("none", SalesDueTermsCodes.InstalmentCountOutOfRange, null)]
    [InlineData("gap", SalesDueTermsCodes.SequenceInvalid, 1)]
    [InlineData("basis", SalesDueTermsCodes.BasisInvalid, 0)]
    [InlineData("negative-days", SalesDueTermsCodes.DaysOutOfRange, 0)]
    [InlineData("too-many-days", SalesDueTermsCodes.DaysOutOfRange, 0)]
    [InlineData("zero-share", SalesDueTermsCodes.ShareInvalid, 0)]
    [InlineData("over-share", SalesDueTermsCodes.ShareInvalid, 0)]
    [InlineData("signed-share", SalesDueTermsCodes.ShareInvalid, 0)]
    [InlineData("not-whole", SalesDueTermsCodes.SharesNotWhole, null)]
    [InlineData("over-whole", SalesDueTermsCodes.SharesNotWhole, null)]
    public void The_terms_are_refused_with_a_stable_code_naming_the_instalment(string fault, string code, int? index)
    {
        IReadOnlyList<SalesDueInstalment> instalments = fault switch
        {
            "none" => [],
            "gap" => [Instalment(1, SalesDueBasis.OrderDate, 1, "50"), Instalment(3, SalesDueBasis.OrderDate, 2, "50")],
            "basis" => [Instalment(1, (SalesDueBasis)9, 1, "100")],
            "negative-days" => [Instalment(1, SalesDueBasis.OrderDate, -1, "100")],
            "too-many-days" => [Instalment(1, SalesDueBasis.OrderDate, SalesDueTerms.MaximumDaysAfter + 1, "100")],
            "zero-share" => [Instalment(1, SalesDueBasis.OrderDate, 1, "0"), Instalment(2, SalesDueBasis.OrderDate, 2, "100")],
            "over-share" => [Instalment(1, SalesDueBasis.OrderDate, 1, "100.01")],
            "signed-share" => [Instalment(1, SalesDueBasis.OrderDate, 1, "+100")],
            "not-whole" => [Instalment(1, SalesDueBasis.OrderDate, 1, "33.33"), Instalment(2, SalesDueBasis.OrderDate, 2, "66.66")],
            _ => [Instalment(1, SalesDueBasis.OrderDate, 1, "60"), Instalment(2, SalesDueBasis.OrderDate, 2, "40.001")],
        };

        var refused = Assert.Throws<SalesDueTermsException>(() => SalesDueTerms.Create(instalments));

        Assert.Equal((code, index), (refused.Code, refused.InstalmentIndex));
    }

    [Fact]
    public void Too_many_instalments_are_refused()
    {
        Assert.Equal(SalesDueTermsCodes.InstalmentCountOutOfRange, Assert.Throws<SalesDueTermsException>(() => SalesDueTerms.Create(
            [.. Enumerable.Range(1, SalesDueTerms.MaximumInstalments + 1).Select(sequence => Instalment(sequence, SalesDueBasis.OrderDate, sequence, "1"))])).Code);
        Assert.Equal(4, SalesDueTerms.Create([.. Enumerable.Range(1, 4).Select(sequence => Instalment(sequence, SalesDueBasis.ShipmentDate, sequence * 30, "25"))])
            .Instalments.Count);
    }

    [Fact]
    public void Two_terms_saying_the_same_compare_equal_by_value_and_any_difference_is_a_change()
    {
        var left = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 30, "50"), Instalment(2, SalesDueBasis.OrderDate, 60, "50")]);
        var spelled = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 30, "50.0"), Instalment(2, SalesDueBasis.OrderDate, 60, "50.00")]);
        var days = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 31, "50"), Instalment(2, SalesDueBasis.OrderDate, 60, "50")]);
        var basis = SalesDueTerms.Create([Instalment(1, SalesDueBasis.ShipmentDate, 30, "50"), Instalment(2, SalesDueBasis.OrderDate, 60, "50")]);

        Assert.True(SalesDueTerms.SameTerms(left, spelled));
        // A value object: equal by value (so a header record holding it compares by value), equal hashes for equal values.
        Assert.Equal(left, spelled);
        Assert.Equal(left.GetHashCode(), spelled.GetHashCode());
        Assert.NotEqual(left, days);
        Assert.NotEqual(left, basis);
        Assert.True(SalesDueTerms.SameTerms(null, null));
        Assert.False(SalesDueTerms.SameTerms(left, days));
        Assert.False(SalesDueTerms.SameTerms(left, basis));
        Assert.False(SalesDueTerms.SameTerms(left, null));
    }

    [Fact]
    public void The_wire_names_the_types_round_trips_and_its_strict_reader_refuses_an_unknown_type_or_a_signed_share()
    {
        Assert.Equal(["OrderDate", "ShipmentDate"], SalesDueBasisNamesV1.All);
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 0, "10"), Instalment(2, SalesDueBasis.ShipmentDate, 90, "90")]);
        var wire = SalesDraftHeaderApiV1.ToWire(terms)!;
        Assert.Equal([(1, "OrderDate", 0, "10"), (2, "ShipmentDate", 90, "90")],
            wire.Instalments.Select(item => (item.SequenceNo, item.Basis, item.DaysAfter, item.SharePercent)));
        Assert.True(SalesDueTerms.SameTerms(terms, SalesDraftHeaderApiV1.ToDomain(wire)));

        const string Body = """{"expectedVersion":"4","reason":"r","partyId":"33333333-3333-4333-8333-333333333333","companyAccountId":"44444444-4444-4444-8444-444444444444","currencyId":"55555555-5555-4555-8555-555555555555","deliveryAddress":null,"contact":null,"salesOwnerUserId":null,"dueTerms":{"instalments":[{"sequenceNo":1,"basis":"BASIS","daysAfter":30,"sharePercent":"SHARE"}]}}""";
        Assert.Equal("ShipmentDate", SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "ShipmentDate").Replace("SHARE", "100")).DueTerms!.Instalments[0].Basis);
        // SAT-D129: the removed invoice-date type is an unknown name, refused like any other.
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "InvoiceDate").Replace("SHARE", "100")));
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "orderDate").Replace("SHARE", "100")));
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "OrderDate").Replace("SHARE", "-1")));
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("\"contact\":null,", "").Replace("BASIS", "OrderDate").Replace("SHARE", "100")));
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("\"contact\":null,", "\"contact\":null,\"extra\":1,").Replace("BASIS", "OrderDate").Replace("SHARE", "100")));
    }
}
