using GoymenERP.Api.Endpoints;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOJ-D20 review list query, without a server: only an optional single <c>limit</c> of ASCII digits is read; any other
/// key, a repeated key or a value that is not a whole number is refused before the query service runs (the range check
/// itself is the service's). SENTETIK.
/// </summary>
public sealed class InventoryTransferScaleCheckReviewQueryParserTests
{
    [Theory]
    [InlineData("", true, false, 0)]
    [InlineData("limit=1", true, true, 1)]
    [InlineData("limit=200", true, true, 200)]
    [InlineData("limit=0", true, true, 0)]
    [InlineData("limit=999999999", true, true, 999999999)]
    [InlineData("limit=", false, false, 0)]
    [InlineData("limit=-1", false, false, 0)]
    [InlineData("limit=%2B5", false, false, 0)]
    [InlineData("limit=5.0", false, false, 0)]
    [InlineData("limit=%205", false, false, 0)]
    [InlineData("limit=1000000000", false, false, 0)]
    [InlineData("limit=5&limit=6", false, false, 0)]
    [InlineData("limit=5&cursor=x", false, false, 0)]
    [InlineData("status=NotChecked", false, false, 0)]
    public void The_limit_rule_admits_one_optional_ascii_digit_limit_and_nothing_else(string raw, bool valid, bool hasLimit,
        int expected)
    {
        var query = new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(raw));

        Assert.Equal(valid, InventoryEndpoints.TryReadScaleCheckReviewLimit(query, out var limit));
        Assert.Equal(hasLimit ? expected : (int?)null, limit);
    }
}
