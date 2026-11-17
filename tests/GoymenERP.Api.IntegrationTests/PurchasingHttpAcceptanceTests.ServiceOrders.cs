using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Service_order_approval_binds_expected_version_and_keeps_replay_headers_and_price_withholding()
    {
        var owner = new ServiceOrderHttpOwner { Replay = true };
        using var host = ServiceOrderHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/service-orders/{PurchaseOrderId:D}/approval", "SENTETIK-service",
            "{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK onay\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("1", owner.Request!.ExpectedVersion);
        Assert.Equal("SENTETIK-service", owner.Metadata!.IdempotencyKey);
        Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Withheld", body.GetProperty("order").GetProperty("priceDisclosure").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("order").GetProperty("lines")[0].GetProperty("unitPrice").ValueKind);
        AssertPrivateNoStore(response);
    }
    [Theory]
    [InlineData("{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK\",\"companyId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"expectedVersion\":\"1\",\"expectedVersion\":\"2\",\"reason\":\"SENTETIK\"}")]
    [InlineData("{\"expectedVersion\":\"1\"}")]
    public async Task Service_order_rejects_unknown_duplicate_or_missing_authoritative_fields_before_calling_the_owner(string json)
    {
        var owner = new ServiceOrderHttpOwner(); using var host = ServiceOrderHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/service-orders/{PurchaseOrderId:D}/approval", "SENTETIK-shape", json));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(owner.Request);
    }
    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(503)]
    public async Task Service_order_owner_refusal_never_discloses_prices_or_source_details(int status)
    {
        var owner = new ServiceOrderHttpOwner { Refusal = status }; using var host = ServiceOrderHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync($"/api/v1/purchasing/service-orders/{PurchaseOrderId:D}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("SENTETIK-private", body); Assert.DoesNotContain("unitPrice", body);
        AssertPrivateNoStore(response);
    }
    private WebApplicationFactory<Program> ServiceOrderHost(ServiceOrderHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => { services.RemoveAll<IPurchaseServiceOrderServiceV1>(); services.AddSingleton<IPurchaseServiceOrderServiceV1>(owner); }));
    private sealed class ServiceOrderHttpOwner : IPurchaseServiceOrderServiceV1
    {
        public bool Replay { get; init; } public int? Refusal { get; init; }
        public ApprovePurchaseServiceOrderV1? Request { get; private set; } public PurchasingCommandMetadata? Metadata { get; private set; }
        private static PurchaseServiceOrderViewV1 View() => new(CompanyA, PurchaseOrderId, "SENTETIK-HIZ-2026-1", "2", "ServicePurchase", "Approved",
            Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 17), Guid.NewGuid(), "TRY", true, "Withheld",
            [new(Guid.NewGuid(), Guid.NewGuid(), "1", "SENTETIK hizmet", UnitId, "kg", "10", "0", null, null)], [], [], []);
        public Task<PurchaseServiceOrderViewV1> ReadAsync(Guid orderId, CancellationToken token = default) => Refusal is { } status
            ? throw new PurchasingServiceException(status, "SENTETIK-private", detail: "SENTETIK-private") : Task.FromResult(View());
        public Task<IReadOnlyList<PurchaseServiceOrderViewV1>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<PurchaseServiceOrderViewV1>>([View()]);
        public Task<PurchaseServiceOrderReferencesV1> ReferencesAsync(CancellationToken token = default) => Task.FromResult(new PurchaseServiceOrderReferencesV1(CompanyA, [], []));
        public Task<PurchaseServiceOrderResponseV1> ApproveAsync(Guid id, ApprovePurchaseServiceOrderV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default)
        { Request = request; Metadata = metadata; return Task.FromResult(new PurchaseServiceOrderResponseV1(View(), Replay, "SENTETIK-original")); }
        public Task<PurchaseServiceOrderResponseV1> CreateAsync(CreatePurchaseServiceOrderV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PurchaseServiceOrderResponseV1> AcceptAsync(Guid id, AcceptPurchaseServiceV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
}
