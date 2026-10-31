using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-008 / SAT-D86 / SAT-D101: the order's due terms (value object) and the ONE deterministic due-date calculator — every
/// DEC-SAL-008 model (fatura vadesi, araç/sevkiyat, sipariş tarihli nokta vade, taksit, karma) from its basis date plus whole
/// calendar days; a missing basis date is unresolved (fail-closed), nothing is defaulted or moved off a weekend.
/// </summary>
public sealed class SalesDueTermsTests
{
    private static readonly DateOnly OrderDate = new(2026, 11, 2);
    private static readonly DateOnly InvoiceDate = new(2026, 11, 20);
    private static readonly DateOnly ShipmentDate = new(2026, 11, 18);

    private static SalesDueInstalment Instalment(int sequence, SalesDueBasis basis, int days, string share) => new(sequence, basis, days, share);

    [Fact]
    public void Net_days_from_the_invoice_date_is_the_invoice_date_plus_the_calendar_days()
    {
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.InvoiceDate, 30, "100")]);

        var result = SalesDueDateCalculator.Compute(terms, OrderDate, InvoiceDate, shipmentDate: null);

        Assert.True(result.IsResolved);
        Assert.Equal([new SalesDueDate(1, new DateOnly(2026, 12, 20), "100")], result.DueDates);
    }

    [Fact]
    public void Instalments_of_different_bases_each_count_from_their_own_date_karma()
    {
        var terms = SalesDueTerms.Create([
            Instalment(1, SalesDueBasis.OrderDate, 0, "25"),
            Instalment(2, SalesDueBasis.ShipmentDate, 15, "25.5"),
            Instalment(3, SalesDueBasis.InvoiceDate, 60, "49.50"),
        ]);

        var result = SalesDueDateCalculator.Compute(terms, OrderDate, InvoiceDate, ShipmentDate);

        Assert.Equal([
            new SalesDueDate(1, OrderDate, "25"),
            new SalesDueDate(2, new DateOnly(2026, 12, 3), "25.5"),
            new SalesDueDate(3, new DateOnly(2027, 1, 19), "49.50"),
        ], result.DueDates);
    }

    [Fact]
    public void A_missing_basis_date_or_missing_terms_is_unresolved_never_a_guess()
    {
        var shipment = SalesDueTerms.Create([Instalment(1, SalesDueBasis.ShipmentDate, 10, "100")]);
        var invoice = SalesDueTerms.Create([Instalment(1, SalesDueBasis.InvoiceDate, 10, "100")]);

        Assert.Equal(SalesDueDateUnresolvedReason.ShipmentDateMissing,
            SalesDueDateCalculator.Compute(shipment, OrderDate, InvoiceDate, null).Unresolved);
        Assert.Equal(SalesDueDateUnresolvedReason.InvoiceDateMissing,
            SalesDueDateCalculator.Compute(invoice, OrderDate, null, ShipmentDate).Unresolved);
        var none = SalesDueDateCalculator.Compute(null, OrderDate, InvoiceDate, ShipmentDate);
        Assert.Equal((SalesDueDateUnresolvedReason.NoTerms, 0), (none.Unresolved!.Value, none.DueDates.Count));
    }

    [Fact]
    public void The_calendar_days_are_counted_as_they_are_a_weekend_due_date_does_not_move()
    {
        // 2026-11-20 + 1 day = Saturday 2026-11-21: no business-day shift is decided, so none is applied (NEEDS-SINAN).
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.InvoiceDate, 1, "100")]);
        Assert.Equal(new DateOnly(2026, 11, 21), SalesDueDateCalculator.Compute(terms, OrderDate, InvoiceDate, null).DueDates[0].DueDate);
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
            "gap" => [Instalment(1, SalesDueBasis.InvoiceDate, 1, "50"), Instalment(3, SalesDueBasis.InvoiceDate, 2, "50")],
            "basis" => [Instalment(1, (SalesDueBasis)9, 1, "100")],
            "negative-days" => [Instalment(1, SalesDueBasis.InvoiceDate, -1, "100")],
            "too-many-days" => [Instalment(1, SalesDueBasis.InvoiceDate, SalesDueTerms.MaximumDaysAfter + 1, "100")],
            "zero-share" => [Instalment(1, SalesDueBasis.InvoiceDate, 1, "0"), Instalment(2, SalesDueBasis.InvoiceDate, 2, "100")],
            "over-share" => [Instalment(1, SalesDueBasis.InvoiceDate, 1, "100.01")],
            "signed-share" => [Instalment(1, SalesDueBasis.InvoiceDate, 1, "+100")],
            "not-whole" => [Instalment(1, SalesDueBasis.InvoiceDate, 1, "33.33"), Instalment(2, SalesDueBasis.InvoiceDate, 2, "66.66")],
            _ => [Instalment(1, SalesDueBasis.InvoiceDate, 1, "60"), Instalment(2, SalesDueBasis.InvoiceDate, 2, "40.001")],
        };

        var refused = Assert.Throws<SalesDueTermsException>(() => SalesDueTerms.Create(instalments));

        Assert.Equal((code, index), (refused.Code, refused.InstalmentIndex));
    }

    [Fact]
    public void Too_many_instalments_are_refused_and_the_maximum_is_accepted()
    {
        Assert.Equal(SalesDueTermsCodes.InstalmentCountOutOfRange, Assert.Throws<SalesDueTermsException>(() => SalesDueTerms.Create(
            [.. Enumerable.Range(1, SalesDueTerms.MaximumInstalments + 1).Select(sequence => Instalment(sequence, SalesDueBasis.InvoiceDate, sequence, "1"))])).Code);
        Assert.Equal(4, SalesDueTerms.Create([.. Enumerable.Range(1, 4).Select(sequence => Instalment(sequence, SalesDueBasis.InvoiceDate, sequence * 30, "25"))])
            .Instalments.Count);
    }

    [Fact]
    public void Two_terms_saying_the_same_compare_equal_by_value_and_any_difference_is_a_change()
    {
        var left = SalesDueTerms.Create([Instalment(1, SalesDueBasis.InvoiceDate, 30, "50"), Instalment(2, SalesDueBasis.InvoiceDate, 60, "50")]);
        var spelled = SalesDueTerms.Create([Instalment(1, SalesDueBasis.InvoiceDate, 30, "50.0"), Instalment(2, SalesDueBasis.InvoiceDate, 60, "50.00")]);
        var days = SalesDueTerms.Create([Instalment(1, SalesDueBasis.InvoiceDate, 31, "50"), Instalment(2, SalesDueBasis.InvoiceDate, 60, "50")]);
        var basis = SalesDueTerms.Create([Instalment(1, SalesDueBasis.ShipmentDate, 30, "50"), Instalment(2, SalesDueBasis.InvoiceDate, 60, "50")]);

        Assert.True(SalesDueTerms.SameTerms(left, spelled));
        Assert.True(SalesDueTerms.SameTerms(null, null));
        Assert.False(SalesDueTerms.SameTerms(left, days));
        Assert.False(SalesDueTerms.SameTerms(left, basis));
        Assert.False(SalesDueTerms.SameTerms(left, null));
    }

    [Fact]
    public void The_wire_round_trips_and_its_strict_reader_refuses_an_unknown_basis_or_a_signed_share()
    {
        var terms = SalesDueTerms.Create([Instalment(1, SalesDueBasis.OrderDate, 0, "10"), Instalment(2, SalesDueBasis.ShipmentDate, 90, "90")]);
        var wire = SalesDraftHeaderApiV1.ToWire(terms)!;
        Assert.Equal([(1, "OrderDate", 0, "10"), (2, "ShipmentDate", 90, "90")],
            wire.Instalments.Select(item => (item.SequenceNo, item.Basis, item.DaysAfter, item.SharePercent)));
        Assert.True(SalesDueTerms.SameTerms(terms, SalesDraftHeaderApiV1.ToDomain(wire)));

        const string Body = """{"expectedVersion":"4","reason":"r","partyId":"33333333-3333-4333-8333-333333333333","companyAccountId":"44444444-4444-4444-8444-444444444444","currencyId":"55555555-5555-4555-8555-555555555555","deliveryAddress":null,"contact":null,"salesOwnerUserId":null,"dueTerms":{"instalments":[{"sequenceNo":1,"basis":"BASIS","daysAfter":30,"sharePercent":"SHARE"}]}}""";
        Assert.Equal("InvoiceDate", SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "InvoiceDate").Replace("SHARE", "100")).DueTerms!.Instalments[0].Basis);
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "invoiceDate").Replace("SHARE", "100")));
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("BASIS", "InvoiceDate").Replace("SHARE", "-1")));
        // Strict: a missing member or an unknown one is a shape error.
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("\"contact\":null,", "").Replace("BASIS", "InvoiceDate").Replace("SHARE", "100")));
        Assert.Throws<System.Text.Json.JsonException>(() => SalesDraftHeaderApiV1.Read(Body.Replace("\"contact\":null,", "\"contact\":null,\"extra\":1,").Replace("BASIS", "InvoiceDate").Replace("SHARE", "100")));
    }
}
