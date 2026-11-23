using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Proof = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryManufacturingTransferReversalProof;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>Isolated actual-row-shape proofs, not SQL, authority or public workflow evidence.</summary>
public sealed class InventoryManufacturingTransferReversalProofTests
{
    [Fact]
    public void A_dispatch_still_in_transit_requires_its_owner_undo_even_when_a_caller_claims_the_old_position()
    {
        var fixture = new Fixture(); fixture.Add(accepted: false); var facts = fixture.Facts();
        var error = Assert.Throws<InventoryManufacturingCostGenerationException>(() => Proof.RequireUndo(fixture.Epoch(), facts));
        Assert.Equal((409, Proof.RequiredCode), (error.Status, error.Code));
    }

    [Fact]
    public void A_real_cancellation_or_a_completed_linked_reverse_is_accepted_but_an_unrelated_return_is_not()
    {
        var cancelled = new Fixture(); cancelled.Add(accepted: false, cancelled: true);
        Proof.RequireUndo(cancelled.Epoch(), cancelled.Facts());
        var linked = new Fixture(); var original = linked.Add(accepted: true); linked.Add(accepted: true, reverseOf: original);
        Proof.RequireUndo(linked.Epoch(), linked.Facts());
        var unrelated = new Fixture(); unrelated.Add(accepted: true); unrelated.Add(accepted: true, back: true);
        var error = Assert.Throws<InventoryManufacturingCostGenerationException>(() => Proof.RequireUndo(unrelated.Epoch(), unrelated.Facts()));
        Assert.Equal(Proof.RequiredCode, error.Code);
    }

    [Fact]
    public void A_pre_epoch_transfer_is_not_confused_with_a_post_epoch_return_and_incomplete_reverse_does_not_restore_it()
    {
        var before = new Fixture(); before.Add(accepted: true); var original = Assert.Single(before.Facts().Dispatches);
        Proof.RequireUndo(before.Epoch() with { Dispatches = [original] }, before.Facts());
        before.Add(accepted: true, reverseOf: (original.TransferId, original.LineId));
        Assert.Throws<InventoryManufacturingCostGenerationException>(() => Proof.RequireUndo(
            before.Epoch() with { Dispatches = [original] }, before.Facts()));
        var transit = new Fixture(); var source = transit.Add(accepted: true); transit.Add(accepted: false, reverseOf: source);
        Assert.Throws<InventoryManufacturingCostGenerationException>(() => Proof.RequireUndo(transit.Epoch(), transit.Facts()));
    }

    [Fact]
    public void Wrong_company_or_a_cancel_header_without_the_actual_counter_movement_is_unreadable()
    {
        var wrongCompany = new Fixture(); wrongCompany.Add(accepted: false, cancelled: true);
        wrongCompany.Movements[0].CompanyId = Guid.NewGuid();
        Assert.Equal(Proof.UnavailableCode, Assert.Throws<InventoryManufacturingCostGenerationException>(() => wrongCompany.Facts()).Code);
        var missing = new Fixture(); missing.Add(accepted: false, cancelled: true);
        missing.Movements.RemoveAt(1);
        var facts = missing.Facts();
        Assert.Equal(Proof.UnavailableCode, Assert.Throws<InventoryManufacturingCostGenerationException>(() => Proof.RequireUndo(missing.Epoch(), facts)).Code);
    }

    [Fact]
    public void Frozen_position_scope_accepts_only_remaining_mass_at_the_same_owner_custodian_and_location()
    {
        var fixture = new Fixture(); var position = fixture.Epoch().Positions[0];
        Proof.RequirePositionScope([position], [position with { QuantityKg = new("20", "3") }]);
        foreach (var wrong in new[] { position with { LocationId = Guid.NewGuid() }, position with { OwnerId = Guid.NewGuid() },
            position with { CustodianId = Guid.NewGuid() }, position with { QuantityKg = new("11", "1") } })
            Assert.Throws<InventoryManufacturingCostGenerationException>(() => Proof.RequirePositionScope([position], [wrong]));
    }

    private sealed class Fixture
    {
        private readonly Guid company = Guid.NewGuid(), product = Guid.NewGuid(), label = Guid.NewGuid(), source = Guid.NewGuid(), target = Guid.NewGuid();
        private readonly List<InventoryTransferRow> headers = [];
        private readonly List<InventoryTransferLineRow> lines = [];
        private readonly List<InventoryTransferDispatchRow> dispatches = [];
        private readonly List<InventoryTransferDispatchLineRow> measured = [];
        private readonly List<InventoryTransferDispatchCancellationRow> cancellations = [];
        private readonly List<InventoryTransferAcceptanceRow> acceptances = [];
        private readonly List<InventoryTransferReversalLineRow> links = [];
        private readonly List<InventoryTransferReversalRow> reverseHeaders = [];
        private readonly List<InventoryTransferBucketRow> buckets = [];
        internal List<MovementRow> Movements { get; } = [];
        private readonly List<MovementLineRow> movementLines = [];

        internal Proof.Epoch Epoch() => new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), label, product, "synthetic-source",
            [new(label, 2, "product-v1")], [new(source, (int)StockPartyKind.Company, company, (int)StockPartyKind.Company, company, new("10", "1"))], []);
        internal (Guid Transfer, Guid Line) Add(bool accepted, bool cancelled = false, bool back = false, (Guid Transfer, Guid Line)? reverseOf = null)
        {
            var id = Guid.NewGuid(); var line = Guid.NewGuid(); var backwards = back || reverseOf is not null;
            var from = backwards ? target : source; var to = backwards ? source : target;
            headers.Add(new() { Id = id, CompanyId = company, Status = (int)(cancelled ? InventoryTransferStatus.DispatchCancelled
                : accepted ? InventoryTransferStatus.Accepted : InventoryTransferStatus.InTransit), SourceLocationId = from,
                DestinationLocationId = to, SourceCustodianKind = (int)StockPartyKind.Company, SourceCustodianId = company,
                DestinationCustodianKind = (int)StockPartyKind.Company, DestinationCustodianId = company });
            lines.Add(new() { CompanyId = company, TransferId = id, LineId = line, LabelId = label, PlannedKg = 10m,
                ExpectedLabelVersion = 3 + dispatches.Count * 2 });
            buckets.Add(new() { CompanyId = company, TransferId = id, LineId = line, LabelId = label, ProductId = product,
                OwnerKind = (int)StockPartyKind.Company, OwnerId = company, CustodianKind = (int)StockPartyKind.Company,
                CustodianId = company, LocationId = from, QuantityKg = 10m });
            dispatches.Add(new() { CompanyId = company, TransferId = id, MovementId = Movement(id, null, StockMovementKind.TransferDispatch, from) });
            measured.Add(new() { CompanyId = company, TransferId = id, LineId = line, ActualKg = 10m, LabelVersionAfterDispatch = 2 + dispatches.Count * 2 });
            if (accepted) acceptances.Add(new() { CompanyId = company, TransferId = id, LineId = line,
                MovementId = Movement(id, line, StockMovementKind.TransferAccept, to) });
            if (cancelled) cancellations.Add(new() { CompanyId = company, TransferId = id,
                MovementId = Movement(id, null, StockMovementKind.TransferDispatchCancellation, from) });
            if (reverseOf is { } original)
            {
                reverseHeaders.Add(new() { CompanyId = company, TransferId = id, OriginalTransferId = original.Transfer });
                links.Add(new() { CompanyId = company, TransferId = id, LineId = line, OriginalTransferId = original.Transfer, OriginalLineId = original.Line });
            }
            return (id, line);
        }
        private Guid Movement(Guid transfer, Guid? line, StockMovementKind kind, Guid location)
        {
            var id = Guid.NewGuid(); Movements.Add(new() { Id = id, CompanyId = company, Kind = (int)kind,
                SourceKind = InventoryTransfer.SourceKind, SourceId = transfer, SourceLineId = line });
            movementLines.Add(new() { CompanyId = company, MovementId = id, LineNumber = 1, LabelId = label, ProductId = product,
                Direction = (int)(kind == StockMovementKind.TransferDispatch ? StockDirection.Out : StockDirection.In), OwnerKind = (int)StockPartyKind.Company,
                OwnerId = company, CustodianKind = (int)StockPartyKind.Company, CustodianId = company, LocationId = location, QuantityKg = 10m });
            return id;
        }
        internal Proof.Facts Facts()
        {
            var facts = new Proof.Facts(company, product, headers, lines, dispatches, measured, cancellations, acceptances,
                links, reverseHeaders, buckets, Movements, movementLines);
            facts.Validate(); return facts;
        }
    }
}
