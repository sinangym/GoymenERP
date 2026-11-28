using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Unordered_occurrence_posts_native_v2_and_returns_exact_line_location_and_replay_metadata()
    {
        var owner = new UnorderedHttpOwner { Replay = true }; using var host = UnorderedHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post(PurchasingEndpoints.UnorderedAcceptancesPath, "SENTETIK-occurrence", UnorderedBody()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); AssertPrivateNoStore(response);
        Assert.EndsWith($"/{AcceptanceId:D}/lines/{PurchaseOrderLineId:D}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal("SENTETIK-occurrence", owner.Metadata!.IdempotencyKey); Assert.Equal("0", owner.Request!.Occurrence.ExpectedVersion);
        Assert.Equal(new DateOnly(2026, 11, 28), owner.Request.BusinessDate);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CompanyA, json.GetProperty("record").GetProperty("source").GetProperty("companyId").GetGuid());
        Assert.Equal("SENTETIK beyan", json.GetProperty("record").GetProperty("source").GetProperty("declaration").GetProperty("reason").GetString());
        Assert.Equal(1, owner.Calls);
    }
    [Theory]
    [InlineData("unknown")] [InlineData("duplicate")] [InlineData("nested-unknown")] [InlineData("missing-date")]
    [InlineData("missing-occurrence")] [InlineData("query")] [InlineData("metadata")] [InlineData("media")] [InlineData("media-trailing")] [InlineData("media-case")]
    public async Task Unordered_http_rejects_noncanonical_transport_before_native_owner(string defect)
    {
        var owner = new UnorderedHttpOwner(); using var host = UnorderedHost(owner); using var client = CreateClient(host);
        var body = UnorderedBody();
        if (defect == "unknown") body = body[..^1] + ",\"companyId\":\"00000000-0000-0000-0000-000000000001\"}";
        if (defect == "duplicate") body = body[..^1] + ",\"businessDate\":\"2026-11-29\"}";
        if (defect == "nested-unknown") body = body.Replace("\"expectedVersion\":\"0\"", "\"expectedVersion\":\"0\",\"price\":\"10\"");
        if (defect == "missing-date") body = body.Replace("\"businessDate\":\"2026-11-28\",", "");
        if (defect == "missing-occurrence") body = "{\"businessDate\":\"2026-11-28\"}";
        var media = defect.StartsWith("media", StringComparison.Ordinal);
        var path = defect == "media-case" ? PurchasingEndpoints.UnorderedAcceptancesPath.ToUpperInvariant() : PurchasingEndpoints.UnorderedAcceptancesPath;
        if (defect == "media-trailing") path += "/";
        using var request = Post(path + (defect == "query" ? "?companyId=foreign" : ""), "SENTETIK-shape", body, media ? "text/plain" : "application/json");
        if (defect == "metadata") request.Headers.Remove("Idempotency-Key");
        using var response = await client.SendAsync(request);
        Assert.Equal(media ? HttpStatusCode.UnsupportedMediaType : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, owner.Calls); AssertPrivateNoStore(response);
    }
    [Theory]
    [InlineData(403)] [InlineData(409)] [InlineData(503)]
    public async Task Unordered_native_refusal_is_not_replaced_with_a_created_occurrence(int status)
    {
        var owner = new UnorderedHttpOwner { Refusal = status }; using var host = UnorderedHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post(PurchasingEndpoints.UnorderedAcceptancesPath, "SENTETIK-refused", UnorderedBody()));
        Assert.Equal((HttpStatusCode)status, response.StatusCode); Assert.Null(response.Headers.Location); AssertPrivateNoStore(response);
        Assert.DoesNotContain("SENTETIK-private", await response.Content.ReadAsStringAsync()); Assert.Equal(1, owner.Calls);
    }
    [Theory]
    [InlineData("found", 200)] [InlineData("missing", 404)] [InlineData("foreign", 404)]
    [InlineData("unavailable", 503)] [InlineData("query", 400)]
    public async Task Unordered_line_get_uses_native_source_under_orders_read_and_private_no_store(string mode, int status)
    {
        var source = new UnorderedHttpSource { Mode = mode }; var authority = new UnorderedHttpAuthority();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPurchaseUnorderedAcceptanceSourceV1>(); services.AddSingleton<IPurchaseUnorderedAcceptanceSourceV1>(source);
            services.RemoveAll<IErpAuthorityRevalidationService>(); services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.AddScoped<PurchaseUnorderedAcceptanceReadHostV1>();
        }));
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"{PurchasingEndpoints.UnorderedAcceptancesPath}/{AcceptanceId:D}/lines/{PurchaseOrderLineId:D}" + (mode == "query" ? "?companyId=foreign" : ""));
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response);
        if (mode == "query") { Assert.Equal(0, source.Calls); Assert.Empty(authority.Permissions); }
        else
        {
            Assert.Equal(1, source.Calls); Assert.Equal(CompanyA, source.Company);
            Assert.All(authority.Permissions, p => Assert.Equal(PurchasingPermissionManifestV1.OrdersRead, p)); Assert.Equal(2, authority.Permissions.Count);
        }
        if (mode == "found")
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(AcceptanceId, body.GetProperty("acceptanceId").GetGuid());
            Assert.Equal(PurchaseOrderLineId, body.GetProperty("acceptanceLineId").GetGuid()); Assert.Equal("2", body.GetProperty("acceptedQuantity").GetString());
        }
        else Assert.DoesNotContain("acceptedQuantity", await response.Content.ReadAsStringAsync());
    }
    private sealed class UnorderedHttpAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Permissions.Add(permission); return Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class UnorderedHttpSource : IPurchaseUnorderedAcceptanceSourceV1
    {
        public string Mode { get; init; } = "found"; public int Calls { get; private set; } public Guid Company { get; private set; }
        public Task<PurchaseUnorderedAcceptanceSourceAnswerV1> ReadAsync(Guid companyId, Guid acceptanceId, Guid acceptanceLineId, CancellationToken cancellationToken = default)
        {
            Calls++; Company = companyId;
            var source = new PurchaseUnorderedAcceptanceSourceV1(Mode == "foreign" ? Guid.NewGuid() : companyId, acceptanceId, acceptanceLineId, 1,
                Guid.NewGuid(), Guid.NewGuid(), new PurchaseUnorderedGoodsSubjectV1(ProductId, "7"), UnitId, "2", "2", new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero),
                new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-line"), []);
            return Task.FromResult(new PurchaseUnorderedAcceptanceSourceAnswerV1(Mode == "missing" ? PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound
                : Mode == "unavailable" ? PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable : PurchaseUnorderedAcceptanceSourceOutcomeV1.Found, source));
        }
    }
    private WebApplicationFactory<Program> UnorderedHost(UnorderedHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => { services.RemoveAll<IPurchaseUnorderedAcceptanceServiceV1>(); services.AddSingleton<IPurchaseUnorderedAcceptanceServiceV1>(owner); }));
    private static string UnorderedBody() => $$$"""
        {"businessDate":"2026-11-28","reason":"SENTETIK beyan","occurrence":{"acceptanceId":"{{{AcceptanceId:D}}}","acceptanceLineId":"{{{PurchaseOrderLineId:D}}}","expectedVersion":"0","supplierPartyId":"{{{Guid.NewGuid():D}}}","companyAccountId":"{{{Guid.NewGuid():D}}}","expectedPartyVersion":"2","expectedAccountVersion":"3","subject":{"kind":"Goods","productId":"{{{ProductId:D}}}","productVersion":"7"},"unitId":"{{{UnitId:D}}}","unitProfile":{"profileId":"{{{Guid.NewGuid():D}}}","version":1},"acceptedQuantity":"2","occurredAtUtc":"2026-11-28T00:00:00+00:00","evidence":{"documentId":"{{{Guid.NewGuid():D}}}","documentVersion":1,"sha256":"{{{new string('a',64)}}}","sourceLineId":"SENTETIK-line"},"controlEvidence":[]}}
        """;
    private sealed class UnorderedHttpOwner : IPurchaseUnorderedAcceptanceServiceV1
    {
        public bool Replay { get; init; } public int? Refusal { get; init; } public int Calls { get; private set; }
        public RecordPurchaseUnorderedAcceptanceV2? Request { get; private set; } public PurchasingCommandMetadata? Metadata { get; private set; }
        public Task<PurchaseUnorderedAcceptanceWriteV1> RecordAsync(RecordPurchaseUnorderedAcceptanceV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Legacy recording is not this route.");
        public Task<PurchaseUnorderedAcceptanceWriteV1> RecordOccurrenceAsync(RecordPurchaseUnorderedAcceptanceV2 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++; Request = request; Metadata = metadata;
            if (Refusal is { } status) throw new PurchasingServiceException(status, status == 409 ? PurchasingProblemCodesV1.ConcurrentChange : "service_unavailable", detail: "SENTETIK-private");
            var r = request.Occurrence;
            var source = new PurchaseUnorderedAcceptanceSourceV1(CompanyA, r.AcceptanceId, r.AcceptanceLineId, 1, r.SupplierPartyId, r.CompanyAccountId, r.Subject, r.UnitId, r.AcceptedQuantity, r.AcceptedQuantity, r.OccurredAtUtc, r.Evidence, r.ControlEvidence)
                { Declaration = new(request.BusinessDate, request.Reason) };
            return Task.FromResult(new PurchaseUnorderedAcceptanceWriteV1(new(source, r.ExpectedPartyVersion, r.ExpectedAccountVersion, r.UnitProfile, "SENTETIK-recorder", r.OccurredAtUtc), Replay, "SENTETIK-original"));
        }
    }
}
