using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Expense_invoice_candidates_have_a_monetary_contract_and_reject_client_company_queries()
    {
        var owner = new ExpenseInvoiceHttpWorkspace();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IPurchaseIncomingInvoiceWorkspaceV1>(); services.AddSingleton<IPurchaseIncomingInvoiceWorkspaceV1>(owner); }));
        using var client = CreateClient(host);
        var path = $"/api/v1/purchasing/invoices/{PurchaseOrderId:D}/lines/{PurchaseOrderLineId:D}/additional-cost-candidates";
        using (var response = await client.GetAsync(path + "?pageIndex=0"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); var item = body.GetProperty("items")[0];
            Assert.Equal("4000", item.GetProperty("availableNetAmount").GetString());
            Assert.False(item.TryGetProperty("acceptanceId", out _)); Assert.False(item.TryGetProperty("quantity", out _));
        }
        using (var response = await client.GetAsync(path + "?pageIndex=0&companyId=other"))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, owner.Calls);
    }
    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(503)]
    public async Task Expense_invoice_candidate_refusals_never_disclose_a_previous_financial_source(int status)
    {
        var owner = new ExpenseInvoiceHttpWorkspace { Refusal = status };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IPurchaseIncomingInvoiceWorkspaceV1>(); services.AddSingleton<IPurchaseIncomingInvoiceWorkspaceV1>(owner); }));
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/api/v1/purchasing/invoices/{PurchaseOrderId:D}/lines/{PurchaseOrderLineId:D}/additional-cost-candidates");
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("availableNetAmount", body); Assert.DoesNotContain("SENTETIK-private", body);
    }
    private sealed class ExpenseInvoiceHttpWorkspace : IPurchaseIncomingInvoiceWorkspaceV1
    {
        public int? Refusal { get; init; } public int Calls { get; private set; }
        public Task<PurchaseIncomingInvoicePageV1> ListAsync(string? number, int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseInvoiceReceiptCandidatePageV1> CandidatesAsync(Guid invoice, Guid line, int page, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseInvoiceAdditionalCostCandidatePageV1> AdditionalCostCandidatesAsync(Guid invoice, Guid line, int page, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Refusal is { } status) throw new PurchasingServiceException(status, "SENTETIK-private", detail: "SENTETIK-private");
            return Task.FromResult(new PurchaseInvoiceAdditionalCostCandidatePageV1(invoice, line, "1", ProductId, page, false,
                [new(AcceptanceId, PurchaseOrderId, "SENTETIK-SIP-1", "SENTETIK nakliye", null, null, new(2026, 11, 17), "10000", "4000", "20")]));
        }
    }
}
