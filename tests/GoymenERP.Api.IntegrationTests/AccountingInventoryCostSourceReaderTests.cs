using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Adapter contract sources use synthetic owner ports. These facts are not real SQL/Inventory acceptance.</summary>
public sealed partial class AccountingInventoryCostSourceReaderTests
{
    [Fact]
    public async Task Original_commercial_revision_zero_is_valid_while_missing_frozen_source_remains_unavailable()
    {
        var f = new Fixture();
        var originalRevision = f.Line with { ShipmentSource = f.Line.ShipmentSource! with { PriceBasisRevision = 0 } };
        Assert.Single((await f.Reader.ReadAsync(f.Envelope([originalRevision]))).Components);
        Assert.Equal("sales_cost_shipment_source_missing", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => f.Reader.ReadAsync(f.Envelope([f.Line with { ShipmentSource = null }])))).Reason);
    }

    [Fact]
    public async Task Initial_cogs_uses_the_whole_physical_stamp_and_never_scales_by_commercial_invoice_kg_or_revision()
    {
        var f = new Fixture();
        var answer = await f.Reader.ReadAsync(f.Envelope());
        var component = Assert.Single(answer.Components);
        Assert.Equal(new("10001", "100"), component.SignedTlAmount);
        Assert.Equal(f.Line.LineId, component.InvoiceLineId);
        Assert.Equal(2, f.Logistics.Calls);
        Assert.Equal(1, f.Inventory.Calls);
        Assert.Contains("\"quantityKg\":3", component.SourceProofJson);
        Assert.NotEqual(f.Logistics.Proof.SourceOrderRevision, f.Line.ShipmentSource!.PriceBasisRevision);
        Assert.NotEqual(f.Logistics.Proof.DispatchQuantity, f.Inventory.Value.Lines[0].QuantityKg);
    }

    [Fact]
    public async Task A_known_non_decimal_stamp_is_kept_exact_without_inventing_a_currency_rounding_policy()
    {
        var f = new Fixture(); var line = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [line with { QuantityKg = 1m,
            Tl = line.Tl with { Amount = null, UnitCostRatio = new("1", "3") } }] };
        var component = Assert.Single((await f.Reader.ReadAsync(f.Envelope())).Components);
        Assert.Equal(new("1", "3"), component.SignedTlAmount);
        Assert.False(AccountingInventoryCostAmountsV1.TryExactDecimal(component.SignedTlAmount, 2, out _));
    }

    [Fact]
    public async Task An_unavailable_original_stamp_is_not_replaced_by_a_later_average_or_consumed_cost_proposal()
    {
        var f = new Fixture(); var line = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [line with { Tl = line.Tl with {
            Status = InventoryMovementCostStampStatusV1.Unavailable, Amount = null, UnitCostRatio = null, Reason = "cost-not-yet-known" } }] };
        var refusal = await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => f.Reader.ReadAsync(f.Envelope()));
        Assert.Equal("sales_cost_original_tl_unavailable", refusal.Reason);
    }

    [Fact]
    public async Task A_different_physical_instruction_line_or_changed_logistics_proof_is_unavailable()
    {
        var f = new Fixture(); var original = f.Inventory.Value;
        f.Inventory.Value = original with { Lines = [original.Lines[0] with { LoadingInstructionLineId = Guid.NewGuid() }] };
        Assert.Equal("sales_cost_inventory_binding_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => f.Reader.ReadAsync(f.Envelope()))).Reason);
        f.Inventory.Value = original; f.Logistics.Calls = 0; f.Logistics.ChangeOnSecond = true;
        Assert.Equal("sales_cost_logistics_proof_changed", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => f.Reader.ReadAsync(f.Envelope()))).Reason);
    }

    [Fact]
    public async Task Foreign_company_and_company_changes_during_owner_await_are_refused()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => f.Reader.ReadAsync(f.Envelope() with { CompanyId = Guid.NewGuid() }));
        Assert.Equal(0, f.Inventory.Calls);
        f.Inventory.AfterRead = () => f.Company.CompanyId = Guid.NewGuid();
        Assert.Equal("company_context_mismatch", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => f.Reader.ReadAsync(f.Envelope()))).Reason);
    }

    [Fact]
    public async Task Duplicate_physical_claims_and_legacy_missing_claims_cannot_create_a_second_or_zero_initial_cost()
    {
        var f = new Fixture();
        var duplicate = f.Line with { LineId = Guid.NewGuid(), LineNo = 2 };
        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => f.Reader.ReadAsync(f.Envelope([f.Line, duplicate])));
        var missing = f.Line with { ShipmentSource = null };
        Assert.Equal("sales_cost_shipment_source_missing", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => f.Reader.ReadAsync(f.Envelope([missing])))).Reason);
        var service = missing with { ItemType = "Service" };
        Assert.Empty((await f.Reader.ReadAsync(f.Envelope([service]))).Components);
    }

    [Fact]
    public async Task The_actual_sales_additive_wire_shape_is_read_without_a_new_event_version()
    {
        var f = new Fixture(); var source = f.Line.ShipmentSource!;
        var producer = new GoymenERP.Modules.Sales.Application.Contracts.SalesInvoiceShipmentSourceV1(source.ShipmentId,
            source.DispatchNoteId, source.DispatchNoteLineNo, source.SalesOrderId, source.SalesOrderLineId,
            source.PriceBasisRevision, source.ProductId, source.UnitId, source.InvoicedQuantity);
        var wire = JsonSerializer.Serialize(producer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(source, JsonSerializer.Deserialize<AccountingSalesInvoiceShipmentSourceV1>(wire, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Single((await f.Reader.ReadAsync(f.Envelope())).Components);
    }

    private sealed class Fixture
    {
        public MutableCompany Company { get; } = new();
        public LogisticsPort Logistics { get; }
        public InventoryPort Inventory { get; }
        public AccountingInventoryCostSourceReaderV1 Reader { get; }
        public AccountingSalesInvoiceFinalizedLineV1 Line { get; }
        public WorkInboxEventV1? LastEnvelope { get; private set; }
        public Fixture()
        {
            var claim = new AccountingSalesInvoiceShipmentSourceV1(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
                9, Guid.NewGuid(), Guid.NewGuid(), "5");
            Line = new(Guid.NewGuid(), 1, "Steel", "Domestic", 20m, null, 500m, 100m, 0m, 500m, 100m, 0m, claim);
            Logistics = new(new(Company.CompanyId, claim.DispatchNoteId, 1, claim.ShipmentId, Guid.NewGuid(), Guid.NewGuid(),
                claim.SalesOrderId, claim.SalesOrderLineId, 2, claim.ProductId, claim.UnitId, 5m, "SENTETIK-logistics-1"));
            Inventory = new(new(InventoryShipmentCostReadOutcomeV1.Available, Company.CompanyId, claim.ShipmentId, Guid.NewGuid(),
                [new(1, Logistics.Proof.InstructionId, Logistics.Proof.InstructionLineId, Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-LABEL",
                    claim.ProductId, "product-1", StockPartyKind.Company, Company.CompanyId, StockPartyKind.Company, Company.CompanyId,
                    Guid.NewGuid(), BlockState.Free, 3m, new(InventoryMovementCostStampStatusV1.Known,
                        SettingsCurrencyIdentitiesV1.TurkishLira, null, 100.01m, 3, "1", null, new("10001", "300")))], "SENTETIK-inventory-1"));
            Reader = new(Company, Logistics, Inventory, salesClaims: new CurrentFixtureClaims(this));
        }
        public WorkInboxEventV1 Envelope(IReadOnlyList<AccountingSalesInvoiceFinalizedLineV1?>? lines = null)
        {
            var source = new AccountingSalesInvoiceFinalizedEventV1(Guid.NewGuid(), 5, Guid.NewGuid(), Guid.NewGuid(),
                SettingsCurrencyIdentitiesV1.TurkishLira, 1m, 600m, 600m, Company.CompanyId, 500m, 100m, 0m, 500m, 100m, 0m,
                lines ?? [Line], new(2026, 11, 17), Guid.NewGuid().ToString("D"), "SENTETIK-2026-1");
            return LastEnvelope = new("sales", Guid.NewGuid(), Company.CompanyId, "sales.invoice.finalized.v1", 1,
                JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTimeOffset.UtcNow, "SENTETIK-cost", null);
        }
    }
    private sealed class CurrentFixtureClaims(Fixture f) : ISalesFinalizedShipmentClaimReadPortV1
    {
        public Task<SalesShipmentClaimBatchV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> shipmentIds,
            Func<CancellationToken, Task> fence, CancellationToken cancellationToken = default) =>
            new FinalizedClaims(f, f.LastEnvelope ?? throw new InvalidOperationException("SENTETIK envelope missing"))
                .ReadAsync(companyId, shipmentIds, fence, cancellationToken);
    }
    private sealed class MutableCompany : ICompanyContext
    { public Guid CompanyId { get; set; } = Guid.NewGuid(); public bool IsResolved => true; public void Set(Guid id) => CompanyId = id; }
    private sealed class LogisticsPort(LogisticsInvoicedShipmentProofV1 proof) : ILogisticsInvoicedShipmentProofReadPortV1
    {
        public LogisticsInvoicedShipmentProofV1 Proof { get; } = proof;
        public int Calls { get; set; }
        public bool ChangeOnSecond { get; set; }
        public Task<LogisticsInvoicedShipmentProofReadV1> GetAsync(Guid companyId, Guid dispatchNoteId, int dispatchNoteLineNo, Guid shipmentId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new LogisticsInvoicedShipmentProofReadV1(LogisticsInvoicedShipmentProofOutcomeV1.Found,
            ChangeOnSecond && Calls == 2 ? Proof with { SourceVersion = "SENTETIK-changed" } : Proof)); }
    }
    private sealed class InventoryPort(InventoryShipmentCostReadV1 value) : IInventoryShipmentCostReadPortV1
    {
        public InventoryShipmentCostReadV1 Value { get; set; } = value;
        public int Calls { get; set; }
        public Action? AfterRead { get; set; }
        public Task<InventoryShipmentCostReadV1> GetAsync(Guid companyId, Guid shipmentId, Guid movementId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The caller must not guess a movement id.");
        public Task<InventoryShipmentCostReadV1> GetByShipmentAsync(Guid companyId, Guid shipmentId, CancellationToken cancellationToken = default)
        { Calls++; AfterRead?.Invoke(); return Task.FromResult(Value); }
    }
}
