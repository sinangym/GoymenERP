using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>ALS-D111 + ALS-D113: the legal-name change request is maker-checker; one approval by another person.</summary>
public sealed class PartyLegalNameChangeRequestTests
{
    private static readonly DateTimeOffset RequestedAt = DateTimeOffset.UnixEpoch.AddDays(1);

    [Fact]
    public void A_new_request_is_pending_with_trimmed_names_and_the_party_version_it_was_made_on()
    {
        var request = NewRequest("  Eski Unvan A.Ş. ", " Yeni Unvan A.Ş.  ");

        Assert.Equal(PartyLegalNameChangeStatus.Pending, request.Status);
        Assert.Equal("Eski Unvan A.Ş.", request.CurrentLegalName);
        Assert.Equal("Yeni Unvan A.Ş.", request.ProposedLegalName);
        Assert.Equal(3, request.PartyVersionAtRequest);
        Assert.Equal("Sicil değişikliği", request.Reason);
        Assert.Equal("maker", request.RequestedBy);
        Assert.Null(request.DecidedBy);
        Assert.Null(request.DecidedAtUtc);
        Assert.Equal(1, request.Version);
    }

    [Fact]
    public void A_request_for_the_same_name_or_without_a_reason_or_ids_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() => NewRequest("Unvan A.Ş.", " Unvan A.Ş. "));
        Assert.ThrowsAny<ArgumentException>(() => NewRequest("Unvan A.Ş.", "Başka A.Ş.", reason: "   "));
        Assert.ThrowsAny<ArgumentException>(() => NewRequest("Unvan A.Ş.", new string('x', Party.LegalNameMaximumLength + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewRequest("Unvan A.Ş.", "Başka A.Ş.", partyVersion: 0));
        Assert.Throws<ArgumentException>(() => new PartyLegalNameChangeRequest(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Unvan", "Başka", 1, "Gerekçe", "maker", RequestedAt));
        Assert.Throws<ArgumentException>(() => new PartyLegalNameChangeRequest(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "Unvan", "Başka", 1, "Gerekçe", "maker", RequestedAt));
    }

    [Fact]
    public void Another_person_approves_once_and_the_request_never_moves_again()
    {
        var request = NewRequest("Eski Unvan", "Yeni Unvan");
        var decidedAt = RequestedAt.AddHours(1);

        request.Approve("checker", "  Kontrol edildi ", decidedAt);

        Assert.Equal(PartyLegalNameChangeStatus.Approved, request.Status);
        Assert.Equal("checker", request.DecidedBy);
        Assert.Equal(decidedAt, request.DecidedAtUtc);
        Assert.Equal("Kontrol edildi", request.DecisionNote);
        Assert.Equal(2, request.Version);
        Assert.Throws<InvalidOperationException>(() => request.Reject("other", null, decidedAt));
        Assert.Throws<InvalidOperationException>(() => request.Approve("other", null, decidedAt));
        Assert.Equal(PartyLegalNameChangeStatus.Approved, request.Status);
    }

    [Fact]
    public void A_rejection_keeps_an_empty_note_as_none()
    {
        var request = NewRequest("Eski Unvan", "Yeni Unvan");

        request.Reject("checker", "   ", RequestedAt);

        Assert.Equal(PartyLegalNameChangeStatus.Rejected, request.Status);
        Assert.Null(request.DecisionNote);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_proposer_cannot_decide_their_own_request(bool approve)
    {
        var request = NewRequest("Eski Unvan", "Yeni Unvan");

        Assert.True(request.IsRequestedBy("maker"));
        Assert.False(request.IsRequestedBy("MAKER"));
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (approve)
            {
                request.Approve("maker", null, RequestedAt);
            }
            else
            {
                request.Reject("maker", null, RequestedAt);
            }
        });
        Assert.Equal(PartyLegalNameChangeStatus.Pending, request.Status);
        Assert.Equal(1, request.Version);
    }

    [Fact]
    public void A_note_past_the_limit_is_refused_and_nothing_changes()
    {
        var request = NewRequest("Eski Unvan", "Yeni Unvan");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            request.Approve("checker", new string('x', PartyLegalNameChangeRequest.NoteMaximumLength + 1), RequestedAt));
        Assert.Equal(PartyLegalNameChangeStatus.Pending, request.Status);
    }

    private static PartyLegalNameChangeRequest NewRequest(
        string current,
        string proposed,
        string reason = "Sicil değişikliği",
        long partyVersion = 3) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), current, proposed, partyVersion, reason, "maker", RequestedAt);
}
