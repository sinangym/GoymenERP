using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class NegotiableInstrumentEventPolicyTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000016");

    [Theory]
    [InlineData(NegotiableInstrumentState.Portfolio)]
    [InlineData(NegotiableInstrumentState.AtBank)]
    [InlineData(NegotiableInstrumentState.Collateral)]
    [InlineData(NegotiableInstrumentState.Endorsed)]
    [InlineData(NegotiableInstrumentState.Collected)]
    [InlineData(NegotiableInstrumentState.Dishonored)]
    [InlineData(NegotiableInstrumentState.Returned)]
    [InlineData(NegotiableInstrumentState.Protested)]
    public void Locked_non_reversal_states_are_representable_without_a_reversal_link(
        NegotiableInstrumentState state)
    {
        var issues = NegotiableInstrumentEventPolicy.Validate(Valid() with { State = state }, CompanyId);

        Assert.Empty(issues);
    }

    [Fact]
    public void Reversal_requires_another_historical_event_and_preserves_the_link()
    {
        var sourceEventId = Guid.NewGuid();
        var candidate = Valid() with
        {
            EventId = Guid.NewGuid(),
            State = NegotiableInstrumentState.Reversed,
            ReversesEventId = sourceEventId,
        };

        var issues = NegotiableInstrumentEventPolicy.Validate(candidate, CompanyId);

        Assert.Empty(issues);
        Assert.Equal(sourceEventId, candidate.ReversesEventId);
    }

    [Fact]
    public void Reversal_without_source_or_reversing_itself_is_refused()
    {
        var missing = Valid() with { State = NegotiableInstrumentState.Reversed };
        var self = missing with { ReversesEventId = missing.EventId };

        Assert.Contains(
            NegotiableInstrumentEventPolicy.Validate(missing, CompanyId),
            issue => issue.Code == "finance_instrument_reversal_source_required");
        Assert.Contains(
            NegotiableInstrumentEventPolicy.Validate(self, CompanyId),
            issue => issue.Code == "finance_instrument_reversal_source_required");
    }

    [Fact]
    public void Ordinary_state_cannot_smuggle_a_reversal_source()
    {
        var issues = NegotiableInstrumentEventPolicy.Validate(
            Valid() with { ReversesEventId = Guid.NewGuid() },
            CompanyId);

        Assert.Contains(issues, issue => issue.Code == "finance_instrument_reversal_source_not_allowed");
    }

    [Fact]
    public void Foreign_company_empty_identities_and_invalid_amount_are_refused()
    {
        var candidate = Valid() with
        {
            InstrumentId = Guid.Empty,
            DocumentId = Guid.Empty,
            FaceValue = new MoneyText("0", Guid.Empty),
        };

        var issues = NegotiableInstrumentEventPolicy.Validate(candidate, Guid.NewGuid());

        Assert.Contains(issues, issue => issue.Code == "company_context_invalid");
        Assert.Contains(issues, issue => issue.Code == "identity_required");
        Assert.Contains(issues, issue => issue.Code == "finance_instrument_amount_invalid");
    }

    [Fact]
    public void Noncanonical_versions_missing_maturity_and_non_utc_occurrence_are_refused()
    {
        var candidate = Valid() with
        {
            ExpectedVersion = "0",
            Profile = new ProfileReference("not-a-profile-id", "version"),
            MaturityDate = default,
            OccurredAtUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(3)),
        };

        var issues = NegotiableInstrumentEventPolicy.Validate(candidate, CompanyId);

        Assert.Contains(issues, issue => issue.Code == "finance_instrument_version_invalid");
        Assert.Contains(issues, issue => issue.Code == "finance_instrument_maturity_required");
        Assert.Contains(issues, issue => issue.Code == "finance_instrument_occurrence_invalid");
    }

    private static NegotiableInstrumentEventV1 Valid() => new(
        Guid.NewGuid(),
        CompanyId,
        Guid.NewGuid(),
        "3",
        NegotiableInstrumentKind.Cheque,
        NegotiableInstrumentDirection.Receivable,
        NegotiableInstrumentState.Portfolio,
        new MoneyText("25000.00", Guid.NewGuid()),
        new ProfileReference(Guid.NewGuid().ToString("D"), "2"),
        new DateOnly(2026, 12, 31),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "finance-user",
        new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero),
        "Instrument state evidence",
        ReversesEventId: null);
}
