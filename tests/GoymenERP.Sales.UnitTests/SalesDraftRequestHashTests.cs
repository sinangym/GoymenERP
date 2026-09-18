using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// R3 / #257: the replay identity of a create is the idempotency key plus the canonical content.
/// Two attempts are "the same" only when their wire content agrees exactly.
/// </summary>
public sealed class SalesDraftRequestHashTests
{
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Unit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid LineA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid LineB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static SalesOrderDraftLineRequest Line(Guid id, string quantity) => new(id, Product, "7", Unit, quantity);

    private static CreateSalesOrderDraftRequest Request(params SalesOrderDraftLineRequest[] lines) =>
        new("Order", Party, Account, new DateOnly(2026, 9, 18), Currency, lines);

    [Fact]
    public void The_same_content_hashes_the_same_and_the_hash_is_a_lowercase_sha256()
    {
        var first = SalesDraftRequestHash.Compute(Request(Line(LineA, "12.5")));
        var second = SalesDraftRequestHash.Compute(Request(Line(LineA, "12.5")));
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first, first.ToLowerInvariant());
    }

    [Fact]
    public void Quantity_text_is_content_so_5_00_and_5_are_different_attempts()
    {
        // CARİ #306 P3: "5.00" and "5" are different wire content; under the same key the second is a
        // payload mismatch (409), never a replay. The value is the same, the attempt is not.
        Assert.NotEqual(SalesDraftRequestHash.Compute(Request(Line(LineA, "5.00"))),
            SalesDraftRequestHash.Compute(Request(Line(LineA, "5"))));
    }

    [Fact]
    public void Line_order_identities_and_optional_fields_are_content()
    {
        var ab = SalesDraftRequestHash.Compute(Request(Line(LineA, "1"), Line(LineB, "2")));
        var ba = SalesDraftRequestHash.Compute(Request(Line(LineB, "2"), Line(LineA, "1")));
        Assert.NotEqual(ab, ba);
        Assert.NotEqual(SalesDraftRequestHash.Compute(Request(Line(LineA, "1"))),
            SalesDraftRequestHash.Compute(Request(Line(LineB, "1"))));
        Assert.NotEqual(SalesDraftRequestHash.Compute(Request(Line(LineA, "1"))),
            SalesDraftRequestHash.Compute(Request(Line(LineA, "1")) with { Note = "x" }));
        Assert.NotEqual(SalesDraftRequestHash.Compute(Request(Line(LineA, "1"))),
            SalesDraftRequestHash.Compute(Request(Line(LineA, "1") with { Note = " " })));
    }

    [Fact]
    public void A_null_request_is_a_caller_error()
    {
        Assert.Throws<ArgumentNullException>(() => SalesDraftRequestHash.Compute(null!));
    }
}
