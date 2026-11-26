using GoymenERP.Api.SalesRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Composition tests with explicit synthetic owner evidence; not SQL or real financial acceptance.</summary>
public sealed partial class SalesOrderOwnerAxesHostTests
{
    [Fact]
    public async Task Actual_owner_proofs_complete_the_three_axes_without_cost_or_bank_match_authority()
    {
        var f = new Fixture();
        var answer = await f.Read();
        Assert.True(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.True(answer.FinancialClosure.Complete);
        Assert.True(f.Fences > 20);
        Assert.Equal(2, f.FinanceCalls);
        Assert.Equal(new[] { f.Invoice }, f.FinanceIds);
        Assert.Null(await f.Host().ReadOrderOwnerAxesAsync(f.Order)); // no caller authority on legacy seam
    }

    [Theory]
    [InlineData("purchasing")]
    [InlineData("production")]
    [InlineData("physical-allocation")]
    [InlineData("future-allocation")]
    public async Task Own_open_source_obligation_blocks_only_the_source_axis(string kind)
    {
        var f = new Fixture { OpenSource = kind };
        var answer = await f.Read();
        Assert.False(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.True(answer.FinancialClosure.Complete);
    }

    [Theory]
    [InlineData("foreign-shipment")]
    [InlineData("truncated-shares")]
    [InlineData("unposted-shipment")]
    [InlineData("missing-stock-proof")]
    [InlineData("wrong-stock-source")]
    [InlineData("wrong-kilograms")]
    [InlineData("wrong-invoice-quantity")]
    [InlineData("cancelled-invoice")]
    [InlineData("wrong-admission")]
    [InlineData("wrong-invoice-version")]
    [InlineData("unknown-finance")]
    [InlineData("missing-installment-proof")]
    [InlineData("changed-proof")]
    public async Task Missing_mismatched_or_changing_proof_cannot_complete_all_axes(string fault)
    {
        var f = new Fixture { Fault = fault };
        var answer = await f.Read();
        Assert.False(answer!.Source.Complete && answer.Invoicing.Complete && answer.FinancialClosure.Complete);
    }

    [Fact]
    public async Task Every_related_invoice_is_required_even_when_one_is_settled_and_no_bank_match_is_requested()
    {
        var f = new Fixture { TwoInvoices = true, Unsettled = true };
        var answer = await f.Read();
        Assert.True(answer!.Source.Complete);
        Assert.True(answer.Invoicing.Complete);
        Assert.False(answer.FinancialClosure.Complete);
        Assert.Equal(2, f.FinanceIds.Count);
    }

    [Fact]
    public async Task Permission_or_server_company_loss_stops_further_owner_reads()
    {
        var f = new Fixture();
        f.AfterPhysicalRead = () => f.User.Authenticated = false;
        Assert.False((await f.Read())!.FinancialClosure.Complete);
        Assert.Equal(0, f.FinanceCalls);
        var foreign = new Fixture();
        Assert.False((await foreign.Host().ReadOrderOwnerAxesAsync(
            foreign.Query with { CompanyId = Guid.NewGuid() }, foreign.Fence))!.Source.Complete);
        Assert.Equal(0, foreign.Fences);
    }

    [Fact]
    public async Task Uncomposed_subcontract_owners_are_explicitly_missing_and_cancellation_propagates()
    {
        var f = new Fixture();
        var answer = await f.Host().ReadOrderOwnerAxesAsync(f.Query with { IsSubcontractService = true }, f.Fence);
        Assert.Equal(SalesDraftStatusAxisStatesV1.NoSource, answer!.Source.State);
        Assert.Equal(0, f.FinanceCalls);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Host().ReadOrderOwnerAxesAsync(f.Query, f.Fence, cancelled.Token));
    }

    private sealed partial class Fixture : ISalesInvoiceableShipmentReadPortV1, ILogisticsInvoicedShipmentProofReadPortV1,
        IInventoryShipmentCostReadPortV1, ISalesFinalizedShipmentClaimReadPortV1, ISalesOrderInvoicedQuantityReadPortV1,
        ISalesOrderAllocationReadPortV2, IManufacturingSalesOrderSupplyReadPortV1,
        IPurchaseSalesOrderObligationReadPortV1, IFinanceSalesInvoiceSettlementPortV1
    {
        public readonly Guid CompanyId = Guid.NewGuid(), Order = Guid.NewGuid(), Line = Guid.NewGuid(), Product = Guid.NewGuid(),
            Unit = Guid.NewGuid(), Invoice = Guid.NewGuid(), OtherInvoice = Guid.NewGuid(), Admission = Guid.NewGuid(),
            Instruction = Guid.NewGuid(), InstructionLine = Guid.NewGuid(), Movement = Guid.NewGuid(), Party = Guid.NewGuid(), Account = Guid.NewGuid();
        public CompanyContext Company { get; } = new();
        public CurrentUser User { get; }
        public string? Fault { get; init; }
        public string? OpenSource { get; init; }
        public bool TwoInvoices { get; init; }
        public bool Unsettled { get; init; }
        public int Fences, FinanceCalls, PhysicalReads;
        public IReadOnlyList<Guid> FinanceIds = [];
        public Action? AfterPhysicalRead;
        private readonly CorrelationContext correlation = new();
        private readonly Guid shipment = Guid.NewGuid(), otherShipment = Guid.NewGuid(), note = Guid.NewGuid(), invoiceLine = Guid.NewGuid(), otherInvoiceLine = Guid.NewGuid();
        public Fixture() { Company.Set(CompanyId); User = new CurrentUser(CompanyId); correlation.Set("SENTETIK-closure", null, "Web", null, null); }
        public SalesOrderClosureQueryV1 Query => new(CompanyId, Order, Party, Account, false, [new(Line, Product, Unit, true, "10")]);
        public SalesOrderOwnerAxesHostV1 Host() => new(Company, User, correlation, this, this, this, this, this, this, this, this, this,
            ComposeSubcontract ? this : null, ComposeSubcontract ? this : null, ComposeSubcontract ? this : null);
        public Task<SalesOrderOwnerAxesV1?> Read() => Host().ReadOrderOwnerAxesAsync(Query, Fence);
        public Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Fences++; if (!User.Authenticated) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        private SalesInvoiceableShipmentV1 Share(Guid id) => new(note, "SENTETIK-001", null, DateTimeOffset.UnixEpoch,
            Party, Account, id == shipment ? 1 : 2, id, DateTimeOffset.UnixEpoch,
            Fault == "foreign-shipment" ? Guid.NewGuid() : Order, Line, Product, Unit, TwoInvoices ? 5m : 10m);
        private SalesInvoiceableShipmentV1[] Shares => TwoInvoices ? [Share(shipment), Share(otherShipment)] : [Share(shipment)];
        public Task<SalesInvoiceableShipmentAnswerV1> ReadInvoiceableShipmentsAsync(SalesInvoiceableShipmentQueryV1 query, CancellationToken cancellationToken)
        {
            Assert.Equal(new[] { Order }, query.OrderIds); Assert.Equal(Account, query.CompanyAccountId);
            return Task.FromResult(new SalesInvoiceableShipmentAnswerV1(SalesDispatchReadOutcomeV1.Found, CompanyId,
                Fault == "unposted-shipment" ? [] : Shares, Fault == "truncated-shares", SalesInvoicedStateSourceV1.NoSource));
        }
        public Task<LogisticsInvoicedShipmentProofReadV1> GetAsync(Guid companyId, Guid dispatchNoteId, int dispatchNoteLineNo, Guid shipmentId, CancellationToken cancellationToken) =>
            Task.FromResult(new LogisticsInvoicedShipmentProofReadV1(LogisticsInvoicedShipmentProofOutcomeV1.Found,
                new(CompanyId, note, dispatchNoteLineNo, shipmentId, Instruction, InstructionLine, Order, Line, 1, Product, Unit, TwoInvoices ? 5m : 10m, "instruction:4")));
        public Task<InventoryShipmentCostReadV1> GetAsync(Guid companyId, Guid shipmentId, Guid movementId, CancellationToken cancellationToken) =>
            GetByShipmentAsync(companyId, shipmentId, cancellationToken);
        public Task<InventoryShipmentCostReadV1> GetByShipmentAsync(Guid companyId, Guid shipmentId, CancellationToken cancellationToken)
        {
            PhysicalReads++;
            var stamp = new InventoryMovementCostStampV1(InventoryMovementCostStampStatusV1.Unavailable, Guid.Empty, null, null, null, "SENTETIK", "missing_cost");
            var row = new InventoryShipmentCostLineV1(1, Instruction, Fault == "wrong-stock-source" ? Guid.NewGuid() : InstructionLine,
                Guid.NewGuid(), Guid.NewGuid(), "SENTETIK", Product, "1", StockPartyKind.Company, CompanyId,
                StockPartyKind.Company, CompanyId, Guid.NewGuid(), BlockState.Free, Fault == "wrong-kilograms" ? 9m : TwoInvoices ? 5m : 10m, stamp);
            var answer = new InventoryShipmentCostReadV1(Fault == "missing-stock-proof" ? InventoryShipmentCostReadOutcomeV1.NotFound : InventoryShipmentCostReadOutcomeV1.Available,
                CompanyId, shipmentId, Movement, [row], Fault == "changed-proof" ? PhysicalReads.ToString() : "physical:1");
            AfterPhysicalRead?.Invoke(); return Task.FromResult(answer);
        }
        public Task<SalesShipmentClaimBatchV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> shipmentIds, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
        {
            var result = Shares.Select(share => {
                var id = share.ShipmentId == shipment ? Invoice : OtherInvoice;
                var lineId = share.ShipmentId == shipment ? invoiceLine : otherInvoiceLine;
                var source = new SalesInvoiceShipmentSourceV1(share.ShipmentId, note, share.NoteLineNo, Order, Line, 1, Product, Unit,
                    Fault == "wrong-invoice-quantity" ? "9" : share.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return new SalesShipmentClaimReadV1(share.ShipmentId, SalesShipmentClaimReadOutcomeV1.Available,
                    new(CompanyId, id, lineId, 1, Admission, 4, source, Guid.NewGuid(), new string('a',64), DateTimeOffset.UnixEpoch,
                        new(id, 4, Fault == "cancelled-invoice" ? "OfficiallyCancelled" : "Finalized", null, null, null)), [], "claims:1");
            }).ToArray();
            // Stable immutable event IDs, including across the second observation.
            result = result.Select(x => x with { Claim = x.Claim! with { FinalizedEventId = x.Claim.InvoiceId } }).ToArray();
            return Task.FromResult(new SalesShipmentClaimBatchV1(CompanyId, SalesShipmentClaimReadOutcomeV1.Available, result, "batch:1"));
        }
        Task<SalesOrderInvoicedQuantityReadV1> ISalesOrderInvoicedQuantityReadPortV1.ReadAsync(Guid companyId, Guid salesOrderId, Func<CancellationToken, Task> fence, CancellationToken cancellationToken) =>
            Task.FromResult(ComposeSubcontract ? SubcontractQuantities() : new SalesOrderInvoicedQuantityReadV1(CompanyId, Order, SalesOrderInvoicedQuantityReadOutcomeV1.Available,
                Shares.Select(x => new SalesOrderInvoicedQuantityLineV1(x.ShipmentId == shipment ? Invoice : OtherInvoice,
                    x.ShipmentId == shipment ? invoiceLine : otherInvoiceLine, Line, Unit, x.Quantity, "Normal", "billed:1")).ToArray(), "billed:1", CancelledProofs is { Length: > 0 } || Fault == "historical-cancelled" ? true : Fault == "missing-invoice-history" ? null : false, CancelledProofs));
        public Task<SalesOrderAllocationReadAnswerV2> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(new SalesOrderAllocationReadAnswerV2(CompanyId, Order,
                OpenSource is "physical-allocation" or "future-allocation" ? [new(Line, OpenSource == "physical-allocation" ? SalesOrderAllocationKindsV2.Physical : SalesOrderAllocationKindsV2.FutureSupply, 1, 1m, OpenSource == "physical-allocation" ? null : Unit)] : []));
        public Task<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>> ListOpenForSalesOrderAsync(Guid companyId, Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>>(OpenSource == "production" ? [new(CompanyId, Movement, "Approved", null, null, Line, 10m, null)] : []);
        Task<PurchaseSalesOrderObligationReadV1> IPurchaseSalesOrderObligationReadPortV1.ReadAsync(Guid companyId, Guid salesOrderId, Func<CancellationToken, Task> fence, CancellationToken cancellationToken) =>
            Task.FromResult(new PurchaseSalesOrderObligationReadV1(CompanyId, Order, OpenSource == "purchasing" ? PurchaseSalesOrderObligationStatusV1.Open : PurchaseSalesOrderObligationStatusV1.Complete));
        public Task<ReadResult<FinanceSalesInvoiceSettlementBatchV1>> ReadAsync(FinanceContext context, IReadOnlyList<Guid> invoiceIds, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
        {
            FinanceCalls++; FinanceIds = invoiceIds;
            var rows = invoiceIds.Where(id => !OmitCancelledSettlement || CancelledProofs?.All(x => x.InvoiceId != id) != false).Select(id => SettlementOverride?.Invoke(id) ?? new FinanceSalesInvoiceSettlementV1(id,
                Fault == "unknown-finance" ? FinanceSalesInvoiceSettlementStatusV1.Unknown : Unsettled && (id == OtherInvoice || ComposeSubcontract) ? FinanceSalesInvoiceSettlementStatusV1.Open : FinanceSalesInvoiceSettlementStatusV1.Settled,
                Fault == "wrong-admission" ? Guid.NewGuid() : SubcontractInvoice?.Finalization?.AdmissionId ?? Admission,
                Fault == "wrong-invoice-version" ? "99" : SubcontractInvoice?.Finalization?.FinalizedVersion.ToString() ?? "4")).ToArray();
            return Task.FromResult(new ReadResult<FinanceSalesInvoiceSettlementBatchV1>(ReadOutcome.Found,
                new(CompanyId, Fault == "missing-installment-proof" ? [] : rows)));
        }
    }
    private sealed class CurrentUser(Guid companyId) : ICurrentUser
    {
        public bool Authenticated = true;
        public bool IsAuthenticated => Authenticated;
        public string? UserId => "SENTETIK-closure";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
}
