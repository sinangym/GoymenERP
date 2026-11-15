using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// ALS-D99 / ALS-D100: the warning list's fail-closed answer rule, its stable wire spellings, the per-company derived
/// idempotency key of the group command and the separate group permission name.
/// </summary>
public sealed class CounterpartyDeactivationWarningTests
{
    [Fact]
    public void Every_owner_kind_has_one_stable_wire_spelling()
    {
        Assert.Equal(
            ["finance-open-balance", "sales-open-documents", "purchasing-open-documents"],
            Enum.GetValues<CounterpartyDeactivationExposureKindV1>()
                .Select(CounterpartyDeactivationExposureWireV1.Kind)
                .ToArray());
    }

    [Fact]
    public void Every_outcome_has_one_stable_wire_spelling()
    {
        Assert.Equal(
            ["unavailable", "denied", "clear", "open"],
            Enum.GetValues<CounterpartyDeactivationExposureOutcomeV1>()
                .Select(CounterpartyDeactivationExposureWireV1.Outcome)
                .ToArray());
    }

    [Fact]
    public void An_undefined_kind_or_outcome_is_refused_instead_of_spelled()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CounterpartyDeactivationExposureWireV1.Kind((CounterpartyDeactivationExposureKindV1)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CounterpartyDeactivationExposureWireV1.Outcome((CounterpartyDeactivationExposureOutcomeV1)99));
    }

    [Fact]
    public void An_unavailable_owner_is_the_default_of_the_outcome_enum()
    {
        Assert.Equal(CounterpartyDeactivationExposureOutcomeV1.Unavailable, default(CounterpartyDeactivationExposureOutcomeV1));
    }

    [Theory]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Open, 1)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Open, 42)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Clear, 0)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Denied, 0)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Unavailable, 0)]
    public void A_well_formed_answer_is_accepted(CounterpartyDeactivationExposureOutcomeV1 outcome, int count)
    {
        Assert.True(CounterpartyDeactivationExposureReader.IsWellFormed(
            new CounterpartyDeactivationExposureAnswerV1(outcome, count)));
    }

    [Theory]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Open, 0)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Open, -1)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Clear, 1)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Denied, 3)]
    [InlineData(CounterpartyDeactivationExposureOutcomeV1.Unavailable, 2)]
    [InlineData((CounterpartyDeactivationExposureOutcomeV1)99, 0)]
    [InlineData((CounterpartyDeactivationExposureOutcomeV1)99, 5)]
    public void A_malformed_answer_is_refused_so_the_line_reads_unavailable(
        CounterpartyDeactivationExposureOutcomeV1 outcome,
        int count)
    {
        Assert.False(CounterpartyDeactivationExposureReader.IsWellFormed(
            new CounterpartyDeactivationExposureAnswerV1(outcome, count)));
    }

    [Fact]
    public void A_missing_answer_is_refused()
    {
        Assert.False(CounterpartyDeactivationExposureReader.IsWellFormed(null));
    }

    [Fact]
    public void The_static_answers_are_well_formed_and_carry_no_count()
    {
        foreach (var answer in new[]
                 {
                     CounterpartyDeactivationExposureAnswerV1.Unavailable,
                     CounterpartyDeactivationExposureAnswerV1.Denied,
                     CounterpartyDeactivationExposureAnswerV1.Clear,
                 })
        {
            Assert.True(CounterpartyDeactivationExposureReader.IsWellFormed(answer));
            Assert.Equal(0, answer.OpenItemCount);
        }
    }

    [Fact]
    public void The_derived_company_key_is_deterministic_and_bound_to_the_company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var first = CounterpartyGroupDeactivationService.DeriveCompanyIdempotencyKey("group-key", companyA);
        var again = CounterpartyGroupDeactivationService.DeriveCompanyIdempotencyKey("group-key", companyA);
        var other = CounterpartyGroupDeactivationService.DeriveCompanyIdempotencyKey("group-key", companyB);
        var otherKey = CounterpartyGroupDeactivationService.DeriveCompanyIdempotencyKey("another-key", companyA);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.NotEqual(first, otherKey);
        Assert.Equal($"group-key:{companyA:N}", first);
        Assert.NotEqual("group-key", first);
    }

    [Fact]
    public void The_group_permission_is_the_separate_registered_name()
    {
        Assert.Equal(
            ErpPermissions.CounterpartyPartiesGroupDeactivate,
            CounterpartyGroupDeactivationPermissionsV1.GroupDeactivate);
        Assert.NotEqual(
            ErpPermissions.CounterpartyAccountsStatusUpdate,
            CounterpartyGroupDeactivationPermissionsV1.GroupDeactivate);
    }

    [Fact]
    public void The_group_outcome_spellings_are_unique()
    {
        var spellings = new[]
        {
            CounterpartyGroupDeactivationOutcomesV1.Deactivated,
            CounterpartyGroupDeactivationOutcomesV1.AlreadyPassive,
            CounterpartyGroupDeactivationOutcomesV1.VersionConflict,
            CounterpartyGroupDeactivationOutcomesV1.NotFound,
            CounterpartyGroupDeactivationOutcomesV1.Forbidden,
            CounterpartyGroupDeactivationOutcomesV1.WarningAcknowledgementRequired,
            CounterpartyGroupDeactivationOutcomesV1.IdempotencyConflict,
            CounterpartyGroupDeactivationOutcomesV1.Failed,
            CounterpartyGroupDeactivationOutcomesV1.NotRequested,
        };

        Assert.Equal(spellings.Length, spellings.Distinct(StringComparer.Ordinal).Count());
    }
}
