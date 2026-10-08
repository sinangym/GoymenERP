using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesOrderDraftContractTests
{
    private static string Example(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Examples", name + ".json"));
    private static T Read<T>(string name) =>
        JsonSerializer.Deserialize<T>(Example(name), SalesDraftJson.Options)!;

    [Fact]
    public void Retry_preserves_line_identity_and_unknown_price()
    {
        var request = SalesDraftJson.ReadCreate(Example("create-request"));
        var retry = SalesDraftJson.ReadCreate(JsonSerializer.Serialize(request, SalesDraftJson.Options));
        Assert.Equal(JsonSerializer.Serialize(request.Lines[0], SalesDraftJson.Options),
            JsonSerializer.Serialize(retry.Lines[0], SalesDraftJson.Options));
        Assert.Equal("1000", retry.Lines[0].Quantity);
        Assert.Null(retry.Lines[0].UnitPrice);
        Assert.Null(retry.Lines[0].PriceUnitId);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("partyId")]
    [InlineData("companyAccountId")]
    [InlineData("currencyId")]
    [InlineData("businessDate")]
    [InlineData("lines")]
    public void Required_create_members_cannot_be_missing_or_null(string field)
    {
        var node = JsonNode.Parse(Example("create-request"))!.AsObject();
        node.Remove(field);
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
        node[field] = null;
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }

    [Theory]
    [InlineData("companyId")]
    [InlineData("actorUserId")]
    [InlineData("state")]
    [InlineData("version")]
    public void Body_cannot_supply_server_fields(string field)
    {
        var node = JsonNode.Parse(Example("create-request"))!;
        node[field] = "unauthorized";
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }

    [Fact]
    public void Null_line_empty_lines_and_duplicate_identity_are_rejected()
    {
        var node = JsonNode.Parse(Example("create-request"))!;
        var lines = node["lines"]!.AsArray();
        lines.Add(lines[0]!.DeepClone());
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
        lines.Clear();
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
        lines.Add((JsonNode?)null);
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }

    [Fact]
    public void Price_requires_own_unit_and_omission_means_unknown_on_create()
    {
        var node = JsonNode.Parse(Example("create-request"))!;
        var line = node["lines"]![0]!.AsObject();
        line.Remove("unitPrice");
        Assert.Null(SalesDraftJson.ReadCreate(node.ToJsonString()).Lines[0].UnitPrice);
        line["unitPrice"] = "0";
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
        line["priceUnitId"] = "77777777-7777-7777-7777-777777777777";
        var request = SalesDraftJson.ReadCreate(node.ToJsonString());
        Assert.NotEqual(request.Lines[0].OrderUnitId, request.Lines[0].PriceUnitId);
        Assert.Equal("0", request.Lines[0].UnitPrice);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,000")]
    [InlineData("NaN")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("1.")]
    public void Invalid_quantity_shape_is_rejected(string quantity)
    {
        var node = JsonNode.Parse(Example("create-request"))!;
        node["lines"]![0]!["quantity"] = quantity;
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }

    [Fact]
    public void Decimal_text_is_not_rounded_or_converted_before_owner_validation()
    {
        var node = JsonNode.Parse(Example("create-request"))!;
        const string exact = "123456789012345678901234567890.12345678901234567890123456789";
        node["lines"]![0]!["quantity"] = exact;
        Assert.Equal(exact, SalesDraftJson.ReadCreate(node.ToJsonString()).Lines[0].Quantity);
    }

    [Fact]
    public void Invalid_calendar_date_is_rejected()
    {
        var node = JsonNode.Parse(Example("create-request"))!;
        node["businessDate"] = "2026-02-30";
        Assert.Throws<JsonException>(() => SalesDraftJson.ReadCreate(node.ToJsonString()));
    }

    [Fact]
    public void Response_requires_version_and_explicit_nullable_members()
    {
        var node = JsonNode.Parse(Example("create-response"))!;
        node["detail"]!["draft"]!.AsObject().Remove("version");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesOrderDraftResponse>(node.ToJsonString(), SalesDraftJson.Options));
        node = JsonNode.Parse(Example("detail-response"))!;
        node.AsObject().Remove("note");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesOrderDraftDetailView>(node.ToJsonString(), SalesDraftJson.Options));
    }

    [Fact]
    public void Create_replay_list_and_detail_examples_share_identity()
    {
        var first = Read<SalesOrderDraftResponse>("create-response");
        var replay = Read<SalesOrderDraftResponse>("replay-response");
        var detail = Read<SalesOrderDraftDetailView>("detail-response");
        var list = Read<SalesOrderDraftListResponse>("list-response");
        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Detail.Draft with { CommercialStage = null }, replay.Detail.Draft);
        Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.Detail.Draft, detail.Draft);
        var listed = Assert.Single(list.Items);
        Assert.Equal((detail.Draft.Id, detail.Draft.PartyId, detail.Draft.DocumentNo),
            (listed.Id, listed.PartyId, listed.DocumentNo));
        Assert.Equal(detail.Draft.PartyId, detail.PartyDisplay.PartyId);
        Assert.Equal(listed.PartyId, listed.PartyDisplay.PartyId);
        Assert.Equal(detail.PartyDisplay, listed.PartyDisplay);
        Assert.Equal(("CAR-000001", "Example Customer A.S.", "Example Customer"),
            (detail.PartyDisplay.Code, detail.PartyDisplay.LegalName, detail.PartyDisplay.TradeName));
        Assert.Equal(SalesCommercialStageV1.Order, first.Detail.Draft.CommercialStage);
        Assert.Null(replay.Detail.Draft.CommercialStage);
        Assert.Equal("Rev.0", detail.Draft.Revision);
        Assert.Equal("9007199254740993", detail.Draft.Version);
        Assert.Null(list.NextCursor);
    }

    [Fact]
    public void Product_owner_input_maps_exact_request_without_claiming_eligibility()
    {
        var request = SalesDraftJson.ReadCreate(Example("create-request"));
        using var doc = JsonDocument.Parse(Example("product-owner-selection"));
        var input = doc.RootElement;
        Assert.Equal(request.Lines[0].ProductId, input.GetProperty("productId").GetGuid());
        Assert.Equal(request.Lines[0].ExpectedProductVersion, input.GetProperty("expectedProductVersion").GetString());
        Assert.Equal(request.Lines[0].OrderUnitId, input.GetProperty("orderUnitId").GetGuid());
        Assert.Equal(request.BusinessDate.ToString("yyyy-MM-dd"), input.GetProperty("effectiveOn").GetString());
    }

    [Theory]
    [InlineData("error-company", 409, "company_mismatch")]
    [InlineData("error-account", 409, "passive_company_account")]
    [InlineData("error-product", 422, "unsupported_product")]
    [InlineData("error-replay", 409, "same_key_payload_mismatch")]
    [InlineData("error-provider", 503, "settings_provider_unavailable")]
    [InlineData("error-settings-access", 403, "settings_access_denied")]
    [InlineData("error-forbidden", 403, null)]
    public void Error_examples_include_unavailable_and_denied_separately(string file, int status, string? code)
    {
        var problem = Read<SalesDraftProblem>(file);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Code);
        if (code is not null) Assert.Contains(code, SalesDraftFieldCodesV1.All);
    }
}
