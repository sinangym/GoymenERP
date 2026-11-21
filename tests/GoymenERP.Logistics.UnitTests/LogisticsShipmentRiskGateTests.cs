using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Outbox;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// STK-S27 (L-1..L-7, FNS-D30, SAT-D285): the pure definite-shipment risk gate rule. Within passes and consumes nothing; a stop
/// passes only with a valid single-shipment exception (its number is carried for the single-use record); every other stop is a
/// closed refusal code; an answer that is not an answer to THIS shipment is null (the service answers 503, records nothing).
/// No amount, limit or risk figure is read or produced. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsShipmentRiskGateTests
{
    private static readonly Guid Shipment = Guid.Parse("51515151-5151-4151-8151-515151515151");
    private static readonly Guid Order = Guid.Parse("52525252-5252-4252-8252-525252525252");
    private static readonly Guid Instruction = Guid.Parse("53535353-5353-4353-8353-535353535353");
    private static readonly Guid OtherId = Guid.Parse("54545454-5454-4454-8454-545454545454");
    private const int SyntheticExceptionNo = 7;
    private static readonly DateOnly SyntheticValidOn = new(2026, 9, 25);

    [Fact]
    public void Within_the_limit_passes_with_facts_and_consumes_no_exception_even_when_a_valid_one_exists()
    {
        var plain = LogisticsShipmentRiskGate.Evaluate(Read(SalesShipmentRiskResultV1.Within, null), Shipment, Order, Instruction);
        var withValid = LogisticsShipmentRiskGate.Evaluate(Read(SalesShipmentRiskResultV1.Within, null,
            SalesShipmentRiskExceptionStatusV1.Valid, SyntheticExceptionNo, SyntheticValidOn), Shipment, Order, Instruction);

        Assert.NotNull(plain);
        Assert.True(plain.Passes);
        Assert.Null(plain.RefusalCode);
        Assert.Equal(new LogisticsShipmentRiskGateFactsV1(Shipment, Order, Instruction, "within", null, "none", null,
            SalesShipmentRiskGateContractV1.Version), plain.Facts);
        Assert.NotNull(withValid);
        Assert.True(withValid.Passes);
        Assert.Equal("valid", withValid.ExceptionStatus);
        Assert.Null(withValid.Facts!.RiskExceptionNo);
    }

    [Theory]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, "over", "over_limit")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet, "unavailable",
        "credit_limit_not_set")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable, "unavailable",
        "source_unavailable")]
    public void A_stop_with_a_valid_exception_passes_and_carries_the_exception_number(SalesShipmentRiskResultV1 result,
        SalesShipmentRiskHoldReasonV1 reason, string resultText, string reasonText)
    {
        var decision = LogisticsShipmentRiskGate.Evaluate(Read(result, reason, SalesShipmentRiskExceptionStatusV1.Valid,
            SyntheticExceptionNo, SyntheticValidOn), Shipment, Order, Instruction);

        Assert.NotNull(decision);
        Assert.True(decision.Passes);
        Assert.Null(decision.RefusalCode);
        Assert.Equal(new LogisticsShipmentRiskGateFactsV1(Shipment, Order, Instruction, resultText, reasonText, "valid",
            SyntheticExceptionNo, SalesShipmentRiskGateContractV1.Version), decision.Facts);
    }

    [Theory]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, SalesShipmentRiskExceptionStatusV1.None,
        LogisticsShipmentRiskGateCodesV1.OverLimit, "none")]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, SalesShipmentRiskExceptionStatusV1.Pending,
        LogisticsShipmentRiskGateCodesV1.OverLimit, "pending")]
    [InlineData(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit, SalesShipmentRiskExceptionStatusV1.Lapsed,
        LogisticsShipmentRiskGateCodesV1.OverLimit, "lapsed")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet,
        SalesShipmentRiskExceptionStatusV1.Rejected, LogisticsShipmentRiskGateCodesV1.CreditLimitNotSet, "rejected")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable,
        SalesShipmentRiskExceptionStatusV1.Withdrawn, LogisticsShipmentRiskGateCodesV1.RiskUnavailable, "withdrawn")]
    [InlineData(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable,
        SalesShipmentRiskExceptionStatusV1.None, LogisticsShipmentRiskGateCodesV1.RiskUnavailable, "none")]
    public void A_stop_without_a_valid_exception_is_the_closed_refusal_code_of_its_reason(SalesShipmentRiskResultV1 result,
        SalesShipmentRiskHoldReasonV1 reason, SalesShipmentRiskExceptionStatusV1 status, string code, string statusText)
    {
        var decision = LogisticsShipmentRiskGate.Evaluate(Read(result, reason, status), Shipment, Order, Instruction);

        Assert.NotNull(decision);
        Assert.False(decision.Passes);
        Assert.Null(decision.Facts);
        Assert.Equal(code, decision.RefusalCode);
        Assert.Equal(statusText, decision.ExceptionStatus);
        Assert.Contains(code, LogisticsShipmentRiskGateCodesV1.TerminalCodes);
    }

    [Theory]
    [InlineData("null-read")]
    [InlineData("empty-order")]
    [InlineData("empty-instruction")]
    [InlineData("other-order")]
    [InlineData("other-instruction")]
    [InlineData("exception-other-order")]
    [InlineData("exception-other-instruction")]
    [InlineData("other-contract")]
    [InlineData("undefined-result")]
    [InlineData("undefined-reason")]
    [InlineData("undefined-status")]
    [InlineData("within-with-reason")]
    [InlineData("over-without-reason")]
    [InlineData("over-with-credit-reason")]
    [InlineData("unavailable-without-reason")]
    [InlineData("unavailable-with-over-reason")]
    [InlineData("valid-without-number")]
    [InlineData("valid-with-zero-number")]
    [InlineData("valid-without-day")]
    public void An_answer_that_is_not_an_answer_to_this_shipment_is_null(string fault)
    {
        var within = Read(SalesShipmentRiskResultV1.Within, null);
        var over = Read(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit);
        var valid = Read(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit,
            SalesShipmentRiskExceptionStatusV1.Valid, SyntheticExceptionNo, SyntheticValidOn);
        var (read, order, instruction) = fault switch
        {
            "null-read" => ((SalesShipmentRiskGateReadV1?)null, Order, Instruction),
            "empty-order" => (within with { OrderId = Guid.Empty, Exception = within.Exception with { OrderId = Guid.Empty } },
                Guid.Empty, Instruction),
            "empty-instruction" => (within with
            {
                LoadingInstructionId = Guid.Empty,
                Exception = within.Exception with { LoadingInstructionId = Guid.Empty },
            }, Order, Guid.Empty),
            "other-order" => (within with { OrderId = OtherId }, Order, Instruction),
            "other-instruction" => (within with { LoadingInstructionId = OtherId }, Order, Instruction),
            "exception-other-order" => (within with { Exception = within.Exception with { OrderId = OtherId } }, Order, Instruction),
            "exception-other-instruction" => (within with { Exception = within.Exception with { LoadingInstructionId = OtherId } },
                Order, Instruction),
            "other-contract" => (within with { ContractVersion = "sales.shipment-risk-gate.v2" }, Order, Instruction),
            "undefined-result" => (within with { Result = (SalesShipmentRiskResultV1)99 }, Order, Instruction),
            "undefined-reason" => (over with { HoldReason = (SalesShipmentRiskHoldReasonV1)99 }, Order, Instruction),
            "undefined-status" => (within with { Exception = within.Exception with { Status = (SalesShipmentRiskExceptionStatusV1)99 } },
                Order, Instruction),
            "within-with-reason" => (within with { HoldReason = SalesShipmentRiskHoldReasonV1.OverLimit }, Order, Instruction),
            "over-without-reason" => (over with { HoldReason = null }, Order, Instruction),
            "over-with-credit-reason" => (over with { HoldReason = SalesShipmentRiskHoldReasonV1.CreditLimitNotSet }, Order, Instruction),
            "unavailable-without-reason" => (within with { Result = SalesShipmentRiskResultV1.Unavailable }, Order, Instruction),
            "unavailable-with-over-reason" => (over with { Result = SalesShipmentRiskResultV1.Unavailable }, Order, Instruction),
            "valid-without-number" => (valid with { Exception = valid.Exception with { ExceptionNo = null } }, Order, Instruction),
            "valid-with-zero-number" => (valid with { Exception = valid.Exception with { ExceptionNo = 0 } }, Order, Instruction),
            "valid-without-day" => (valid with { Exception = valid.Exception with { ValidOn = null } }, Order, Instruction),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        Assert.Null(LogisticsShipmentRiskGate.Evaluate(read, Shipment, order, instruction));
    }

    [Theory]
    [InlineData(SalesShipmentRiskExceptionStatusV1.None, "none")]
    [InlineData(SalesShipmentRiskExceptionStatusV1.Pending, "pending")]
    [InlineData(SalesShipmentRiskExceptionStatusV1.Valid, "valid")]
    [InlineData(SalesShipmentRiskExceptionStatusV1.Lapsed, "lapsed")]
    [InlineData(SalesShipmentRiskExceptionStatusV1.Rejected, "rejected")]
    [InlineData(SalesShipmentRiskExceptionStatusV1.Withdrawn, "withdrawn")]
    public void Every_exception_status_has_one_stable_lower_case_name(SalesShipmentRiskExceptionStatusV1 status, string text)
    {
        Assert.Equal(text, LogisticsShipmentRiskGate.StatusText(status));
    }

    [Fact]
    public void An_undefined_exception_status_has_no_name()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogisticsShipmentRiskGate.StatusText((SalesShipmentRiskExceptionStatusV1)99));
    }

    [Fact]
    public void The_terminal_codes_are_exactly_the_four_recorded_refusals_and_never_the_unavailable_gate()
    {
        Assert.Equal(
            new[]
            {
                LogisticsShipmentRiskGateCodesV1.CreditLimitNotSet,
                LogisticsShipmentRiskGateCodesV1.ExceptionUsed,
                LogisticsShipmentRiskGateCodesV1.OverLimit,
                LogisticsShipmentRiskGateCodesV1.RiskUnavailable,
            },
            LogisticsShipmentRiskGateCodesV1.TerminalCodes.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(LogisticsShipmentRiskGateCodesV1.GateUnavailable, LogisticsShipmentRiskGateCodesV1.TerminalCodes);
    }

    [Theory]
    [InlineData(0UL, "00000000000000000000")]
    [InlineData(2001UL, "00000000000000002001")]
    [InlineData(ulong.MaxValue, "18446744073709551615")]
    public void The_outbox_cursor_is_a_fixed_width_decimal_whose_ordinal_order_is_the_rowversion_order(ulong position, string text)
    {
        Assert.Equal(text, LogisticsDefiniteShipmentOutboxEventSource.FormatPosition(position));
        Assert.True(string.CompareOrdinal(LogisticsDefiniteShipmentOutboxEventSource.FormatPosition(9UL),
            LogisticsDefiniteShipmentOutboxEventSource.FormatPosition(10UL)) < 0);
    }

    private static SalesShipmentRiskGateReadV1 Read(SalesShipmentRiskResultV1 result, SalesShipmentRiskHoldReasonV1? reason,
        SalesShipmentRiskExceptionStatusV1 status = SalesShipmentRiskExceptionStatusV1.None, int? exceptionNo = null,
        DateOnly? validOn = null) =>
        new(Order, Instruction, result, reason, new SalesShipmentRiskExceptionReadV1(Order, Instruction, status, exceptionNo, validOn),
            SalesShipmentRiskGateContractV1.Version);
}
