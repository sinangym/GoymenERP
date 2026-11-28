using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesRiskExceptionDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("d5050000-0000-4000-8000-000000000001"), Id = Guid.Parse("d5050000-0000-4000-8000-000000000002"),
        Order = Guid.Parse("d5050000-0000-4000-8000-000000000003"), Shipment = Guid.Parse("d5050000-0000-4000-8000-000000000004"),
        Actor = Guid.Parse("d5050000-0000-4000-8000-000000000005"), Maker = Guid.Parse("d5050000-0000-4000-8000-000000000006");
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero).AddTicks(1);
    internal static readonly DateOnly Today = new(2026, 11, 28);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(9L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Work_current_human_ten_fields_keep_complete_native_thirteen_and_lossless_separate_order_version(long orderVersion)
    {
        var d = Facts(orderVersion); var view = WorkSalesRiskExceptionDecisionPreviewV1.FromNative(d);
        Assert.Equal(d.Exception, view.Exception); Assert.Equal(orderVersion.ToString(CultureInfo.InvariantCulture), view.OrderVersion);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, Json)); var root = json.RootElement;
        Assert.Equal(10, root.EnumerateObject().Count()); Assert.True(root.TryGetProperty("exception", out var risk));
        Assert.Equal(13, risk.EnumerateObject().Count()); Assert.True(root.TryGetProperty("orderVersion", out var orderVersionJson));
        Assert.Equal(JsonValueKind.String, orderVersionJson.ValueKind); Assert.Equal(orderVersion.ToString(CultureInfo.InvariantCulture), orderVersionJson.GetString());
        Assert.True(risk.TryGetProperty("requestedAtUtc", out var at)); Assert.Equal(At, at.GetDateTimeOffset());
        Assert.True(risk.TryGetProperty("reason", out var reason)); Assert.Equal("  SENTETIK risk nedeni  ", reason.GetString());
        foreach (var hidden in new[] { "sourceFactsDigest", "previewDigest", "receipt", "audit", "payloadHash", "unitPrice", "riskLimit", "riskExposure", "margin" })
            Assert.DoesNotContain(hidden, root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Work_max_risk_version_remains_text_with_no_incrementable_actions_and_native_json_is_unchanged()
    {
        var s = Facts().Source!; var d = Seal(s with { Recorded = s.Recorded with { Version = long.MaxValue } });
        var view = WorkSalesRiskExceptionDecisionPreviewV1.FromNative(d);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), view.Exception.Version);
        Assert.False(view.ApprovalSupported); Assert.False(view.RejectionSupported);
        var response = new SalesRiskExceptionCommandResult(view.Exception, false, "SENTETIK-original");
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(response, SalesDraftJson.Options));
        Assert.Equal(3, native.RootElement.EnumerateObject().Count()); Assert.True(native.RootElement.TryGetProperty("exception", out var exception));
        Assert.Equal(13, exception.EnumerateObject().Count()); Assert.True(exception.TryGetProperty("version", out var version));
        Assert.Equal(JsonValueKind.String, version.ValueKind); Assert.Equal(view.Exception.Version, version.GetString());
    }
    [Theory] [InlineData("source")] [InlineData("preview")] [InlineData("null")] [InlineData("null-exception")] [InlineData("flags")]
    public void Malformed_current_owner_facts_are_refused_before_projection(string fault)
    {
        var d = Facts(); d = fault switch
        { "source" => d with { SourceFactsDigest = new('a', 64) }, "preview" => d with { PreviewDigest = new('b', 64) },
            "null-exception" => d with { Exception = null! }, "flags" => d with { RejectionSupported = false }, _ => null! };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesRiskExceptionDecisionPreviewV1.FromNative(d)).Status);
    }
    [Fact]
    public void Frozen_risk_request_is_four_fields_and_never_accepts_a_native_target_or_private_facts()
    {
        var options = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var body = new WorkDecisionRequestV1("1", false, "  SENTETIK ret  "); var wire = JsonSerializer.Serialize(body, options);
        using var json = JsonDocument.Parse(wire); Assert.Equal(4, json.RootElement.EnumerateObject().Count());
        Assert.Equal(body, JsonSerializer.Deserialize<WorkDecisionRequestV1>(wire, options));
        foreach (var field in new[] { "orderId", "exceptionNo", "companyId", "actorId", "validOn", "sourceFactsDigest", "salesRiskExceptionProof" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(wire)!.AsObject(); node[field] = "SENTETIK";
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(node.ToJsonString(), options));
        }
    }
    internal static SalesRiskExceptionDecisionDetailV1 Facts(long orderVersion = 9) => Seal(new(Company, "SENTETIK-SO-5", orderVersion, "Draft", "Approved", false,
        new(Id, Order, 3, Shipment, "Requested", 1, "  SENTETIK risk nedeni  ", Maker.ToString("D"), At, null, null, null, null), null));
    internal static SalesRiskExceptionDecisionDetailV1 Seal(SalesRiskExceptionDecisionSourceV1 source, DateOnly? today = null) =>
        SalesRiskExceptionService.ProjectRiskDecision(source, today ?? Today);
}
