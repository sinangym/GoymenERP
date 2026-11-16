using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.SalesRuntime;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-SC33 (LOJ-D19, SAT-D285): the host registers the Sales risk-exception usage consumer for the Logistics definite-shipment
/// source; the pair passes the dispatcher's start validation, and registering never enables the dispatcher.
/// </summary>
public sealed class SalesLogisticsShipmentInboxRegistrationTests
{
    [Fact]
    public void The_sales_usage_consumer_is_registered_for_the_logistics_source_and_validates_without_enabling_the_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddWorkOutboxDispatcher(new ConfigurationBuilder().Build());
        services.AddLogisticsDefiniteShipmentOutbox();
        services.AddSalesLogisticsShipmentInboxConsumer();

        var registry = WorkOutboxRegistry.GetOrAdd(services);
        Assert.Empty(registry.Validate());
        var consumer = Assert.Single(registry.Consumers, c => c.ConsumerName == SalesLogisticsShipmentInboxV1.ConsumerName);
        Assert.Equal(typeof(SalesLogisticsShipmentInboxConsumer), consumer.ImplementationType);
        Assert.Equal(SalesLogisticsShipmentInboxV1.Subscriptions, consumer.Subscriptions);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(WorkOutboxDispatcher));
    }

    [Fact]
    public void The_sales_usage_consumer_alone_fails_start_validation_because_its_source_is_unregistered()
    {
        var services = new ServiceCollection();
        services.AddSalesLogisticsShipmentInboxConsumer();

        var errors = WorkOutboxRegistry.GetOrAdd(services).Validate();
        Assert.Equal(SalesLogisticsShipmentInboxV1.Subscriptions.Count, errors.Count);
        Assert.All(errors, e => Assert.Contains("unregistered source 'logistics'", e, StringComparison.Ordinal));
    }
}
