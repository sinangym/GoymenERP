using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Search;
using GoymenERP.BuildingBlocks.Search;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-002: GET /api/v1/search on the real host composition (in-memory test provider). It needs an authenticated actor and
/// a company context; answers only the kinds the actor reads (the bootstrap group admin holds the Cari read and neither the
/// Sales read nor the global Product authority; the viewer holds none); is never cached; refuses any request shape other
/// than one bounded <c>q</c>.
/// </summary>
public sealed class GlobalSearchEndpointTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task Only_the_kinds_the_actor_reads_are_answered_and_the_answer_is_not_cached()
    {
        using var admin = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);

        using var response = await admin.GetAsync($"{GlobalSearchApiV1.Path}?q=zz-yok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("zz-yok", body.GetProperty("query").GetString());
        var group = Assert.Single(body.GetProperty("groups").EnumerateArray());
        Assert.Equal(GlobalSearchKindsV1.CounterpartyParty, group.GetProperty("kind").GetString());
        Assert.Equal(0, group.GetProperty("items").GetArrayLength());
        Assert.False(group.GetProperty("hasMore").GetBoolean());

        using var viewer = factory.CreateClientFor(TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
        var none = await viewer.GetFromJsonAsync<JsonElement>($"{GlobalSearchApiV1.Path}?q=zz-yok");
        Assert.Equal(0, none.GetProperty("groups").GetArrayLength());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=z")]
    [InlineData("?q=zz&kind=sales.order")]
    [InlineData("?q=zz&q=yy")]
    public async Task Any_other_request_shape_is_refused(string query)
    {
        using var admin = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);

        using var response = await admin.GetAsync($"{GlobalSearchApiV1.Path}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"code\":\"validation_failed\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_anything_is_searched()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync($"{GlobalSearchApiV1.Path}?q=zz");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
