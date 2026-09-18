using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SALES-P1 (T03): a sales order draft is born Draft / Rev.0 with 1–500 lines; every line carries
/// what the Product and Settings owners answered for this company and business date; Ö2 holds on
/// every quantity; the delivery plan never exceeds the line. Every identity here is synthetic.
/// </summary>
public sealed class SalesOrderDraftTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 14, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 18);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid PieceUnit = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesDraftHeader Header() => new(Party, Account, Currency, Day);

    private static MoneyProfileSnapshot Money(Guid? company = null, DateOnly? effectiveOn = null) =>
        new(company ?? Company, Currency, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", effectiveOn ?? Day);

    private static UnitProfileSnapshot KgUnitProfile(int profileScale = 3, Guid? company = null) =>
        new(company ?? Company, KgUnit, "kg", KgProfile, "4", true, profileScale, Math.Min(profileScale, 3));

    private static UnitProfileSnapshot PieceUnitProfile() =>
        new(Company, PieceUnit, "pcs", Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "1", false, 0, 0);

    private static ProductSnapshot Snapshot(UnitProfileSnapshot orderUnit, DateOnly? effectiveOn = null, Guid? bindingCompany = null)
    {
        var binding = new UnitBindingSnapshot(bindingCompany ?? Company, orderUnit.UnitId, orderUnit.ProfileId, orderUnit.ProfileVersion);
        return new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", effectiveOn ?? Day, binding, binding, binding);
    }

    private static SalesDraftLineInput Line(decimal quantity, string? text = null, UnitProfileSnapshot? unit = null,
        IReadOnlyList<DeliverySliceInput>? slices = null, ProductSnapshot? product = null, Guid? lineId = null,
        CommercialDraftFields? commercial = null, string? note = null)
    {
        var orderUnit = unit ?? KgUnitProfile();
        return new SalesDraftLineInput(lineId ?? Guid.NewGuid(), product ?? Snapshot(orderUnit), orderUnit,
            text ?? quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), quantity,
            RequestedDeliverySlices: slices, Commercial: commercial, Note: note);
    }

    private static SalesOrderDraft Draft() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0);

    [Fact]
    public void A_draft_is_born_as_draft_rev0_version1_with_its_company_customer_currency_and_money_profile()
    {
        var draft = Draft();
        Assert.Equal((SalesDraftKind.Order, SalesDraftState.Draft, 0, 1L), (draft.Kind, draft.State, draft.Revision, draft.Version));
        Assert.Equal((Company, Party, Account, Currency, Day), (draft.CompanyId, draft.Header.PartyId, draft.Header.CompanyAccountId, draft.Header.CurrencyId, draft.Header.BusinessDate));
        Assert.Equal("SO-2026-000001", draft.DocumentNo);
        Assert.Equal(Currency, draft.Money.CurrencyId);
        Assert.Empty(draft.Lines);
        Assert.Equal(T0.ToUniversalTime(), draft.CreatedAtUtc);
        Assert.Equal(SalesRuleCodes.DraftWithoutLines, CodeOf(draft.RequireStorable));
    }

    [Fact]
    public void Creation_needs_real_identities_a_complete_money_profile_of_this_company_and_well_formed_references()
    {
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, " ", Header(), Money(), T0));
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header() with { PartyId = Guid.Empty }, Money(), T0));
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header() with { CompanyAccountId = Guid.Empty }, Money(), T0));
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header(), Money() with { CurrencyId = Guid.NewGuid() }, T0));
        Assert.Throws<ArgumentException>(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header() with { Contact = new OwnerReference(Guid.Empty, "1") }, Money(), T0));
        Assert.Throws<ArgumentNullException>(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header(), null!, T0));
        Assert.Equal(SalesRuleCodes.SnapshotCompanyMismatch,
            CodeOf(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header(), Money(OtherCompany), T0)));
        // The money profile is the business date's (ÜRÜN #306 P3-2): another date is another answer.
        Assert.Equal(SalesRuleCodes.SnapshotDateMismatch,
            CodeOf(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header(), Money(effectiveOn: Day.AddDays(1)), T0)));
        Assert.Equal(SalesRuleCodes.NoteTooLong,
            CodeOf(() => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header() with { Note = new string('x', 4001) }, Money(), T0)));
    }

    [Fact]
    public void A_line_keeps_the_owners_snapshots_the_exact_quantity_and_the_clients_text()
    {
        var draft = Draft();
        var line = draft.AddLine(1, Line(12.500m, "12.500"), T0);

        Assert.Equal((1, Product, "7", KgUnit, 3, 3), (line.LineNo, line.Product.ProductId, line.Product.ProductVersion, line.OrderUnit.UnitId, line.OrderUnit.ProfileScale, line.OrderUnit.Scale));
        Assert.Equal(Quantity.Of(12.5m, QuantityUnit.Kilogram), line.Quantity);
        Assert.Equal("12.500", line.QuantityText);
        Assert.Equal(CommercialDraftFields.None, line.Commercial);
        Assert.Empty(line.RequestedDeliverySlices);
        Assert.Equal(Quantity.Of(12.5m, QuantityUnit.Kilogram), line.UnplannedQuantity);
        Assert.Equal(2, draft.Version);
        Assert.Single(draft.Lines);
        draft.RequireStorable();
    }

    [Fact]
    public void Every_mutation_names_the_version_it_read()
    {
        var draft = Draft();
        var stale = Assert.Throws<SalesVersionConflictException>(() => draft.AddLine(2, Line(1m), T0));
        Assert.Equal((SalesRuleCodes.VersionConflict, 2L, 1L), (stale.Code, stale.ExpectedVersion, stale.CurrentVersion));
        Assert.Empty(draft.Lines);
        draft.AddLine(1, Line(1m), T0);
        Assert.Throws<SalesVersionConflictException>(() => draft.AddLine(1, Line(1m), T0));
        Assert.Equal((1, 2L), (draft.Lines.Count, draft.Version));
    }

    [Theory]
    [InlineData(3, "1.0005")]
    [InlineData(2, "1.005")]
    [InlineData(5, "1.0004")]
    public void More_decimals_than_the_applied_scale_are_refused_never_rounded(int profileScale, string text)
    {
        // Ö2: the profile may narrow the kilogram precision, never widen it past 3.
        var draft = Draft();
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(SalesRuleCodes.QuantityScaleExceeded,
            CodeOf(() => draft.AddLine(1, Line(value, text, KgUnitProfile(profileScale)), T0)));
        Assert.Empty(draft.Lines);
        Assert.Equal(1, draft.Version);
    }

    [Fact]
    public void The_applied_scale_is_derived_from_the_profile_and_a_snapshot_cannot_widen_it()
    {
        // ÜRÜN #306 P3-1: a snapshot saying Scale 3 over a profile of 1, or 5 over Ö2's 3, is malformed, not a permission.
        var draft = Draft();
        Assert.Equal(1, SalesOrderDraft.AppliedScale(KgUnitProfile(1)));
        Assert.Equal(3, SalesOrderDraft.AppliedScale(KgUnitProfile(5)));
        Assert.Equal(0, SalesOrderDraft.AppliedScale(PieceUnitProfile() with { ProfileScale = 2, Scale = 0 }));
        var widened = KgUnitProfile(1) with { Scale = 3 };
        Assert.Throws<ArgumentException>(() => draft.AddLine(1, Line(1.25m, "1.25", widened, product: Snapshot(widened)), T0));
        var beyondCap = KgUnitProfile(5) with { Scale = 5 };
        Assert.Throws<ArgumentException>(() => draft.AddLine(1, Line(1m, "1", beyondCap, product: Snapshot(beyondCap)), T0));
        Assert.Empty(draft.Lines);
        Assert.Equal(1, draft.Version);
    }

    [Theory]
    [InlineData("12.51")]
    [InlineData("12,5")]
    [InlineData(" 12.5")]
    [InlineData("")]
    public void The_quantity_text_must_be_the_quantity(string text)
    {
        // ÜRÜN #306 P3-3: the text is kept verbatim, but it is bound to the value it names.
        var draft = Draft();
        Assert.Throws<ArgumentException>(() => draft.AddLine(1, Line(12.5m, text), T0));
        Assert.Empty(draft.Lines);
    }

    [Fact]
    public void Trailing_zeros_are_not_precision_and_pieces_are_whole()
    {
        var draft = Draft();
        var kg = draft.AddLine(1, Line(12.5000m, "12.5000", KgUnitProfile(1)), T0);
        Assert.Equal(12.5m, kg.Quantity.Value);
        var pieces = draft.AddLine(2, Line(20m, "20", PieceUnitProfile()), T0);
        Assert.Equal(Quantity.Of(20m, QuantityUnit.Piece), pieces.Quantity);
        Assert.Equal(SalesRuleCodes.QuantityScaleExceeded, CodeOf(() => draft.AddLine(3, Line(20.5m, "20.5", PieceUnitProfile()), T0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.AddLine(3, Line(0m, "0"), T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.AddLine(3, Line(-1m, "-1"), T0));
    }

    [Fact]
    public void A_snapshot_of_another_company_another_date_or_another_order_unit_is_refused_by_code()
    {
        var draft = Draft();
        var unit = KgUnitProfile();
        Assert.Equal(SalesRuleCodes.SnapshotCompanyMismatch,
            CodeOf(() => draft.AddLine(1, Line(1m, unit: KgUnitProfile(company: OtherCompany), product: Snapshot(unit)), T0)));
        Assert.Equal(SalesRuleCodes.SnapshotCompanyMismatch,
            CodeOf(() => draft.AddLine(1, Line(1m, product: Snapshot(unit, bindingCompany: OtherCompany)), T0)));
        Assert.Equal(SalesRuleCodes.SnapshotDateMismatch,
            CodeOf(() => draft.AddLine(1, Line(1m, product: Snapshot(unit, effectiveOn: Day.AddDays(-1))), T0)));
        // The selected order unit must be the unit the Product owner binds as the order unit.
        Assert.Equal(SalesRuleCodes.ProductUnitMismatch,
            CodeOf(() => draft.AddLine(1, Line(1m, unit: PieceUnitProfile(), product: Snapshot(unit)), T0)));
        Assert.Equal(SalesRuleCodes.ProductUnitMismatch,
            CodeOf(() => draft.AddLine(1, Line(1m, unit: KgUnitProfile(), product: Snapshot(KgUnitProfile() with { ProfileVersion = "5" })), T0)));
        Assert.Empty(draft.Lines);
        Assert.Equal(1, draft.Version);
    }

    [Fact]
    public void Delivery_slices_are_same_unit_unique_positive_and_never_more_than_the_line()
    {
        // #257: 1000 = 400 + 600 leaves 0; 400 + 700 is refused; 400 + 400 leaves 200 unplanned, never delivered.
        var draft = Draft();
        DeliverySliceInput Slice(decimal kg, Guid? unit = null, Guid? id = null) =>
            new(id ?? Guid.NewGuid(), unit ?? KgUnit, kg, Day.AddDays(7));

        var exact = draft.AddLine(1, Line(1000m, slices: [Slice(400m), Slice(600m)]), T0);
        Assert.True(exact.UnplannedQuantity.IsZero);
        var partial = draft.AddLine(2, Line(1000m, slices: [Slice(400m), Slice(400m)]), T0);
        Assert.Equal(Quantity.Of(200m, QuantityUnit.Kilogram), partial.UnplannedQuantity);
        Assert.Equal(2, partial.RequestedDeliverySlices.Count);

        Assert.Equal(SalesRuleCodes.SlicesExceedQuantity, CodeOf(() => draft.AddLine(3, Line(1000m, slices: [Slice(400m), Slice(700m)]), T0)));
        Assert.Equal(SalesRuleCodes.SliceUnitMismatch, CodeOf(() => draft.AddLine(3, Line(1000m, slices: [Slice(400m, unit: PieceUnit)]), T0)));
        var id = Guid.NewGuid();
        Assert.Equal(SalesRuleCodes.DuplicateSliceId, CodeOf(() => draft.AddLine(3, Line(1000m, slices: [Slice(400m, id: id), Slice(100m, id: id)]), T0)));
        Assert.Equal(SalesRuleCodes.QuantityScaleExceeded, CodeOf(() => draft.AddLine(3, Line(1000m, slices: [Slice(0.0005m)]), T0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.AddLine(3, Line(1000m, slices: [Slice(0m)]), T0));
        Assert.Throws<ArgumentException>(() => draft.AddLine(3, Line(1000m, slices: [Slice(1m, id: Guid.Empty)]), T0));
        Assert.Equal((2, 3L), (draft.Lines.Count, draft.Version));
    }

    [Fact]
    public void Commercial_fields_are_kept_as_text_and_only_the_price_unit_pairing_is_enforced()
    {
        var draft = Draft();
        var priced = draft.AddLine(1, Line(1m, commercial: new CommercialDraftFields("12.50", KgUnit, "0")), T0);
        Assert.Equal(("12.50", KgUnit, "0"), (priced.Commercial.UnitPrice, priced.Commercial.PriceUnitId, priced.Commercial.DiscountPercent));
        Assert.Equal(SalesRuleCodes.PriceUnitPairing, CodeOf(() => draft.AddLine(2, Line(1m, commercial: new CommercialDraftFields("12.50", null, null)), T0)));
        Assert.Equal(SalesRuleCodes.PriceUnitPairing, CodeOf(() => draft.AddLine(2, Line(1m, commercial: new CommercialDraftFields(null, KgUnit, null)), T0)));
        var unpriced = draft.AddLine(2, Line(1m, commercial: new CommercialDraftFields(null, null, "5")), T0);
        Assert.Equal("5", unpriced.Commercial.DiscountPercent);
    }

    [Fact]
    public void Line_identity_note_and_count_bounds_are_held_by_the_aggregate()
    {
        var draft = Draft();
        var id = Guid.NewGuid();
        draft.AddLine(1, Line(1m, lineId: id), T0);
        Assert.Equal(SalesRuleCodes.DuplicateLineId, CodeOf(() => draft.AddLine(2, Line(1m, lineId: id), T0)));
        Assert.Throws<ArgumentException>(() => draft.AddLine(2, Line(1m, lineId: Guid.Empty), T0));
        Assert.Equal(SalesRuleCodes.NoteTooLong, CodeOf(() => draft.AddLine(2, Line(1m, note: new string('n', 4001)), T0)));
        Assert.Throws<ArgumentNullException>(() => draft.AddLine(2, null!, T0));

        for (var i = 2; i <= SalesOrderDraft.MaximumLines; i++)
        {
            draft.AddLine(draft.Version, Line(1m), T0);
        }

        Assert.Equal(SalesOrderDraft.MaximumLines, draft.Lines.Count);
        Assert.Equal(SalesRuleCodes.TooManyLines, CodeOf(() => draft.AddLine(draft.Version, Line(1m), T0)));
        Assert.Equal(SalesOrderDraft.MaximumLines + 1L, draft.Version);
    }

    [Fact]
    public void The_views_cannot_be_used_to_bypass_the_rules()
    {
        var draft = Draft();
        var line = draft.AddLine(1, Line(1m, slices: [new DeliverySliceInput(Guid.NewGuid(), KgUnit, 1m, Day)]), T0);
        var before = draft.Version;
        Assert.False(draft.Lines is List<SalesOrderDraftLine>);
        Assert.False(line.RequestedDeliverySlices is List<DeliverySlice>);
        Assert.Throws<NotSupportedException>(() => ((IList<SalesOrderDraftLine>)draft.Lines).Add(line));
        Assert.Throws<NotSupportedException>(() => ((IList<DeliverySlice>)line.RequestedDeliverySlices).Clear());
        Assert.Single(draft.Lines);
        Assert.Single(line.RequestedDeliverySlices);
        Assert.Equal(before, draft.Version);
    }

    [Fact]
    public void The_line_level_guard_is_a_second_authority_and_reports_itself_as_such()
    {
        var unit = KgUnitProfile();
        var slices = new[] { new DeliverySlice(Guid.NewGuid(), Quantity.Of(2m, QuantityUnit.Kilogram), Day) };
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => new SalesOrderDraftLine(Guid.NewGuid(), 1, Snapshot(unit), unit,
            Quantity.Of(1m, QuantityUnit.Kilogram), "1", null, null, CommercialDraftFields.None, slices)));
        Assert.Equal(SalesRuleCodes.InternalInvariant, CodeOf(() => new SalesOrderDraftLine(Guid.NewGuid(), 1, Snapshot(unit), unit,
            Quantity.Zero(QuantityUnit.Kilogram), "0", null, null, CommercialDraftFields.None, [])));
    }

    [Fact]
    public void Every_rule_code_is_distinct_and_in_the_sales_namespace()
    {
        var codes = typeof(SalesRuleCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
        Assert.NotEmpty(codes);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.StartsWith("sales_", code, StringComparison.Ordinal));
    }
}
