using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F-018 / plan S2 (FNS-D30, SAT-D310): the shipment risk gate read port. The decision is the S1 classification mapped once
/// (Within / Over-OverLimit / Unavailable-CreditLimitNotSet / Unavailable-SourceUnavailable), the answer carries no amount
/// (SAT-D162), the shipment's single-shipment exception is embedded, and every doubt fails closed. The service-level system read
/// is in <c>SalesDraftServiceTests.ShipmentRiskGate</c>.
/// </summary>
public sealed class SalesShipmentRiskGateTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Shipment = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 15);

    [Fact]
    public void The_mapping_is_within_for_no_warning_over_limit_for_over_and_credit_limit_not_set_for_a_missing_limit()
    {
        Assert.Equal(new SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1.Within, null), SalesShipmentRiskGateDecisions.From(null));
        Assert.Equal(new SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit),
            SalesShipmentRiskGateDecisions.From(Assessment(SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.OverLimit)));
        Assert.Equal(new SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet),
            SalesShipmentRiskGateDecisions.From(Assessment(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet)));
        Assert.Equal(new SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable),
            SalesShipmentRiskGateDecisions.From(SalesOrderRiskWarnings.SourceUnavailable(Day)));
    }

    [Fact]
    public void A_combination_the_mapping_does_not_know_is_source_unavailable_never_within_or_over()
    {
        foreach (var assessment in new[]
                 {
                     Assessment(SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.CreditLimitNotSet),
                     Assessment(SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.SourceUnavailable),
                     Assessment(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.OverLimit),
                     Assessment((SalesOrderRiskWarningOutcome)99, SalesOrderRiskWarningCategory.OverLimit),
                     Assessment(SalesOrderRiskWarningOutcome.Unavailable, (SalesOrderRiskWarningCategory)99),
                 })
        {
            Assert.Equal(SalesShipmentRiskGateDecisions.SourceUnavailable, SalesShipmentRiskGateDecisions.From(assessment));
        }
    }

    [Fact]
    public void Through_the_s1_classification_the_gate_and_the_warning_never_disagree()
    {
        var limit = SalesRiskInput.Available([new SalesRiskAmount(Guid.NewGuid(), 100m)], null);

        Assert.Equal(SalesShipmentRiskGateDecisions.Within,
            SalesShipmentRiskGateDecisions.From(SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Within), limit, Day)));
        Assert.Equal(SalesShipmentRiskResultV1.Over,
            SalesShipmentRiskGateDecisions.From(SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Over), limit, Day)).Result);
        Assert.Equal(SalesShipmentRiskHoldReasonV1.CreditLimitNotSet,
            SalesShipmentRiskGateDecisions.From(SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Unavailable),
                SalesRiskInput.NoSource("finance_credit_limit_not_set"), Day)).HoldReason);
        Assert.Equal(SalesShipmentRiskHoldReasonV1.CreditLimitNotSet,
            SalesShipmentRiskGateDecisions.From(SalesOrderRiskWarnings.Classify(
                Evaluation(SalesRiskOutcome.Unavailable, new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.CreditLimitNotDefined)),
                SalesRiskInput.Available([], null), Day)).HoldReason);
        Assert.Equal(SalesShipmentRiskGateDecisions.SourceUnavailable,
            SalesShipmentRiskGateDecisions.From(SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Unavailable),
                SalesRiskInput.Unavailable("finance_credit_limit_unreadable"), Day)));
    }

    [Fact]
    public void The_contract_answers_no_amount_its_version_is_stable_and_an_unfilled_result_is_unavailable()
    {
        var amountTypes = new[] { typeof(decimal), typeof(decimal?), typeof(double), typeof(float) };
        foreach (var type in new[] { typeof(SalesShipmentRiskGateReadV1), typeof(SalesShipmentRiskGateDecisionV1) })
        {
            foreach (var property in type.GetProperties())
            {
                Assert.DoesNotContain(property.PropertyType, amountTypes);
                Assert.DoesNotContain("Amount", property.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("Limit", property.Name, StringComparison.Ordinal);
                Assert.DoesNotContain("Balance", property.Name, StringComparison.Ordinal);
            }
        }

        Assert.Equal(
            new[] { "OrderId", "LoadingInstructionId", "Result", "HoldReason", "Exception", "ContractVersion" },
            typeof(SalesShipmentRiskGateReadV1).GetProperties().Select(property => property.Name).ToArray());
        Assert.Equal("sales.shipment-risk-gate.v1", SalesShipmentRiskGateContractV1.Version);
        Assert.Equal(SalesShipmentRiskResultV1.Unavailable, default(SalesShipmentRiskResultV1));
        Assert.Equal((1, 2, 3), ((int)SalesShipmentRiskHoldReasonV1.OverLimit, (int)SalesShipmentRiskHoldReasonV1.CreditLimitNotSet,
            (int)SalesShipmentRiskHoldReasonV1.SourceUnavailable));
        Assert.Equal(
            new[] { SalesShipmentRiskHoldReasonV1.OverLimit, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet, SalesShipmentRiskHoldReasonV1.SourceUnavailable },
            Enum.GetValues<SalesShipmentRiskHoldReasonV1>());
    }

    [Fact]
    public async Task The_port_embeds_the_shipments_exception_and_stamps_the_contract_version()
    {
        var source = new FakeSource(new(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit));
        var valid = new SalesShipmentRiskExceptionReadV1(Order, Shipment, SalesShipmentRiskExceptionStatusV1.Valid, 1, Day);
        var exceptions = new FakeExceptions(valid);

        var read = await Port(source, exceptions).GetAsync(Order, Shipment);

        Assert.Equal(new SalesShipmentRiskGateReadV1(Order, Shipment, SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit,
            valid, SalesShipmentRiskGateContractV1.Version), read);
        Assert.Equal([Order], source.Asked);
        Assert.Equal([(Order, Shipment)], exceptions.Asked);
    }

    [Fact]
    public void A_self_contradicting_decision_is_never_passed_on_it_becomes_source_unavailable()
    {
        var none = new SalesShipmentRiskExceptionReadV1(Order, Shipment, SalesShipmentRiskExceptionStatusV1.None, null, null);
        foreach (var decision in new SalesShipmentRiskGateDecisionV1[]
                 {
                     new(SalesShipmentRiskResultV1.Within, SalesShipmentRiskHoldReasonV1.OverLimit),
                     new(SalesShipmentRiskResultV1.Over, null),
                     new(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet),
                     new(SalesShipmentRiskResultV1.Unavailable, null),
                     new(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.OverLimit),
                     new((SalesShipmentRiskResultV1)99, null),
                 })
        {
            var read = SalesShipmentRiskGateReadPort.Compose(Order, Shipment, decision, none);
            Assert.Equal((SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable), (read.Result, read.HoldReason));
        }

        foreach (var decision in new[]
                 {
                     SalesShipmentRiskGateDecisions.Within,
                     SalesShipmentRiskGateDecisions.SourceUnavailable,
                     new SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet),
                 })
        {
            var read = SalesShipmentRiskGateReadPort.Compose(Order, Shipment, decision, none);
            Assert.Equal((decision.Result, decision.HoldReason), (read.Result, read.HoldReason));
        }
    }

    [Fact]
    public void An_exception_answer_about_another_order_or_shipment_is_an_error_never_embedded()
    {
        var decision = SalesShipmentRiskGateDecisions.Within;

        Assert.Throws<InvalidOperationException>(() => SalesShipmentRiskGateReadPort.Compose(Order, Shipment, decision,
            new SalesShipmentRiskExceptionReadV1(Guid.NewGuid(), Shipment, SalesShipmentRiskExceptionStatusV1.Valid, 1, Day)));
        Assert.Throws<InvalidOperationException>(() => SalesShipmentRiskGateReadPort.Compose(Order, Shipment, decision,
            new SalesShipmentRiskExceptionReadV1(Order, Guid.NewGuid(), SalesShipmentRiskExceptionStatusV1.Valid, 1, Day)));
        Assert.Throws<ArgumentNullException>(() => SalesShipmentRiskGateReadPort.Compose(Order, Shipment, null!,
            new SalesShipmentRiskExceptionReadV1(Order, Shipment, SalesShipmentRiskExceptionStatusV1.None, null, null)));
        Assert.Throws<ArgumentNullException>(() => SalesShipmentRiskGateReadPort.Compose(Order, Shipment, decision, null!));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_throws_before_asking_and_an_owner_error_propagates()
    {
        var source = new FakeSource(SalesShipmentRiskGateDecisions.Within);
        var exceptions = new FakeExceptions(new SalesShipmentRiskExceptionReadV1(Order, Shipment, SalesShipmentRiskExceptionStatusV1.None, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SalesShipmentRiskGateReadPort(source, exceptions, new CompanyContext()).GetAsync(Order, Shipment));
        Assert.Empty(source.Asked);
        Assert.Empty(exceptions.Asked);

        // The exception port's own fail-closed error (e.g. no Europe/Istanbul zone) stops the gate: it is never read as "none".
        exceptions.Failure = new InvalidOperationException("SENTETIK exception port failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Port(source, exceptions).GetAsync(Order, Shipment));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port(source, exceptions).GetAsync(Order, Shipment, cancelled.Token));
    }

    private static SalesShipmentRiskGateReadPort Port(ISalesShipmentRiskGateSource source, ISalesShipmentRiskExceptionReadPortV1 exceptions)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new SalesShipmentRiskGateReadPort(source, exceptions, company);
    }

    private static SalesOrderRiskAssessment Assessment(SalesOrderRiskWarningOutcome outcome, SalesOrderRiskWarningCategory category) =>
        new(outcome, category, Day, SalesOrderRiskWarnings.PartyRiskContractV1);

    private static SalesRiskEvaluation Evaluation(SalesRiskOutcome outcome, params SalesRiskReason[] reasons) =>
        new(outcome, reasons, null, null, null);

    private sealed class FakeSource(SalesShipmentRiskGateDecisionV1 decision) : ISalesShipmentRiskGateSource
    {
        public List<Guid> Asked { get; } = [];

        public Task<SalesShipmentRiskGateDecisionV1> EvaluateShipmentRiskGateAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add(orderId);
            return Task.FromResult(decision);
        }
    }

    private sealed class FakeExceptions(SalesShipmentRiskExceptionReadV1 answer) : ISalesShipmentRiskExceptionReadPortV1
    {
        public List<(Guid OrderId, Guid LoadingInstructionId)> Asked { get; } = [];

        public Exception? Failure { get; set; }

        public Task<SalesShipmentRiskExceptionReadV1> GetAsync(Guid orderId, Guid loadingInstructionId, CancellationToken cancellationToken = default)
        {
            Asked.Add((orderId, loadingInstructionId));
            return Failure is null ? Task.FromResult(answer) : Task.FromException<SalesShipmentRiskExceptionReadV1>(Failure);
        }
    }
}
