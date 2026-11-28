using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesPriceFloorDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("5a020000-0000-4000-8000-000000000001"), Id = Guid.Parse("5a020000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("5a020000-0000-4000-8000-000000000003"), Maker = Guid.Parse("5a020000-0000-4000-8000-000000000004"),
        PriceList = Guid.Parse("5a020000-0000-4000-8000-000000000005"), Unit = Guid.Parse("5a020000-0000-4000-8000-000000000006");
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero).AddTicks(1);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData("9", true)] [InlineData("9007199254740993", true)] [InlineData("9223372036854775807", false)]
    public void Human_native_facts_are_lossless_text_versions_raw_quantities_and_no_private_source_or_receipt(string version, bool supported)
    {
        var detail = Facts(long.Parse(version, CultureInfo.InvariantCulture)); var view = WorkSalesPriceFloorDecisionPreviewV1.FromNative(detail);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, Json)); var root = json.RootElement;
        Assert.Equal(14, root.EnumerateObject().Count()); Assert.Equal(version, root.GetProperty("version").GetString());
        Assert.Equal(supported, view.ApprovalSupported); Assert.Equal(detail.Lines, view.Lines); Assert.Equal(2, view.Lines.Count);
        Assert.Equal("0001.2500", view.Lines[0].Quantity); Assert.Equal("9007199254740993", view.Lines[0].ProductVersion);
        Assert.Equal(At, view.Lines[0].FloorException!.RequestedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(detail.Lines, Json), JsonSerializer.Serialize(view.Lines, Json));
        Assert.All(root.GetProperty("lines").EnumerateArray(), line =>
        {
            Assert.Equal(10, line.EnumerateObject().Count()); Assert.Equal("9007199254740993", line.GetProperty("productVersion").GetString());
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
        var d = Facts(disclosure: SalesPriceDisclosure.Withheld); var view = WorkSalesPriceFloorDecisionPreviewV1.FromNative(d);
        Assert.All(view.Lines, line => Assert.Null(line.Pricing));
        Assert.Equal(SalesPriceDisclosure.Withheld, view.PriceDisclosure);
        Assert.Equal(16, JsonSerializer.SerializeToElement(d, Json).EnumerateObject().Count());
        var lines = d.Lines.ToList(); d = d with { Lines = lines }; var projected = WorkSalesPriceFloorDecisionPreviewV1.FromNative(d);
        var before = JsonSerializer.Serialize(projected, Json); lines.Clear(); Assert.Equal(before, JsonSerializer.Serialize(projected, Json));
    }

    [Theory] [InlineData("source")] [InlineData("digest")] [InlineData("quantity")] [InlineData("price")] [InlineData("null")]
    public void Malformed_or_unbound_owner_human_facts_fail_closed(string fault)
    {
        var d = Facts(); d = fault switch
        {
            "source" => d with { Source = null }, "digest" => d with { SourceFactsDigest = new('a', 64) },
            "quantity" => d with { Lines = d.Lines.Select(x => x with { Quantity = "999" }).ToArray() },
            "price" => d with { Lines = d.Lines.Select(x => x with { Pricing = x.Pricing! with { UnitPrice = "999" } }).ToArray() },
            _ => null!
        };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesPriceFloorDecisionPreviewV1.FromNative(d)).Status);
    }

    [Fact]
    public void Strict_selection_has_only_native_lines_and_legacy_four_field_body_is_unchanged()
    {
        var strict = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var request = new WorkDecisionRequestV1("9", true, null) { SalesPriceFloor = new([new(Facts().Lines[0].LineId, "  SENTETIK ham gerekçe  ")]) };
        var wire = JsonSerializer.Serialize(request, strict); var restored = JsonSerializer.Deserialize<WorkDecisionRequestV1>(wire, strict)!;
        Assert.Equal(request.ExpectedVersion, restored.ExpectedVersion); Assert.Equal(request.SalesPriceFloor!.Lines, restored.SalesPriceFloor!.Lines);
        using var json = JsonDocument.Parse(wire); Assert.Equal(5, json.RootElement.EnumerateObject().Count());
        var selection = json.RootElement.GetProperty("salesPriceFloor"); Assert.Single(selection.EnumerateObject());
        var line = Assert.Single(selection.GetProperty("lines").EnumerateArray()); Assert.Equal(2, line.EnumerateObject().Count());
        var extra = System.Text.Json.Nodes.JsonNode.Parse(wire)!; extra["salesPriceFloor"]!["companyId"] = Company.ToString("D");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(extra.ToJsonString(), strict));
        var missing = System.Text.Json.Nodes.JsonNode.Parse(wire)!; missing["salesPriceFloor"]!.AsObject().Remove("lines");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(missing.ToJsonString(), strict));
        Assert.Equal(4, JsonSerializer.SerializeToElement(new WorkDecisionRequestV1("1", true, null), strict).EnumerateObject().Count());
    }

    internal static SalesPriceFloorDecisionDetailV1 Facts(long version = 9, SalesPriceDisclosure disclosure = SalesPriceDisclosure.Visible) =>
        SalesDraftService.ProjectPriceFloorDecision(Source(version), disclosure);
    internal static SalesPriceFloorDecisionSourceV1 Source(long version = 9)
    {
        var reference = new PriceReferenceSnapshot(PriceList, "SENTETIK Liste", 3, new(2026, 1, 1), null, "TRY", Unit, "100", "95", "Excluded", "0", null);
        var lines = Enumerable.Range(1, 2).Select(n => new SalesPriceFloorDecisionSourceLineV1(
            Guid.Parse($"5a020000-0000-4000-8001-{n:D12}"), n, Guid.Parse($"5a020000-0000-4000-8002-{n:D12}"), "9007199254740993", "SNT-" + n,
            "SENTETIK Ürün", Unit, n == 1 ? "0001.2500" : "2.500", new("90.5", Unit, null, reference,
                FloorException: new(Maker.ToString("D"), At, PriceList, 3, Unit, "90.5", "90.5", "95")))).ToArray();
        return new(Company, Id, "SENTETIK-SO-FLOOR", version, 0, "Draft", "Preparing", Company, Company, Company, new(2026, 11, 28), Array.AsReadOnly(lines));
    }
}
