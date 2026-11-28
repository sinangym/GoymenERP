using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    // Controlled HTTP owner protocol and real scoped no-I/O DI identity only.
    // Not a genuine SQL receipt/rollback or actual first-use/browser acceptance claim.
    [Fact]
    public void Mixed_price_alias_resolves_the_configured_native_parent_once_per_scope_without_a_second_owner_or_child_approval_service()
    {
        using var scope = factory.Services.CreateScope(); var services = scope.ServiceProvider;
        var parent = services.GetRequiredService<PurchasingService>();
        Assert.Same(parent, services.GetRequiredService<IPurchaseMixedServicePriceRevisionServiceV1>());
        Assert.Same(parent, services.GetRequiredService<IPurchasingService>());
        Assert.Same(parent, services.GetRequiredService<IPurchaseMixedOrderApprovalPreviewV1>());
        Assert.Same(parent, services.GetRequiredService<IPurchaseMixedOrderMembershipServiceV1>());
        using var other = factory.Services.CreateScope();
        Assert.NotSame(parent, other.ServiceProvider.GetRequiredService<IPurchaseMixedServicePriceRevisionServiceV1>());
    }

    [Fact]
    public async Task Mixed_price_route_preserves_exact_parent_and_child_versions_native_zero_key_correlation_and_original_replay_headers()
    {
        var owner = new MixedPriceHttpOwner { Replay = true }; using var host = MixedPriceHost(owner); using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id"); client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-price-correlation");
        using var response = await client.SendAsync(Post(MixedPricePath(), "SENTETIK-price-key", MixedPriceBody()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response); Assert.Equal(1, owner.Calls);
        Assert.Equal(PurchaseOrderId, owner.OrderId); var body = Assert.IsType<RevisePurchaseMixedServicePricesRequest>(owner.Request);
        Assert.Equal("9007199254740993", body.ExpectedVersion); Assert.Equal(2, body.Services.Count);
        Assert.Equal("9223372036854775806", body.Services[0].ExpectedVersion); Assert.Equal("9223372036854775807", body.Services[1].ExpectedVersion);
        Assert.Equal(AcceptanceId, body.Services[0].ServiceOrderId); Assert.Equal(PurchaseOrderLineId, Assert.Single(body.Services[0].LineIds));
        Assert.Equal("0", Assert.Single(Assert.Single(body.Changes).Lines).UnitPrice);
        Assert.Equal("  SENTETIK native reason  ", body.Reason); // The native owner owns trimming and content hash semantics.
        Assert.Equal(("SENTETIK-price-key", "SENTETIK-price-correlation"), (owner.Metadata!.IdempotencyKey, owner.Metadata.CorrelationId));
        Assert.Equal(TimeSpan.Zero, owner.Metadata.RequestedAtUtc.Offset);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-price-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("receipt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("children", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("companyId")] [InlineData("supplierPartyId")] [InlineData("receipt")] [InlineData("currentVersion")] [InlineData("mixedServices")]
    public async Task Mixed_price_route_rejects_foreign_or_unknown_root_fields_before_owner(string field)
    {
        var owner = new MixedPriceHttpOwner(); using var host = MixedPriceHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post(MixedPricePath(), "SENTETIK-price-foreign",
            MixedPriceBody()[..^1] + $",\"{field}\":\"SENTETIK\"}}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, owner.Calls); AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData("query", 400)] [InlineData("key", 400)] [InlineData("content", 415)]
    public async Task Mixed_price_route_requires_existing_native_metadata_json_and_forbids_query_override(string fault, int expected)
    {
        var owner = new MixedPriceHttpOwner(); using var host = MixedPriceHost(owner); using var client = CreateClient(host);
        using var request = Post(MixedPricePath() + (fault == "query" ? "?companyId=foreign" : ""), "SENTETIK-price-required", MixedPriceBody());
        if (fault == "key") request.Headers.Remove(PurchasingApiV1.IdempotencyHeader);
        if (fault == "content") request.Content = new StringContent(MixedPriceBody(), System.Text.Encoding.UTF8, "text/plain");
        using var response = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode); Assert.Equal(0, owner.Calls); AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(503)]
    public async Task Mixed_price_native_refusal_or_unknown_has_no_partial_success_or_private_receipt(int status)
    {
        var owner = new MixedPriceHttpOwner { Failure = status }; using var host = MixedPriceHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post(MixedPricePath(), "SENTETIK-price-failed", MixedPriceBody()));
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response); Assert.Equal(1, owner.Calls);
        Assert.DoesNotContain("SENTETIK-private-price-proof", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Theory]
    [InlineData("root-duplicate")] [InlineData("service-duplicate")] [InlineData("line-duplicate")]
    [InlineData("service-foreign")] [InlineData("change-foreign")] [InlineData("line-foreign")]
    [InlineData("root-case")] [InlineData("root-missing")] [InlineData("lines-null")]
    public void Mixed_price_closed_native_reader_rejects_nested_duplicates_unknown_case_missing_or_null_fields(string fault)
    {
        var json = MixedPriceBody();
        json = fault switch {
            "root-duplicate" => json[..^1] + ",\"reason\":\"SENTETIK\"}",
            "service-duplicate" => json.Replace("\"expectedVersion\":\"9223372036854775806\"", "\"expectedVersion\":\"9223372036854775806\",\"expectedVersion\":\"2\"", StringComparison.Ordinal),
            "line-duplicate" => json.Replace("\"unitPrice\":\"0\"", "\"unitPrice\":\"0\",\"unitPrice\":\"1\"", StringComparison.Ordinal),
            "service-foreign" => json.Replace("\"lineIds\":[", "\"companyId\":\"SENTETIK\",\"lineIds\":[", StringComparison.Ordinal),
            "change-foreign" => json.Replace("\"lines\":[", "\"receipt\":{},\"lines\":[", StringComparison.Ordinal),
            "line-foreign" => json.Replace("\"unitPrice\":\"0\"", "\"unitPrice\":\"0\",\"preparedBy\":\"SENTETIK\"", StringComparison.Ordinal),
            "root-case" => json.Replace("\"changes\"", "\"Changes\"", StringComparison.Ordinal),
            "root-missing" => json.Replace(",\"reason\":\"  SENTETIK native reason  \"", "", StringComparison.Ordinal),
            _ => json.Replace("\"unitPrice\":\"0\"", "\"unitPrice\":null", StringComparison.Ordinal)
        };
        Assert.Throws<JsonException>(() => PurchaseMixedOrderJsonV1.ReadServicePriceRevision(json));
    }

    [Fact]
    public void Mixed_price_contract_is_exact_and_does_not_change_existing_native_approve_or_supplier_revision_null_hash()
    {
        var value = PurchaseMixedOrderJsonV1.ReadServicePriceRevision(MixedPriceBody());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, PurchasingJson.Options));
        Assert.Equal(new[] { "expectedVersion", "services", "changes", "reason" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.Equal(new[] { "serviceOrderId", "expectedVersion", "lineIds" }, json.RootElement.GetProperty("services")[0].EnumerateObject().Select(x => x.Name));
        Assert.Equal(new[] { "serviceOrderId", "lines" }, json.RootElement.GetProperty("changes")[0].EnumerateObject().Select(x => x.Name));
        Assert.Equal(new[] { "lineId", "unitPrice" }, json.RootElement.GetProperty("changes")[0].GetProperty("lines")[0].EnumerateObject().Select(x => x.Name));
        var native = PurchaseMixedOrderJsonV1.ReadApprove("{\"expectedVersion\":\"2\"}");
        Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, PurchaseOrderId, native),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, PurchaseOrderId, native with { MixedServices = null }));
        var supplier = new RevisePurchaseOrderSupplierRequest("2", AcceptanceId, CompanyA, "SENTETIK supplier");
        Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, PurchaseOrderId, supplier),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, PurchaseOrderId, supplier with { MixedServices = null }));
    }

    private static string MixedPricePath() => $"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/service-price-revisions";
    private static readonly Guid HeldPriceService = Guid.Parse("cda56784-1023-4bce-8000-000000000001");
    private static string MixedPriceBody() => $$"""{"expectedVersion":"9007199254740993","services":[{"serviceOrderId":"{{AcceptanceId:D}}","expectedVersion":"9223372036854775806","lineIds":["{{PurchaseOrderLineId:D}}"]},{"serviceOrderId":"{{HeldPriceService:D}}","expectedVersion":"9223372036854775807","lineIds":["{{PurchaseOrderLineId:D}}"]}],"changes":[{"serviceOrderId":"{{AcceptanceId:D}}","lines":[{"lineId":"{{PurchaseOrderLineId:D}}","unitPrice":"0"}]}],"reason":"  SENTETIK native reason  "}""";
    private WebApplicationFactory<Program> MixedPriceHost(MixedPriceHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => { services.RemoveAll<IPurchaseMixedServicePriceRevisionServiceV1>();
            services.AddScoped<IPurchaseMixedServicePriceRevisionServiceV1>(_ => owner); }));
    private sealed class MixedPriceHttpOwner : IPurchaseMixedServicePriceRevisionServiceV1
    {
        public int Calls { get; private set; }
        public Guid OrderId { get; private set; }
        public RevisePurchaseMixedServicePricesRequest? Request { get; private set; }
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public int? Failure { get; init; }
        public bool Replay { get; init; }
        public Task<PurchaseOrderResponse> ReviseMixedServicePricesAsync(Guid id, RevisePurchaseMixedServicePricesRequest request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++; OrderId = id; Request = request; Metadata = metadata;
            if (Failure is { } status) throw new PurchasingServiceException(status,
                status == 409 ? PurchasingProblemCodesV1.VersionConflict : "SENTETIK-private-price-proof");
            return Task.FromResult(new PurchaseOrderResponse(OrderDetail(id), Replay, "SENTETIK-price-original"));
        }
    }
}
