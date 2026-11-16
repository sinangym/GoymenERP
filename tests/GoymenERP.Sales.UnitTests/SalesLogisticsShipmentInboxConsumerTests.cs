using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// LOJ-D19, SAT-D285: the Sales inbox consumer of the Logistics definite-shipment events. A known event of the ambient company
/// that names a risk exception reaches the usage store once; an event without an exception changes nothing; every broken
/// contract (source, type, version, company, envelope, payload, or a store refusal) is a permanent failure, never retried; a
/// duplicate delivery is quiet. Every identity is synthetic.
/// </summary>
public sealed class SalesLogisticsShipmentInboxConsumerTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Order = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Instruction = Guid.Parse("e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1");
    private static readonly Guid Shipment = Guid.Parse("a5a5a5a5-a5a5-4a5a-8a5a-a5a5a5a5a5a5");
    private static readonly Guid EventId = Guid.Parse("c3c3c3c3-c3c3-4c3c-8c3c-c3c3c3c3c3c3");
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(3));

    private static string Payload(Guid? company = null, Guid? shipment = null, Guid? order = null, Guid? instruction = null,
        int? exceptionNo = 1, Guid? exceptionId = null) =>
        JsonSerializer.Serialize(new
        {
            companyId = company ?? Company,
            shipmentId = shipment ?? Shipment,
            salesOrderId = order ?? Order,
            loadingInstructionId = instruction ?? Instruction,
            riskExceptionNo = exceptionNo,
            riskExceptionId = exceptionId,
            amount = new { value = 1250.5m, currency = "TRY" },
        });

    private static WorkInboxEventV1 Event(string? payload = null, string eventType = SalesLogisticsShipmentInboxV1.RecordedEventType,
        string source = SalesLogisticsShipmentInboxV1.SourceName, int version = SalesLogisticsShipmentInboxV1.Version,
        Guid? company = null, Guid? eventId = null, string correlation = " corr-1 ", string? causation = "cause-1") =>
        new(source, eventId ?? EventId, company ?? Company, eventType, version, payload ?? Payload(), T0, correlation, causation);

    [Fact]
    public async Task A_recorded_shipment_that_used_an_exception_reaches_the_store_once_as_consumed()
    {
        var store = new Store();

        await new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(Event(), CancellationToken.None);

        var usage = Assert.Single(store.Usages);
        Assert.Equal(
            (SalesRiskExceptionUsageKind.Consumed, Shipment, Order, Instruction, (int?)1, (Guid?)null),
            (usage.Kind, usage.ShipmentId, usage.OrderId, usage.LoadingInstructionId, usage.ExceptionNo, usage.ExceptionId));
        Assert.Equal((SalesLogisticsShipmentInboxV1.SourceName, EventId, T0.ToUniversalTime(), "corr-1", (string?)"cause-1"),
            (usage.SourceName, usage.SourceEventId, usage.OccurredAtUtc, usage.CorrelationId, usage.CausationId));
        Assert.Equal(TimeSpan.Zero, usage.OccurredAtUtc.Offset);
    }

    [Fact]
    public async Task A_cancelled_shipment_that_had_used_an_exception_reaches_the_store_as_dropped()
    {
        var store = new Store();
        var exceptionId = Guid.Parse("d4d4d4d4-d4d4-4d4d-8d4d-d4d4d4d4d4d4");

        await new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(
            Event(Payload(exceptionNo: null, exceptionId: exceptionId), SalesLogisticsShipmentInboxV1.CancelledEventType),
            CancellationToken.None);

        var usage = Assert.Single(store.Usages);
        Assert.Equal((SalesRiskExceptionUsageKind.Dropped, (int?)null, (Guid?)exceptionId),
            (usage.Kind, usage.ExceptionNo, usage.ExceptionId));
    }

    [Theory]
    [InlineData(SalesLogisticsShipmentInboxV1.RecordedEventType)]
    [InlineData(SalesLogisticsShipmentInboxV1.CancelledEventType)]
    public async Task A_shipment_without_an_exception_changes_nothing_in_Sales(string eventType)
    {
        var store = new Store();

        await new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(
            Event(Payload(exceptionNo: null), eventType), CancellationToken.None);

        Assert.Empty(store.Usages);
    }

    [Fact]
    public async Task A_duplicate_delivery_that_the_store_already_holds_is_quiet()
    {
        var store = new Store { Answer = SalesRiskExceptionUsageOutcome.AlreadyRecorded };

        await new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(Event(), CancellationToken.None);

        Assert.Single(store.Usages);
    }

    [Theory]
    [InlineData(SalesRiskExceptionUsageOutcome.ExceptionNotFound, "risk_exception_not_found")]
    [InlineData(SalesRiskExceptionUsageOutcome.InstructionMismatch, "risk_exception_instruction_mismatch")]
    [InlineData(SalesRiskExceptionUsageOutcome.NotApproved, "risk_exception_not_approved")]
    [InlineData(SalesRiskExceptionUsageOutcome.ConflictingShipment, "risk_exception_shipment_conflict")]
    public async Task A_store_refusal_is_a_permanent_failure_with_its_own_code(SalesRiskExceptionUsageOutcome outcome, string code)
    {
        var store = new Store { Answer = outcome };

        var failure = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(Event(), CancellationToken.None));

        Assert.Equal(code, failure.Code);
    }

    [Fact]
    public async Task A_store_exception_such_as_a_lost_race_is_not_permanent_and_is_retried()
    {
        var store = new Store { Failure = new InvalidOperationException("synthetic concurrent change") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(Event(), CancellationToken.None));

        Assert.Single(store.Usages);
    }

    public static TheoryData<string, int, string> UnsupportedEvents => new()
    {
        { "inventory", SalesLogisticsShipmentInboxV1.Version, SalesLogisticsShipmentInboxV1.RecordedEventType },
        { SalesLogisticsShipmentInboxV1.SourceName, 2, SalesLogisticsShipmentInboxV1.RecordedEventType },
        { SalesLogisticsShipmentInboxV1.SourceName, SalesLogisticsShipmentInboxV1.Version, "logistics.definite-shipment.recorded.v2" },
        { SalesLogisticsShipmentInboxV1.SourceName, SalesLogisticsShipmentInboxV1.Version, "inventory.definite-shipment.recorded.v1" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedEvents))]
    public async Task Another_source_version_or_event_type_is_unsupported_and_stores_nothing(string source, int version, string eventType)
    {
        var store = new Store();

        var failure = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company))
                .HandleAsync(Event(eventType: eventType, source: source, version: version), CancellationToken.None));

        Assert.Equal("unsupported_event", failure.Code);
        Assert.Empty(store.Usages);
    }

    [Fact]
    public async Task An_event_of_another_company_than_the_ambient_one_or_an_unresolved_context_is_refused()
    {
        var store = new Store();

        var other = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company))
                .HandleAsync(Event(Payload(company: OtherCompany), company: OtherCompany), CancellationToken.None));
        var unresolved = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company, resolved: false)).HandleAsync(Event(), CancellationToken.None));

        Assert.Equal(("company_context_mismatch", "company_context_mismatch"), (other.Code, unresolved.Code));
        Assert.Empty(store.Usages);
    }

    [Fact]
    public async Task A_payload_of_another_company_than_its_envelope_is_invalid()
    {
        var store = new Store();

        var failure = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company))
                .HandleAsync(Event(Payload(company: OtherCompany)), CancellationToken.None));

        Assert.Equal("invalid_payload", failure.Code);
        Assert.Empty(store.Usages);
    }

    [Fact]
    public async Task An_envelope_without_an_event_id_or_a_correlation_is_invalid()
    {
        var store = new Store();
        var consumer = new SalesLogisticsShipmentInboxConsumer(store, new Context(Company));

        var noId = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            consumer.HandleAsync(Event(eventId: Guid.Empty), CancellationToken.None));
        var noCorrelation = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            consumer.HandleAsync(Event(correlation: "  "), CancellationToken.None));

        Assert.Equal(("invalid_envelope", "invalid_envelope"), (noId.Code, noCorrelation.Code));
        Assert.Empty(store.Usages);
    }

    public static TheoryData<string> InvalidPayloads => new()
    {
        "not json",
        "null",
        Payload(shipment: Guid.Empty),
        Payload(order: Guid.Empty),
        Payload(instruction: Guid.Empty),
        Payload(exceptionNo: 0),
        Payload(exceptionNo: null, exceptionId: Guid.Empty),
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task A_payload_that_cannot_be_read_or_misses_an_identity_is_invalid_and_stores_nothing(string payload)
    {
        var store = new Store();

        var failure = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            new SalesLogisticsShipmentInboxConsumer(store, new Context(Company)).HandleAsync(Event(payload), CancellationToken.None));

        Assert.Equal("invalid_payload", failure.Code);
        Assert.Empty(store.Usages);
    }

    [Fact]
    public void The_consumer_subscribes_to_exactly_the_two_versioned_Logistics_events()
    {
        Assert.Equal(
            new[]
            {
                new WorkInboxSubscriptionV1("logistics", "logistics.definite-shipment.recorded.v1", 1),
                new WorkInboxSubscriptionV1("logistics", "logistics.definite-shipment.cancelled.v1", 1),
            },
            SalesLogisticsShipmentInboxV1.Subscriptions);
        Assert.Null(SalesLogisticsShipmentInboxV1.KindOf("logistics.definite-shipment.updated.v1"));
        Assert.Null(SalesLogisticsShipmentInboxV1.KindOf(null));
    }

    private sealed class Store : ISalesRiskExceptionUsageStore
    {
        public List<SalesRiskExceptionUsageV1> Usages { get; } = [];

        public SalesRiskExceptionUsageOutcome Answer { get; init; } = SalesRiskExceptionUsageOutcome.Recorded;

        public Exception? Failure { get; init; }

        public Task<SalesRiskExceptionUsageOutcome> RecordRiskExceptionUsageAsync(SalesRiskExceptionUsageV1 usage,
            CancellationToken cancellationToken = default)
        {
            Usages.Add(usage);
            return Failure is null ? Task.FromResult(Answer) : Task.FromException<SalesRiskExceptionUsageOutcome>(Failure);
        }
    }

    private sealed class Context(Guid companyId, bool resolved = true) : ICompanyContext
    {
        public bool IsResolved => resolved;
        public Guid CompanyId => resolved ? companyId : Guid.Empty;
        public void Set(Guid value) => throw new NotSupportedException();
    }
}
