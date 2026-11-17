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
    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task Supplier_revision_failure_preserves_public_boundary_and_never_returns_order_data(int status)
    {
        var owner = new SupplierRevisionHttpOwner { FailureStatus = status };
        using var host = SupplierRevisionHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/supplier-revisions", "SENTETIK-key", SupplierRevisionBody()));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("detail", out _));
        if (status == 503) Assert.Equal("service_unavailable", body.GetProperty("code").GetString());
        AssertPrivateNoStore(response);
    }

    [Fact]
    public async Task Supplier_revision_binds_expected_version_reason_and_key_and_price_route_keeps_decimal_text()
    {
        var owner = new SupplierRevisionHttpOwner();
        using var host = SupplierRevisionHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/supplier-revisions", "SENTETIK-key", SupplierRevisionBody()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((PurchaseOrderId, "3", "SENTETIK-key", "SENTETIK reason"), (owner.Order, owner.Revision!.ExpectedVersion, owner.Metadata!.IdempotencyKey, owner.Revision.Reason));
        using var priceResponse = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/lines/{PurchaseOrderLineId:D}/supplier-revision-price",
            "SENTETIK-price", "{\"expectedVersion\":\"4\",\"price\":{\"unitPrice\":\"12.3456\",\"discountPercent\":\"5\"}}"));
        Assert.Equal(HttpStatusCode.OK, priceResponse.StatusCode);
        Assert.Equal((PurchaseOrderLineId, "12.3456", "5"), (owner.Line, owner.Price!.Price.UnitPrice, owner.Price.Price.DiscountPercent));
    }

    [Fact]
    public async Task Supplier_revision_rejects_client_company_in_body_before_calling_the_owner_and_replay_preserves_headers()
    {
        var owner = new SupplierRevisionHttpOwner();
        using var host = SupplierRevisionHost(owner); using var client = CreateClient(host);
        using var invalid = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/supplier-revisions", "SENTETIK-key",
            SupplierRevisionBody()[..^1] + $",\"companyId\":\"{CompanyA:D}\"}}"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Null(owner.Revision);
        owner.Replay = true;
        using var replay = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/supplier-revisions", "SENTETIK-key", SupplierRevisionBody()));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    private static string SupplierRevisionBody() => JsonSerializer.Serialize(new RevisePurchaseOrderSupplierRequest("3", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK reason"), PurchasingJson.Options);
    private WebApplicationFactory<Program> SupplierRevisionHost(SupplierRevisionHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => { services.RemoveAll<IPurchasingSupplierRevisionServiceV1>(); services.AddScoped<IPurchasingSupplierRevisionServiceV1>(_ => owner); }));
    private sealed class SupplierRevisionHttpOwner : IPurchasingSupplierRevisionServiceV1
    {
        public int? FailureStatus { get; init; }
        public bool Replay { get; set; }
        public Guid Order { get; private set; }
        public Guid Line { get; private set; }
        public RevisePurchaseOrderSupplierRequest? Revision { get; private set; }
        public SetPurchaseSupplierRevisionPriceRequest? Price { get; private set; }
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public Task<PurchaseOrderResponse> ReviseOrderSupplierAsync(Guid orderId, RevisePurchaseOrderSupplierRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        { Order = orderId; Revision = request; Metadata = metadata; return Answer(); }
        public Task<PurchaseOrderResponse> SetSupplierRevisionPriceAsync(Guid orderId, Guid lineId, SetPurchaseSupplierRevisionPriceRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        { Order = orderId; Line = lineId; Price = request; Metadata = metadata; return Answer(); }
        public Task<PurchaseSupplierRevisionHistoryView> GetSupplierRevisionsAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchaseSupplierRevisionHistoryView(CompanyA, orderId, "3", 0, []));
        private Task<PurchaseOrderResponse> Answer() => FailureStatus is { } status
            ? throw new PurchasingServiceException(status, status == 409 ? "purchasing_supplier_revision_blocked" : "SENTETIK-internal-code")
            : Task.FromResult(new PurchaseOrderResponse(OrderDetail(Order, "Draft", "4"), Replay, "SENTETIK-original"));
    }
}
