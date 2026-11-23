using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// F-024 (NS-01): the one segregation of duties rule every approving module applies — a different person may always approve,
/// the preparer only when the company's setting allows it, and a missing person never passes. The decided default (no
/// recorded version) keeps preparer and approver apart.
/// </summary>
public sealed class SegregationOfDutiesRuleV1Tests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void The_decided_default_keeps_the_preparer_from_approving()
    {
        Assert.False(SegregationOfDutiesRuleV1.DefaultPreparerMayApprove);
        var unrecorded = new SettingsSegregationOfDutiesPolicyV1(CompanyId, SegregationOfDutiesRuleV1.DefaultPreparerMayApprove, 0);

        Assert.False(SegregationOfDutiesRuleV1.PermitsApproval(unrecorded, "preparer-actor", "preparer-actor"));
        Assert.True(SegregationOfDutiesRuleV1.PermitsApproval(unrecorded, "preparer-actor", "approver-actor"));
    }

    [Fact]
    public void A_relaxed_company_lets_the_preparer_approve_and_still_lets_another_person_approve()
    {
        var relaxed = new SettingsSegregationOfDutiesPolicyV1(CompanyId, true, 2);

        Assert.True(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, "preparer-actor", "preparer-actor"));
        Assert.True(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, "preparer-actor", "approver-actor"));
    }

    [Theory]
    [InlineData("preparer-actor", " preparer-actor ")]
    [InlineData(" preparer-actor", "preparer-actor")]
    public void Surrounding_whitespace_does_not_hide_the_same_person(string preparedBy, string approvedBy)
    {
        var separated = new SettingsSegregationOfDutiesPolicyV1(CompanyId, false, 1);

        Assert.True(SegregationOfDutiesRuleV1.IsSamePerson(preparedBy, approvedBy));
        Assert.False(SegregationOfDutiesRuleV1.PermitsApproval(separated, preparedBy, approvedBy));
    }

    [Fact]
    public void Actor_ids_are_compared_ordinally_so_a_case_variant_is_another_actor_id()
    {
        Assert.False(SegregationOfDutiesRuleV1.IsSamePerson("preparer-actor", "PREPARER-ACTOR"));
    }

    [Theory]
    [InlineData(null, "approver-actor")]
    [InlineData("", "approver-actor")]
    [InlineData("   ", "approver-actor")]
    [InlineData("preparer-actor", null)]
    [InlineData("preparer-actor", "")]
    [InlineData("preparer-actor", "   ")]
    public void A_missing_preparer_or_approver_never_passes_even_when_relaxed(string? preparedBy, string? approvedBy)
    {
        var relaxed = new SettingsSegregationOfDutiesPolicyV1(CompanyId, true, 1);
        var separated = new SettingsSegregationOfDutiesPolicyV1(CompanyId, false, 1);

        Assert.False(SegregationOfDutiesRuleV1.IsSamePerson(preparedBy, approvedBy));
        Assert.False(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, preparedBy, approvedBy));
        Assert.False(SegregationOfDutiesRuleV1.PermitsApproval(separated, preparedBy, approvedBy));
    }

    [Fact]
    public void A_null_policy_is_refused_rather_than_read_as_permissive()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SegregationOfDutiesRuleV1.PermitsApproval(null!, "preparer-actor", "approver-actor"));
    }

    [Fact]
    public void A_locked_dual_control_never_lets_the_preparer_approve_even_when_the_company_is_relaxed()
    {
        var relaxed = new SettingsSegregationOfDutiesPolicyV1(CompanyId, true, 3);

        Assert.True(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, SegregationOfDutiesControlKindV1.General,
            "preparer-actor", "preparer-actor"));
        Assert.False(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, SegregationOfDutiesControlKindV1.LockedDualControl,
            "preparer-actor", "preparer-actor"));
        Assert.True(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, SegregationOfDutiesControlKindV1.LockedDualControl,
            "preparer-actor", "approver-actor"));
    }

    [Fact]
    public void An_unknown_control_kind_is_treated_as_locked()
    {
        var relaxed = new SettingsSegregationOfDutiesPolicyV1(CompanyId, true, 3);

        Assert.False(SegregationOfDutiesRuleV1.PermitsApproval(relaxed, (SegregationOfDutiesControlKindV1)0,
            "preparer-actor", "preparer-actor"));
    }

    [Fact]
    public void The_policy_free_locked_rule_needs_a_different_present_person()
    {
        Assert.True(SegregationOfDutiesRuleV1.PermitsLockedDualControlApproval("preparer-actor", "approver-actor"));
        Assert.False(SegregationOfDutiesRuleV1.PermitsLockedDualControlApproval("preparer-actor", " preparer-actor "));
        Assert.False(SegregationOfDutiesRuleV1.PermitsLockedDualControlApproval(null, "approver-actor"));
        Assert.False(SegregationOfDutiesRuleV1.PermitsLockedDualControlApproval("preparer-actor", "   "));
    }

    [Fact]
    public void The_two_argument_rule_is_the_general_check()
    {
        var relaxed = new SettingsSegregationOfDutiesPolicyV1(CompanyId, true, 4);
        var separated = new SettingsSegregationOfDutiesPolicyV1(CompanyId, false, 4);

        Assert.Equal(
            SegregationOfDutiesRuleV1.PermitsApproval(relaxed, SegregationOfDutiesControlKindV1.General, "preparer-actor", "preparer-actor"),
            SegregationOfDutiesRuleV1.PermitsApproval(relaxed, "preparer-actor", "preparer-actor"));
        Assert.Equal(
            SegregationOfDutiesRuleV1.PermitsApproval(separated, SegregationOfDutiesControlKindV1.General, "preparer-actor", "preparer-actor"),
            SegregationOfDutiesRuleV1.PermitsApproval(separated, "preparer-actor", "preparer-actor"));
    }

    [Fact]
    public void Only_the_system_marker_is_read_as_an_automatic_close()
    {
        Assert.True(SegregationOfDutiesRuleV1.IsRecordedAutomatically(SegregationOfDutiesRuleV1.AutomaticCloseActorId));
        Assert.False(SegregationOfDutiesRuleV1.IsRecordedAutomatically(Guid.NewGuid().ToString("D")));
        Assert.False(SegregationOfDutiesRuleV1.IsRecordedAutomatically(null));
        Assert.False(SegregationOfDutiesRuleV1.IsRecordedAutomatically(
            SegregationOfDutiesRuleV1.AutomaticCloseActorId.ToUpperInvariant()));
    }
}
