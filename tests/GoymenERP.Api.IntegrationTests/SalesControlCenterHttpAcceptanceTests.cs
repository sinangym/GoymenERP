using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-37 (Kontrol Merkezi) HTTP boundary over a SENTETIK <see cref="ISalesControlCenterService"/> (no database, no owner):
/// SAT-D246 (EK49) puts every order of an invoice item on the wire as the <c>salesOrderIds</c> array, in the order the service
/// answered it, and no single <c>salesOrderId</c> member remains; a query string answers 400 before the service. The service's own
/// rules are unit-tested; the store is the SQL Server class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesControlCenterHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task An_invoice_item_carries_every_order_as_the_salesOrderIds_array_in_order_and_no_single_salesOrderId()
    {
        Guid orderA = Guid.NewGuid(), orderB = Guid.NewGuid(), shipmentOrder = Guid.NewGuid();
        Guid invoiceId = Guid.NewGuid(), shipmentId = Guid.NewGuid(), noteId = Guid.NewGuid();
        var day = new DateOnly(2026, 11, 9);
        var service = new StubControlCenterService
        {
            Answer = new SalesControlCenterResponse(day, SalesControlCenterApiV1.DefaultUninvoicedDispatchGraceDays,
                [new SalesControlCenterCheckView(SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch, SalesControlCenterCheckStateView.Measured, false)],
                [
                    new SalesControlCenterItemView(SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch, SalesControlCenterSeverityView.Info,
                        Party, Account, "SF-2026-000001", day, 0, 3, invoiceId, null, null, [orderB, orderA]),
                    new SalesControlCenterItemView(SalesControlCenterCheckKindView.UninvoicedDispatchOverdue, SalesControlCenterSeverityView.Critical,
                        Party, Account, "IRS-2026-000001", day.AddDays(-9), 9, 1, null, noteId, shipmentId, [shipmentOrder]),
                ]),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesEndpoints.ControlCenterPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal([orderB, orderA], items[0].GetProperty("salesOrderIds").EnumerateArray().Select(id => id.GetGuid()).ToArray());
        Assert.Equal([shipmentOrder], items[1].GetProperty("salesOrderIds").EnumerateArray().Select(id => id.GetGuid()).ToArray());
        Assert.All(items, item => Assert.False(item.TryGetProperty("salesOrderId", out _)));
        Assert.Equal(invoiceId, items[0].GetProperty("invoiceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("shipmentId").ValueKind);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_query_string_on_the_control_center_answers_400_before_the_service()
    {
        var service = new StubControlCenterService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(SalesEndpoints.ControlCenterPath + "?salesOrderId=" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    private WebApplicationFactory<Program> CreateHost(ISalesControlCenterService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesControlCenterService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private sealed class StubControlCenterService : ISalesControlCenterService
    {
        public SalesControlCenterResponse Answer { get; set; } = new(new DateOnly(2026, 11, 9),
            SalesControlCenterApiV1.DefaultUninvoicedDispatchGraceDays, [], []);

        public int Calls { get; private set; }

        public Task<SalesControlCenterResponse> GetControlCenterAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Answer);
        }
    }
}
