using System.Net;
using System.Text;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftHttpAcceptanceTests
{
    [Fact]
    public async Task Subcontract_tax_revision_keeps_the_approved_command_metadata_and_replay_headers()
    {
        var service = new StubDraftService { SubcontractTaxReplay = true }; using var host = CreateHost(service); using var client = CreateClient(host);
        var id = Guid.NewGuid();
        using var request = TaxPost(id, "SENTETIK-tax-http", $$"""{"expectedVersion":"4","lineId":"{{Guid.NewGuid():D}}","tax":{"mode":"Default"},"reason":"SENTETIK vergi"}""");
        using var response = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode)SalesDraftApiV1.StateCommandStatus, response.StatusCode);
        Assert.Equal("SENTETIK-tax-http", service.SubcontractTaxMetadata!.IdempotencyKey);
        Assert.Equal("4", service.SubcontractTaxRequest!.ExpectedVersion);
        Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }
    [Theory]
    [InlineData("companyId", "SENTETIK")]
    [InlineData("unitPrice", "10")]
    [InlineData("permission", "SENTETIK")]
    public async Task A_subcontract_tax_revision_refuses_free_price_company_or_authority_fields_before_the_owner(string field, string value)
    {
        var service = new StubDraftService(); using var host = CreateHost(service); using var client = CreateClient(host);
        using var request = TaxPost(Guid.NewGuid(), "SENTETIK-tax-shape",
            $$"""{"expectedVersion":"4","lineId":"{{Guid.NewGuid():D}}","tax":{"mode":"Default"},"reason":"SENTETIK","{{field}}":"{{value}}"}""");
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(service.SubcontractTaxRequest);
    }
    private static HttpRequestMessage TaxPost(Guid id, string key, string body)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sales/drafts/{id:D}/subcontract-tax-revision")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        message.Headers.Add("Idempotency-Key", key); message.Headers.Add("X-Correlation-Id", "SENTETIK-tax-correlation"); return message;
    }
    private sealed partial class StubDraftService : ISalesSubcontractTaxServiceV1
    {
        public bool SubcontractTaxReplay { get; init; }
        public SalesSubcontractTaxRevisionV1? SubcontractTaxRequest { get; private set; }
        public SalesCommandMetadata? SubcontractTaxMetadata { get; private set; }
        public Task<SalesSubcontractTaxOptionsV1> GetSubcontractTaxOptionsAsync(Guid draftId, CancellationToken token = default) =>
            throw new SalesDraftServiceException(404, "not_found");
        public Task<SalesOrderDraftResponse> ReviseSubcontractTaxAsync(Guid id, SalesSubcontractTaxRevisionV1 request, SalesCommandMetadata metadata, CancellationToken token = default)
        {
            SubcontractTaxRequest = request; SubcontractTaxMetadata = metadata;
            return Task.FromResult(new SalesOrderDraftResponse(Detail(id), SubcontractTaxReplay, "SENTETIK-original"));
        }
    }
}
