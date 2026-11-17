using System.Net;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Subcontract_tax_transport_carries_only_actual_list_choices_and_keeps_replay_metadata()
    {
        var owner = new ServiceOrderHttpOwner { Replay = true }; using var host = ServiceOrderHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/service-orders/{PurchaseOrderId:D}/tax", "SENTETIK-tax-http",
            $$"""{"expectedVersion":"3","lineId":"{{Guid.NewGuid():D}}","useDefault":false,"expectedDefaultVersion":"1","expectedSourceVersion":"SENTETIK-lists:1","vatRateCode":"10","exemptionCode":null,"withholdingRatioCode":null,"reason":"SENTETIK vergi"}"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("3", owner.TaxRequest!.ExpectedVersion);
        Assert.Equal("10", owner.TaxRequest.VatRateCode); Assert.Equal("SENTETIK-lists:1", owner.TaxRequest.ExpectedSourceVersion);
        Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single()); AssertPrivateNoStore(response);
    }
    [Theory]
    [InlineData("companyId", "00000000-0000-0000-0000-000000000001")]
    [InlineData("vatPercent", "10")]
    [InlineData("physicalApprovalVersion", "2")]
    public async Task Subcontract_tax_transport_refuses_client_authority_or_free_rate_fields_before_the_owner(string name, string value)
    {
        var owner = new ServiceOrderHttpOwner(); using var host = ServiceOrderHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/service-orders/{PurchaseOrderId:D}/tax", "SENTETIK-tax-shape",
            $$"""{"expectedVersion":"3","lineId":"{{Guid.NewGuid():D}}","useDefault":true,"expectedDefaultVersion":"1","expectedSourceVersion":"SENTETIK-lists:1","vatRateCode":null,"exemptionCode":null,"withholdingRatioCode":null,"reason":"SENTETIK","{{name}}":"{{value}}"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(owner.TaxRequest);
    }
}
