using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

/// <summary>SENTETIK. YOL-D31 (WP14): the party card's group-company link is set, cleared and versioned like a card field.</summary>
public sealed class PartyGroupCompanyLinkTests
{
    private static readonly Guid GroupCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [Fact]
    public void Linking_moves_one_version_a_repeat_is_no_change_and_clearing_moves_again()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.Parse("CAR-000001"), PartyKind.LegalEntity, "SENTETIK Grup Şirketi");
        Assert.Null(party.GroupCompanyId);
        var now = DateTimeOffset.UtcNow;
        Assert.True(party.ChangeGroupCompany(GroupCompany, now));
        Assert.Equal((GroupCompany, 2L), (party.GroupCompanyId!.Value, party.Version));
        Assert.False(party.ChangeGroupCompany(GroupCompany, now));
        Assert.Equal(2L, party.Version);
        Assert.True(party.ChangeGroupCompany(null, now));
        Assert.Equal(((Guid?)null, 3L), (party.GroupCompanyId, party.Version));
        Assert.Throws<ArgumentException>(() => party.ChangeGroupCompany(Guid.Empty, now));
    }
}
