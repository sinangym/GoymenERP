using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// F1 (STK-S4; LOJ-D9, plan L-8) contract tests without a database: the delivery weighing's net kilograms answer by
/// (instruction, weighing) under the context company only; a missing, mismatched or cancelled source is NoSource and a record
/// the rebuild refuses is Unavailable — never a zero net. The answer carries facts only (per-line loaded at weighing, loaded,
/// shipped, delivered, each shipment share) and splits nothing. The port reads nothing without a resolved company and nothing
/// for another company's key. The rebuild on real rows is proven on SQL Server
/// (<c>LogisticsInstructionSqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsSubcontractDeliveryNetWeightReadPortTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 9, 30, 0, TimeSpan.FromHours(3));
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Operator = "SENTETIK-kantar";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (LoadingInstruction Instruction, LoadingInstructionLine First, LoadingInstructionLine Second) Issued(Guid? company = null)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), company ?? CompanyA, Guid.NewGuid(), Guid.NewGuid(), T0);
        var first = instruction.AddLine(1, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 2), Guid.NewGuid(), KgUnit, null, Kg(20_000m), T0);
        var second = instruction.AddLine(2, Guid.NewGuid(), new SourceOrderLineRef(Guid.NewGuid(), Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6_000m), T0);
        instruction.Issue(3, T0);
        return (instruction, first, second);
    }

    /// <summary>Two lines loaded (18 000.5 + 5 999.5), weighed (net 23 880), then a later load, two shipments and a delivery.</summary>
    private static (LoadingInstruction Instruction, LoadingInstructionLine First, LoadingInstructionLine Second, Guid WeighingId, Guid ShipmentA, Guid ShipmentB)
        WeighedAndShipped()
    {
        var (instruction, first, second) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), first.Id, Kg(18_000.5m), MeasurementKind.Measured, T0);
        instruction.RecordLoad(5, Guid.NewGuid(), second.Id, Kg(5_999.5m), MeasurementKind.AllocatedFromVehicleWeight, T0);
        var weighingId = Guid.NewGuid();
        instruction.RecordWeighing(6, weighingId, WeighingDirection.TareFirst, Kg(15_000m), Kg(38_880m), Operator, T0);
        instruction.RecordLoad(7, Guid.NewGuid(), first.Id, Kg(1m), MeasurementKind.Measured, T0.AddMinutes(1));
        var shipmentA = Guid.NewGuid();
        var shipmentB = Guid.NewGuid();
        instruction.MarkShipped(8, shipmentA, first.Id, Kg(18_001.5m), T0.AddMinutes(2));
        instruction.MarkShipped(9, shipmentB, second.Id, Kg(5_000m), T0.AddMinutes(3));
        instruction.MarkDelivered(10, Guid.NewGuid(), second.Id, shipmentB, Kg(4_000m), T0.AddMinutes(4));
        return (instruction, first, second, weighingId, shipmentA, shipmentB);
    }

    private static CompanyContext Context(Guid? company)
    {
        var context = new CompanyContext();
        if (company is { } id)
        {
            context.Set(id);
        }

        return context;
    }

    [Fact]
    public void An_available_answer_states_the_weighing_net_as_exact_kilogram_text_with_every_line_and_shipment_fact_and_splits_nothing()
    {
        var (instruction, first, second, weighingId, shipmentA, shipmentB) = WeighedAndShipped();

        var answer = SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, instruction);

        Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.Available, (string?)null, CompanyA, instruction.Id, weighingId),
            (answer.Status, answer.ReasonCode, answer.CompanyId, answer.LoadingInstructionId, answer.WeighingId));
        var value = Assert.IsType<LogisticsSubcontractDeliveryNetWeightV1>(answer.Value);
        Assert.Equal(("23880", "kg", LogisticsNetWeightMeasurementKindV1.ManualWeighbridge, "TareFirst", "24000", "-120",
                LogisticsSubcontractDeliveryNetWeightContractV1.OriginalWeighingRevision, instruction.Version, T0.ToUniversalTime()),
            (value.NetKg, value.Unit, value.MeasurementKind, value.Direction, value.LoadedKgAtWeighing, value.DifferenceKg,
                value.WeighingRevision, value.InstructionVersion, value.RecordedAtUtc));

        // Per line: what it had loaded when weighed (the load after the weighing is not in it), and its current totals.
        Assert.Equal(2, value.Lines.Count);
        Assert.Equal(new LogisticsSubcontractDeliveryNetWeightLineV1(first.Id, first.Source.OrderId, first.Source.LineId, 2, first.ProductId, KgUnit,
            "18000.5", "18001.5", "18001.5", "0"), value.Lines[0]);
        Assert.Equal(new LogisticsSubcontractDeliveryNetWeightLineV1(second.Id, second.Source.OrderId, second.Source.LineId, 0, second.ProductId, KgUnit,
            "5999.5", "5999.5", "5000", "4000"), value.Lines[1]);
        Assert.Equal(decimal.Parse(value.LoadedKgAtWeighing, CultureInfo.InvariantCulture),
            value.Lines.Sum(line => decimal.Parse(line.LoadedKgAtWeighing, CultureInfo.InvariantCulture)));

        // Each shipment share in recording order: the declared kilograms per shipment line. No share, ratio or residue.
        Assert.Equal(
            new[]
            {
                new LogisticsSubcontractDeliveryShipmentV1(shipmentA, first.Id, "18001.5", T0.AddMinutes(2).ToUniversalTime()),
                new LogisticsSubcontractDeliveryShipmentV1(shipmentB, second.Id, "5000", T0.AddMinutes(3).ToUniversalTime()),
            },
            value.Shipments.ToArray());
    }

    [Fact]
    public void The_split_denominator_is_the_weighbridge_net_and_never_the_declared_or_shipment_share_kilograms()
    {
        // KLN-D89: the denominator is the measured net (23 880); the declared shipment shares (18 001.5 + 5 000) and the
        // loaded totals are facts that differ from it, and later shipments never move the net.
        var (instruction, _, _, weighingId, _, _) = WeighedAndShipped();

        var value = SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, instruction).Value!;

        var net = decimal.Parse(value.NetKg, CultureInfo.InvariantCulture);
        var declared = value.Shipments.Sum(shipment => decimal.Parse(shipment.ShippedKg, CultureInfo.InvariantCulture));
        var loaded = decimal.Parse(value.LoadedKgAtWeighing, CultureInfo.InvariantCulture);
        Assert.Equal((23_880m, 23_001.5m, 24_000m), (net, declared, loaded));
        Assert.NotEqual(declared, net);
        Assert.NotEqual(loaded, net);
    }

    [Fact]
    public void An_instruction_without_a_weighing_or_with_another_weighing_id_answers_no_source_never_a_zero_net()
    {
        var (instruction, first, _) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), first.Id, Kg(10m), MeasurementKind.Measured, T0);
        var requested = Guid.NewGuid();

        var none = SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, requested, instruction);
        Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.WeighingNotRecorded),
            (none.Status, none.ReasonCode));
        Assert.Null(none.Value);

        instruction.RecordWeighing(5, Guid.NewGuid(), WeighingDirection.GrossFirst, Kg(25m), Kg(15m), Operator, T0);
        foreach (var weighingId in new[] { requested, Guid.Empty })
        {
            var other = SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, instruction);
            Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.WeighingMismatch, weighingId),
                (other.Status, other.ReasonCode, other.WeighingId));
            Assert.Null(other.Value);
        }
    }

    [Fact]
    public void A_missing_instruction_or_one_of_another_company_or_another_id_answers_no_source()
    {
        var (instruction, first, _) = Issued(CompanyB);
        instruction.RecordLoad(4, Guid.NewGuid(), first.Id, Kg(10m), MeasurementKind.Measured, T0);
        var weighingId = Guid.NewGuid();
        instruction.RecordWeighing(5, weighingId, WeighingDirection.TareFirst, Kg(15m), Kg(25m), Operator, T0);

        var answers = new[]
        {
            SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, null),
            SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, instruction),
            SubcontractDeliveryNetWeightReader.Read(CompanyB, Guid.NewGuid(), weighingId, instruction),
            SubcontractDeliveryNetWeightReader.Read(Guid.Empty, instruction.Id, weighingId, instruction),
        };

        Assert.All(answers, answer =>
        {
            Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionNotFound),
                (answer.Status, answer.ReasonCode));
            Assert.Null(answer.Value);
        });
        Assert.Equal(LogisticsSubcontractDeliveryNetWeightStatusV1.Available,
            SubcontractDeliveryNetWeightReader.Read(CompanyB, instruction.Id, weighingId, instruction).Status);
    }

    [Fact]
    public void A_cancelled_instruction_keeps_its_weighing_record_but_answers_no_source()
    {
        var (instruction, first, _) = Issued();
        instruction.RecordLoad(4, Guid.NewGuid(), first.Id, Kg(10m), MeasurementKind.Measured, T0);
        var weighingId = Guid.NewGuid();
        instruction.RecordWeighing(5, weighingId, WeighingDirection.TareFirst, Kg(15m), Kg(25m), Operator, T0);
        instruction.CancelInstruction(6, Operator, "SENTETIK iptal", T0);

        var answer = SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, instruction);

        Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionCancelled),
            (answer.Status, answer.ReasonCode));
        Assert.Null(answer.Value);
    }

    [Fact]
    public void A_weighing_before_any_load_answers_zero_loaded_facts_but_a_positive_net()
    {
        var (instruction, first, _) = Issued();
        var weighingId = Guid.NewGuid();
        instruction.RecordWeighing(4, weighingId, WeighingDirection.TareFirst, Kg(15_000m), Kg(15_250.125m), Operator, T0);
        instruction.RecordLoad(5, Guid.NewGuid(), first.Id, Kg(250m), MeasurementKind.Measured, T0);

        var value = SubcontractDeliveryNetWeightReader.Read(CompanyA, instruction.Id, weighingId, instruction).Value!;

        Assert.Equal(("250.125", "0", "250.125"), (value.NetKg, value.LoadedKgAtWeighing, value.DifferenceKg));
        Assert.Equal(new[] { "0", "0" }, value.Lines.Select(line => line.LoadedKgAtWeighing).ToArray());
        Assert.Equal(new[] { "250", "0" }, value.Lines.Select(line => line.LoadedKg).ToArray());
        Assert.Empty(value.Shipments);
    }

    [Fact]
    public async Task The_port_reads_the_context_company_instruction_through_the_store_and_answers_the_reader_mapping()
    {
        var (instruction, _, _, weighingId, _, _) = WeighedAndShipped();
        var store = new SingleInstructionStore(new StoredLoadingInstruction(instruction, DateOnly.FromDateTime(T0.Date)));
        var port = new LogisticsSubcontractDeliveryNetWeightReadPort(store, Context(CompanyA));

        var answer = await port.GetAsync(CompanyA, instruction.Id, weighingId);

        Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.Available, "23880"), (answer.Status, answer.Value!.NetKg));
        Assert.Equal(new[] { instruction.Id }, store.Reads.ToArray());

        var unknown = await port.GetAsync(CompanyA, Guid.NewGuid(), weighingId);
        Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionNotFound),
            (unknown.Status, unknown.ReasonCode));
    }

    [Fact]
    public async Task Another_company_key_or_an_empty_instruction_id_answers_no_source_without_any_read()
    {
        var (instruction, _, _, weighingId, _, _) = WeighedAndShipped();
        var store = new SingleInstructionStore(new StoredLoadingInstruction(instruction, DateOnly.FromDateTime(T0.Date)));
        var port = new LogisticsSubcontractDeliveryNetWeightReadPort(store, Context(CompanyA));

        var answers = new[]
        {
            await port.GetAsync(CompanyB, instruction.Id, weighingId),
            await port.GetAsync(Guid.Empty, instruction.Id, weighingId),
            await port.GetAsync(CompanyA, Guid.Empty, weighingId),
        };

        Assert.All(answers, answer =>
        {
            Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionNotFound),
                (answer.Status, answer.ReasonCode));
            Assert.Null(answer.Value);
        });
        Assert.Equal(CompanyB, answers[0].CompanyId);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_refuses_before_any_read()
    {
        var store = new SingleInstructionStore(null);
        var port = new LogisticsSubcontractDeliveryNetWeightReadPort(store, Context(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(CompanyA, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Empty(store.Reads);
    }

    [Fact]
    public async Task A_stored_record_the_rebuild_refuses_answers_unavailable_never_a_net()
    {
        var instructionId = Guid.NewGuid();
        var weighingId = Guid.NewGuid();
        var faults = new Exception[]
        {
            new InvalidOperationException("SENTETIK: kayıtlı kantar tartımının pay sırası paylarla uyuşmuyor."),
            new LogisticsRuleException(LogisticsRuleCodes.LoadExceedsInstructed, "SENTETIK"),
            new ArgumentException("SENTETIK: kilogram ölçeği aşıldı."),
        };

        foreach (var fault in faults)
        {
            var port = new LogisticsSubcontractDeliveryNetWeightReadPort(new SingleInstructionStore(null) { Fault = fault }, Context(CompanyA));

            var answer = await port.GetAsync(CompanyA, instructionId, weighingId);

            Assert.Equal((LogisticsSubcontractDeliveryNetWeightStatusV1.Unavailable, LogisticsSubcontractDeliveryNetWeightReasonCodesV1.RecordUnreadable,
                    CompanyA, instructionId, weighingId),
                (answer.Status, answer.ReasonCode, answer.CompanyId, answer.LoadingInstructionId, answer.WeighingId));
            Assert.Null(answer.Value);
        }
    }

    [Fact]
    public async Task A_cancelled_read_and_a_store_refusal_are_not_turned_into_an_answer()
    {
        var store = new SingleInstructionStore(null) { Fault = new LogisticsStoreException(LogisticsStoreCodes.CompanyContextMismatch, "SENTETIK") };
        var port = new LogisticsSubcontractDeliveryNetWeightReadPort(store, Context(CompanyA));

        await Assert.ThrowsAsync<LogisticsStoreException>(() => port.GetAsync(CompanyA, Guid.NewGuid(), Guid.NewGuid()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => port.GetAsync(CompanyA, Guid.NewGuid(), Guid.NewGuid(), cancelled.Token));
    }

    [Fact]
    public void The_logistics_module_registers_the_net_weight_read_as_a_scoped_service()
    {
        var services = new ServiceCollection().AddLogisticsModule(new Settings("Server=unused.invalid;Database=unused;"));

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILogisticsSubcontractDeliveryNetWeightReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(LogisticsSubcontractDeliveryNetWeightReadPort)),
            (registration.Lifetime, registration.ImplementationType));
        Assert.Equal("logistics.subcontract-delivery-net-weight.v1", LogisticsSubcontractDeliveryNetWeightContractV1.ContractName);
    }

    [Fact]
    public void The_contract_status_and_measurement_values_are_stable()
    {
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<LogisticsSubcontractDeliveryNetWeightStatusV1>().Select(status => (int)status).ToArray());
        Assert.Equal(new[] { "Available", "NoSource", "Unavailable" }, Enum.GetNames<LogisticsSubcontractDeliveryNetWeightStatusV1>());
        Assert.Equal(new[] { 1 }, Enum.GetValues<LogisticsNetWeightMeasurementKindV1>().Select(kind => (int)kind).ToArray());
        Assert.Equal(("kg", 1), (LogisticsSubcontractDeliveryNetWeightContractV1.KilogramUnit, LogisticsSubcontractDeliveryNetWeightContractV1.OriginalWeighingRevision));
        Assert.Throws<ArgumentNullException>(() => LogisticsSubcontractDeliveryNetWeightReadV1.Available(CompanyA, Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    /// <summary>A store that holds at most one rebuilt instruction of the context company and records every read.</summary>
    private sealed class SingleInstructionStore(StoredLoadingInstruction? stored) : ILogisticsInstructionStore
    {
        public List<Guid> Reads { get; } = [];

        public Exception? Fault { get; init; }

        public Task<StoredLoadingInstruction?> FindInstructionAsync(Guid instructionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add(instructionId);
            if (Fault is { } fault)
            {
                throw fault;
            }

            return Task.FromResult(stored is not null && stored.Instruction.Id == instructionId ? stored : null);
        }

        public Task<LogisticsStoreResult> StoreInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
            Func<LoadingInstruction> build, CancellationToken cancellationToken = default,
            IReadOnlyList<LogisticsOrderLineAggregateLimit>? aggregateLimits = null) => throw new NotSupportedException("The read writes nothing.");

        public Task<LogisticsReplay?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The read writes nothing.");

        public Task RecordRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, LogisticsTerminalRefusal refusal,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The read writes nothing.");

        public Task<LogisticsStoreResult?> MutateInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
            Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The read writes nothing.");

        public Task<decimal> SumShippedForOrderLineAsync(Guid orderId, Guid orderLineId, Guid unitId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The read uses the rebuilt instruction only.");

        public Task<LogisticsInstructionPage> ListInstructionsAsync(int pageSize, string? cursor, LoadingInstructionStatus? status = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The read uses the rebuilt instruction only.");

        public Task<IReadOnlyList<StoredDispatchNoteReference>> ListSalesOrderDispatchNotesAsync(Guid salesOrderId, int limit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The read uses the rebuilt instruction only.");

        public Task<StoredApprovedDispatchInvoiceSource?> FindApprovedDispatchInvoiceSourceAsync(Guid dispatchNoteId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The read uses the rebuilt instruction only.");
    }

    private sealed class Settings(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
