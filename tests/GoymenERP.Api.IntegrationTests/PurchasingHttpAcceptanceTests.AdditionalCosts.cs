using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// Wire contract only; the fake owner grants no production authority and makes no SQL acceptance claim.
public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Additional_cost_uses_only_the_server_company_and_preserves_the_exact_expense_replay_receipt()
    {
        var owner = new AdditionalCostHttpOwner { Replay = true };
        using var host = AdditionalCostHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post("/api/v1/purchasing/additional-costs", "SENTETIK-expense", AdditionalCostBody()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
        Assert.Equal("3", owner.Request!.ExpectedOrderVersion); Assert.Equal("100.01", owner.Request.NetAmount);
        Assert.Equal("SENTETIK-expense", owner.Metadata!.IdempotencyKey);
        Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("SENTETIK-original", response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CompanyA, body.GetProperty("cost").GetProperty("companyId").GetGuid());
        Assert.Equal("120.01", body.GetProperty("cost").GetProperty("grossAmount").GetString());
        Assert.False(body.GetProperty("cost").TryGetProperty("inventoryMovementId", out _));
    }

    [Theory]
    [InlineData(",\"companyId\":\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData(",\"netAmount\":\"1\"")]
    [InlineData(",\"grossAmount\":\"120.01\"")]
    public async Task Additional_cost_rejects_client_company_duplicate_money_and_computed_totals_before_the_owner(string tail)
    {
        var owner = new AdditionalCostHttpOwner(); using var host = AdditionalCostHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post("/api/v1/purchasing/additional-costs", "SENTETIK-malformed", AdditionalCostBody()[..^1] + tail + "}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(owner.Request);
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(503)]
    public async Task Additional_cost_source_refusal_hides_internal_money_and_supplier_metadata(int status)
    {
        var owner = new AdditionalCostHttpOwner { Refusal = status }; using var host = AdditionalCostHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync($"/api/v1/purchasing/additional-costs/sources/1/{PurchaseOrderId:D}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("SENTETIK-private", body);
        Assert.DoesNotContain("currencyId", body); Assert.DoesNotContain("supplierPartyId", body);
    }

    [Fact]
    public async Task Additional_cost_references_require_one_exact_business_date_query()
    {
        var owner = new AdditionalCostHttpOwner(); using var host = AdditionalCostHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/purchasing/additional-costs/references?on=2026-11-17&companyId=other");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, owner.ReferenceReads);
    }

    private static string AdditionalCostBody() => $$"""{"costId":"{{AcceptanceId:D}}","expectedVersion":"0","orderId":"{{PurchaseOrderId:D}}","sourceKind":1,"expectedOrderVersion":"3","acceptanceId":null,"supplierPartyId":"{{ProductId:D}}","companyAccountId":"{{UnitId:D}}","businessDate":"2026-11-17","expenseTypeId":null,"expectedExpenseTypeVersion":null,"description":"SENTETIK nakliye","netAmount":"100.01","vatRateCode":"SENTETIK-20","expectedVatListVersion":"1","reason":"SENTETIK belge"}""";
    private WebApplicationFactory<Program> AdditionalCostHost(AdditionalCostHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => { services.RemoveAll<IPurchaseAdditionalCostServiceV1>(); services.AddSingleton<IPurchaseAdditionalCostServiceV1>(owner); }));
    private sealed class AdditionalCostHttpOwner : IPurchaseAdditionalCostServiceV1
    {
        public bool Replay { get; init; } public int? Refusal { get; init; } public int ReferenceReads { get; private set; }
        public RecordPurchaseAdditionalCostV1? Request { get; private set; } public PurchasingCommandMetadata? Metadata { get; private set; }
        public Task<PurchaseAdditionalCostSourceViewV1> SourceAsync(PurchaseAdditionalCostSourceKind kind, Guid order, CancellationToken token = default) => Refusal is { } status
            ? throw new PurchasingServiceException(status, "SENTETIK-private", detail: "SENTETIK-private")
            : Task.FromResult(new PurchaseAdditionalCostSourceViewV1(CompanyA, order, "3", "SENTETIK-SIP-1", kind.ToString(), ProductId, "TRY", []));
        public Task<IReadOnlyList<PurchaseAdditionalCostViewV1>> ListAsync(PurchaseAdditionalCostSourceKind kind, Guid order, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<PurchaseAdditionalCostViewV1>>([]);
        public Task<PurchaseAdditionalCostReferencesV1> ReferencesAsync(DateOnly on, CancellationToken token = default)
        { ReferenceReads++; return Task.FromResult(new PurchaseAdditionalCostReferencesV1(CompanyA, false, [], on, "1", [])); }
        public Task<PurchaseAdditionalCostResponseV1> RecordAsync(RecordPurchaseAdditionalCostV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default)
        {
            Request = request; Metadata = metadata;
            return Task.FromResult(new PurchaseAdditionalCostResponseV1(new(CompanyA, request.CostId, "1", request.OrderId, request.ExpectedOrderVersion,
                "SENTETIK-SIP-1", "GoodsOrder", null, request.SupplierPartyId, request.CompanyAccountId, request.BusinessDate, ProductId, "TRY",
                request.Description, null, null, null, request.NetAmount, "20", "20", "120.01", "SENTETIK-actor", request.Reason,
                new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero)), Replay, "SENTETIK-original"));
        }
    }
}
