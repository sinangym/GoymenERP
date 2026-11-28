using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// Controlled native-owner transport and real no-I/O DI composition; not a relational transaction/acceptance proof.
public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public void Mixed_composition_aliases_use_the_same_scoped_native_parent_and_child_owners()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var parent = services.GetRequiredService<PurchasingService>();
        Assert.Same(parent, services.GetRequiredService<IPurchasingService>());
        Assert.Same(parent, services.GetRequiredService<IPurchaseMixedOrderMembershipServiceV1>());
        Assert.Same(parent, services.GetRequiredService<IPurchaseMixedOrderApprovalPreviewV1>());
        Assert.Same(parent, services.GetRequiredService<IPurchasingSupplierRevisionServiceV1>());
        var child = services.GetRequiredService<PurchaseServiceOrderServiceV1>();
        Assert.Same(child, services.GetRequiredService<IPurchaseServiceOrderServiceV1>());
        Assert.Same(child, services.GetRequiredService<IPurchaseServiceOrderApprovalPreparationV1>());
        using var other = factory.Services.CreateScope();
        Assert.NotSame(parent, other.ServiceProvider.GetRequiredService<PurchasingService>());
        Assert.NotSame(child, other.ServiceProvider.GetRequiredService<PurchaseServiceOrderServiceV1>());
    }

    [Fact]
    public async Task Mixed_membership_transport_preserves_exact_native_identity_versions_lines_key_and_original_replay()
    {
        var owner = new MixedHttpOwner { Replay = true };
        using var host = MixedHost(owner); using var client = CreateClient(host);
        var request = MixedMembershipBody();
        using var response = await client.SendAsync(Post(MixedMembershipPath(), "SENTETIK-membership", request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal(PurchaseOrderId, owner.OrderId);
        Assert.Equal("9007199254740993", owner.Membership!.ExpectedVersion);
        var child = Assert.Single(owner.Membership.Services);
        Assert.Equal(AcceptanceId, child.ServiceOrderId);
        Assert.Equal("9223372036854775807", child.ExpectedVersion); // The selected child is read, not incremented, by binding.
        Assert.Equal(PurchaseOrderLineId, Assert.Single(child.LineIds));
        Assert.Equal("SENTETIK-membership", owner.Metadata!.IdempotencyKey);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    [Theory]
    [InlineData("companyId")]
    [InlineData("currentVersion")]
    [InlineData("receipt")]
    public async Task Mixed_membership_rejects_foreign_or_unknown_transport_fields_before_owner(string name)
    {
        var owner = new MixedHttpOwner(); using var host = MixedHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post(MixedMembershipPath(), "SENTETIK-invalid",
            MixedMembershipBody()[..^1] + $",\"{name}\":\"SENTETIK\"}}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(owner.Membership); AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData("query", 400)]
    [InlineData("key", 400)]
    [InlineData("content", 415)]
    public async Task Mixed_membership_requires_native_metadata_json_and_no_query_override(string boundary, int status)
    {
        var owner = new MixedHttpOwner(); using var host = MixedHost(owner); using var client = CreateClient(host);
        using var request = Post(MixedMembershipPath() + (boundary == "query" ? "?companyId=foreign" : ""),
            "SENTETIK-admission", MixedMembershipBody());
        if (boundary == "key") request.Headers.Remove(PurchasingApiV1.IdempotencyHeader);
        if (boundary == "content") request.Content = new StringContent(MixedMembershipBody(), System.Text.Encoding.UTF8, "text/plain");
        using var response = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode)status, response.StatusCode); Assert.Null(owner.Membership); AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(409)] [InlineData(503)]
    public async Task Mixed_owner_failure_releases_no_private_native_facts_or_partial_success(int status)
    {
        var owner = new MixedHttpOwner { Failure = status }; using var host = MixedHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post(MixedMembershipPath(), "SENTETIK-refused", MixedMembershipBody()));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.DoesNotContain("SENTETIK-private", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mixed_preview_releases_the_owner_projection_with_exact_read_precondition_and_no_command_metadata()
    {
        var owner = new MixedHttpOwner(); using var host = MixedHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/mixed-approval-preview?expectedVersion=9007199254740993");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
        Assert.Equal((PurchaseOrderId, "9007199254740993"), (owner.OrderId, owner.ReadVersion));
        Assert.Null(owner.Membership); Assert.Null(owner.Metadata);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "companyId", "parent", "services" }, body.EnumerateObject().Select(x => x.Name));
        Assert.Equal(CompanyA, body.GetProperty("companyId").GetGuid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?expectedVersion=1&companyId=foreign")]
    [InlineData("?expectedVersion=1&expectedVersion=2")]
    [InlineData("?expectedVersion=01")]
    [InlineData("?expectedVersion=9223372036854775808")]
    public async Task Mixed_preview_rejects_missing_ambiguous_or_noncanonical_version_before_owner(string query)
    {
        var owner = new MixedHttpOwner(); using var host = MixedHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/mixed-approval-preview{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(owner.ReadVersion); AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData(403)] [InlineData(503)]
    public async Task Mixed_preview_denied_or_unavailable_is_not_an_empty_success(int status)
    {
        var owner = new MixedHttpOwner { Failure = status }; using var host = MixedHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/mixed-approval-preview?expectedVersion=2");
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response);
        Assert.DoesNotContain("services", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Mixed_intent_closed_readers_preserve_legacy_null_wire_and_reject_nested_duplicates()
    {
        var plain = PurchaseMixedOrderJsonV1.ReadApprove("{\"expectedVersion\":\"2\"}");
        var explicitNull = PurchaseMixedOrderJsonV1.ReadApprove("{\"expectedVersion\":\"2\",\"mixedServices\":null}");
        Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, PurchaseOrderId, plain),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, PurchaseOrderId, explicitNull));
        Assert.DoesNotContain("mixedServices", JsonSerializer.Serialize(explicitNull, PurchasingJson.Options));
        var selected = $$"""[{"serviceOrderId":"{{AcceptanceId:D}}","expectedVersion":"3","lineIds":["{{PurchaseOrderLineId:D}}"]}]""";
        Assert.Single(PurchaseMixedOrderJsonV1.ReadApprove($$"""{"expectedVersion":"2","mixedServices":{{selected}}}""").MixedServices!);
        Assert.Throws<JsonException>(() => PurchaseMixedOrderJsonV1.ReadApprove(
            $$"""{"expectedVersion":"2","mixedServices":{{selected.Replace("\"expectedVersion\":\"3\"", "\"expectedVersion\":\"3\",\"expectedVersion\":\"4\"", StringComparison.Ordinal)}}}"""));
        var revision = SupplierRevisionBody()[..^1] + ",\"mixedServices\":" + selected + "}";
        Assert.Single(PurchaseMixedOrderJsonV1.ReadSupplierRevision(revision).MixedServices!);
        Assert.Throws<JsonException>(() => PurchaseMixedOrderJsonV1.ReadSupplierRevision(revision[..^1] + ",\"receipt\":{}}"));
    }

    [Fact]
    public async Task Linked_supplier_Draft_price_route_calls_only_the_existing_narrow_owner_command()
    {
        var owner = new ServiceOrderHttpOwner(); using var host = ServiceOrderHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/service-orders/{PurchaseOrderId:D}/supplier-revision-prices",
            "SENTETIK-linked-price", $$"""{"expectedVersion":"7","lines":[{"lineId":"{{PurchaseOrderLineId:D}}","unitPrice":"0"}],"reason":"SENTETIK tekrar giriş"}"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
        Assert.Null(owner.PriceRequest); Assert.Null(owner.Request);
        Assert.Equal("7", owner.SupplierRevisionPriceRequest!.ExpectedVersion);
        Assert.Equal("0", Assert.Single(owner.SupplierRevisionPriceRequest.Lines).UnitPrice);
        Assert.Equal("SENTETIK-linked-price", owner.Metadata!.IdempotencyKey);
    }

    private static string MixedMembershipPath() => $"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/service-memberships";
    private static string MixedMembershipBody() => $$"""{"expectedVersion":"9007199254740993","services":[{"serviceOrderId":"{{AcceptanceId:D}}","expectedVersion":"9223372036854775807","lineIds":["{{PurchaseOrderLineId:D}}"]}]}""";
    private WebApplicationFactory<Program> MixedHost(MixedHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPurchaseMixedOrderMembershipServiceV1>();
            services.RemoveAll<IPurchaseMixedOrderApprovalPreviewV1>();
            services.AddScoped<IPurchaseMixedOrderMembershipServiceV1>(_ => owner);
            services.AddScoped<IPurchaseMixedOrderApprovalPreviewV1>(_ => owner);
        }));
    private sealed class MixedHttpOwner : IPurchaseMixedOrderMembershipServiceV1, IPurchaseMixedOrderApprovalPreviewV1
    {
        public int? Failure { get; init; }
        public bool Replay { get; init; }
        public Guid OrderId { get; private set; }
        public string? ReadVersion { get; private set; }
        public BindPurchaseOrderServicesRequest? Membership { get; private set; }
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public Task<PurchaseOrderResponse> BindServicesAsync(Guid id, BindPurchaseOrderServicesRequest request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            OrderId = id; Membership = request; Metadata = metadata; Refuse();
            return Task.FromResult(new PurchaseOrderResponse(OrderDetail(id), Replay, "SENTETIK-original"));
        }
        public Task<PurchaseMixedOrderApprovalPreviewV1> ReadMixedOrderApprovalPreviewAsync(Guid id, string expectedVersion,
            CancellationToken cancellationToken = default)
        {
            OrderId = id; ReadVersion = expectedVersion; Refuse();
            return Task.FromResult(new PurchaseMixedOrderApprovalPreviewV1(CompanyA, OrderDetail(id), []));
        }
        private void Refuse()
        {
            if (Failure is { } status) throw new PurchasingServiceException(status,
                status == 409 ? PurchasingProblemCodesV1.VersionConflict : "SENTETIK-private");
        }
    }
}
