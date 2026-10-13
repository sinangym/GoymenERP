using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOpenItemReadCursorTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Item = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Canonical_cursor_round_trips_the_stable_keyset()
    {
        var cursor = FinanceOpenItemReadCursor.Encode(Company, "reader", CreatedAtUtc, Item);

        var decoded = FinanceOpenItemReadCursor.Decode(cursor, Company, "reader");

        Assert.NotNull(decoded);
        Assert.Equal(CreatedAtUtc, decoded.Value.CreatedAtUtc);
        Assert.Equal(Item, decoded.Value.Id);
    }

    [Fact]
    public void Cursor_is_bound_to_company_actor_query_and_ordering_contract()
    {
        var cursor = FinanceOpenItemReadCursor.Encode(Company, "reader", CreatedAtUtc, Item);
        var anotherQuery = FinanceOpenItemReadCursor.Encode(
            Company, "reader", CreatedAtUtc, Item, "open-items:other-query");

        Assert.Null(FinanceOpenItemReadCursor.Decode(cursor, Guid.NewGuid(), "reader"));
        Assert.Null(FinanceOpenItemReadCursor.Decode(cursor, Company, "other-reader"));
        Assert.Null(FinanceOpenItemReadCursor.Decode(anotherQuery, Company, "reader"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not_a_cursor")]
    public void Malformed_cursor_is_rejected(string cursor)
    {
        Assert.Null(FinanceOpenItemReadCursor.Decode(cursor, Company, "reader"));
    }

    [Fact]
    public void Non_canonical_base64url_is_rejected()
    {
        var cursor = FinanceOpenItemReadCursor.Encode(Company, "reader", CreatedAtUtc, Item);

        Assert.Null(FinanceOpenItemReadCursor.Decode(cursor + "=", Company, "reader"));
    }
}
