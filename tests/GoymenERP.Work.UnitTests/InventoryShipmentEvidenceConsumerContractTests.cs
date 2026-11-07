using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Work.UnitTests;

public sealed class InventoryShipmentEvidenceConsumerContractTests
{
    private static readonly Guid MessageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ShipmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LoadingInstructionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LoadingInstructionLineId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AllocationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid LabelId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MovementId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset OccurredAtUtc =
        new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Exact_inventory_event_maps_to_one_read_only_satisfied_allocation_observation()
    {
        var admitted = InventoryShipmentEvidenceConsumerV1.Admit(Envelope());

        Assert.Equal(CompanyId, admitted.Observation.CompanyId);
        Assert.Equal(InventoryShipmentEvidenceConsumerV1.EventType, admitted.Observation.SourceKey);
        Assert.Equal($"inventory.stock-allocation:{AllocationId:D}", admitted.Observation.ScopeKey);
        Assert.Equal(7, admitted.Observation.ObservationOrdinal);
        Assert.Equal(WorkSourceAvailabilityV2.Available, admitted.Observation.Availability);
        Assert.Equal(WorkDesiredStateV2.Satisfied, admitted.Observation.DesiredState);
        Assert.Equal(MessageId, admitted.Observation.EvidenceId);
        Assert.Equal(admitted.Envelope.PayloadSha256, admitted.Observation.PayloadSha256);
        Assert.Equal("2500.125", admitted.Payload.QuantityKg);
    }

    [Fact]
    public void Explicit_null_causation_is_preserved_and_does_not_become_missing()
    {
        var admitted = InventoryShipmentEvidenceConsumerV1.Admit(Envelope(causationId: null));

        Assert.Null(admitted.Payload.CausationId);
        Assert.Null(admitted.Envelope.CausationId);
    }

    [Theory]
    [InlineData("extra", "1")]
    [InlineData("missing", null)]
    [InlineData("wrongType", "1")]
    [InlineData("duplicate", "1")]
    public void Property_set_and_types_are_strict(string mutation, string? unused)
    {
        _ = unused;
        var payload = PayloadNode();
        string json;
        if (mutation == "extra")
        {
            payload["unexpected"] = 1;
            json = payload.ToJsonString();
        }
        else if (mutation == "missing")
        {
            payload.Remove("movementId");
            json = payload.ToJsonString();
        }
        else if (mutation == "wrongType")
        {
            payload["allocationVersion"] = 7;
            json = payload.ToJsonString();
        }
        else
        {
            var canonical = PayloadJson();
            json = canonical.Insert(canonical.LastIndexOf('}'), $",\"companyId\":\"{CompanyId:D}\"");
        }

        Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
            InventoryShipmentEvidenceConsumerV1.Admit(Envelope(payloadJson: json)));
    }

    [Fact]
    public void Envelope_payload_identity_version_time_and_correlation_must_match()
    {
        var valid = Envelope();
        var mismatches = new[]
        {
            valid with { CompanyId = Guid.NewGuid() },
            valid with { AggregateId = Guid.NewGuid() },
            valid with { AggregateVersion = 8, Sequence = 8 },
            valid with { OccurredAtUtc = OccurredAtUtc.AddSeconds(1) },
            valid with { CorrelationId = "correlation-b" },
            valid with { CausationId = "causation-b" }
        };

        foreach (var mismatch in mismatches)
        {
            Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
                InventoryShipmentEvidenceConsumerV1.Admit(mismatch));
        }
    }

    [Fact]
    public void Altered_payload_and_non_registered_source_fail_closed()
    {
        var valid = Envelope();
        Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
            InventoryShipmentEvidenceConsumerV1.Admit(valid with
            {
                PayloadJson = valid.PayloadJson.Replace("2500.125", "2500.126", StringComparison.Ordinal)
            }));

        Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
            InventoryShipmentEvidenceConsumerV1.Admit(valid with { EventType = "inventory.other.v1" }));
        Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
            InventoryShipmentEvidenceConsumerV1.Admit(valid with { SchemaVersion = 2 }));
    }

    [Fact]
    public void Envelope_requires_positive_equal_aggregate_version_and_sequence()
    {
        var valid = Envelope();
        foreach (var mismatch in new[]
                 {
                     valid with { AggregateVersion = 0, Sequence = 0 },
                     valid with { AggregateVersion = 7, Sequence = 8 }
                 })
        {
            Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
                InventoryShipmentEvidenceConsumerV1.Admit(mismatch));
        }
    }

    [Theory]
    [InlineData("07")]
    [InlineData("+7")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Version_text_requires_existing_canonical_positive_integer_shape(string version)
    {
        var payload = PayloadNode();
        payload["allocationVersion"] = version;
        var json = payload.ToJsonString();

        Assert.Throws<InventoryShipmentEvidenceContractException>(() =>
            InventoryShipmentEvidenceConsumerV1.Admit(Envelope(payloadJson: json)));
    }

    [Fact]
    public void Admission_has_no_persistence_or_delivery_side_effect()
    {
        var first = InventoryShipmentEvidenceConsumerV1.Admit(Envelope());
        var replay = InventoryShipmentEvidenceConsumerV1.Admit(Envelope());

        Assert.Equal(first, replay);
        Assert.Equal(InventoryShipmentEvidenceConsumerV1.ConsumerName, "work.shipment-evidence.v1");
    }

    [Theory]
    [InlineData(null, null, 7L, false, InventoryShipmentEvidenceDeliveryDecisionV1.Ready)]
    [InlineData(7L, null, 7L, false, InventoryShipmentEvidenceDeliveryDecisionV1.Ready)]
    [InlineData(7L, null, 8L, false, InventoryShipmentEvidenceDeliveryDecisionV1.GapPending)]
    [InlineData(7L, null, 6L, false, InventoryShipmentEvidenceDeliveryDecisionV1.Conflict)]
    [InlineData(7L, 7L, 8L, false, InventoryShipmentEvidenceDeliveryDecisionV1.Ready)]
    [InlineData(7L, 7L, 9L, false, InventoryShipmentEvidenceDeliveryDecisionV1.GapPending)]
    [InlineData(7L, 8L, 8L, false, InventoryShipmentEvidenceDeliveryDecisionV1.Conflict)]
    [InlineData(7L, 8L, 8L, true, InventoryShipmentEvidenceDeliveryDecisionV1.Replay)]
    public void Durable_ordering_uses_first_sequence_as_high_water_and_waits_on_later_gaps(
        long? initial,
        long? completed,
        long sequence,
        bool alreadyCompleted,
        InventoryShipmentEvidenceDeliveryDecisionV1 expected)
    {
        var actual = InventoryShipmentEvidenceStoreV1.Evaluate(
            initial, completed, sequence, alreadyCompleted);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Message_replay_requires_full_source_envelope_equality()
    {
        var envelope = Envelope();
        var row = InboxRow(envelope);

        Assert.True(InventoryShipmentEvidenceStoreV1.SameEnvelope(row, envelope));
        Assert.False(InventoryShipmentEvidenceStoreV1.SameEnvelope(
            row, envelope with { PayloadJson = envelope.PayloadJson + " " }));
        Assert.False(InventoryShipmentEvidenceStoreV1.SameEnvelope(
            row, envelope with { PayloadSha256 = new string('0', 64) }));
        Assert.False(InventoryShipmentEvidenceStoreV1.SameEnvelope(
            row, envelope with { AggregateVersion = 8, Sequence = 8 }));
        Assert.False(InventoryShipmentEvidenceStoreV1.SameEnvelope(
            row, envelope with { CorrelationId = "other-correlation" }));
    }

    [Fact]
    public void Persistence_model_has_tenant_filters_dedup_and_aggregate_sequence_guards()
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=WorkContractOnly;Trusted_Connection=True;")
            .Options;
        using var context = new WorkDbContext(options, new FixedCompanyContext(CompanyId));

        var inbox = context.Model.FindEntityType(typeof(WorkExternalEventInboxRow));
        var highWater = context.Model.FindEntityType(typeof(WorkExternalEventHighWaterRow));

        Assert.NotNull(inbox);
        Assert.NotNull(highWater);
        Assert.NotEmpty(inbox!.GetDeclaredQueryFilters());
        Assert.NotEmpty(highWater!.GetDeclaredQueryFilters());
        Assert.Equal(["CompanyId", "ConsumerName", "MessageId"],
            inbox.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(inbox.GetIndexes(), index => index.IsUnique
            && index.GetDatabaseName() == "UX_WorkExternalInbox_AggregateSequence"
            && index.Properties.Select(property => property.Name).SequenceEqual(
                ["CompanyId", "ConsumerName", "EventType", "AggregateType", "AggregateId", "Sequence"]));
        Assert.Equal(
            ["CompanyId", "ConsumerName", "EventType", "AggregateType", "AggregateId"],
            highWater.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(inbox.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(highWater.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(highWater.FindProperty("Version")!.IsConcurrencyToken);

        Assert.Contains("20261016121000_AddWorkExternalEventInbox", context.Database.GetMigrations());
        var createScript = context.Database.GenerateCreateScript();
        Assert.Contains("[work].[ExternalEventHighWater]", createScript, StringComparison.Ordinal);
        Assert.Contains("[work].[ExternalEventInbox]", createScript, StringComparison.Ordinal);
        Assert.Contains("UX_WorkExternalInbox_AggregateSequence", createScript, StringComparison.Ordinal);
        Assert.Contains("CK_WorkExternalInbox_Payload", createScript, StringComparison.Ordinal);
    }

    private static InventoryShipmentEvidenceEnvelopeV1 Envelope(
        string? payloadJson = null,
        string? causationId = "causation-a")
    {
        var json = payloadJson ?? PayloadJson(causationId);
        return new(
            MessageId,
            CompanyId,
            InventoryShipmentEvidenceConsumerV1.EventType,
            InventoryShipmentEvidenceConsumerV1.SchemaVersion,
            InventoryShipmentEvidenceConsumerV1.SourceOperation,
            InventoryShipmentEvidenceConsumerV1.AggregateType,
            AllocationId,
            7,
            7,
            json,
            InventoryShipmentEvidenceConsumerV1.ComputePayloadSha256(json),
            OccurredAtUtc,
            "correlation-a",
            causationId);
    }

    private static string PayloadJson(string? causationId = "causation-a") =>
        PayloadNode(causationId).ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static JsonObject PayloadNode(string? causationId = "causation-a") => new()
    {
        ["companyId"] = CompanyId,
        ["shipmentId"] = ShipmentId,
        ["loadingInstructionId"] = LoadingInstructionId,
        ["loadingInstructionLineId"] = LoadingInstructionLineId,
        ["allocationId"] = AllocationId,
        ["allocationVersion"] = "7",
        ["labelId"] = LabelId,
        ["labelVersion"] = "3",
        ["movementId"] = MovementId,
        ["quantityKg"] = "2500.125",
        ["occurredAtUtc"] = OccurredAtUtc,
        ["correlationId"] = "correlation-a",
        ["causationId"] = causationId
    };

    private static WorkExternalEventInboxRow InboxRow(
        InventoryShipmentEvidenceEnvelopeV1 envelope) => new()
        {
            CompanyId = envelope.CompanyId,
            ConsumerName = InventoryShipmentEvidenceConsumerV1.ConsumerName,
            MessageId = envelope.MessageId,
            EventType = envelope.EventType,
            SchemaVersion = envelope.SchemaVersion,
            SourceOperation = envelope.SourceOperation,
            AggregateType = envelope.AggregateType,
            AggregateId = envelope.AggregateId,
            AggregateVersion = envelope.AggregateVersion,
            Sequence = envelope.Sequence,
            PayloadJson = envelope.PayloadJson,
            PayloadSha256 = envelope.PayloadSha256,
            OccurredAtUtc = envelope.OccurredAtUtc,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            ReceivedAtUtc = OccurredAtUtc
        };

    private sealed class FixedCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; } = companyId;
        public void Set(Guid value) => throw new InvalidOperationException();
    }
}
