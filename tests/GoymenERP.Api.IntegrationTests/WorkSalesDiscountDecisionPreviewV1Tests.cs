using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesDiscountDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("5a030000-0000-4000-8000-000000000001"), Id = Guid.Parse("5a030000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("5a030000-0000-4000-8000-000000000003"), Maker = Guid.Parse("5a030000-0000-4000-8000-000000000004"),
        PriceList = Guid.Parse("5a030000-0000-4000-8000-000000000005"), Unit = Guid.Parse("5a030000-0000-4000-8000-000000000006");
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero).AddTicks(1);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData("9", true)] [InlineData("9007199254740993", true)] [InlineData("9223372036854775807", false)]
    public void Human_native_facts_are_lossless_text_versions_raw_quantities_and_no_private_source_or_receipt(string version, bool supported)
    {
        var detail = Facts(long.Parse(version, CultureInfo.InvariantCulture)); var view = WorkSalesDiscountDecisionPreviewV1.FromNative(detail);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, Json)); var root = json.RootElement;
        Assert.Equal(15, root.EnumerateObject().Count()); Assert.Equal(version, root.GetProperty("version").GetString());
        Assert.Equal(supported, view.ApprovalSupported); Assert.Equal(detail.Lines, view.Lines); Assert.Equal(2, view.Lines.Count);
        Assert.Equal("0001.2500", view.Lines[0].Quantity); Assert.Equal("9007199254740993", view.Lines[0].ProductVersion);
        Assert.Equal(At, view.Lines[0].DiscountCheck!.CheckedAtUtc); Assert.True(view.Lines[0].DiscountCheck!.RequiresApproval);
        Assert.False(view.IsSubcontractService); Assert.Equal("85.0000", view.Lines[0].Pricing!.UnitPrice);
        Assert.Equal(JsonSerializer.Serialize(detail.Lines, Json), JsonSerializer.Serialize(view.Lines, Json));
        Assert.All(root.GetProperty("lines").EnumerateArray(), line =>
        {
            Assert.Equal(10, line.EnumerateObject().Count()); Assert.Equal("9007199254740993", line.GetProperty("productVersion").GetString());
            Assert.Equal(8, line.GetProperty("discountCheck").EnumerateObject().Count());
            Assert.Equal(13, line.GetProperty("pricing").EnumerateObject().Count());
            Assert.Equal(13, line.GetProperty("pricing").GetProperty("priceReference").EnumerateObject().Count());
        });
        Assert.DoesNotContain("digest", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receipt", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceFacts", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commercial", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Withheld_human_has_no_numeric_pricing_and_the_approved_native_shape_is_unchanged()
    {
        var d = Facts(disclosure: SalesPriceDisclosure.Withheld); var view = WorkSalesDiscountDecisionPreviewV1.FromNative(d);
        Assert.All(view.Lines, line => { Assert.Null(line.Pricing); Assert.Null(line.DiscountCheck!.ListUnitPrice); Assert.Equal("10", line.DiscountCheck.LimitPercent); });
        Assert.Equal(SalesPriceDisclosure.Withheld, view.PriceDisclosure);
        Assert.Equal(17, JsonSerializer.SerializeToElement(d, Json).EnumerateObject().Count());
        var lines = d.Lines.ToList(); d = d with { Lines = lines }; var projected = WorkSalesDiscountDecisionPreviewV1.FromNative(d);
        var before = JsonSerializer.Serialize(projected, Json); lines.Clear(); Assert.Equal(before, JsonSerializer.Serialize(projected, Json));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void Native_subcontract_fact_is_preserved_without_creating_a_discount_approval(bool subcontract)
    {
        var d = Facts(subcontract: subcontract); var view = WorkSalesDiscountDecisionPreviewV1.FromNative(d);
        Assert.Equal(subcontract, view.IsSubcontractService); Assert.Equal(!subcontract, view.ApprovalSupported);
        Assert.Equal(15, JsonSerializer.SerializeToElement(view, Json).EnumerateObject().Count());
    }

    [Theory] [InlineData("source")] [InlineData("digest")] [InlineData("quantity")] [InlineData("price")] [InlineData("null-lines")] [InlineData("null-line")] [InlineData("null")]
    public void Malformed_or_unbound_owner_human_facts_fail_closed(string fault)
    {
        var d = Facts(); d = fault switch
        {
            "source" => d with { Source = null }, "null-lines" => d with { Lines = null! }, "null-line" => d with { Lines = [null!] }, "digest" => d with { SourceFactsDigest = new('a', 64) },
            "quantity" => d with { Lines = d.Lines.Select(x => x with { Quantity = "999" }).ToArray() },
            "price" => d with { Lines = d.Lines.Select(x => x with { Pricing = x.Pricing! with { UnitPrice = "999" } }).ToArray() },
            _ => null!
        };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesDiscountDecisionPreviewV1.FromNative(d)).Status);
    }

    [Fact]
    public void Strict_selection_has_only_native_lines_and_legacy_four_field_body_is_unchanged()
    {
        var strict = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var request = new WorkDecisionRequestV1("9", true, null) { SalesDiscount = new([new(Facts().Lines[0].LineId, "  SENTETIK ham gerekçe  ")]) };
        var wire = JsonSerializer.Serialize(request, strict); var restored = JsonSerializer.Deserialize<WorkDecisionRequestV1>(wire, strict)!;
        Assert.Equal(request.ExpectedVersion, restored.ExpectedVersion); Assert.Equal(request.SalesDiscount!.Lines, restored.SalesDiscount!.Lines);
        using var json = JsonDocument.Parse(wire); Assert.Equal(5, json.RootElement.EnumerateObject().Count());
        var selection = json.RootElement.GetProperty("salesDiscount"); Assert.Single(selection.EnumerateObject());
        var line = Assert.Single(selection.GetProperty("lines").EnumerateArray()); Assert.Equal(2, line.EnumerateObject().Count());
        var extra = System.Text.Json.Nodes.JsonNode.Parse(wire)!; extra["salesDiscount"]!["companyId"] = Company.ToString("D");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(extra.ToJsonString(), strict));
        var missing = System.Text.Json.Nodes.JsonNode.Parse(wire)!; missing["salesDiscount"]!.AsObject().Remove("lines");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(missing.ToJsonString(), strict));
        Assert.Equal(4, JsonSerializer.SerializeToElement(new WorkDecisionRequestV1("1", true, null), strict).EnumerateObject().Count());
    }

    internal static SalesDiscountDecisionDetailV1 Facts(long version = 9, SalesPriceDisclosure disclosure = SalesPriceDisclosure.Visible,
        bool listed = true, bool subcontract = false) => SalesDraftService.ProjectDiscountDecision(Source(version, listed, subcontract), disclosure);
    internal static SalesDiscountDecisionSourceV1 Source(long version = 9, bool listed = true, bool subcontract = false)
    {
        var reference = new PriceReferenceSnapshot(PriceList, "SENTETIK Liste", 3, new(2026, 1, 1), null, "TRY", Unit, "100", null, "Excluded", "0", null);
        var commercial = SalesOrderDraft.WithDiscountLimitCheck(new("85.0000", Unit, null, listed ? reference : null),
            SalesDiscountLimit.Defined(10m, PriceList, 3), Maker.ToString("D"), At);
        var lines = Enumerable.Range(1, 2).Select(n => new SalesDiscountDecisionSourceLineV1(
            Guid.Parse($"5a030000-0000-4000-8001-{n:D12}"), n, Guid.Parse($"5a030000-0000-4000-8002-{n:D12}"), "9007199254740993", "SNT-" + n,
            "SENTETIK Ürün", Unit, n == 1 ? "0001.2500" : "2.500", commercial)).ToArray();
        return new(Company, Id, "SENTETIK-SO-DISCOUNT", version, 0, "Draft", "Preparing", Company, Company, Company, new(2026, 11, 28), subcontract, Array.AsReadOnly(lines));
    }
}
