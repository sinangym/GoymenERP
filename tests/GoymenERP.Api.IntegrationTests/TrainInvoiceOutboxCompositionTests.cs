using GoymenERP.Api.Shell;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed class TrainInvoiceOutboxCompositionTests
{
    [Fact]
    public void The_host_binds_actual_Sales_events_to_the_Accounting_consumer_without_starting_dispatch()
    {
        var services = new ServiceCollection();
        services.AddTrainCompletionOwnerBindings();
        services.AddWorkOutboxDispatcher(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        // Runs the real cross-registration validator, not a test substitute.
        Assert.NotNull(provider.GetRequiredService<IOptions<WorkOutboxRegistrationOptions>>().Value);
        var registry = provider.GetRequiredService<WorkOutboxRegistry>();
        var source = Assert.Single(registry.Sources, item => item.SourceName == AccountingSalesInvoiceFinalizedInboxV1.SourceName);
        Assert.Equal(typeof(SalesOutboxEventSourceV1), source.ImplementationType);
        var consumer = Assert.Single(registry.Consumers, item => item.ConsumerName == AccountingSalesInvoiceFinalizedInboxV1.ConsumerName);
        Assert.Equal(typeof(AccountingSalesInvoiceFinalizedInboxConsumer), consumer.ImplementationType);
        foreach (var subscription in consumer.Subscriptions)
            Assert.Contains(source.Publishes, item => item.EventType == subscription.EventType && item.Version == subscription.Version);
        var accountingCosts = Assert.Single(registry.Consumers,
            item => item.ConsumerName == AccountingInventoryCostInboxV1.ConsumerName);
        Assert.Equal(typeof(AccountingInventoryCostInboxConsumerV1), accountingCosts.ImplementationType);
        Assert.Equal(2, accountingCosts.Subscriptions.Count);
        foreach (var subscription in accountingCosts.Subscriptions)
        {
            var owner = Assert.Single(registry.Sources, item => item.SourceName == subscription.SourceName);
            Assert.Contains(owner.Publishes, item => item.EventType == subscription.EventType && item.Version == subscription.Version);
        }
        Assert.Equal(typeof(AccountingInventoryCostSourceReaderV1),
            Assert.Single(services, item => item.ServiceType == typeof(IAccountingInventoryCostSourceReaderV1)).ImplementationType);
        Assert.Equal(typeof(AccountingInventoryCostStoreV1),
            Assert.Single(services, item => item.ServiceType == typeof(IAccountingInventoryCostStoreV1)).ImplementationType);
        var purchaseCostConsumer = Assert.Single(registry.Consumers,
            item => item.ConsumerName == InventoryPurchaseInvoiceCostInboxV1.ConsumerName);
        Assert.Equal(typeof(InventoryPurchaseInvoiceCostInboxConsumerV1), purchaseCostConsumer.ImplementationType);
        var purchasing = Assert.Single(registry.Sources,
            item => item.SourceName == InventoryPurchaseInvoiceCostInboxV1.SourceName);
        foreach (var subscription in purchaseCostConsumer.Subscriptions)
            Assert.Contains(purchasing.Publishes,
                item => item.EventType == subscription.EventType && item.Version == subscription.Version);
        var costStore = Assert.Single(services,
            item => item.ServiceType == typeof(IInventoryPurchaseInvoiceCostStoreV1));
        Assert.Equal(typeof(InventoryPurchaseInvoiceCostStoreV1), costStore.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, costStore.Lifetime);
        var supplierReturnCost = Assert.Single(services,
            item => item.ServiceType == typeof(IPurchaseSupplierReturnInvoiceCostReadPortV1));
        Assert.Equal(typeof(PurchaseSupplierReturnInvoiceCostReadPort), supplierReturnCost.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, supplierReturnCost.Lifetime);
        var inventory = Assert.Single(registry.Sources,
            item => item.SourceName == InventoryOutboxContractV1.SourceName);
        Assert.Contains(inventory.Publishes,
            item => item.EventType == InventoryPurchaseInvoiceCostDifferenceV1.EventType && item.Version == 1);
        var finance = Assert.Single(registry.Sources, item => item.SourceName == FinanceFinancialMovementEventsV1.SourceName);
        Assert.Equal(typeof(FinanceCreditOutboxEventSourceV1), finance.ImplementationType);
        var financeConsumer = Assert.Single(registry.Consumers, item => item.ConsumerName == FinanceJournalSourceInboxV1.ConsumerName);
        Assert.Equal(typeof(FinanceJournalSourceInboxConsumerV1), financeConsumer.ImplementationType);
        Assert.Equal(2, financeConsumer.Subscriptions.Count);
        foreach (var subscription in financeConsumer.Subscriptions)
            Assert.Contains(finance.Publishes, item => item.EventType == subscription.EventType && item.Version == subscription.Version);
        Assert.Contains(finance.Publishes, item => item.EventType == CreditMovementProposalInboxV1.EventType);
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(WorkOutboxDispatcher));
    }
}
