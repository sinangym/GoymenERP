using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesOverDeliveryRequestDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("4d110000-0000-4000-8000-000000000001"), Id = Guid.Parse("4d110000-0000-4000-8000-000000000002"),
        Order = Guid.Parse("4d110000-0000-4000-8000-000000000003"), Actor = Guid.Parse("4d110000-0000-4000-8000-000000000004"),
        Maker = Guid.Parse("4d110000-0000-4000-8000-000000000005"), Line = Guid.Parse("4d110000-0000-4000-8000-000000000006");
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero).AddTicks(1);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(9L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Work_human_whitelist_preserves_native_request18_order11_line8_and_only_changes_order_version_to_text(long orderVersion)
    {
        var d = Facts() with { Order = Facts().Order with { Version = orderVersion } }; d = Seal(d);
        var view = WorkSalesOverDeliveryRequestDecisionPreviewV1.FromNative(d);
        Assert.Equal(d.Request, view.Request); Assert.Equal(d.Order.Line, view.Order.Line);
        Assert.Equal(orderVersion.ToString(CultureInfo.InvariantCulture), view.Order.Version);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, Json)); var root = json.RootElement;
        Assert.Equal(5, root.EnumerateObject().Count()); Assert.Equal(18, root.GetProperty("request").EnumerateObject().Count());
        var order = root.GetProperty("order"); Assert.Equal(11, order.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.String, order.GetProperty("version").ValueKind);
        Assert.Equal(8, order.GetProperty("line").EnumerateObject().Count());
        Assert.Equal("0001.2500", order.GetProperty("line").GetProperty("currentQuantity").GetString());
        Assert.Equal(At, root.GetProperty("request").GetProperty("requestedAtUtc").GetDateTimeOffset());
        foreach (var hidden in new[] { "digest", "receipt", "payload", "audit", "unitPrice", "margin", "sourceJson" })
            Assert.DoesNotContain(hidden, root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(d.Order, Json));
        Assert.Equal(JsonValueKind.Number, native.RootElement.GetProperty("version").ValueKind); // Native wire stays numeric.
    }

    [Theory] [InlineData("source")] [InlineData("preview")] [InlineData("line")] [InlineData("kind")] [InlineData("state")] [InlineData("null")]
    public void Malformed_owner_human_facts_never_reach_the_wire(string fault)
    {
        var d = Facts(); d = fault switch
        {
            "source" => d with { SourceFactsDigest = new('a', 64) }, "preview" => d with { PreviewDigest = new('b', 64) },
            "line" => Seal(d with { Order = d.Order with { Line = d.Order.Line! with { Id = Guid.NewGuid() } } }),
            "kind" => Seal(d with { Request = d.Request with { Kind = "QuoteExpiredConversion" } }),
            "state" => Seal(d with { Request = d.Request with { Status = "Unknown" } }), _ => null!
        };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesOverDeliveryRequestDecisionPreviewV1.FromNative(d)).Status);
    }

    [Fact]
    public void Frozen_Work_body_stays_four_fields_and_strict_reader_refuses_raw_native_target_or_human_fields()
    {
        var options = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var q = new WorkDecisionRequestV1("1", false, "  SENTETIK ret  "); var wire = JsonSerializer.Serialize(q, options);
        using var json = JsonDocument.Parse(wire); Assert.Equal(4, json.RootElement.EnumerateObject().Count());
        Assert.Equal(q, JsonSerializer.Deserialize<WorkDecisionRequestV1>(wire, options));
        foreach (var field in new[] { "orderId", "requestNo", "lineId", "sourceFactsDigest", "requestedTotal" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(wire)!.AsObject(); node[field] = "SENTETIK";
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(node.ToJsonString(), options));
        }
    }

    internal static SalesOverDeliveryRequestDecisionDetailV1 Facts()
    {
        var request = new SalesApprovalRequestView("OverDelivery", Order, 3, "Rev.0", "Pending", "1", Line, "1.25", "0.03", "1.28",
            Guid.Parse("4d110000-0000-4000-8000-000000000007"), Guid.Parse("4d110000-0000-4000-8000-000000000008"),
            null, Maker.ToString("D"), At, null, null, null);
        var line = new SalesOverDeliveryRequestLineV1(Line, int.MaxValue, Guid.NewGuid(), "SNT-P", "Sentetik ürün", Guid.NewGuid(), "KG", "0001.2500");
        return Seal(new(Company, Id, request, new(Order, "SENTETIK-SO-4", 9, 0, "Draft", "Approved", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new(2026, 11, 28), line), true, true, "", ""));
    }
    internal static SalesOverDeliveryRequestDecisionDetailV1 Seal(SalesOverDeliveryRequestDecisionDetailV1 d)
    {
        d = d with { SourceFactsDigest = SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(d.CompanyId, d.RequestId, d.Request, d.Order.DocumentNo) };
        return d with { PreviewDigest = SalesOverDeliveryRequestDecisionDigestsV1.PreviewDigest(d) };
    }
}
