using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LogisticsTransferWorkflowEndpointContractTests
{
    [Theory]
    [InlineData("GetLogisticsTransferOutgoingDocumentByTransferV1", "logistics.transfer-outgoing-documents.read", false)]
    [InlineData("CreateLogisticsTransferOutgoingDocumentV1", "logistics.dispatch-notes.create", true)]
    [InlineData("FinalizeLogisticsTransferOutgoingDocumentV1", "logistics.dispatch-notes.approve", true)]
    [InlineData("CancelLogisticsTransferOutgoingDocumentV1", "logistics.dispatch-notes.cancel", true)]
    [InlineData("ListLogisticsTransferDepartureCandidatesV1", "logistics.transfer-departures.record", false)]
    [InlineData("GetLogisticsTransferDepartureV1", "logistics.transfer-departures.record", false)]
    [InlineData("RecordLogisticsTransferDepartureV1", "logistics.transfer-departures.record", true)]
    public async Task Transfer_workflow_routes_have_the_exact_owner_permission_company_fence_no_store_and_command_replay_metadata(
        string name, string permission, bool command)
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient(); using var health = await client.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();
        var route = factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
            .Single(x => x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.NotNull(route.Metadata.GetMetadata<RequiresCompanyContextAttribute>());
        Assert.NotNull(route.Metadata.GetMetadata<PrivateNoStoreOnAuthorizationFailureMetadata>());
        Assert.Contains(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), x => x.Policy == permission);
        Assert.Equal(command, route.Metadata.Any(x => x.GetType().Name == "IdempotentCommandMetadata"));
        var statuses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(x => x.StatusCode).ToHashSet();
        foreach (var status in new[] { 400, 401, 403, 503 }) Assert.Contains(status, statuses);
        if (command) Assert.Contains(409, statuses);
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"1\",\"kind\":2,\"occurredAtUtc\":\"2026-11-01T00:00:00Z\",\"reason\":\"SENTETIK\",\"companyId\":\"foreign\"}")]
    [InlineData("{\"expectedVersion\":\"1\",\"kind\":1,\"occurredAtUtc\":\"2026-11-01T00:00:00Z\",\"reason\":\"SENTETIK\",\"weighing\":{\"direction\":1,\"firstKg\":\"1000\",\"secondKg\":\"1005\",\"netKg\":\"5\"}}")]
    public void Transfer_departure_body_rejects_client_company_and_client_computed_net(string body) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecordTransferDepartureV1>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public void Outgoing_document_body_rejects_extra_company_scope_and_preserves_omitted_association_for_legacy_replay()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var body = "{\"documentId\":\"11111111-1111-4111-8111-111111111111\",\"transferId\":\"22222222-2222-4222-8222-222222222222\",\"orderedTransferLineIds\":[],\"companyId\":\"foreign\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateTransferOutgoingDocumentV1>(body, options));
        var legacy = JsonSerializer.Deserialize<CreateTransferOutgoingDocumentV1>(body.Replace(",\"companyId\":\"foreign\"", "", StringComparison.Ordinal), options)!;
        Assert.Null(legacy.EDispatchAssociation);
        Assert.DoesNotContain("eDispatchAssociation", JsonSerializer.Serialize(legacy, options), StringComparison.Ordinal);
    }
}
