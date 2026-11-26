using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderOwnerAxesHostTests
{
    [Theory]
    [InlineData("historical-cancelled")]
    [InlineData("missing-invoice-history")]
    public async Task Settled_active_replacement_does_not_hide_old_cancelled_invoice_without_Finance_reversal_proof(string fault)
    {
        // The native history metadata represents old cancelled A; the current claim represents settled replacement B.
        // This is consumer composition evidence. The SQL owner test proves metadata from actual official cancellation.
        var f = new Fixture { Fault = fault };
        var answer = await f.Read();
        Assert.True(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.False(answer.FinancialClosure.Complete);
        Assert.Equal(SalesDraftStatusAxisStatesV1.Unreadable, answer.FinancialClosure.State);
        Assert.Equal(0, f.FinanceCalls);
    }

    // Native Sales invoice lifecycle/calculators; the downstream owner answers are explicit SYNTHETIC composition evidence.
    [Fact]
    public async Task Subcontract_service_fee_net_kilograms_and_customer_owned_physical_kilograms_are_independent()
    {
        var f = SubcontractFixture();
        var answer = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = true }, f.Fence);
        Assert.True(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.True(answer.FinancialClosure.Complete);
        Assert.Equal(12m, f.SubcontractInvoice!.Lines.Single().Input.Quantity);
        Assert.Equal("10", f.Query.Lines.Single().ShippedQuantity);
        Assert.Equal(0, f.PhysicalReads); // The company-owned shipment COST port is never asked.
        Assert.Equal([f.Invoice], f.FinanceIds);
    }

    [Theory]
    [InlineData("foreign-customer")]
    [InlineData("wrong-material")]
    [InlineData("wrong-service")]
    [InlineData("missing-movement")]
    [InlineData("released-history")]
    [InlineData("fee-as-physical")]
    [InlineData("missing-weighing")]
    [InlineData("changed-subcontract")]
    [InlineData("wrong-invoice-quantity")]
    [InlineData("wrong-admission")]
    [InlineData("wrong-invoice-version")]
    [InlineData("unknown-finance")]
    [InlineData("cancelled-history")]
    public async Task Subcontract_incomplete_or_mismatched_owner_proof_never_completes_all_axes(string fault)
    {
        var f = SubcontractFixture(fault);
        var result = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = true }, f.Fence);
        Assert.False(result!.Source.Complete && result.Invoicing.Complete && result.FinancialClosure.Complete);
        Assert.Equal(0, f.PhysicalReads);
    }

    [Theory]
    [InlineData("subcontract-hold")]
    [InlineData("production")]
    [InlineData("purchasing")]
    public async Task Subcontract_only_this_orders_open_source_share_blocks_source_axis(string openSource)
    {
        var f = SubcontractFixture(openSource: openSource);
        var result = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = true }, f.Fence);
        Assert.False(result!.Source.Complete);
        Assert.True(result.Invoicing.Complete);
        Assert.True(result.FinancialClosure.Complete);
    }

    [Fact]
    public async Task Subcontract_unsettled_installment_keeps_financial_axis_open_without_requesting_bank_match()
    {
        var f = SubcontractFixture(unsettled: true);
        var result = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = true }, f.Fence);
        Assert.True(result!.Source.Complete);
        Assert.True(result.Invoicing.Complete);
        Assert.False(result.FinancialClosure.Complete);
    }

    private static Fixture SubcontractFixture(string? fault = null, string? openSource = null, bool unsettled = false)
    {
        var f = new Fixture { ComposeSubcontract = true, Fault = fault, OpenSource = openSource, Unsettled = unsettled };
        var now = DateTimeOffset.UtcNow; var day = DateOnly.FromDateTime(now.UtcDateTime);
        var currency = SettingsCurrencyIdentitiesV1.TurkishLira;
        var tax = new SalesLineTaxSnapshot(f.Product, day, "Normal", "Service", "Resolved", null, "Record", Guid.NewGuid(), 1,
            "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        var invoice = SalesSubcontractServiceInvoice.Create(f.Invoice, f.CompanyId, "SENTETIK-SUB-CLOSE",
            new(f.Party, f.Account, currency, Guid.NewGuid(), "1", 2, day),
            new(f.Order, f.Instruction, f.Weighing, 1, 1, "12"),
            [new(f.SubcontractInvoiceLine, f.Line, 1, f.Product, f.Unit, 12m, "12", new CommercialDraftFields("2.50", f.Unit, null), tax)],
            12m, "SENTETIK-maker", now);
        var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "100")]);
        invoice.SetDuePlan(terms, SalesDueDateCalculator.Compute(terms, day, null).DueDates);
        invoice.Submit(1, "SENTETIK-submitter", now);
        invoice.Approve(2, "SENTETIK-checker", now);
        invoice.RecordMatch(3, "SENTETIK-matcher", new("SENTETIK-GIB", Guid.NewGuid().ToString("D"), f.Party, currency,
            invoice.Totals.Payable.Amount, invoice.Totals.Payable.Amount, invoice.Totals.Vat.Amount, invoice.Totals.Withholding.Amount,
            day, new(f.CompanyId, Guid.NewGuid(), "approved-import", "1", now, null, null, null, Guid.NewGuid(), new string('a', 64), false)),
            currency, null, 0.05m, false, null, now);
        invoice.Finalize(4, "SENTETIK-finalizer", Guid.NewGuid, now);
        f.SubcontractInvoice = invoice;
        return f;
    }

    private sealed partial class Fixture : ILogisticsSubcontractOrderDeliveryReadPortV1,
        IInventorySubcontractOrderDeliveryProofReadPortV1, ISalesSubcontractServiceInvoiceStore, ISalesSubcontractInvoiceListStoreV1
    {
        public bool ComposeSubcontract { get; init; }
        public Guid SubcontractInvoiceLine => invoiceLine;
        public SalesSubcontractServiceInvoice? SubcontractInvoice;
        public readonly Guid Weighing = Guid.NewGuid(), Material = Guid.NewGuid(), Manufacturing = Guid.NewGuid(), Plan = Guid.NewGuid(),
            Output = Guid.NewGuid(), Label = Guid.NewGuid(), Allocation = Guid.NewGuid();
        private int subcontractReads;
        Task<LogisticsSubcontractOrderDeliveryReadV1> ILogisticsSubcontractOrderDeliveryReadPortV1.ReadAsync(Guid companyId, Guid orderId,
            Func<CancellationToken, Task> fence, CancellationToken token)
        {
            subcontractReads++;
            var source = new LogisticsSubcontractSourceV1(CompanyId, Party, Account, Order, Line, 1, 1, Material, "1", Product,
                Manufacturing, 1, Plan, 1, 1, Output, Label, "10", "manufacturing:1", "SENTETIK-label", "SENTETIK-order", "SRV", "SENTETIK-service");
            var weighing = new LogisticsSubcontractDeliveryNetWeightV1("12", "kg", LogisticsNetWeightMeasurementKindV1.ManualWeighbridge,
                "TareFirst", "10", "2", 1, 1, DateTimeOffset.UnixEpoch,
                [new(InstructionLine, Order, Line, 1, Material, Unit, "10", "10", "10", "10", Product)],
                [new(shipment, InstructionLine, "10", DateTimeOffset.UnixEpoch)], true);
            return Task.FromResult(new LogisticsSubcontractOrderDeliveryReadV1(CompanyId, Order, true,
                [new(Instruction, 1, false, Weighing, Fault == "missing-weighing" ? null : weighing,
                    [new(InstructionLine, Unit, 10m, 10m, 10m, source)], [new(shipment, InstructionLine, "10", DateTimeOffset.UnixEpoch)])],
                Fault == "changed-subcontract" ? $"delivery:{subcontractReads}" : "delivery:1"));
        }
        Task<InventorySubcontractOrderDeliveryProofReadV1> IInventorySubcontractOrderDeliveryProofReadPortV1.ReadAsync(Guid companyId,
            Guid orderId, IReadOnlyList<Guid> ids, Func<CancellationToken, Task> fence, CancellationToken token)
        {
            var source = new InventorySubcontractDeliverySourceV1(CompanyId, Fault == "foreign-customer" ? Guid.NewGuid() : Party, Account,
                Order, Line, 1, 1, Fault == "wrong-material" ? Guid.NewGuid() : Material, "1", Manufacturing, 1, Plan, 1, Output, Label,
                "10", InventorySubcontractDeliveryContractV1.SourceVersion, 1, Fault == "wrong-service" ? Guid.NewGuid() : Product);
            return Task.FromResult(new InventorySubcontractOrderDeliveryProofReadV1(CompanyId, Order, Fault != "released-history",
                OpenSource == "subcontract-hold", [new(shipment, Instruction, InstructionLine,
                    Fault == "missing-movement" ? Guid.Empty : Movement, Allocation, Fault == "fee-as-physical" ? 12m : 10m, source)], "inventory:1"));
        }
        private SalesOrderInvoicedQuantityReadV1 SubcontractQuantities() => new(CompanyId, Order,
            SalesOrderInvoicedQuantityReadOutcomeV1.Available,
            SubcontractInvoice!.Lines.Select(x => new SalesOrderInvoicedQuantityLineV1(Invoice, x.Id, Line, Unit,
                Fault == "wrong-invoice-quantity" ? 10m : x.Input.Quantity, "Subcontract", "subcontract-quantity:1")).ToArray(), "subcontract-quantity:1", CancelledProofs is { Length: > 0 }, CancelledProofs);
        public Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceByWeighingAsync(Guid order, Guid instruction, Guid weighing,
            CancellationToken token) => Task.FromResult(SubcontractInvoice);
        public Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceAsync(Guid id, CancellationToken token) => Task.FromResult(SubcontractInvoice);
        public Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid order, int size, string? cursor, CancellationToken token) =>
            Task.FromResult(new SalesSubcontractInvoiceListV1(Order,
                CancelledProofs is { Length: > 0 }
                    ? [new(Invoice, "SENTETIK", "Finalized", "5", default, default), .. CancelledProofs.Select(x => new SalesSubcontractInvoiceListItemV1(x.InvoiceId, "SENTETIK-old", "OfficiallyCancelled", x.CancellationInvoiceVersion.ToString(), default, default))]
                    : Fault == "cancelled-history"
                    ? [new(Invoice, "SENTETIK", "Finalized", "5", default, default), new(OtherInvoice, "SENTETIK-old", "OfficiallyCancelled", "6", default, default)]
                    : [new(Invoice, "SENTETIK", "Finalized", "5", default, default)], null));
        public Task<SalesSubcontractServiceInvoiceStoreResult> StoreSubcontractServiceInvoiceAsync(SalesDraftWriteEnvelope e, string h,
            Func<string, SalesSubcontractServiceInvoice> build, CancellationToken token) => throw new NotSupportedException();
        public Task<SalesSubcontractServiceInvoiceStoreResult?> FindSubcontractServiceInvoiceReplayAsync(SalesDraftWriteEnvelope e, string h,
            CancellationToken token) => throw new NotSupportedException();
    }
}
