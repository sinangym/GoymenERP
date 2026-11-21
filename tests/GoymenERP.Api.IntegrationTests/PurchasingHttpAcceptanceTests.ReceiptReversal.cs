using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Stub service proves only HTTP wire/replay metadata; separate SQL facts exercise both owner implementations.</summary>
public sealed partial class PurchasingHttpAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_reversal_carries_the_original_replay_headers_and_distinct_quantities(bool replay)
    {
        var service = new ReceiptReversalWireService(replay);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IPurchaseReceiptReversalServiceV1>(_ => service))));
        using var client = CreateClient(host);
        var request = new ReversePurchaseReceiptV1("4", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2", "1.9", "Free",
            "SENTETIK reversal", DateTimeOffset.UnixEpoch);
        using var response = await client.SendAsync(Post(ReceiptPath(PurchaseOrderId, AcceptanceId) + "/reversal",
            "SENTETIK-reversal-wire", JsonSerializer.Serialize(request, PurchasingJson.Options)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
        if (replay)
        {
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-reversal-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }
        Assert.Equal(("2", "1.9", "SENTETIK-reversal-wire"),
            (service.Request!.UndoQuantity, service.Request.MeasuredUndoQuantityKg, service.Metadata!.IdempotencyKey));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(replay, body.GetProperty("purchase").GetProperty("isReplay").GetBoolean());
    }

    private sealed class ReceiptReversalWireService(bool replay) : IPurchaseReceiptReversalServiceV1
    {
        public ReversePurchaseReceiptV1? Request { get; private set; }
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public Task<PurchaseReceiptReversalResponseV1> ReverseGoodsReceiptAsync(Guid orderId, Guid acceptanceId,
            ReversePurchaseReceiptV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Request = request; Metadata = metadata;
            return Task.FromResult(new PurchaseReceiptReversalResponseV1(
                new(CompanyA, request.AdjustmentId, orderId, acceptanceId, PurchaseOrderLineId, PurchaseOrderLineId,
                    "5", "2", "8", false, replay, "SENTETIK-reversal-original"),
                new(CompanyA, request.AdjustmentId, acceptanceId, request.InventoryMovementId, Guid.NewGuid(), Guid.NewGuid(), "3",
                    request.MeasuredUndoQuantityKg, [], replay, "SENTETIK-reversal-original"), null));
        }
    }
}
