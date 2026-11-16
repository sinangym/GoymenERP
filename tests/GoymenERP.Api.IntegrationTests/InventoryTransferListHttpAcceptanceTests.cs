using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryTransferListHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Transfer = Guid.Parse("8a180000-0000-4000-8000-000000000001");
    private static readonly Guid Source = Guid.Parse("8a180000-0000-4000-8000-000000000002");
    private static readonly Guid Destination = Guid.Parse("8a180000-0000-4000-8000-000000000003");

    [Fact]
    public async Task The_list_returns_only_real_owner_location_names_and_marks_the_bounded_window()
    {
        var locations = new Locations(false);
        using var host = Host(locations);
        using var client = Client(host);
        using var response = await client.GetAsync(InventoryTransferApiV1.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        var result = await response.Content.ReadFromJsonAsync<InventoryTransferDisplayListV1>();
        Assert.NotNull(result);
        Assert.True(result.WindowFull);
        var item = Assert.Single(result.Items);
        Assert.Equal((Transfer, "SYN-KAYNAK", "Sentetik kaynak", "SYN-HEDEF", "Sentetik hedef"),
            (item.Transfer.TransferId, item.SourceLocationCode, item.SourceLocationName, item.DestinationLocationCode, item.DestinationLocationName));
        Assert.Equal(TestCompanyIds.CompanyA, locations.CompanyId);
        Assert.Equal(new[] { Source, Destination }.Order(), locations.Ids.Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_unavailable_location_facts_never_fall_back_to_guid_names(bool throws)
    {
        using var host = Host(new Locations(true, throws));
        using var client = Client(host);
        using var response = await client.GetAsync(InventoryTransferApiV1.Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("sourceLocationName", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> Host(Locations locations) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IInventoryTransferQueryV1>(); services.AddSingleton<IInventoryTransferQueryV1>(new Transfers());
        services.RemoveAll<IOrganizationLocationDisplayReaderV1>(); services.AddSingleton<IOrganizationLocationDisplayReaderV1>(locations);
    }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private sealed class Transfers : IInventoryTransferQueryV1
    {
        public Task<InventoryTransferListV1> ListAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult(
            new InventoryTransferListV1(TestCompanyIds.CompanyA, [new(Transfer, TestCompanyIds.CompanyA, "1", "Planned", Source,
                Destination, DateTimeOffset.Parse("2026-11-16T09:00:00Z"), null)], true));
        public Task<InventoryTransferViewV1> GetAsync(Guid transferId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The list must not read unrelated transfer details.");
    }

    private sealed class Locations(bool missing, bool throws = false) : IOrganizationLocationDisplayReaderV1
    {
        public Guid CompanyId { get; private set; }
        public IReadOnlyCollection<Guid> Ids { get; private set; } = [];
        public Task<IReadOnlyList<OrganizationLocationDisplayV1>> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken = default)
        {
            CompanyId = companyId; Ids = ids;
            if (throws) throw new InvalidOperationException("Synthetic owner unavailable.");
            return Task.FromResult<IReadOnlyList<OrganizationLocationDisplayV1>>(missing ? []
                : [new(Source, "SYN-KAYNAK", "Sentetik kaynak"), new(Destination, "SYN-HEDEF", "Sentetik hedef")]);
        }
    }
}
