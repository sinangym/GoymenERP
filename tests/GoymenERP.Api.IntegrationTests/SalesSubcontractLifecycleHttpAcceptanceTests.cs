using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK HTTP boundary only; real authority and business state are covered in service/SQL fact sources.</summary>
public sealed class SalesSubcontractLifecycleHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public async Task Subcontract_invoice_selection_returns_only_order_metadata_and_rejects_unsupported_or_repeated_query_fields()
    {
        var service = new ListStub();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesSubcontractInvoiceListServiceV1>();
            services.AddSingleton<ISalesSubcontractInvoiceListServiceV1>(service);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        var orderId = Guid.NewGuid();
        var path = $"{SalesEndpoints.SubcontractServiceInvoicesPath}/by-order/{orderId:D}";
        foreach (var query in new[] { "?pageSize=1&pageSize=2", "?cursor=one&cursor=two", "?companyId=other", "?price=true" })
        {
            using var invalid = await client.GetAsync(path + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(0, service.Calls);
        using var response = await client.GetAsync(path + "?pageSize=3&cursor=SENTETIK-cursor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(orderId, service.OrderId);
        Assert.Equal(3, service.Query?.PageSize);
        Assert.Equal("SENTETIK-cursor", service.Query?.Cursor);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(orderId, json.RootElement.GetProperty("salesOrderId").GetGuid());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(["id", "documentNo", "state", "version", "businessDate", "createdAtUtc"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(JsonValueKind.String, item.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("match")]
    [InlineData("finalize")]
    [InlineData("resend-finance")]
    public async Task Subcontract_lifecycle_v1_routes_require_one_key_strict_json_and_return_replay_headers(string action)
    {
        var service = new Stub();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesSubcontractInvoiceLifecycleServiceV1>();
            services.AddSingleton<ISalesSubcontractInvoiceLifecycleServiceV1>(service);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        var id = Guid.NewGuid();
        var path = $"{SalesEndpoints.SubcontractServiceInvoicesPath}/{id:D}/{action}";
        var body = action == "match" ? "{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"SENTETIK-1\",\"ettn\":\"e41d6f79-b532-47f8-9821-6be258535fad\"}" : "{\"expectedVersion\":\"3\"}";
        using (var missing = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var malformedRequest = new HttpRequestMessage(HttpMethod.Post, path))
        {
            malformedRequest.Headers.Add("Idempotency-Key", "SENTETIK-sc34-key");
            malformedRequest.Content = new StringContent("{\"expectedVersion\":\"3\",\"companyId\":\"forbidden\"}", Encoding.UTF8, "application/json");
            using var malformed = await client.SendAsync(malformedRequest);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }
        Assert.Equal(0, service.Calls);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", "SENTETIK-sc34-key");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(action, service.Action);
        Assert.Equal("SENTETIK-sc34-key", service.Key);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("totals").GetProperty("payableAmount").ValueKind);
        Assert.True(json.RootElement.TryGetProperty("lifecycle", out _));
    }

    private sealed class ListStub : ISalesSubcontractInvoiceListServiceV1
    {
        public int Calls { get; private set; }
        public Guid OrderId { get; private set; }
        public SalesInvoiceListQuery? Query { get; private set; }
        public Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid salesOrderId, SalesInvoiceListQuery query,
            CancellationToken cancellationToken = default)
        {
            Calls++; OrderId = salesOrderId; Query = query;
            return Task.FromResult(new SalesSubcontractInvoiceListV1(salesOrderId,
                [new(Guid.NewGuid(), "SENTETIK-SFH", "Approved", "3", new(2026, 11, 17), DateTimeOffset.UnixEpoch)], null));
        }
    }

    private sealed class Stub : ISalesSubcontractInvoiceLifecycleServiceV1
    {
        public int Calls { get; private set; }
        public string? Action { get; private set; }
        public string? Key { get; private set; }
        private Task<SalesSubcontractServiceInvoiceCommandResult> Answer(Guid id, SalesCommandMetadata metadata, string action)
        {
            Calls++; Action = action; Key = metadata.IdempotencyKey;
            var amount = new SalesInvoiceAmountsView("10.00", "2.00", "0.00", "12.00", "12.00");
            var detail = new SalesSubcontractServiceInvoiceDetailView(id, "SENTETIK-SFH", "Draft", "3", new(2026, 11, 17),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "10", amount,
                [], "SENTETIK-preparer", DateTimeOffset.UnixEpoch, true);
            return Task.FromResult(new SalesSubcontractServiceInvoiceCommandResult(detail, true, "SENTETIK-correlation"));
        }
        public Task<SalesSubcontractServiceInvoiceCommandResult> SubmitSubcontractInvoiceAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "submit");
        public Task<SalesSubcontractServiceInvoiceCommandResult> ApproveSubcontractInvoiceAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "approve");
        public Task<SalesSubcontractServiceInvoiceCommandResult> RejectSubcontractInvoiceAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "reject");
        public Task<SalesSubcontractServiceInvoiceCommandResult> CancelSubcontractInvoiceAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "cancel");
        public Task<SalesSubcontractServiceInvoiceCommandResult> MatchSubcontractInvoiceAsync(Guid id, MatchSalesInvoiceRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "match");
        public Task<SalesSubcontractServiceInvoiceCommandResult> FinalizeSubcontractInvoiceAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "finalize");
        public Task<SalesSubcontractServiceInvoiceCommandResult> ResendSubcontractInvoiceToFinanceAsync(Guid id, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(id, metadata, "resend-finance");
    }
}
