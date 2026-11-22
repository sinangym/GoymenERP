using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-3 (SAT-D434/SAT-D435, SAT-D437, KLN-D119) through the service: a purchase order line may be in the approved piece unit with
/// a whole quantity; at approval each piece general line records the Product owner's theoretical kg per piece (golden K10/R1
/// figures), a line without a kilogram source refuses the approval fail-closed (not terminal), denied is 403, an unwired or
/// unreadable owner 503, and a kilogram-only order never asks. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly Guid PieceUnitId = SettingsUnitIdentitiesV1.Piece;

    [Fact]
    public async Task A_piece_line_takes_a_whole_quantity_and_a_fraction_of_a_piece_is_refused_never_rounded()
    {
        var world = new World();

        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "100", unitId: PieceUnitId), Metadata("k-piece"));
        Assert.Equal("100", Assert.Single(created.Detail.Lines).Ordered);
        var stored = (await world.Store.FindOrderAsync(created.Detail.Order.Id))!.Order;
        Assert.Equal(QuantityUnit.Piece, stored.Lines[0].Quantity.Unit);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CreateOrderAsync(OrderOf(null, null, "2.5", unitId: PieceUnitId), Metadata("k-piece-fraction")));
        Assert.Equal((422, PurchasingProblemCodesV1.UnsupportedUnit), (refusal.Status, refusal.Code));
        Assert.Contains(refusal.Errors, error => error.Code == PurchaseUnitCodes.QuantityScaleExceeded);
    }

    [Fact]
    public async Task Approval_records_the_owners_theoretical_kilograms_per_piece_on_the_piece_line_once()
    {
        var port = new FakeTheoreticalKilograms { Answer = PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(12.345m, "product.kg-per-unit.v1/sentetik") };
        var world = new World { TheoreticalKilograms = port };
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "100", unitId: PieceUnitId), Metadata("k-piece-approve-order"));

        var approved = await world.AsApprover(() => world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"),
            Metadata("k-piece-approve")));

        Assert.Equal("Approved", approved.Detail.Order.Status);
        var line = (await world.Store.FindOrderAsync(created.Detail.Order.Id))!.Order.Lines[0];
        // Golden R1 / K01: k 12,345 recorded at approval; 100 pieces → 1 234,500 kg theoretical (TK2).
        Assert.Equal((12.345m, "product.kg-per-unit.v1/sentetik"), (line.TheoreticalKilograms!.KilogramsPerUnit, line.TheoreticalKilograms.SourceVersion));
        Assert.Equal([(Product, PieceUnitId, Day)], port.Calls);
    }

    [Fact]
    public async Task A_piece_line_without_a_kilogram_source_refuses_the_approval_and_the_same_key_succeeds_once_a_source_exists()
    {
        var port = new FakeTheoreticalKilograms
        {
            Answer = PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.NoSource, "product_kg_per_unit_no_source"),
        };
        var world = new World { TheoreticalKilograms = port };
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10", unitId: PieceUnitId), Metadata("k-piece-nosource-order"));

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() =>
            world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"), Metadata("k-piece-nosource"))));

        Assert.Equal((422, PurchasingProblemCodesV1.TheoreticalKilogramsMissing), (refusal.Status, refusal.Code));
        var draft = (await world.Store.FindOrderAsync(created.Detail.Order.Id))!.Order;
        Assert.Equal(("Draft", 2L), (draft.Status.ToString(), draft.Version));
        Assert.Null(draft.Lines[0].TheoreticalKilograms);

        port.Answer = PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(2.5m, "product.kg-per-unit.v1/sentetik-2");
        var approved = await world.AsApprover(() => world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"),
            Metadata("k-piece-nosource")));
        Assert.Equal("Approved", approved.Detail.Order.Status);
    }

    [Theory]
    [InlineData("unwired", 503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable)]
    [InlineData("unavailable", 503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable)]
    [InlineData("too-fine", 503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable)]
    [InlineData("throws", 503, PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable)]
    [InlineData("denied", 403, "forbidden")]
    public async Task An_unreadable_or_denied_owner_refuses_a_piece_order_approval_and_records_nothing(string fault, int status, string code)
    {
        var port = new FakeTheoreticalKilograms
        {
            Answer = fault switch
            {
                "denied" => PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Denied),
                "too-fine" => PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(1.0000001m, "product.kg-per-unit.v1/sentetik"),
                _ => PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Unavailable),
            },
            Throws = fault == "throws",
        };
        var world = new World { TheoreticalKilograms = fault == "unwired" ? null : port };
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10", unitId: PieceUnitId), Metadata($"k-piece-{fault}-order"));

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() =>
            world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"), Metadata($"k-piece-{fault}"))));

        Assert.Equal((status, code), (refusal.Status, refusal.Code));
        Assert.Equal(2L, (await world.Store.FindOrderAsync(created.Detail.Order.Id))!.Order.Version);
    }

    [Fact]
    public async Task A_kilogram_only_order_is_approved_without_asking_the_theoretical_kilogram_owner()
    {
        var port = new FakeTheoreticalKilograms { Throws = true };
        var world = new World { TheoreticalKilograms = port };
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-kg-only-order"));

        var approved = await world.AsApprover(() => world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"),
            Metadata("k-kg-only")));

        Assert.Equal("Approved", approved.Detail.Order.Status);
        Assert.Empty(port.Calls);
        Assert.Null((await world.Store.FindOrderAsync(created.Detail.Order.Id))!.Order.Lines[0].TheoreticalKilograms);
    }

    private sealed class FakeTheoreticalKilograms : IPurchaseTheoreticalKilogramsPerUnitPortV1
    {
        public PurchaseTheoreticalKilogramsPerUnitAnswerV1 Answer { get; set; } =
            PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Unavailable);
        public bool Throws { get; set; }
        public List<(Guid ProductId, Guid UnitId, DateOnly EffectiveOn)> Calls { get; } = [];

        public Task<PurchaseTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((productId, unitId, effectiveOn));
            return Throws ? Task.FromException<PurchaseTheoreticalKilogramsPerUnitAnswerV1>(new InvalidOperationException("SENTETIK"))
                : Task.FromResult(Answer);
        }
    }
}
