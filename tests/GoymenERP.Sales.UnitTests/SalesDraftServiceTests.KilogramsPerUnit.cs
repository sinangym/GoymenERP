using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D437 (AK-4): a piece line's theoretical kg/unit is read from the Product owner at the internal approval, pinned on the
/// document and never re-read; a piece line without a figure refuses the approval (fail-closed); kg lines are untouched.
/// Golden K01 / M01 use k = 12,345 kg/adet (ADET_KG_FORMUL_GOLDEN_V1).
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid PieceUnit = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");

    private static CreateSalesOrderDraftRequest PieceRequest(string quantity = "100") =>
        new("Order", Party, Account, Day, Currency,
            [new SalesOrderDraftLineRequest(Guid.NewGuid(), Product, "7", PieceUnit, quantity)],
            DueTerms: TestDueTerms.PointDue30Wire);

    private static World PieceWorld(FakeKilogramsPerUnit? source = null)
    {
        var world = new World { KilogramsPerUnit = source ?? new FakeKilogramsPerUnit() };
        world.Settings.PieceUnits.Add(PieceUnit);
        return world;
    }

    [Fact]
    public async Task SAT_D437_piece_line_approval_pins_the_owner_figure_and_the_detail_shows_it()
    {
        var source = new FakeKilogramsPerUnit { Figure = 12.345m };
        var world = PieceWorld(source);
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-create"))).Detail;
        Assert.Null(created.Lines[0].TheoreticalKilogramsPerUnit); // TK7: no figure before the approval.

        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-submit"));
        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-approve"));

        Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);
        Assert.Equal("12.345", approved.Detail.Lines[0].TheoreticalKilogramsPerUnit);
        var draft = Assert.Single(world.Store.Drafts);
        var pin = Assert.Single(draft.KilogramsPerUnitSnapshots);
        Assert.Equal((draft.Lines[0].Id, 0, Product, PieceUnit, 12.345m), (pin.LineId, pin.Revision, pin.ProductId, pin.UnitId, pin.KilogramsPerUnit));
        Assert.Equal(draft.Approval.DecidedAtUtc, pin.RecordedAtUtc);
        Assert.Contains("\"productVersion\":\"7\"", pin.SourceProof, StringComparison.Ordinal);
        Assert.Equal([(Product, PieceUnit)], source.Asked.Select(asked => (asked.ProductId, asked.UnitId)));
    }

    [Fact]
    public async Task SAT_D437_a_later_card_change_does_not_move_the_pinned_figure()
    {
        var source = new FakeKilogramsPerUnit { Figure = 12.345m };
        var world = PieceWorld(source);
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-m10-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-m10-submit"));
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-m10-approve"));

        source.Figure = 12.4m; // Golden M10 / TM4: the card changed after the approval.
        var detail = await world.Service().GetDraftDetailAsync(created.Draft.Id);

        Assert.Equal("12.345", detail.Lines[0].TheoreticalKilogramsPerUnit);
        Assert.Single(source.Asked);
    }

    [Theory]
    [InlineData(ProductTheoreticalKilogramsPerUnitCodesV1.NoSource)]
    [InlineData(ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard)]
    public async Task SAT_D473_a_readable_card_without_a_figure_approves_the_piece_line_with_its_figure_left_empty(string ownerCode)
    {
        var source = new FakeKilogramsPerUnit { Outcome = ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, Code = ownerCode };
        var world = PieceWorld(source);
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-nosource-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-nosource-submit"));

        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-nosource-approve"));

        Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);
        Assert.Null(approved.Detail.Lines[0].TheoreticalKilogramsPerUnit);
        Assert.Single(source.Asked);
        var draft = Assert.Single(world.Store.Drafts);
        Assert.Empty(draft.KilogramsPerUnitSnapshots);

        // SAT-D437: a card fixed later never moves the approved document; a replay answers the recorded approval.
        world.KilogramsPerUnit = new FakeKilogramsPerUnit { Figure = 12.345m };
        var replay = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-nosource-approve"));
        Assert.True(replay.IsReplay);
        Assert.Null(replay.Detail.Lines[0].TheoreticalKilogramsPerUnit);
        Assert.Empty(Assert.Single(world.Store.Drafts).KilogramsPerUnitSnapshots);
    }

    [Theory]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused, ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict, ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict)]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable, ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable, ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable)]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound, null, SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable)]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied, null, SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable)]
    public async Task SAT_D473_an_owner_that_could_not_answer_still_refuses_the_approval_and_records_nothing(
        ProductTheoreticalKilogramsPerUnitOutcomeV1 outcome, string? ownerCode, string fieldCode)
    {
        var world = PieceWorld(new FakeKilogramsPerUnit { Outcome = outcome, Code = ownerCode });
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-none-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-none-submit"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-none-approve")));

        Assert.Equal((409, SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable), (refused.Status, refused.Code));
        Assert.Equal([new SalesDraftFieldError("lines[0].orderUnitId", fieldCode)], refused.Errors);
        var draft = Assert.Single(world.Store.Drafts);
        Assert.Equal(InternalApprovalState.PendingApproval, draft.ApprovalState);
        Assert.Empty(draft.KilogramsPerUnitSnapshots);

        // Not recorded: once the card has a kilogram source the same key approves.
        world.KilogramsPerUnit = new FakeKilogramsPerUnit { Figure = 12.345m };
        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-none-approve"));
        Assert.Equal("12.345", approved.Detail.Lines[0].TheoreticalKilogramsPerUnit);
    }

    [Fact]
    public async Task SAT_D437_an_uncomposed_owner_refuses_a_piece_line_approval()
    {
        var world = new World();
        world.Settings.PieceUnits.Add(PieceUnit);
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-absent-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-absent-submit"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-absent-approve")));

        Assert.Equal((409, SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task SAT_D437_an_owner_answer_for_another_product_is_refused_never_pinned()
    {
        var world = PieceWorld(new FakeKilogramsPerUnit { Figure = 12.345m, AnswerProduct = Guid.NewGuid() });
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-mismatch-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-mismatch-submit"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-mismatch-approve")));

        Assert.Equal(SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable, refused.Code);
        Assert.Empty(Assert.Single(world.Store.Drafts).KilogramsPerUnitSnapshots);
    }

    [Fact]
    public async Task SAT_D437_a_kilogram_line_approval_never_reads_the_owner()
    {
        var source = new FakeKilogramsPerUnit { Outcome = ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, Code = ProductTheoreticalKilogramsPerUnitCodesV1.NoSource };
        var world = new World { KilogramsPerUnit = source };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("kpu-kg-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-kg-submit"));

        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-kg-approve"));

        Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);
        Assert.Null(approved.Detail.Lines[0].TheoreticalKilogramsPerUnit);
        Assert.Empty(source.Asked);
        Assert.Empty(Assert.Single(world.Store.Drafts).KilogramsPerUnitSnapshots);
    }

    [Fact]
    public void SAT_D437_the_domain_refuses_a_pin_outside_an_approval_or_for_a_kilogram_line()
    {
        var snapshot = new SalesLineKilogramsPerUnitSnapshot(Guid.NewGuid(), 0, Product, PieceUnit, 12.345m, "{}",
            new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => (snapshot with { KilogramsPerUnit = 0m }).Validate());
        Assert.Throws<InvalidOperationException>(() => (snapshot with { KilogramsPerUnit = 1.0000001m }).Validate());
        Assert.Throws<InvalidOperationException>(() => (snapshot with { SourceProof = " " }).Validate());
        snapshot.Validate();
    }

    [Fact]
    public async Task SAT_D443_a_metre_order_unit_is_refused_as_unsupported_never_counted_as_pieces()
    {
        // Golden K07 / M05 / M15: metre is not opened in this train ("bu trende gösterilmez"); the line is refused, not a piece line.
        var world = PieceWorld();
        world.Settings.MetreUnits.Add(Metre);
        var request = PieceRequest() with { Lines = [PieceRequest().Lines[0] with { OrderUnitId = Metre, Quantity = "12.5" }] };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(request, Metadata("kpu-metre")));

        Assert.Equal((422, "unsupported_unit"), (refused.Status, refused.Code));
        Assert.Equal("lines[0].orderUnitId", Assert.Single(refused.Errors).Field);
        Assert.Empty(world.Store.Drafts);
    }

    [Fact]
    public async Task SAT_D443_a_count_profile_that_is_not_the_standard_piece_identity_is_refused()
    {
        var other = Guid.Parse("10000000-0000-4000-8000-000000000443");
        var world = PieceWorld();
        world.Settings.PieceUnits.Add(other);
        var request = PieceRequest() with { Lines = [PieceRequest().Lines[0] with { OrderUnitId = other }] };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(request, Metadata("kpu-other-count")));

        Assert.Equal((422, "unsupported_unit"), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Drafts);
    }

    [Theory]
    [InlineData(ProductPhysicalUnitKindV1.Metre)]
    [InlineData(ProductPhysicalUnitKindV1.Kilogram)]
    [InlineData(ProductPhysicalUnitKindV1.Tonne)]
    public async Task SAT_D443_an_owner_figure_for_a_non_piece_unit_kind_is_never_pinned(ProductPhysicalUnitKindV1 kind)
    {
        var source = new FakeKilogramsPerUnit { Figure = 1.235m, UnitKind = kind };
        var world = PieceWorld(source);
        var created = (await world.Service().CreateDraftAsync(PieceRequest(), Metadata("kpu-kind-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("kpu-kind-submit"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("kpu-kind-approve")));

        Assert.Equal((409, SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable), (refused.Status, refused.Code));
        Assert.Empty(Assert.Single(world.Store.Drafts).KilogramsPerUnitSnapshots);
    }

    private static readonly Guid Metre = Guid.Parse("564b68b4-02a8-5e72-af2a-4f5cb9b91702");

    private sealed class FakeKilogramsPerUnit : IProductTheoreticalKilogramsPerUnitReadPortV1
    {
        public decimal Figure { get; set; } = 12.345m;
        public ProductTheoreticalKilogramsPerUnitOutcomeV1 Outcome { get; set; } = ProductTheoreticalKilogramsPerUnitOutcomeV1.Found;
        public string? Code { get; set; }
        public Guid? AnswerProduct { get; set; }
        public ProductPhysicalUnitKindV1 UnitKind { get; set; } = ProductPhysicalUnitKindV1.Piece;
        public List<(Guid ProductId, Guid UnitId, DateOnly EffectiveOn)> Asked { get; } = [];

        public Task<ProductTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((productId, unitId, effectiveOn));
            return Task.FromResult(Outcome == ProductTheoreticalKilogramsPerUnitOutcomeV1.Found
                ? ProductTheoreticalKilogramsPerUnitAnswerV1.Found(Figure, new ProductTheoreticalKilogramsPerUnitEvidenceV1(Company,
                    AnswerProduct ?? productId, unitId, UnitKind, "7", "product-unit-conversion-v1", 3, null, null,
                    Guid.Parse("10000000-0000-4000-8000-000000000437"), 2, 7.85m,
                    Guid.Parse("10000000-0000-4000-8000-000000000438"), 1, "settings-unit-semantics-v1"))
                : ProductTheoreticalKilogramsPerUnitAnswerV1.Without(Outcome, Code));
        }
    }
}
