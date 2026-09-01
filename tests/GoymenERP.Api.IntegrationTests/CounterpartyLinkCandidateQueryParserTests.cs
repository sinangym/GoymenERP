using System.Text;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyLinkCandidateQueryParserTests
{
    [Fact]
    public void Empty_query_uses_contract_defaults()
    {
        var result = Parse(
            new Dictionary<string, StringValues>(),
            out var query,
            out var error);

        Assert.Equal(CounterpartyLinkCandidateQueryParseResult.Succeeded, result);
        Assert.Null(query.Query);
        Assert.Equal(25, query.PageSize);
        Assert.Null(query.Cursor);
        Assert.Empty(error);
    }

    [Fact]
    public void Query_is_trimmed_and_normalized_to_NFC()
    {
        var decomposed = "  I\u0307zmir  ";

        var result = Parse(
            new Dictionary<string, StringValues>
            {
                ["q"] = decomposed,
                ["pageSize"] = "100",
                ["cursor"] = "opaque-cursor",
            },
            out var query,
            out var error);

        Assert.Equal(CounterpartyLinkCandidateQueryParseResult.Succeeded, result);
        Assert.Equal("İzmir", query.Query);
        Assert.True(query.Query!.IsNormalized(NormalizationForm.FormC));
        Assert.Equal(100, query.PageSize);
        Assert.Equal("opaque-cursor", query.Cursor);
        Assert.Empty(error);
    }

    [Fact]
    public void Whitespace_query_is_treated_as_absent()
    {
        var result = Parse(
            new Dictionary<string, StringValues> { ["q"] = " \t\r\n " },
            out var query,
            out _);

        Assert.Equal(CounterpartyLinkCandidateQueryParseResult.Succeeded, result);
        Assert.Null(query.Query);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 25")]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void Invalid_page_size_is_validation_failure(string pageSize)
    {
        var result = Parse(
            new Dictionary<string, StringValues> { ["pageSize"] = pageSize },
            out _,
            out var error);

        Assert.Equal(CounterpartyLinkCandidateQueryParseResult.ValidationFailed, result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Search_limit_counts_unicode_scalars_not_utf16_code_units()
    {
        var accepted = string.Concat(Enumerable.Repeat("😀", 200));
        var rejected = accepted + "😀";

        Assert.Equal(
            CounterpartyLinkCandidateQueryParseResult.Succeeded,
            Parse(
                new Dictionary<string, StringValues> { ["q"] = accepted },
                out var acceptedQuery,
                out _));
        Assert.Equal(accepted, acceptedQuery.Query);

        Assert.Equal(
            CounterpartyLinkCandidateQueryParseResult.ValidationFailed,
            Parse(
                new Dictionary<string, StringValues> { ["q"] = rejected },
                out _,
                out _));
    }

    [Fact]
    public void Unknown_or_repeated_non_cursor_parameter_is_validation_failure()
    {
        Assert.Equal(
            CounterpartyLinkCandidateQueryParseResult.ValidationFailed,
            Parse(
                new Dictionary<string, StringValues> { ["unknown"] = "value" },
                out _,
                out _));
        Assert.Equal(
            CounterpartyLinkCandidateQueryParseResult.ValidationFailed,
            Parse(
                new Dictionary<string, StringValues>
                {
                    ["q"] = new StringValues(["first", "second"]),
                },
                out _,
                out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" cursor")]
    [InlineData("cursor ")]
    public void Empty_or_noncanonical_cursor_is_invalid_cursor(string cursor)
    {
        var result = Parse(
            new Dictionary<string, StringValues> { ["cursor"] = cursor },
            out _,
            out var error);

        Assert.Equal(CounterpartyLinkCandidateQueryParseResult.InvalidCursor, result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Repeated_cursor_is_invalid_cursor()
    {
        var result = Parse(
            new Dictionary<string, StringValues>
            {
                ["cursor"] = new StringValues(["first", "second"]),
            },
            out _,
            out _);

        Assert.Equal(CounterpartyLinkCandidateQueryParseResult.InvalidCursor, result);
    }

    private static CounterpartyLinkCandidateQueryParseResult Parse(
        IReadOnlyDictionary<string, StringValues> values,
        out CounterpartyLinkCandidateQuery query,
        out string error)
    {
        var collection = new QueryCollection(
            values.ToDictionary(entry => entry.Key, entry => entry.Value));
        return CounterpartyLinkCandidateQueryParser.Parse(collection, out query, out error);
    }
}
