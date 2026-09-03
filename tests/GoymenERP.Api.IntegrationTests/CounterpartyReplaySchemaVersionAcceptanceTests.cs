using System.Reflection;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyReplaySchemaVersionAcceptanceTests
{
    [Fact]
    public void UnknownDurableSchemaVersion_IsRejectedBeforeDeserialization()
    {
        AssertUnknownSchemaRejectedBeforeDeserialization(typeof(CounterpartyCommandService));
        AssertUnknownSchemaRejectedBeforeDeserialization(
            typeof(CounterpartyCompanyAccountLinkService));
    }

    [Fact]
    public void SupportedDurableSchemaVersion_ReachesDeserializer()
    {
        AssertSupportedSchemaReachesDeserializer(typeof(CounterpartyCommandService));
        AssertSupportedSchemaReachesDeserializer(
            typeof(CounterpartyCompanyAccountLinkService));
    }

    private static void AssertUnknownSchemaRejectedBeforeDeserialization(Type serviceType)
    {
        var invocation = InvokeResolver(serviceType, schemaVersion: 2);
        var exception = Assert.IsType<InvalidOperationException>(invocation.InnerException);
        Assert.Contains("bilinmeyen şema sürümü", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertSupportedSchemaReachesDeserializer(Type serviceType)
    {
        var invocation = InvokeResolver(serviceType, schemaVersion: 1);
        Assert.IsType<JsonException>(invocation.InnerException);
    }

    private static TargetInvocationException InvokeResolver(Type serviceType, int schemaVersion)
    {
        var requestHash = new string('A', 64);
        var actorId = "schema-version-test-actor";
        var execution = CommandExecution.Complete(new CommandExecutionData(
            Guid.NewGuid(),
            TestCompanyIds.CompanyA,
            "schema-version-test-command.v1",
            new string('B', 64),
            requestHash,
            actorId,
            "Created",
            StatusCodes.Status201Created,
            "[]",
            "schema-version-original-correlation",
            DateTimeOffset.UtcNow,
            SchemaVersion: schemaVersion));
        var metadata = new CounterpartyCommandMetadata(
            TestCompanyIds.CompanyA,
            actorId,
            "schema-version-current-correlation",
            CausationId: null,
            SourceChannel: "Web",
            ClientIpAddress: null,
            UserAgent: null,
            IdempotencyKey: "schema-version-test-key");
        var resolver = serviceType.GetMethod(
            "ResolveExistingExecution",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolver);

        return Assert.Throws<TargetInvocationException>(() =>
            resolver.Invoke(null, [execution, requestHash, metadata]));
    }
}
