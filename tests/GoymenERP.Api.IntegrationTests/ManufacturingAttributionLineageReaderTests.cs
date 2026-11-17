using GoymenERP.Api.AccountingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingAttributionLineageReaderTests
{
    [Fact]
    public async Task The_actual_adapter_copies_pinned_owner_returns_without_replacing_gross_quantity_by_net_quantity()
    {
        var fixture = new Fixture();
        var value = await fixture.Reader.ReadAsync(fixture.Source, _ => Task.CompletedTask);
        var shipment = Assert.Single(Assert.Single(value.Outputs).Shipments);
        Assert.Equal(3m, shipment.PhysicalQuantityKg); Assert.Equal(new("1", "1"), shipment.NetRootQuantityKg);
        var returned = Assert.Single(shipment.Returns!);
        Assert.Equal(fixture.Return.ReturnId, returned.ReturnId); Assert.Equal(fixture.Return.MovementId, returned.MovementId);
        Assert.Equal(fixture.Return.SourceIssueMovementId, returned.SourceIssueMovementId);
        Assert.Equal(2m, returned.QuantityKg); Assert.True(ManufacturingAttributionShipmentEvidenceV1.IsValid(shipment));
    }

    [Fact]
    public async Task A_tampered_return_or_context_changed_after_the_owner_await_never_becomes_an_attribution_source()
    {
        var fixture = new Fixture(); var snapshot = fixture.Owner.Value.Snapshot!; var output = snapshot.Outputs[0];
        var shipment = output.Lineage.Shipments[0];
        fixture.Owner.Value = fixture.Owner.Value with { Snapshot = snapshot with { Outputs = [output with
            { Lineage = output.Lineage with { Shipments = [shipment with
                { Returns = [fixture.Return with { SourceIssueMovementId = Guid.NewGuid() }] }] } }] } };
        Assert.Equal("manufacturing_attribution_output_unavailable", (await Assert.ThrowsAsync<AccountingStoreException>(
            () => fixture.Reader.ReadAsync(fixture.Source, _ => Task.CompletedTask))).Code);
        fixture = new Fixture(); fixture.Owner.AfterRead = () => fixture.Company.Set(Guid.NewGuid());
        Assert.Equal("company_context_mismatch", (await Assert.ThrowsAsync<AccountingStoreException>(
            () => fixture.Reader.ReadAsync(fixture.Source, _ => Task.CompletedTask))).Code);
    }

    // The port is synthetic; the adapter and immutable Inventory evidence codec are the production owners.
    private sealed class Fixture
    {
        public MutableCompany Company { get; } = new();
        public InventoryManufacturingConsumedCostReturnV1 Return { get; }
        public ManufacturingCostDifferenceEventV1 Source { get; }
        public OwnerPort Owner { get; }
        public ManufacturingAttributionLineageReaderV1 Reader { get; }
        public Fixture()
        {
            var id = Guid.NewGuid(); var order = Guid.NewGuid(); var generation = Guid.NewGuid(); var output = Guid.NewGuid();
            var receipt = Guid.NewGuid(); var label = Guid.NewGuid(); var issue = Guid.NewGuid();
            var at = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
            Return = new(Guid.NewGuid(), Guid.NewGuid(), 1, issue, 2m, at.AddMinutes(1));
            var shipment = new InventoryManufacturingConsumedCostShipmentV1(Guid.NewGuid(), issue, 1, Guid.NewGuid(), label,
                Guid.NewGuid(), "1", StockPartyKind.Company, Company.CompanyId, StockPartyKind.Company, Company.CompanyId,
                Guid.NewGuid(), BlockState.Free, 3m, new("1", "1"), new("1", "1"), [Return], at);
            var lineage = InventoryManufacturingConsumedCostLineageEvidenceV1.Create(Company.CompanyId, output, receipt,
                label, 3m, new("1", "1"), [shipment]);
            var historyHash = new string('a', 64);
            var snapshot = new InventoryManufacturingConsumedCostLineageSnapshotV1(Company.CompanyId, order, generation, "1", "1",
                (InventoryManufacturingCostGenerationStateV1)3, "SENTETIK-input", id,
                [new(output, receipt, label, 3m, new("1", "1"), lineage)]);
            Owner = new(new(InventoryManufacturingConsumedCostLineageOutcomeV1.Available, Company.CompanyId, generation, "1",
                snapshot, InventoryManufacturingConsumedCostLineageEvidenceV1.SourceVersion + "/" + historyHash));
            Source = new(Company.CompanyId, order, generation, "1", "1", 3, "SENTETIK-input",
                [new(output, receipt, label, 3m, 2m, 1m, 40m, 80m, 40m, null, [], 120m)], id, false, "SENTETIK-return",
                new(InventoryManufacturingConsumedCostLineageEvidenceV1.SourceVersion, generation, "1", historyHash));
            Reader = new(Owner, Company);
        }
    }
    private sealed class MutableCompany : ICompanyContext
    { public Guid CompanyId { get; private set; } = Guid.NewGuid(); public bool IsResolved => true; public void Set(Guid id) => CompanyId = id; }
    private sealed class OwnerPort(InventoryManufacturingConsumedCostLineageReadV1 value) : IInventoryManufacturingConsumedCostLineageReadPortV1
    {
        public InventoryManufacturingConsumedCostLineageReadV1 Value { get; set; } = value;
        public Action? AfterRead { get; set; }
        public Task<InventoryManufacturingConsumedCostLineageReadV1> ReadAsync(Guid companyId, Guid generationId, string generationVersion, CancellationToken cancellationToken = default)
        { AfterRead?.Invoke(); return Task.FromResult(Value); }
    }
}
