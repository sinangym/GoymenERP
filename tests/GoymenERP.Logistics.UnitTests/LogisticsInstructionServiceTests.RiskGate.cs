using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

// STK-S27 (L-1..L-7, FNS-D30, SAT-D310, SAT-D285): the definite shipment reads the Sales risk gate on every execution, last
// before the write. SENTETIK / NOT_ENV: the gate answer is a fake owner; the evaluator matrix is in LogisticsShipmentRiskGateTests.
public sealed partial class LogisticsInstructionServiceTests
{
    private const int SyntheticExceptionNo = 3;
    private static readonly DateOnly SyntheticExceptionDay = new(2026, 9, 25);

    [Fact]
    public async Task Shipment_within_the_risk_limit_ships_stores_the_gate_facts_and_its_replay_never_reads_the_gate_again()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var owner = Assert.IsType<FakeRiskGate>(world.RiskGate);
        var request = Shipment();

        var shipped = await world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-within"));
        var replay = await world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-within", correlation: "retry"));

        Assert.Equal(("2", false, true), (shipped.Detail.Lines[0].Shipped, shipped.IsReplay, replay.IsReplay));
        var gate = Assert.Single(world.Store.Gates);
        Assert.Equal(new LogisticsShipmentRiskGateFactsV1(request.ShipmentId, Order, id, "within", null, "none", null,
            SalesShipmentRiskGateContractV1.Version), gate);
        Assert.Equal(1, owner.Calls);
        Assert.Equal((Order, id), Assert.Single(owner.Requests));
        Assert.Single(runner.Stock.Requests);
    }

    [Theory]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, SalesShipmentRiskExceptionStatusV1.None,
        LogisticsShipmentRiskGateCodesV1.OverLimit, "none")]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, SalesShipmentRiskExceptionStatusV1.Pending,
        LogisticsShipmentRiskGateCodesV1.OverLimit, "pending")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet, SalesShipmentRiskExceptionStatusV1.Lapsed,
        LogisticsShipmentRiskGateCodesV1.CreditLimitNotSet, "lapsed")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable, SalesShipmentRiskExceptionStatusV1.Rejected,
        LogisticsShipmentRiskGateCodesV1.RiskUnavailable, "rejected")]
    public async Task Shipment_stopped_by_the_risk_gate_without_a_valid_exception_is_a_recorded_terminal_refusal_without_stock(
        SalesShipmentRiskResultV1 result, SalesShipmentRiskHoldReasonV1 reason, SalesShipmentRiskExceptionStatusV1 status,
        string code, string statusText)
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var owner = new FakeRiskGate { Result = result, HoldReason = reason, Status = status };
        world.RiskGate = owner;
        var request = Shipment();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-stop")));
        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-stop", correlation: "retry")));
        var mismatch = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request with { Quantity = "1" }, Metadata("ship-risk-stop", correlation: "changed")));

        Assert.Equal((409, code, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal(new LogisticsFieldError(LogisticsShipmentRiskGateCodesV1.ExceptionErrorField, statusText), Assert.Single(refusal.Errors));
        Assert.Equal((409, code, true, "synthetic-correlation"), (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Equal(LogisticsProblemCodesV1.SameKeyPayloadMismatch, mismatch.Code);
        Assert.Equal(code, Assert.Single(world.Store.Refusals).Code);
        // The replay is answered from the recorded refusal before any owner is read: the gate was read once.
        Assert.Equal(1, owner.Calls);
        Assert.Empty(world.Store.Gates);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Theory]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, "over", "over_limit")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet, "unavailable", "credit_limit_not_set")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable, "unavailable", "source_unavailable")]
    public async Task A_valid_single_shipment_exception_lets_one_stopped_shipment_through_and_a_second_use_is_a_recorded_terminal_refusal(
        SalesShipmentRiskResultV1 result, SalesShipmentRiskHoldReasonV1 reason, string resultText, string reasonText)
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var owner = ValidException(result, reason);
        world.RiskGate = owner;
        var first = Shipment();

        var shipped = await world.Service(runner).RecordShipmentAsync(id, line, first, Metadata("ship-risk-exception-1"));

        Assert.Equal("2", shipped.Detail.Lines[0].Shipped);
        Assert.Equal(new LogisticsShipmentRiskGateFactsV1(first.ShipmentId, Order, id, resultText, reasonText, "valid", SyntheticExceptionNo,
            SalesShipmentRiskGateContractV1.Version), Assert.Single(world.Store.Gates));

        // SAT-D285 (1): the same exception cannot carry a second definite shipment of the order.
        var second = Shipment() with { ExpectedVersion = "5" };
        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, second, Metadata("ship-risk-exception-2")));
        var replay = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, second, Metadata("ship-risk-exception-2", correlation: "retry")));

        Assert.Equal((409, LogisticsShipmentRiskGateCodesV1.ExceptionUsed, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal((409, LogisticsShipmentRiskGateCodesV1.ExceptionUsed, true), (replay.Status, replay.Code, replay.IsReplay));
        Assert.Equal(LogisticsShipmentRiskGateCodesV1.ExceptionUsed, Assert.Single(world.Store.Refusals).Code);
        Assert.Single(world.Store.Gates);
        Assert.Single(runner.Stock.Requests);
        Assert.Equal(2, owner.Calls);
        Assert.Equal("5", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);
    }

    [Fact]
    public async Task A_shipment_within_the_limit_consumes_no_exception_even_when_a_valid_one_exists()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        world.RiskGate = new FakeRiskGate
        {
            Status = SalesShipmentRiskExceptionStatusV1.Valid,
            ExceptionNo = SyntheticExceptionNo,
            ValidOn = SyntheticExceptionDay,
        };

        await world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-risk-within-1"));
        await world.Service(runner).RecordShipmentAsync(id, line, Shipment() with { ExpectedVersion = "5" }, Metadata("ship-risk-within-2"));

        Assert.Equal(2, world.Store.Gates.Count);
        Assert.All(world.Store.Gates, gate => Assert.Equal(("within", "valid", (int?)null), (gate.Result, gate.ExceptionStatus, gate.RiskExceptionNo)));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(2, runner.Stock.Requests.Count);
    }

    [Fact]
    public async Task An_unregistered_risk_gate_is_unavailable_records_nothing_and_the_same_key_ships_once_it_answers()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        world.RiskGate = null;
        var request = Shipment();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-missing")));

        Assert.Equal((503, LogisticsShipmentRiskGateCodesV1.GateUnavailable), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal(0, runner.Commits);
        Assert.Equal("4", (await world.Service().GetInstructionDetailAsync(id)).Instruction.Version);

        world.RiskGate = new FakeRiskGate();
        var shipped = await world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-missing"));
        Assert.Equal(("2", false), (shipped.Detail.Lines[0].Shipped, shipped.IsReplay));
    }

    [Fact]
    public async Task A_failing_risk_gate_is_unavailable_records_nothing_and_the_gate_is_read_again_on_the_next_execution()
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var owner = new FakeRiskGate { Fault = new InvalidOperationException("synthetic owner failure") };
        world.RiskGate = owner;
        var request = Shipment();

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-fault")));

        Assert.Equal((503, LogisticsShipmentRiskGateCodesV1.GateUnavailable), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal(0, runner.Commits);

        // The 503 recorded nothing, so the same key is a new execution: the gate is read again (now over the limit).
        owner.Fault = null;
        owner.Result = SalesShipmentRiskResultV1.Over;
        owner.HoldReason = SalesShipmentRiskHoldReasonV1.OverLimit;
        var stopped = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, request, Metadata("ship-risk-fault")));
        Assert.Equal((409, LogisticsShipmentRiskGateCodesV1.OverLimit, false), (stopped.Status, stopped.Code, stopped.IsReplay));
        Assert.Equal(2, owner.Calls);
        Assert.Empty(runner.Stock.Requests);
    }

    [Theory]
    [InlineData("other-order")]
    [InlineData("other-instruction")]
    [InlineData("other-contract")]
    [InlineData("within-with-reason")]
    [InlineData("over-without-reason")]
    [InlineData("valid-without-number")]
    [InlineData("valid-without-day")]
    public async Task A_risk_gate_answer_that_is_not_an_answer_to_this_shipment_is_refused_as_an_invalid_owner_and_records_nothing(string fault)
    {
        var (world, runner, id, line) = await LoadedWorldAsync();
        var owner = fault switch
        {
            "other-order" => new FakeRiskGate { AnswerOrderId = Guid.NewGuid() },
            "other-instruction" => new FakeRiskGate { AnswerInstructionId = Guid.NewGuid() },
            "other-contract" => new FakeRiskGate { ContractVersion = "synthetic.v0" },
            "within-with-reason" => new FakeRiskGate { HoldReason = SalesShipmentRiskHoldReasonV1.OverLimit },
            "over-without-reason" => new FakeRiskGate { Result = SalesShipmentRiskResultV1.Over },
            _ => ValidException(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit),
        };
        if (fault == "valid-without-number") owner.ExceptionNo = null;
        if (fault == "valid-without-day") owner.ValidOn = null;
        world.RiskGate = owner;

        var refusal = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() =>
            world.Service(runner).RecordShipmentAsync(id, line, Shipment(), Metadata("ship-risk-invalid")));

        Assert.Equal((503, LogisticsProblemCodesV1.ReferenceProviderInvalid), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Gates);
        Assert.Empty(runner.Stock.Requests);
        Assert.Equal(0, runner.Commits);
    }

    private static FakeRiskGate ValidException(SalesShipmentRiskResultV1 result, SalesShipmentRiskHoldReasonV1 reason) => new()
    {
        Result = result,
        HoldReason = reason,
        Status = SalesShipmentRiskExceptionStatusV1.Valid,
        ExceptionNo = SyntheticExceptionNo,
        ValidOn = SyntheticExceptionDay,
    };

    /// <summary>The Sales owner's gate answer, for the ids asked unless a test plants another order/instruction.</summary>
    private sealed class FakeRiskGate : ISalesShipmentRiskGateReadPortV1
    {
        public SalesShipmentRiskResultV1 Result { get; set; } = SalesShipmentRiskResultV1.Within;
        public SalesShipmentRiskHoldReasonV1? HoldReason { get; set; }
        public SalesShipmentRiskExceptionStatusV1 Status { get; set; } = SalesShipmentRiskExceptionStatusV1.None;
        public int? ExceptionNo { get; set; }
        public DateOnly? ValidOn { get; set; }
        public Guid? AnswerOrderId { get; set; }
        public Guid? AnswerInstructionId { get; set; }
        public string ContractVersion { get; set; } = SalesShipmentRiskGateContractV1.Version;
        public Exception? Fault { get; set; }
        public int Calls { get; private set; }
        public List<(Guid OrderId, Guid InstructionId)> Requests { get; } = [];

        public Task<SalesShipmentRiskGateReadV1> GetAsync(Guid orderId, Guid loadingInstructionId, CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add((orderId, loadingInstructionId));
            if (Fault is { } fault) throw fault;
            var order = AnswerOrderId ?? orderId;
            var instruction = AnswerInstructionId ?? loadingInstructionId;
            return Task.FromResult(new SalesShipmentRiskGateReadV1(order, instruction, Result, HoldReason,
                new SalesShipmentRiskExceptionReadV1(order, instruction, Status, ExceptionNo, ValidOn), ContractVersion));
        }
    }

    /// <summary>
    /// The store's gate write: the facts beside the shipment in the same save, and a consumed exception kept unique per
    /// (order, exception number) — a second use is the store's <see cref="LogisticsShipmentRiskGateCodesV1.ExceptionUsed"/>
    /// rule refusal (the real store's pre-check; its unique key is the SQL evidence). A replay writes nothing.
    /// </summary>
    private sealed partial class FakeStore : ILogisticsShipmentRiskGateStoreV1
    {
        private readonly HashSet<(Guid SalesOrderId, int ExceptionNo)> _consumedExceptions = [];
        public List<LogisticsShipmentRiskGateFactsV1> Gates { get; } = [];

        public async Task<LogisticsStoreResult?> MutateInstructionWithShipmentRiskGateAsync(LogisticsWriteEnvelope envelope, string requestHash,
            string commandName, Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
            LogisticsShipmentRiskGateFactsV1 gate, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(gate);
            Assert.Equal(instructionId, gate.LoadingInstructionId);
            if (_attempts.ContainsKey((commandName, envelope.IdempotencyKey)))
            {
                return await MutateInstructionAsync(envelope, requestHash, commandName, instructionId, mutate, audit, cancellationToken);
            }

            if (gate.RiskExceptionNo is { } wanted && _consumedExceptions.Contains((gate.SalesOrderId, wanted)))
            {
                throw new LogisticsRuleException(LogisticsShipmentRiskGateCodesV1.ExceptionUsed, "synthetic consumed exception");
            }

            var result = await MutateInstructionAsync(envelope, requestHash, commandName, instructionId, mutate, audit, cancellationToken);
            if (result is { Outcome: LogisticsWriteOutcome.Recorded })
            {
                Gates.Add(gate);
                if (gate.RiskExceptionNo is { } consumed) _consumedExceptions.Add((gate.SalesOrderId, consumed));
            }

            return result;
        }
    }
}
