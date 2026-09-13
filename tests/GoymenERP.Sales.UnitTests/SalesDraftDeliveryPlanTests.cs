using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesDraftDeliveryPlanTests
{
    private static readonly Guid Unit = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static SalesOrderDraftLineRequest Line(string quantity, params string[] slices) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "7", Unit, quantity,
            RequestedDeliverySlices: slices.Select(q => new SalesDraftDeliverySliceV1(
                Guid.NewGuid(), Unit, q, new DateOnly(2026, 9, 20))).ToArray());

    [Theory]
    [InlineData("1000", "400", "600", "0")]
    [InlineData("1000", "400", "400", "200")]
    [InlineData("1.000", "0.4", "0.60", "0")]
    [InlineData("0.00000000000000000000000000003", "0.00000000000000000000000000001", "0.00000000000000000000000000001", "0.00000000000000000000000000001")]
    [InlineData("1000000000000000000000000000000", "400000000000000000000000000000", "600000000000000000000000000000", "0")]
    public void Exact_same_unit_plan_returns_unscheduled_quantity(string quantity, string first, string second, string remaining)
    {
        Assert.Equal(remaining, SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(Line(quantity, first, second)));
    }

    [Theory]
    [InlineData("1000", "400", "700")]
    [InlineData("0.3", "0.1", "0.20000000000000000000000000001")]
    public void Overplanned_quantity_is_rejected_without_rounding(string quantity, string first, string second)
    {
        Assert.Throws<JsonException>(() => SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(Line(quantity, first, second)));
    }

    [Fact]
    public void Foreign_unit_and_duplicate_slice_identity_are_rejected()
    {
        var line = Line("1000", "400");
        var slice = line.RequestedDeliverySlices![0];
        Assert.Throws<JsonException>(() => SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(
            line with { RequestedDeliverySlices = [slice with { OrderUnitId = Guid.NewGuid() }] }));
        Assert.Throws<JsonException>(() => SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(
            line with { RequestedDeliverySlices = [slice, slice] }));
    }

    [Fact]
    public void Unknown_and_empty_plan_do_not_mean_delivered_quantity()
    {
        var line = Line("1000");
        Assert.Equal("1000", SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(line));
        Assert.Equal("1000", SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(line with { RequestedDeliverySlices = null }));
    }

    [Fact]
    public void Physical_examples_decode_with_slices_and_nullable_commercial_fields()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Examples");
        var request = SalesDraftJson.ReadCreate(File.ReadAllText(Path.Combine(path, "create-request.json")));
        Assert.Equal("0", SalesDraftDeliveryPlanV1.GetUnscheduledQuantity(request.Lines[0]));
        Assert.Equal(2, request.Lines[0].RequestedDeliverySlices!.Count);
        var retry = SalesDraftJson.ReadCreate(JsonSerializer.Serialize(request, SalesDraftJson.Options));
        Assert.Equal(request.Lines[0].RequestedDeliverySlices![0], retry.Lines[0].RequestedDeliverySlices![0]);
        Assert.Null(request.PaymentTerms);
        Assert.Null(request.Lines[0].DiscountPercent);
        foreach (var file in new[] { "create-response", "replay-response" })
        {
            var response = JsonSerializer.Deserialize<SalesOrderDraftResponse>(
                File.ReadAllText(Path.Combine(path, file + ".json")), SalesDraftJson.Options)!;
            Assert.Equal(2, response.Detail.Lines[0].RequestedDeliverySlices!.Count);
            Assert.Null(response.Detail.PaymentTerms);
        }
        var detail = JsonSerializer.Deserialize<SalesOrderDraftDetailView>(
            File.ReadAllText(Path.Combine(path, "detail-response.json")), SalesDraftJson.Options)!;
        Assert.Null(detail.DeliveryAddress);
        Assert.Null(detail.Lines[0].DiscountPercent);
    }

    [Fact]
    public void Supplied_commercial_reference_requires_identity_and_version()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "create-request.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        node["paymentTerms"] = new JsonObject { ["id"] = Guid.Empty.ToString(), ["expectedVersion"] = "7" };
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
        node["paymentTerms"]!["id"] = Guid.NewGuid().ToString();
        Assert.NotNull(SalesDraftJson.ReadCreate(node.ToJsonString()).PaymentTerms);
        node["paymentTerms"]!.AsObject().Remove("expectedVersion");
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }

    [Fact]
    public void Create_decoder_rejects_overplan_and_invalid_slice_date()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Examples", "create-request.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        var slice = node["lines"]![0]!["requestedDeliverySlices"]![1]!;
        slice["quantity"] = "700";
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
        slice["quantity"] = "600";
        slice["requestedDeliveryDate"] = "2026-02-30";
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }
}
