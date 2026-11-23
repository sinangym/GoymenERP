using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace GoymenERP.Api.IntegrationTests;

// HTTP contract source only. The stub is not a real external cancellation status provider.
public sealed class SalesOfficialInvoiceCancellationHttpContractTests(GoymenErpApiFactory factory):IClassFixture<GoymenErpApiFactory>
{
    [Theory]
    [InlineData("invoices",SalesOfficialInvoiceKindV1.Normal)]
    [InlineData("subcontract-service-invoices",SalesOfficialInvoiceKindV1.Subcontract)]
    public async Task Official_cancel_routes_accept_only_version_reason_and_return_price_free_kind_bound_immutable_receipt(string path,SalesOfficialInvoiceKindV1 kind)
    {
        var stub=new Stub();using var host=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.RemoveAll<ISalesOfficialInvoiceCancellationServiceV1>();s.AddSingleton<ISalesOfficialInvoiceCancellationServiceV1>(stub);}));
        using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Development-User",TestUsers.GroupAdmin);client.DefaultRequestHeaders.Add("X-Company-Id",TestCompanyIds.CompanyA.ToString("D"));
        var id=Guid.NewGuid();var route=$"/api/v1/sales/{path}/{id:D}/official-cancellation";
        foreach(var body in new[]{"{\"expectedVersion\":\"4\",\"reason\":\"SENTETIK\",\"evidence\":{}}","{\"expectedVersion\":\"4\",\"expectedVersion\":\"4\",\"reason\":\"SENTETIK\"}","{\"expectedVersion\":4,\"reason\":\"SENTETIK\"}"})
        {using var request=new HttpRequestMessage(HttpMethod.Post,route);request.Headers.Add("Idempotency-Key","SENTETIK-official");request.Content=new StringContent(body,Encoding.UTF8,"application/json");using var denied=await client.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        Assert.Equal(0,stub.Calls);
        using var valid=new HttpRequestMessage(HttpMethod.Post,route);valid.Headers.Add("Idempotency-Key","SENTETIK-official");valid.Content=new StringContent("{\"expectedVersion\":\"4\",\"reason\":\"SENTETIK\"}",Encoding.UTF8,"application/json");
        using var answer=await client.SendAsync(valid);Assert.Equal(HttpStatusCode.OK,answer.StatusCode);Assert.Equal(kind,stub.Kind);Assert.Equal(id,stub.Id);
        Assert.True(answer.Headers.CacheControl is {Private:true,NoStore:true});using var json=JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
        Assert.Equal(kind.ToString(),json.RootElement.GetProperty("kind").GetString());Assert.Equal(id,json.RootElement.GetProperty("invoiceId").GetGuid());
        Assert.False(json.RootElement.TryGetProperty("totals",out _));Assert.False(json.RootElement.TryGetProperty("exchangeRate",out _));
    }
    [Fact]
    public async Task Missing_external_status_source_is_503_and_not_a_successful_user_assertion()
    {
        var stub=new Stub{Unavailable=true};using var host=factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{s.RemoveAll<ISalesOfficialInvoiceCancellationServiceV1>();s.AddSingleton<ISalesOfficialInvoiceCancellationServiceV1>(stub);}));
        using var client=host.CreateClient();client.DefaultRequestHeaders.Add("X-Development-User",TestUsers.GroupAdmin);client.DefaultRequestHeaders.Add("X-Company-Id",TestCompanyIds.CompanyA.ToString("D"));
        using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/sales/invoices/{Guid.NewGuid():D}/official-cancellation");request.Headers.Add("Idempotency-Key","SENTETIK-pending");request.Content=new StringContent("{\"expectedVersion\":\"4\",\"reason\":\"SENTETIK\"}",Encoding.UTF8,"application/json");
        using var answer=await client.SendAsync(request);Assert.Equal(HttpStatusCode.ServiceUnavailable,answer.StatusCode);
        Assert.DoesNotContain("sourceRecordId",await answer.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }
    private sealed class Stub:ISalesOfficialInvoiceCancellationServiceV1
    {
        public int Calls{get;private set;}public Guid Id{get;private set;}public SalesOfficialInvoiceKindV1 Kind{get;private set;}public bool Unavailable{get;init;}
        public Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesOfficialInvoiceKindV1 kind,Guid id,RecordSalesOfficialInvoiceCancellationV1 request,SalesCommandMetadata metadata,CancellationToken ct=default)
        {Calls++;Id=id;Kind=kind;if(Unavailable)throw new SalesDraftServiceException(503,"official_cancellation_evidence_unavailable");var ettn=Guid.NewGuid().ToString("D");var now=DateTimeOffset.UtcNow;var proof=new SalesOfficialInvoiceCancellationEvidenceV1(TestCompanyIds.CompanyA,id,"SENTETIK",ettn,"SENTETIK-status","SENTETIK-record","1",new string('a',64),now.AddMinutes(-1));return Task.FromResult(new SalesOfficialInvoiceCancellationResultV1(new(TestCompanyIds.CompanyA,Guid.NewGuid(),id,kind,"5","4",Guid.NewGuid(),"SENTETIK",ettn,Guid.NewGuid().ToString("D"),now,request.Reason,proof),false,metadata.CorrelationId));}
        public Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 kind,string? cursor,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationViewV1> GetAsync(Guid id,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationViewV1> GetForInvoiceAsync(SalesOfficialInvoiceKindV1 kind,Guid id,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SalesOfficialInvoiceCancellationResultV1> RetryFinanceAsync(Guid id,RetrySalesOfficialInvoiceCancellationFinanceV1 request,SalesCommandMetadata metadata,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
