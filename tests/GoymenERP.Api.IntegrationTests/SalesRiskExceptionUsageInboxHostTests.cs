using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Logistics.Application.Outbox;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-S10 (LOJ-D19, SAT-D285; plan L-5): the host composition of the Sales usage mirror as the Work inbox consumer of the
/// Logistics definite-shipment source. The Sales subscriptions are exactly the events the Logistics source declares (names and
/// version), the start-up registry validation passes only with the Logistics source registered, and registering the consumer
/// never enables the dispatcher (a human gate). No database is touched.
/// </summary>
public sealed class SalesRiskExceptionUsageInboxHostTests
{
    [Fact]
    public void The_Sales_subscriptions_are_exactly_the_events_the_Logistics_source_declares()
    {
        Assert.Equal(LogisticsDefiniteShipmentOutboxV1.SourceName, SalesLogisticsShipmentInboxV1.SourceName);
        Assert.Equal(LogisticsDefiniteShipmentOutboxV1.RecordedEventType, SalesLogisticsShipmentInboxV1.RecordedEventType);
        Assert.Equal(LogisticsDefiniteShipmentOutboxV1.CancelledEventType, SalesLogisticsShipmentInboxV1.CancelledEventType);
        Assert.Equal(LogisticsDefiniteShipmentOutboxV1.Version, SalesLogisticsShipmentInboxV1.Version);
    }

    [Fact]
    public void The_host_registers_the_consumer_once_after_the_Logistics_source_and_start_up_validation_passes()
    {
        var services = new ServiceCollection();
        services.AddLogisticsDefiniteShipmentOutbox();
        services.AddSalesRiskExceptionUsageInbox();

        var registry = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkOutboxRegistry)).ImplementationInstance;
        var consumer = Assert.Single(Assert.IsType<WorkOutboxRegistry>(registry).Consumers);
        Assert.Equal(SalesLogisticsShipmentInboxV1.ConsumerName, consumer.ConsumerName);
        Assert.Equal(typeof(SalesLogisticsShipmentInboxConsumer), consumer.ImplementationType);
        Assert.Equal<WorkInboxSubscriptionV1>(
            new[]
            {
                new WorkInboxSubscriptionV1(SalesLogisticsShipmentInboxV1.SourceName, SalesLogisticsShipmentInboxV1.RecordedEventType, 1),
                new WorkInboxSubscriptionV1(SalesLogisticsShipmentInboxV1.SourceName, SalesLogisticsShipmentInboxV1.CancelledEventType, 1),
            },
            consumer.Subscriptions);
        Assert.Empty(Assert.IsType<WorkOutboxRegistry>(registry).Validate());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOutboxConsumerV1));
        Assert.Throws<InvalidOperationException>(() => services.AddSalesRiskExceptionUsageInbox());
    }

    [Fact]
    public void Without_the_Logistics_source_the_consumer_fails_host_start_validation()
    {
        var services = new ServiceCollection();
        services.AddSalesRiskExceptionUsageInbox();
        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<WorkOutboxRegistrationOptions>>().Value);
        Assert.Contains(failure.Failures,
            message => message.Contains($"unregistered source '{SalesLogisticsShipmentInboxV1.SourceName}'", StringComparison.Ordinal));
    }

    [Fact]
    public void Registering_the_Sales_consumer_never_enables_the_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogisticsDefiniteShipmentOutbox();
        services.AddSalesRiskExceptionUsageInbox();
        services.AddWorkOutboxDispatcher(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(WorkOutboxDispatcherHostedService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(WorkOutboxDispatcher));
    }
}
