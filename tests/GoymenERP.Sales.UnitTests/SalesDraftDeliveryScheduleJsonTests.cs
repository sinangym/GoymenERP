using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-003: the strict wire body of POST …/{id}/delivery-schedule and its canonical request hash. Every member is
/// present (the header and line dates may be explicit null), nothing else is accepted, and ids are named once. The
/// domain decides which lines and slices must be named; this is shape only. SENTETIK.
/// </summary>
public sealed class SalesDraftDeliveryScheduleJsonTests
{
    private const string Line = "11111111-1111-4111-8111-111111111111";
    private const string Slice = "22222222-2222-4222-8222-222222222222";

    private static string Body(string version = "\"5\"", string header = "\"2026-10-01\"", string? lines = null) =>
        $$"""{"expectedVersion":{{version}},"requestedDeliveryDate":{{header}},"lines":{{lines ?? $$"""[{"lineId":"{{Line}}","requestedDeliveryDate":null,"slices":[{"sliceId":"{{Slice}}","requestedDeliveryDate":"2026-10-02"}]}]"""}}}""";

    [Fact]
    public void A_complete_body_reads_with_explicit_nulls_kept()
    {
        var request = SalesDraftJson.ReadDeliverySchedule(Body(header: "null"));

        Assert.Equal(("5", (DateOnly?)null), (request.ExpectedVersion, request.RequestedDeliveryDate));
        var line = Assert.Single(request.Lines);
        Assert.Equal((Guid.Parse(Line), (DateOnly?)null), (line.LineId, line.RequestedDeliveryDate));
        Assert.Equal((Guid.Parse(Slice), new DateOnly(2026, 10, 2)), (Assert.Single(line.Slices).SliceId, line.Slices[0].RequestedDeliveryDate));
    }

    [Theory]
    [InlineData("missing-header")]
    [InlineData("missing-line-date")]
    [InlineData("missing-slices")]
    [InlineData("unknown-member")]
    [InlineData("quantity-member")]
    [InlineData("version-zero")]
    [InlineData("version-number")]
    [InlineData("no-lines")]
    [InlineData("duplicate-line")]
    [InlineData("duplicate-slice")]
    [InlineData("empty-line-id")]
    [InlineData("empty-slice-id")]
    [InlineData("bad-date")]
    public void A_malformed_body_is_a_shape_error(string fault)
    {
        var line = $$"""{"lineId":"{{Line}}","requestedDeliveryDate":null,"slices":[]}""";
        var json = fault switch
        {
            "missing-header" => $$"""{"expectedVersion":"5","lines":[{{line}}]}""",
            "missing-line-date" => Body(lines: $$"""[{"lineId":"{{Line}}","slices":[]}]"""),
            "missing-slices" => Body(lines: $$"""[{"lineId":"{{Line}}","requestedDeliveryDate":null}]"""),
            "unknown-member" => $$"""{"expectedVersion":"5","requestedDeliveryDate":null,"lines":[{{line}}],"approve":true}""",
            "quantity-member" => Body(lines: $$"""[{"lineId":"{{Line}}","requestedDeliveryDate":null,"slices":[{"sliceId":"{{Slice}}","requestedDeliveryDate":"2026-10-02","quantity":"1"}]}]"""),
            "version-zero" => Body(version: "\"0\""),
            "version-number" => Body(version: "5"),
            "no-lines" => Body(lines: "[]"),
            "duplicate-line" => Body(lines: $"[{line},{line}]"),
            "duplicate-slice" => Body(lines: $$"""[{"lineId":"{{Line}}","requestedDeliveryDate":null,"slices":[{"sliceId":"{{Slice}}","requestedDeliveryDate":"2026-10-02"},{"sliceId":"{{Slice}}","requestedDeliveryDate":"2026-10-03"}]}]"""),
            "empty-line-id" => Body(lines: """[{"lineId":"00000000-0000-0000-0000-000000000000","requestedDeliveryDate":null,"slices":[]}]"""),
            "empty-slice-id" => Body(lines: $$"""[{"lineId":"{{Line}}","requestedDeliveryDate":null,"slices":[{"sliceId":"00000000-0000-0000-0000-000000000000","requestedDeliveryDate":"2026-10-02"}]}]"""),
            _ => Body(header: "\"2026-13-01\""),
        };

        Assert.ThrowsAny<JsonException>(() => SalesDraftJson.ReadDeliverySchedule(json));
    }

    [Fact]
    public void The_hash_binds_the_command_the_draft_the_version_and_every_date()
    {
        var draft = Guid.NewGuid();
        var request = SalesDraftJson.ReadDeliverySchedule(Body());
        var hash = SalesDraftRequestHash.Compute(SalesDraftCommands.RescheduleDeliveryV1, draft, request);

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.RescheduleDeliveryV1, draft, SalesDraftJson.ReadDeliverySchedule(Body())));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.RescheduleDeliveryV1, Guid.NewGuid(), request));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.RescheduleDeliveryV1, draft, SalesDraftJson.ReadDeliverySchedule(Body(header: "null"))));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.RescheduleDeliveryV1, draft,
            request with { Lines = [request.Lines[0] with { Slices = [request.Lines[0].Slices[0] with { RequestedDeliveryDate = new DateOnly(2026, 10, 9) }] }] }));
    }
}
