using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// DEC-SAL-002: the strict wire body of POST …/{id}/quantity-revision and its canonical request hash. Every member is present,
/// nothing else is accepted (no unit, product, price or slice member), ids are named once and a quantity is positive plain
/// decimal text. The domain decides which lines must be named and the Ö2 scale; this is shape only. SENTETIK.
/// </summary>
public sealed class SalesDraftQuantityRevisionJsonTests
{
    private const string Line = "11111111-1111-4111-8111-111111111111";

    private static string Body(string version = "\"5\"", string reason = "\"SENTETIK neden\"", string? lines = null) =>
        $$"""{"expectedVersion":{{version}},"reason":{{reason}},"lines":{{lines ?? $$"""[{"lineId":"{{Line}}","quantity":"2.5"}]"""}}}""";

    [Fact]
    public void A_complete_body_reads_as_sent()
    {
        var request = SalesDraftJson.ReadQuantityRevision(Body());

        Assert.Equal(("5", "SENTETIK neden"), (request.ExpectedVersion, request.Reason));
        Assert.Equal((Guid.Parse(Line), "2.5"), (Assert.Single(request.Lines).LineId, request.Lines[0].Quantity));
        // A blank reason is the domain's 422, not a shape error.
        Assert.Equal(" ", SalesDraftJson.ReadQuantityRevision(Body(reason: "\" \"")).Reason);
    }

    [Theory]
    [InlineData("missing-reason")]
    [InlineData("null-reason")]
    [InlineData("long-reason")]
    [InlineData("unknown-member")]
    [InlineData("unit-member")]
    [InlineData("price-member")]
    [InlineData("version-zero")]
    [InlineData("version-number")]
    [InlineData("no-lines")]
    [InlineData("duplicate-line")]
    [InlineData("empty-line-id")]
    [InlineData("zero-quantity")]
    [InlineData("negative-quantity")]
    [InlineData("comma-quantity")]
    [InlineData("number-quantity")]
    public void A_malformed_body_is_a_shape_error(string fault)
    {
        var line = $$"""{"lineId":"{{Line}}","quantity":"2"}""";
        var json = fault switch
        {
            "missing-reason" => $$"""{"expectedVersion":"5","lines":[{{line}}]}""",
            "null-reason" => Body(reason: "null"),
            "long-reason" => Body(reason: $"\"{new string('x', 4001)}\""),
            "unknown-member" => $$"""{"expectedVersion":"5","reason":"r","lines":[{{line}}],"approve":true}""",
            "unit-member" => Body(lines: $$"""[{"lineId":"{{Line}}","quantity":"2","orderUnitId":"{{Line}}"}]"""),
            "price-member" => Body(lines: $$"""[{"lineId":"{{Line}}","quantity":"2","unitPrice":"10"}]"""),
            "version-zero" => Body(version: "\"0\""),
            "version-number" => Body(version: "5"),
            "no-lines" => Body(lines: "[]"),
            "duplicate-line" => Body(lines: $"[{line},{line}]"),
            "empty-line-id" => Body(lines: """[{"lineId":"00000000-0000-0000-0000-000000000000","quantity":"2"}]"""),
            "zero-quantity" => Body(lines: $$"""[{"lineId":"{{Line}}","quantity":"0.000"}]"""),
            "negative-quantity" => Body(lines: $$"""[{"lineId":"{{Line}}","quantity":"-2"}]"""),
            "comma-quantity" => Body(lines: $$"""[{"lineId":"{{Line}}","quantity":"2,5"}]"""),
            _ => Body(lines: $$"""[{"lineId":"{{Line}}","quantity":2}]"""),
        };

        Assert.ThrowsAny<JsonException>(() => SalesDraftJson.ReadQuantityRevision(json));
    }

    [Fact]
    public void The_hash_binds_the_command_the_draft_the_version_the_reason_and_every_quantity()
    {
        var draft = Guid.NewGuid();
        var request = SalesDraftJson.ReadQuantityRevision(Body());
        var hash = SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseQuantityV1, draft, request);

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseQuantityV1, draft, SalesDraftJson.ReadQuantityRevision(Body())));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseQuantityV1, Guid.NewGuid(), request));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseQuantityV1, draft, request with { Reason = "başka" }));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseQuantityV1, draft, request with { ExpectedVersion = "6" }));
        Assert.NotEqual(hash, SalesDraftRequestHash.Compute(SalesDraftCommands.ReviseQuantityV1, draft,
            request with { Lines = [request.Lines[0] with { Quantity = "2.50" }] }));
    }
}
