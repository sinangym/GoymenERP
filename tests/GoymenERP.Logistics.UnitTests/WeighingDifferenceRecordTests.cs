using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// STK-S24 record: the status follows the decision and only a person who did not enter the weighing, a shipment or the record
/// may approve (SAT-D320 (1), SAT-D321 (1)-(2)). Who may approve at all is the approve permission's grant (human gate).
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class WeighingDifferenceRecordTests
{
    private static readonly Guid Company = Guid.Parse("24100000-0000-4000-8000-000000000001");
    private static readonly Guid Record = Guid.Parse("24100000-0000-4000-8000-000000000002");
    private static readonly Guid Note = Guid.Parse("24100000-0000-4000-8000-000000000003");
    private static readonly Guid Instruction = Guid.Parse("24100000-0000-4000-8000-000000000004");
    private static readonly Guid Product = Guid.Parse("24100000-0000-4000-8000-000000000005");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_excess_at_or_above_the_rate_waits_for_approval_and_the_recorder_counts_as_an_entrant()
    {
        var record = Pending();

        Assert.Equal(WeighingDifferenceRecordStatus.PendingApproval, record.Status);
        Assert.Equal(1, record.Version);
        Assert.Equal(["recorder", "shipper", "weigher"], record.EnteredBy.ToArray());
        Assert.Null(record.ApprovedBy);
    }

    [Fact]
    public void No_excess_or_an_excess_below_the_rate_is_recorded_without_approval()
    {
        var noExcess = Create(Evaluation(1008m, physicalKg: 8m), ["weigher"], true);
        var withinRate = Create(Evaluation(1004m, physicalKg: 0m), ["weigher"], true);

        Assert.Equal(WeighingDifferenceDecision.NoExcess, noExcess.Evaluation.Decision);
        Assert.Equal(WeighingDifferenceDecision.WithinRate, withinRate.Evaluation.Decision);
        Assert.All(new[] { noExcess, withinRate }, record => Assert.Equal(WeighingDifferenceRecordStatus.Recorded, record.Status));
        var refusal = Assert.Throws<WeighingDifferenceRuleException>(() => withinRate.Approve(1, "manager", Now));
        Assert.Equal(WeighingDifferenceRuleCodes.NotPendingApproval, refusal.Code);
    }

    [Theory]
    [InlineData("weigher")]
    [InlineData("shipper")]
    [InlineData("recorder")]
    [InlineData(" shipper ")]
    public void A_person_who_entered_the_weighing_a_shipment_or_the_record_cannot_approve_it(string actor)
    {
        var record = Pending();

        var refusal = Assert.Throws<WeighingDifferenceRuleException>(() => record.Approve(1, actor, Now));

        Assert.Equal(WeighingDifferenceRuleCodes.SelfApproval, refusal.Code);
        Assert.Equal(WeighingDifferenceRecordStatus.PendingApproval, record.Status);
        Assert.Equal(1, record.Version);
    }

    [Fact]
    public void A_record_whose_entrants_are_not_all_known_cannot_be_approved()
    {
        var record = Create(Evaluation(1008m, physicalKg: 0m), ["weigher"], enteredByComplete: false);

        var refusal = Assert.Throws<WeighingDifferenceRuleException>(() => record.Approve(1, "manager", Now));

        Assert.Equal(WeighingDifferenceRuleCodes.EnteredByUnknown, refusal.Code);
    }

    [Fact]
    public void A_stale_expected_version_is_a_conflict()
    {
        var record = Pending();

        var refusal = Assert.Throws<WeighingDifferenceRuleException>(() => record.Approve(2, "manager", Now));

        Assert.Equal(WeighingDifferenceRuleCodes.VersionConflict, refusal.Code);
    }

    [Fact]
    public void A_different_person_approves_once_and_the_version_moves_forward()
    {
        var record = Pending();

        record.Approve(1, "manager", Now.AddMinutes(5));

        Assert.Equal(WeighingDifferenceRecordStatus.Approved, record.Status);
        Assert.Equal(2, record.Version);
        Assert.Equal("manager", record.ApprovedBy);
        Assert.Equal(Now.AddMinutes(5), record.ApprovedAtUtc);
        var again = Assert.Throws<WeighingDifferenceRuleException>(() => record.Approve(2, "other-manager", Now));
        Assert.Equal(WeighingDifferenceRuleCodes.NotPendingApproval, again.Code);
    }

    [Fact]
    public void A_restored_record_must_match_its_decision_and_approval()
    {
        var evaluation = Evaluation(1008m, physicalKg: 0m);

        Assert.Throws<ArgumentException>(() => WeighingDifferenceRecord.Restore(Company, Record, Note, Instruction, evaluation,
            ["weigher"], true, WeighingDifferenceRecordStatus.Recorded, 1, "recorder", Now, null, null));
        Assert.Throws<ArgumentException>(() => WeighingDifferenceRecord.Restore(Company, Record, Note, Instruction, evaluation,
            ["weigher"], true, WeighingDifferenceRecordStatus.Approved, 2, "recorder", Now, null, null));
        var approved = WeighingDifferenceRecord.Restore(Company, Record, Note, Instruction, evaluation,
            ["weigher"], true, WeighingDifferenceRecordStatus.Approved, 2, "recorder", Now, "manager", Now);
        Assert.Equal(WeighingDifferenceRecordStatus.Approved, approved.Status);
    }

    private static WeighingDifferenceRecord Pending() =>
        Create(Evaluation(1008m, physicalKg: 0m), ["weigher", "shipper", "weigher"], true);

    private static WeighingDifferenceRecord Create(WeighingDifferenceEvaluation evaluation, IEnumerable<string> enteredBy,
        bool enteredByComplete) =>
        WeighingDifferenceRecord.Create(Company, Record, Note, Instruction, evaluation, enteredBy, enteredByComplete,
            "recorder", Now);

    private static WeighingDifferenceEvaluation Evaluation(decimal scaleKg, decimal physicalKg) =>
        WeighingDifferenceCalculatorTests.Evaluate(
            [WeighingDifferenceCalculatorTests.Line(1, Product, 1000m)],
            [WeighingDifferenceCalculatorTests.Scale(Product, scaleKg, physicalKg)],
            WeighingDifferenceCalculatorTests.Policy(0.5m));
}
