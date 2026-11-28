using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkSalesRequestDecisionPreviewV1Tests
{
    internal static readonly Guid Company = Guid.Parse("5a170000-0000-4000-8000-000000000001"), Id = Guid.Parse("5a170000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("5a170000-0000-4000-8000-000000000003"), Maker = Guid.Parse("5a170000-0000-4000-8000-000000000004");
    internal static readonly DateTimeOffset At = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Submitted", "1")] [InlineData("Approved", "9007199254740993")] [InlineData("Rejected", "9223372036854775807")]
    public void Human_wire_is_exactly_three_fields_and_preserves_every_native_price_free_detail_field(string state, string version)
    {
        var native = Facts(state, version); var view = WorkSalesRequestDecisionPreviewV1.FromNative(native);
        Assert.Equal(JsonSerializer.Serialize(native.Detail, Json), JsonSerializer.Serialize(view.Detail, Json));
        Assert.Equal(version, view.Detail.Request.Version);
        Assert.Collection(view.Detail.Lines, line => Assert.Equal(1, line.LineNo), line => Assert.Equal(2, line.LineNo));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, Json));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
        Assert.True(json.RootElement.TryGetProperty("detail", out var detail)); Assert.Equal(5, detail.EnumerateObject().Count());
        Assert.True(detail.TryGetProperty("request", out var header)); Assert.Equal(10, header.EnumerateObject().Count());
        Assert.True(detail.TryGetProperty("lines", out var lines)); Assert.All(lines.EnumerateArray(), line => Assert.Equal(11, line.EnumerateObject().Count()));
        Assert.DoesNotContain("Digest", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Receipt", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("source-digest")] [InlineData("preview-digest")] [InlineData("flags")] [InlineData("line-count")] [InlineData("null-lines")]
    public void Invalid_or_mixed_owner_human_facts_fail_closed(string fault)
    {
        var d = Facts(); d = fault switch
        {
            "source-digest" => d with { SourceFactsDigest = new('a', 64) }, "preview-digest" => d with { PreviewDigest = new('b', 64) },
            "flags" => Seal(d with { ApprovalSupported = false }),
            "line-count" => Seal(d with { Detail = d.Detail with { Request = d.Detail.Request with { LineCount = 1 } } }),
            _ => d with { Detail = d.Detail with { Lines = null! } },
        };
        Assert.Equal(503, Assert.Throws<WorkDecisionException>(() => WorkSalesRequestDecisionPreviewV1.FromNative(d)).Status);
    }

    [Fact]
    public void Human_projection_does_not_retain_a_provider_owned_mutable_line_collection()
    {
        var d = Facts(); var mutable = d.Detail.Lines.ToList(); d = Seal(d with { Detail = d.Detail with { Lines = mutable } });
        var view = WorkSalesRequestDecisionPreviewV1.FromNative(d); var original = JsonSerializer.Serialize(view, Json);
        mutable[0] = mutable[0] with { Quantity = "999" }; mutable.Clear();
        Assert.Equal(original, JsonSerializer.Serialize(view, Json));
        Assert.Collection(view.Detail.Lines, line => Assert.Equal(1, line.LineNo), line => Assert.Equal(2, line.LineNo));
    }

    internal static SalesRequestDecisionDetailV1 Facts(string state = "Submitted", string version = "1")
    {
        var h = new SalesRequestSummaryView(Id, Company, "SENTETIK-TLP-7", state, version, Guid.Parse("5a170000-0000-4000-8000-000000000005"),
            Guid.Parse("5a170000-0000-4000-8000-000000000006"), new(2026, 11, 28), At, 2);
        var lines = Enumerable.Range(1, 2).Select(n => new SalesRequestLineView(Guid.Parse($"5a170000-0000-4000-8000-{n + 10:D12}"), n,
            Guid.Parse($"5a170000-0000-4000-8000-{n + 20:D12}"), "9007199254740993", $"SNT-{n}", $"Sentetik ürün {n}",
            Guid.Parse("5a170000-0000-4000-8000-000000000007"), "KG", "0001.2500", new(2026, 12, 2), $"SENTETIK satır {n}")).ToArray();
        return Seal(new(new(h, new(2026, 12, 2), "SENTETIK müşteri talebi", lines,
                state == "Submitted" ? null : new(Maker.ToString("D"), At.AddHours(1), state == "Rejected" ? "SENTETIK ret" : null)),
            state == "Submitted", state is "Submitted" or "Approved", "", ""));
    }
    internal static SalesRequestDecisionDetailV1 Seal(SalesRequestDecisionDetailV1 d)
    { d = d with { SourceFactsDigest = SalesRequestDecisionDigestsV1.SourceFactsDigest(d.Detail) }; return d with { PreviewDigest = SalesRequestDecisionDigestsV1.PreviewDigest(d) }; }
}
