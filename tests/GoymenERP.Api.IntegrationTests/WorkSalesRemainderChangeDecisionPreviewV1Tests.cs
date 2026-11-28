using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesRemainderChangeDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("6a110000-0000-4000-8000-000000000001"), Id = Guid.Parse("6a110000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("6a110000-0000-4000-8000-000000000003"), Maker = Guid.Parse("6a110000-0000-4000-8000-000000000004"), Line = Guid.Parse("6a110000-0000-4000-8000-000000000005");
    internal static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero).AddTicks(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(6L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue - 1)]
    public void Human_whitelist_is_ten_fields_with_complete_native_change19_line9_and_lossless_version_text(long version)
    {
        var d = Seal(Facts() with { Version = version.ToString(CultureInfo.InvariantCulture) }); var view = WorkSalesRemainderChangeDecisionPreviewV1.FromNative(d);
        Assert.Equal(d.Change, view.Change); Assert.Equal(d.Line, view.Line); Assert.Equal(d.Version, view.Version);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(view, Json)); var root = wire.RootElement;
        Assert.Equal(10, root.EnumerateObject().Count()); Assert.Equal(19, root.GetProperty("change").EnumerateObject().Count());
        Assert.Equal(9, root.GetProperty("line").EnumerateObject().Count()); Assert.Equal(JsonValueKind.String, root.GetProperty("version").ValueKind);
        Assert.Equal("0001.2500", root.GetProperty("change").GetProperty("requestedQuantity").GetString());
        Assert.Equal(At, root.GetProperty("change").GetProperty("requestedAtUtc").GetDateTimeOffset());
        foreach (var hidden in new[] { "digest", "receipt", "payload", "audit", "draftCreatedBy", "isSalesSupport", "unitPrice", "margin" })
            Assert.DoesNotContain(hidden, root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(d, Json)); Assert.Equal(13, native.RootElement.EnumerateObject().Count());
    }

    [Theory] [InlineData("source")] [InlineData("preview")] [InlineData("line")] [InlineData("support")] [InlineData("version")] [InlineData("null")]
    public void Invalid_native_binding_is_never_projected(string fault)
    {
        var d = Facts(); d = fault switch { "source" => d with { SourceFactsDigest = new('a', 64) }, "preview" => d with { PreviewDigest = new('b', 64) },
            "line" => Seal(d with { Line = d.Line! with { Id = Guid.NewGuid() } }), "support" => d with { IsSalesSupport = null },
            "version" => Seal(d with { Version = "06" }), _ => null! };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesRemainderChangeDecisionPreviewV1.FromNative(d)).Status);
    }

    [Fact]
    public void Work_request_stays_four_fields_and_accepts_no_native_change_identity_or_private_proof()
    {
        var options = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        var q = new WorkDecisionRequestV1("6", false, "  SENTETIK ret  "); var text = JsonSerializer.Serialize(q, options);
        using var wire = JsonDocument.Parse(text); Assert.Equal(4, wire.RootElement.EnumerateObject().Count()); Assert.Equal(q, JsonSerializer.Deserialize<WorkDecisionRequestV1>(text, options));
        foreach (var field in new[] { "draftId", "changeNo", "lineId", "sourceFactsDigest", "originalReceipt" })
        { var node = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject(); node[field] = "SENTETIK"; Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkDecisionRequestV1>(node.ToJsonString(), options)); }
    }

    internal static SalesRemainderDecisionDetailV1 Facts()
    {
        var change = new SalesDraftRemainderChangeV1(3, "Rev.0", Line, int.MaxValue, "Revise", "Pending", "2.0000", "0001.2500", "SENTETIK kalan talebi",
            Maker.ToString("D"), At, "1.00", "0.500", "0.25", null, null, null, null, null);
        return Seal(new(Company, Id, "SENTETIK-SO-6", "6", 0, "Order", "Approved", change,
            new(Line, int.MaxValue, Guid.Parse("6a110000-0000-4000-8000-000000000006"), "SNT-P", "Sentetik ürün", Guid.Parse("6a110000-0000-4000-8000-000000000007"), "KG", "2.0000", "Open"), true, true, "", "")
            { DraftCreatedBy = Maker.ToString("D"), IsSalesSupport = false });
    }
    internal static SalesRemainderDecisionDetailV1 Seal(SalesRemainderDecisionDetailV1 d)
    { d = d with { SourceFactsDigest = SalesRemainderDecisionDigestsV1.SourceFactsDigest(d.CompanyId, d.DraftId, d.DocumentNo, d.Change) }; return d with { PreviewDigest = SalesRemainderDecisionDigestsV1.PreviewDigest(d) }; }
}
