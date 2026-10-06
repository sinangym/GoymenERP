using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>
/// AT-009 domain rules (user decision 2026-09-30): the first number is entered directly; an existing number changes only
/// through a request that a DIFFERENT person decides, and only if the party still carries the number the request saw.
/// </summary>
public sealed class PartyTaxIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaxIdentityNumber Vkn = TaxIdentityNumber.Parse("1234567890");
    private static readonly TaxIdentityNumber OtherVkn = TaxIdentityNumber.Parse("9876543217");

    [Fact]
    public void The_first_entry_is_direct_and_moves_the_version_once()
    {
        var party = NewParty();
        party.EnterTaxIdentityNumber(Vkn, Now);
        Assert.Equal("1234567890", party.TaxIdentityNumber);
        Assert.Equal(2, party.Version);
    }

    [Fact]
    public void An_existing_number_is_never_replaced_by_a_second_entry()
    {
        var party = NewParty();
        party.EnterTaxIdentityNumber(Vkn, Now);
        Assert.Throws<InvalidOperationException>(() => party.EnterTaxIdentityNumber(OtherVkn, Now));
        Assert.Equal("1234567890", party.TaxIdentityNumber);
        Assert.Equal(2, party.Version);
    }

    [Fact]
    public void An_unparsed_number_is_refused()
    {
        var party = NewParty();
        Assert.Throws<ArgumentException>(() => party.EnterTaxIdentityNumber(default, Now));
        Assert.Null(party.TaxIdentityNumber);
    }

    [Fact]
    public void An_approved_change_applies_only_against_the_number_it_was_made_for()
    {
        var party = NewParty();
        party.EnterTaxIdentityNumber(Vkn, Now);
        Assert.Throws<InvalidOperationException>(
            () => party.ApplyApprovedTaxIdentityChange("1111111114", OtherVkn, Now));
        Assert.Throws<InvalidOperationException>(
            () => party.ApplyApprovedTaxIdentityChange("1234567890", Vkn, Now));
        party.ApplyApprovedTaxIdentityChange("1234567890", OtherVkn, Now);
        Assert.Equal("9876543217", party.TaxIdentityNumber);
        Assert.Equal(3, party.Version);
    }

    [Fact]
    public void A_request_needs_an_existing_different_number_and_a_reason()
    {
        Assert.Throws<ArgumentException>(() => NewRequest(current: null));
        Assert.Throws<ArgumentException>(() => NewRequest(current: "9876543217"));
        Assert.Throws<ArgumentException>(() => NewRequest(reason: "  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewRequest(reason: new string('x', 1001)));
        var request = NewRequest();
        Assert.Equal(PartyTaxIdentityChangeStatus.Pending, request.Status);
        Assert.Equal("1234567890", request.CurrentTaxIdentityNumber);
        Assert.Equal("9876543217", request.ProposedTaxIdentityNumber);
        Assert.Null(request.DecidedBy);
    }

    [Fact]
    public void The_maker_can_neither_approve_nor_reject_their_own_request()
    {
        var request = NewRequest();
        Assert.Throws<InvalidOperationException>(() => request.Approve("maker", null, Now));
        Assert.Throws<InvalidOperationException>(() => request.Reject(" maker ", "no", Now));
        Assert.Equal(PartyTaxIdentityChangeStatus.Pending, request.Status);
        Assert.Equal(1, request.Version);
    }

    [Fact]
    public void Another_person_decides_once()
    {
        var request = NewRequest();
        request.Approve("checker", "  belge görüldü ", Now);
        Assert.Equal(PartyTaxIdentityChangeStatus.Approved, request.Status);
        Assert.Equal("checker", request.DecidedBy);
        Assert.Equal("belge görüldü", request.DecisionNote);
        Assert.Equal(2, request.Version);
        Assert.Throws<InvalidOperationException>(() => request.Reject("third", null, Now));

        var rejected = NewRequest();
        rejected.Reject("checker", "  ", Now);
        Assert.Equal(PartyTaxIdentityChangeStatus.Rejected, rejected.Status);
        Assert.Null(rejected.DecisionNote);
    }

    private static Party NewParty() =>
        new(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "Örnek Metal A.Ş.");

    private static PartyTaxIdentityChangeRequest NewRequest(
        string? current = "1234567890", string reason = "Vergi levhası yenilendi") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), current, OtherVkn, 2, reason, "maker", Now);
}
