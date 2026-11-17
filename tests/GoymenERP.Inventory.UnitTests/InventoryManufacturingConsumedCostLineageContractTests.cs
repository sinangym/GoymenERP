using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>Pure physical evidence/transport tests; no accounting money allocation and no SQL acceptance claim.</summary>
public sealed class InventoryManufacturingConsumedCostLineageContractTests
{
    private static readonly Guid Company = Guid.NewGuid(), Output = Guid.NewGuid(), Receipt = Guid.NewGuid(), Root = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Nonterminating_root_kilograms_and_the_bound_return_round_trip_without_any_money_or_quantity_rounding()
    {
        var shipment = Shipment();
        var evidence = Create(shipment);
        var restored = JsonSerializer.Deserialize<InventoryManufacturingConsumedCostLineageV1>(JsonSerializer.Serialize(evidence, Json), Json)!;
        Assert.True(Valid(restored));
        Assert.Equal(new("2", "3"), restored.NetShippedRootQuantityKg);
        var actual = Assert.Single(restored.Shipments);
        Assert.Equal((shipment.MovementId, shipment.ShipmentId, shipment.LoadingInstructionLineId, At),
            (actual.MovementId, actual.ShipmentId, actual.LoadingInstructionLineId, actual.RecordedAtUtc));
        Assert.Equal(shipment.MovementId, Assert.Single(actual.Returns).SourceIssueMovementId);
        Assert.Null(actual.CreationSequence);
    }

    [Fact]
    public void Changed_root_movement_return_quantity_or_recorded_time_cannot_reuse_the_original_evidence_hash()
    {
        var evidence = Create(Shipment()); var shipment = Assert.Single(evidence.Shipments);
        Assert.False(InventoryManufacturingConsumedCostLineageEvidenceV1.IsValid(Company, Output, Receipt, Guid.NewGuid(), 1m, evidence));
        Assert.False(Valid(evidence with { Shipments = [shipment with { MovementId = Guid.NewGuid() }] }));
        Assert.False(Valid(evidence with { Shipments = [shipment with { RecordedAtUtc = At.AddTicks(1) }] }));
        Assert.False(Valid(evidence with { Shipments = [shipment with { Returns = [shipment.Returns[0] with { QuantityKg = 2m }] }] }));
        Assert.False(Valid(evidence with { NetShippedRootQuantityKg = new("0", "1") }));
    }

    [Fact]
    public void A_duplicate_or_unbound_return_and_an_inconsistent_root_fraction_are_refused_before_capture()
    {
        var shipment = Shipment(); var returned = Assert.Single(shipment.Returns);
        Assert.Throws<InvalidOperationException>(() => Create(shipment with { Returns = [returned, returned] }));
        Assert.Throws<InvalidOperationException>(() => Create(shipment with { Returns = [returned with { SourceIssueMovementId = Guid.NewGuid() }] }));
        Assert.Throws<InvalidOperationException>(() => Create(shipment with { RootShare = new("2", "3") }));
        Assert.Throws<InvalidOperationException>(() => Create(shipment with { NetRootQuantityKg = new("6667", "10000") }));
    }

    [Fact]
    public void Equal_actual_recording_times_remain_explicitly_without_a_creation_sequence_or_a_guid_tiebreaker()
    {
        var first = Shipment() with { Returns = [], NetRootQuantityKg = new("1", "1") };
        var second = first with { ShipmentId = Guid.NewGuid(), MovementId = Guid.NewGuid() };
        var evidence = InventoryManufacturingConsumedCostLineageEvidenceV1.Create(Company, Output, Receipt, Root, 3m,
            new("2", "1"), [first, second]);
        Assert.Equal(first.RecordedAtUtc, second.RecordedAtUtc);
        Assert.All(evidence.Shipments, item => Assert.Null(item.CreationSequence));
        Assert.True(InventoryManufacturingConsumedCostLineageEvidenceV1.IsValid(Company, Output, Receipt, Root, 3m, evidence));
        Assert.Throws<InvalidOperationException>(() => Create(Shipment() with { CreationSequence = "00000000000000000001" }));
    }

    [Fact]
    public void Legacy_generation_output_json_keeps_a_missing_lineage_distinct_from_a_proved_empty_shipment_list()
    {
        var old = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationOutputResultV1>(
            "{\"outputId\":\"649ec2de-7d0e-4f40-bdde-52fc418627c3\",\"receiptMovementId\":\"c2de4161-3b62-42af-b1b5-60c5f291c818\",\"labelId\":\"c36ba991-5f57-41bc-8b32-4ade690702e4\",\"netQuantityKg\":1,\"positions\":[]}", Json)!;
        Assert.Null(old.ConsumedCostLineage);
        var empty = InventoryManufacturingConsumedCostLineageEvidenceV1.Create(Company, Output, Receipt, Root, 1m, new("0", "1"), []);
        Assert.Empty(empty.Shipments); Assert.True(Valid(empty));
        Assert.Null(old.OriginalTlAmount); // Missing historical money is not changed into zero by attribution.
        Assert.DoesNotContain("consumedCostLineage", JsonSerializer.Serialize(old, Json));
    }

    [Fact]
    public void A_large_physical_graph_stays_in_history_and_only_a_fixed_size_hash_reference_enters_the_existing_event()
    {
        var first = Shipment() with { QuantityKg = 1m, Returns = [], RootShare = new("1", "1"), NetRootQuantityKg = new("1", "1") };
        var shipments = Enumerable.Range(0, 4096).Select(_ => first with { MovementId = Guid.NewGuid(), ShipmentId = Guid.NewGuid() }).ToArray();
        var evidence = InventoryManufacturingConsumedCostLineageEvidenceV1.Create(Company, Output, Receipt, Root, 4096m, new("4096", "1"), shipments);
        var output = new InventoryManufacturingCostGenerationOutputResultV1(Output, Receipt, Root, 4096m, 0m, 4096m,
            null, null, null, null, [], ConsumedCostLineage: evidence);
        var result = new InventoryManufacturingCostGenerationResultV1(Company, Guid.NewGuid(), Guid.NewGuid(), "1", "2",
            InventoryManufacturingCostGenerationStateV1.Pending, "synthetic-physical-evidence", [output], null, false, "synthetic-correlation");
        var saved = JsonSerializer.Serialize(result, Json);
        var envelope = InventoryManufacturingConsumedCostLineageEvidenceV1.ForOutbox(result, saved);
        var wire = JsonSerializer.Serialize(envelope, Json);
        Assert.True(saved.Length > 1_048_576); // Source-only payload amplification golden; no new physical limit is imposed.
        Assert.True(wire.Length < 4096);
        Assert.Null(Assert.Single(envelope.Outputs).ConsumedCostLineage);
        Assert.Equal(InventoryManufacturingConsumedCostLineageEvidenceV1.Sha256(saved), envelope.ConsumedLineageReference!.HistorySha256);
        Assert.Equal(4096, Assert.Single(result.Outputs).ConsumedCostLineage!.Shipments.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    public void Only_the_exact_canonical_positive_generation_version_is_a_read_identity(string version) =>
        Assert.False(InventoryManufacturingConsumedCostLineageEvidenceV1.TryVersion(version, out _));

    private static InventoryManufacturingConsumedCostLineageV1 Create(InventoryManufacturingConsumedCostShipmentV1 shipment) =>
        InventoryManufacturingConsumedCostLineageEvidenceV1.Create(Company, Output, Receipt, Root, 1m, new("2", "3"), [shipment]);
    private static bool Valid(InventoryManufacturingConsumedCostLineageV1 evidence) =>
        InventoryManufacturingConsumedCostLineageEvidenceV1.IsValid(Company, Output, Receipt, Root, 1m, evidence);
    private static InventoryManufacturingConsumedCostShipmentV1 Shipment()
    {
        var issue = Guid.NewGuid();
        return new(Guid.NewGuid(), issue, 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "product-v1",
            StockPartyKind.Company, Company, StockPartyKind.Company, Company, Guid.NewGuid(), BlockState.Free, 3m,
            new("1", "3"), new("2", "3"), [new(Guid.NewGuid(), Guid.NewGuid(), 1, issue, 1m, At.AddSeconds(1))], At);
    }
}
