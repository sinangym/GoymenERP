using GoymenERP.Api.FinanceRuntime;
using GoymenERP.Api.SettingsRuntime;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Outbox;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Outbox;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.Shell;

/// <summary>W8 owner composition. Registration creates neither records nor grants and does not enable dispatch.</summary>
public static class TrainCompletionHostComposition
{
    public static IServiceCollection AddTrainCompletionOwnerBindings(this IServiceCollection services)
    {
        services.AddFinanceCreditLenderOwnerRead();
        services.AddFinanceCashExchangeSources();
        services.AddFinanceCashExchangeBookValuesV1();
        services.AddSettingsFinancePolicyRuntime();
        services.AddPurchaseInvoiceFinanceAdmissionHost();
        services.AddPurchaseServiceOrderHostV1();
        services.AddInventoryTransferPolicyHostPorts();
        services.AddFinancePartyBalanceTransfersV1();
        services.AddAccountingInvoiceLineCogsRuntime();
        services.AddScoped<IAccountingCogsAccountResolverV1, AccountingCogsAccountResolverV1>();
        services.AddScoped<IAccountingInventoryCostSourceReaderV1, AccountingInventoryCostSourceReaderV1>();
        services.AddScoped<IManufacturingAttributionLineageReaderV1, ManufacturingAttributionLineageReaderV1>();
        services.AddScoped<IPurchaseFirstCostLineageReaderV1, PurchaseFirstCostLineageReaderV1>();
        services.AddScoped<IPostedPurchaseShipmentCostReaderV1, PurchasePostedCostAttributionStoreV1>();
        services.AddScoped<IAccountingInventoryCostStoreV1, AccountingInventoryCostStoreV1>();
        services.AddScoped<AccountingInventoryCostServiceV1>();
        services.AddWorkInboxConsumer<AccountingInventoryCostInboxConsumerV1>(AccountingInventoryCostInboxV1.ConsumerName,
            AccountingInventoryCostInboxV1.Subscriptions.ToArray());
        services.AddInventoryPurchaseInvoiceCostsV1();
        services.AddScoped<IInventoryFirstCostPrerequisitesV1, InventoryFirstCostPrerequisitesSourceV1>();
        services.AddScoped<IPurchaseSupplierReturnInvoiceCostReadPortV1, PurchaseSupplierReturnInvoiceCostReadPort>();
        services.AddScoped<ISalesMarginCostReadPortV1, InventorySalesMarginCostReadPortV1>();
        services.AddScoped<ISalesFinalizedShipmentClaimReadPortV1, SalesFinalizedShipmentClaimReadPortV1>();
        services.AddScoped<IPurchasingPartyAccountPortV2>(provider =>
            (IPurchasingPartyAccountPortV2)provider.GetRequiredService<IPurchasingPartyAccountPortV1>());
        services.AddProductPurchaseUnitConversion(provider => provider.GetRequiredService<ProductRuntimeOptions>().IsEnabled);
        services.AddScoped<IInventoryProductUnitConversionSourceV1, ProductOwnerInventoryUnitConversionSource>();
        services.AddScoped<IManufacturingPaintSelectionSourceV1, ManufacturingPaintSelectionSourceV1>();
        services.AddScoped<PurchaseReceiptAdjustmentParticipantFactoryV1>(provider => (connection, transaction, company, token) =>
            PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, company,
                provider.GetRequiredService<IAuditEventCollector>(), provider.GetRequiredService<IAuditTransactionWriterV1>(), token));
        services.AddScoped<PurchaseReceiptAdjustmentBatchParticipantFactoryV1>(provider => (connection, transaction, company, token) =>
            PurchaseReceiptAdjustmentParticipant.EnlistBatchAsync(connection, transaction, company,
                provider.GetRequiredService<IAuditEventCollector>(), provider.GetRequiredService<IAuditTransactionWriterV1>(), token));
        services.AddScoped<PurchaseReceiptReversalHost>();
        services.AddScoped<IPurchaseReceiptReversalTransactionV1>(provider => provider.GetRequiredService<PurchaseReceiptReversalHost>());
        services.AddScoped<IPurchaseReceiptReversalBatchTransactionV1>(provider => provider.GetRequiredService<PurchaseReceiptReversalHost>());

        services.AddWorkOutboxEventSource<InventoryOutboxEventSource>(InventoryOutboxContractV1.SourceName,
            new(InventoryFutureAllocationReleasedOutboxV1.EventType, 1),
            new(InventoryPurchaseInvoiceCostDifferenceV1.EventType, 1),
            new(InventoryOutboxContractV1.ManufacturingCostDifferenceProposed, InventoryOutboxContractV1.ManufacturingCostDifferenceSchemaVersion));
        services.AddWorkOutboxEventSource<PurchasingOutboxEventSource>(AccountingPurchaseInvoiceFinalizedInboxV1.SourceName,
            new(PurchasingOrderLineRevivedEventV1.EventType, 1),
            new(AccountingPurchaseInvoiceFinalizedInboxV1.EventType, AccountingPurchaseInvoiceFinalizedInboxV1.Version));
        services.AddWorkOutboxEventSource<FinanceCreditOutboxEventSourceV1>(CreditMovementProposalInboxV1.SourceName,
            new WorkOutboxEventTypeV1(CreditMovementProposalInboxV1.EventType, 1),
            new(FinanceFinancialMovementEventsV1.ExpensePaymentRecorded, FinanceFinancialMovementEventsV1.SchemaVersion),
            new(FinanceFinancialMovementEventsV1.CashTransferRecorded, FinanceFinancialMovementEventsV1.SchemaVersion),
            new(FinanceFinancialMovementEventsV1.CashExchangeRecorded, FinanceFinancialMovementEventsV1.SchemaVersion),
            new(FinanceFinancialMovementEventsV1.CashExchangeBookValueApproved, FinanceFinancialMovementEventsV1.SchemaVersion));
        services.AddWorkOutboxEventSource<SalesOutboxEventSourceV1>(SalesOutboxContractV1.SourceName,
            SalesOutboxContractV1.Publishes.ToArray());
        services.AddWorkNotificationHostV1(InventoryOutboxContractV1.SourceName, AccountingPurchaseInvoiceFinalizedInboxV1.SourceName);
        services.AddWorkInboxConsumer<InventoryPurchaseInvoiceCostInboxConsumerV1>(InventoryPurchaseInvoiceCostInboxV1.ConsumerName,
            InventoryPurchaseInvoiceCostInboxV1.Subscriptions.ToArray());
        services.AddWorkInboxConsumer<ManufacturingCostDifferenceInboxConsumerV1>(ManufacturingCostDifferenceInboxV1.ConsumerName,
            ManufacturingCostDifferenceInboxV1.Subscriptions.ToArray());
        services.AddWorkInboxConsumer<CreditMovementProposalInboxConsumerV1>(CreditMovementProposalInboxV1.ConsumerName,
            CreditMovementProposalInboxV1.Subscriptions.ToArray());
        services.AddWorkInboxConsumer<AccountingPurchaseInvoiceFinalizedInboxConsumer>(AccountingPurchaseInvoiceFinalizedInboxV1.ConsumerName,
            AccountingPurchaseInvoiceFinalizedInboxV1.Subscriptions.ToArray());
        services.AddWorkInboxConsumer<AccountingSalesInvoiceFinalizedInboxConsumer>(AccountingSalesInvoiceFinalizedInboxV1.ConsumerName,
            AccountingSalesInvoiceFinalizedInboxV1.Subscriptions.ToArray());
        services.AddWorkInboxConsumer<AccountingSalesInvoiceCancelledInboxConsumerV1>(AccountingSalesInvoiceCancelledInboxV1.ConsumerName,
            AccountingSalesInvoiceCancelledInboxV1.Subscriptions.ToArray());
        services.AddWorkInboxConsumer<FinanceJournalSourceInboxConsumerV1>(FinanceJournalSourceInboxV1.ConsumerName,
            FinanceJournalSourceInboxV1.Subscriptions.ToArray());
        return services;
    }
}
