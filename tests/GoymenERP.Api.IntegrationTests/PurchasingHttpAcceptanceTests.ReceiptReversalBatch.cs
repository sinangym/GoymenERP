using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Synthetic HTTP service proves batch wire, one final version and replay headers; separate SQL facts own atomicity.</summary>
public sealed partial class PurchasingHttpAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_batch_wire_preserves_one_final_version_and_authoritative_general_supply_facts(bool replay)
    {
        var service = new ReceiptBatchWireService(replay);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IPurchaseReceiptReversalBatchServiceV1>(_ => service))));
        using var client = CreateClient(host);
        var request = BatchWireRequest();
        using var response = await client.SendAsync(Post(PurchasingApiV1.OrdersPath + $"/{PurchaseOrderId:D}/goods-receipts/reversal-batch",
            "SENTETIK-batch-wire", JsonSerializer.Serialize(request, PurchasingJson.Options)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
        if (replay) Assert.Equal("SENTETIK-batch-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal("4", service.Request!.ExpectedPurchaseOrderVersion); Assert.Equal(2, service.Request.Lines.Count);
        Assert.Equal("1.9", service.Request.Lines[0].MeasuredUndoQuantityKg);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("5", body.GetProperty("orderVersion").GetString());
        Assert.Equal("0", body.GetProperty("supplies")[0].GetProperty("netOpenedQuantity").GetString());
        Assert.All(body.GetProperty("lines").EnumerateArray(), line => Assert.Equal("5", line.GetProperty("purchase").GetProperty("orderVersion").GetString()));
    }

    [Fact]
    public async Task A_duplicate_acceptance_in_a_receipt_batch_is_rejected_before_the_service_runs()
    {
        var service = new ReceiptBatchWireService(false);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IPurchaseReceiptReversalBatchServiceV1>(_ => service))));
        using var client = CreateClient(host); var request = BatchWireRequest();
        request = request with { Lines = [request.Lines[0], request.Lines[1] with { AcceptanceId = request.Lines[0].AcceptanceId }] };
        using var response = await client.SendAsync(Post(PurchasingApiV1.OrdersPath + $"/{PurchaseOrderId:D}/goods-receipts/reversal-batch",
            "SENTETIK-batch-duplicate", JsonSerializer.Serialize(request, PurchasingJson.Options)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(service.Request);
    }

    private static ReversePurchaseReceiptsV1 BatchWireRequest() => new(Guid.NewGuid(), "4", "SENTETIK batch", DateTimeOffset.UnixEpoch,
        [new(AcceptanceId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2", "1.9", "Free"),
         new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "3", "2.8", "Blocked")]);

    private sealed class ReceiptBatchWireService(bool replay) : IPurchaseReceiptReversalBatchServiceV1
    {
        public ReversePurchaseReceiptsV1? Request { get; private set; }
        public Task<PurchaseReceiptReversalBatchResponseV1> ReverseGoodsReceiptsAsync(Guid orderId, ReversePurchaseReceiptsV1 request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new PurchaseReceiptReversalBatchResponseV1(CompanyA, orderId, request.BatchId, "5", request.Lines.Select(line =>
                new PurchaseReceiptReversalResponseV1(new(CompanyA, line.AdjustmentId, orderId, line.AcceptanceId, PurchaseOrderLineId,
                    PurchaseOrderLineId, "5", "0", "6", false, replay, "SENTETIK-batch-original"),
                    new(CompanyA, line.AdjustmentId, line.AcceptanceId, line.InventoryMovementId, Guid.NewGuid(), Guid.NewGuid(), "2",
                        line.MeasuredUndoQuantityKg, [], replay, "SENTETIK-batch-original"), null)).ToArray(),
                [new(PurchaseOrderLineId, "0", "6", false)], false, replay, "SENTETIK-batch-original"));
        }
    }
}
