using System.Text.Json;
using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInventoryCostSourceReaderTests
{
    [Fact]
    public async Task Original_unknown_cost_links_actual_posted_money_only_when_the_same_finalized_sales_owner_claim_is_proven()
    {
        var f = new Fixture(); var original = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [original with { Tl = original.Tl with {
            Status = InventoryMovementCostStampStatusV1.Unavailable, Amount = null, UnitCostRatio = null, Reason = "unknown-at-shipment" } }] };
        var envelope = f.Envelope(); var posted = new PostedPort(f); var claims = new FinalizedClaims(f, envelope);
        var reader = new AccountingInventoryCostSourceReaderV1(f.Company, f.Logistics, f.Inventory, posted, claims);
        var component = Assert.Single((await reader.ReadAsync(envelope)).Components);
        Assert.Equal(new("10001", "100"), component.SignedTlAmount);
        Assert.Equal(posted.Share, Assert.Single(component.AlreadyPostedComponents!));
        Assert.Single(component.SalesPhysicalClaims!);
        Assert.Equal(1, claims.Calls);
        claims.Cancelled = true;
        Assert.Equal("sales_cost_owner_claim_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => reader.ReadAsync(envelope))).Reason);
    }

    [Fact]
    public async Task A_changed_source_event_or_company_during_attribution_lookup_never_becomes_initial_cogs()
    {
        var f = new Fixture(); var original = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [original with { Tl = original.Tl with {
            Status = InventoryMovementCostStampStatusV1.Unavailable, Amount = null, UnitCostRatio = null, Reason = "unknown-at-shipment" } }] };
        var envelope = f.Envelope(); var posted = new PostedPort(f); var claims = new FinalizedClaims(f, envelope) { WrongEvent = true };
        var reader = new AccountingInventoryCostSourceReaderV1(f.Company, f.Logistics, f.Inventory, posted, claims);
        Assert.Equal("sales_cost_owner_claim_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => reader.ReadAsync(envelope))).Reason);
        claims.WrongEvent = false; posted.AfterRead = () => f.Company.CompanyId = Guid.NewGuid();
        Assert.Equal("company_context_mismatch", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => reader.ReadAsync(envelope))).Reason);
    }

    private sealed class PostedPort : IPostedManufacturingShipmentCostReaderV1
    {
        public PostedManufacturingCostShareV1 Share { get; }
        public Action? AfterRead { get; set; }
        public PostedPort(Fixture f)
        {
            var actual = f.Inventory.Value; var line = actual.Lines[0];
            var physical = new ManufacturingAttributionShipmentV1(actual.ShipmentId, actual.MovementId, line.MovementLineNumber,
                line.LoadingInstructionLineId, line.ProductId, line.LabelId, line.QuantityKg, new("1", "1"), new("3", "1"),
                DateTimeOffset.Parse("2026-11-17T09:00:00Z"), null, false, "{\"synthetic\":true}");
            var value = new PostedManufacturingCostShareV1(f.Company.CompanyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1",
                Guid.NewGuid(), Guid.NewGuid(), SettingsCurrencyIdentitiesV1.TurkishLira, Guid.NewGuid(), Guid.NewGuid(), "100.01", "100.01",
                new("10001", "100"), "0", Guid.NewGuid(), 1, 2, new(2026, 11, 17), physical,
                new string('A', 64), new string('b', 64), new string('c', 64), "");
            Share = value with { Fingerprint = AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        }
        public async Task<PostedManufacturingShipmentCostV1?> ReadAsync(Guid companyId, Guid shipmentId, Guid movementId,
            int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
            CancellationToken cancellationToken = default)
        {
            await freshConsumerFence(cancellationToken); AfterRead?.Invoke(); await freshConsumerFence(cancellationToken);
            return new(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg, [Share]);
        }
    }
    private sealed class FinalizedClaims(Fixture f, WorkInboxEventV1 envelope) : ISalesFinalizedShipmentClaimReadPortV1
    {
        public bool Cancelled { get; set; }
        public bool WrongEvent { get; set; }
        public int Calls { get; private set; }
        public async Task<SalesShipmentClaimBatchV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> shipmentIds,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        {
            Calls++; await freshConsumerFence(cancellationToken);
            var source = JsonSerializer.Deserialize<AccountingSalesInvoiceFinalizedEventV1>(envelope.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var ship = f.Line.ShipmentSource!;
            var lifecycle = new SalesShipmentInvoiceLifecycleV1(source.InvoiceId, source.Version, "Finalized",
                Cancelled ? envelope.OccurredAtUtc : null, Cancelled ? "SENTETIK" : null, Cancelled ? envelope.OccurredAtUtc : null);
            var claim = new SalesFinalizedShipmentClaimV1(companyId, source.InvoiceId, f.Line.LineId, f.Line.LineNo,
                source.AdmissionId, source.Version, new(ship.ShipmentId, ship.DispatchNoteId, ship.DispatchNoteLineNo,
                    ship.SalesOrderId, ship.SalesOrderLineId, ship.PriceBasisRevision, ship.ProductId, ship.UnitId, ship.InvoicedQuantity),
                WrongEvent ? Guid.NewGuid() : envelope.EventId, AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
                envelope.OccurredAtUtc, lifecycle);
            await freshConsumerFence(cancellationToken);
            return new(companyId, SalesShipmentClaimReadOutcomeV1.Available,
                [new(ship.ShipmentId, SalesShipmentClaimReadOutcomeV1.Available, claim, [lifecycle], "SENTETIK-1")], "SENTETIK-batch-1");
        }
    }
}
