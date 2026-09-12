using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductCursorTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Origin = Guid.NewGuid();
    private static readonly ProductListPosition Position = new(Guid.NewGuid(), "RM-1", null);
    private static readonly ProductListQuery Query = new(Roles: "sellable,purchasable");

    [Fact]
    public void Continuation_preserves_position_and_normalizes_equivalent_search_and_role_order()
    {
        using var codec = Codec();
        var cursor = codec.Protect(Authority(), Query with { Q = "  çelik " }, Position);
        var decoded = codec.Unprotect(Authority(), Query with { Q = "çelik", Roles = "purchasable,sellable", Cursor = cursor }, cursor);
        Assert.Equal(Position, decoded);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("origin")]
    [InlineData("revision")]
    public void Another_authority_cannot_reuse_continuation(string changed)
    {
        using var codec = Codec();
        var cursor = codec.Protect(Authority(), Query, Position);
        var other = Authority(changed == "actor" ? Guid.NewGuid() : Actor,
            changed == "origin" ? Guid.NewGuid() : Origin, changed == "revision" ? "revision-2" : "revision-1");
        AssertInvalid(() => codec.Unprotect(other, Query, cursor));
    }

    [Fact]
    public void Every_filter_and_page_shape_is_bound_to_the_cursor()
    {
        using var codec = Codec();
        var cursor = codec.Protect(Authority(), Query, Position);
        ProductListQuery[] changed = [Query with { Q = "different" }, Query with { Lifecycle = "Draft" },
            Query with { GroupId = Guid.NewGuid() }, Query with { ClassId = Guid.NewGuid() }, Query with { FamilyId = Guid.NewGuid() },
            Query with { Roles = "sellable" }, Query with { RoleMatch = "all" }, Query with { Sort = "name" },
            Query with { Direction = "desc" }, Query with { Completeness = "NotEvaluated" }, Query with { PageSize = 25 }];
        foreach (var query in changed) AssertInvalid(() => codec.Unprotect(Authority(), query, cursor));
    }

    [Fact]
    public void Tampering_truncation_wrong_key_and_noncanonical_encoding_are_rejected()
    {
        using var codec = Codec();
        using var other = Codec();
        var cursor = codec.Protect(Authority(), Query, Position);
        AssertInvalid(() => other.Unprotect(Authority(), Query, cursor));
        foreach (var changed in new[] { "", cursor[..^1], cursor + "=", " " + cursor,
                     (cursor[0] == 'A' ? "B" : "A") + cursor[1..], new string('A', 4097) })
            AssertInvalid(() => codec.Unprotect(Authority(), Query, changed));
    }

    [Fact]
    public void Expiry_and_future_issue_time_fail_closed()
    {
        var clock = new Clock();
        using var codec = new ProductCursorCodec(RandomNumberGenerator.GetBytes(32), clock, TimeSpan.FromMinutes(5));
        var cursor = codec.Protect(Authority(), Query, Position);
        clock.Now = clock.Now.AddMinutes(-1);
        AssertInvalid(() => codec.Unprotect(Authority(), Query, cursor));
        clock.Now = clock.Now.AddMinutes(6);
        AssertInvalid(() => codec.Unprotect(Authority(), Query, cursor));
    }

    private static ProductCursorCodec Codec() => new(RandomNumberGenerator.GetBytes(32), new Clock(), TimeSpan.FromMinutes(5));
    private static GroupSharedMasterReadContext Authority(Guid? actor = null, Guid? origin = null, string revision = "revision-1") =>
        GroupSharedMasterReadContext.FromServerDecision(new(true, GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read,
            actor ?? Actor, origin ?? Origin, revision));
    private static void AssertInvalid(Action action)
    {
        var exception = Assert.Throws<ProductOperationException>(action);
        Assert.Equal(ProductApiV1.InvalidCursor, exception.Code);
        Assert.Equal(400, exception.StatusCode);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
