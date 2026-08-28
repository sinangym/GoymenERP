using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Cari.UnitTests;

public sealed class PartyTests
{
    [Theory]
    [InlineData(PartyKind.LegalEntity)]
    [InlineData(PartyKind.NaturalPerson)]
    public void Supported_party_kinds_share_one_party_model(PartyKind kind)
    {
        var party = new Party(
            Guid.NewGuid(),
            PartyCode.FromSequence(42),
            kind,
            "Örnek Taraf",
            "Örnek Ticari Ad");

        Assert.Equal(kind, party.Kind);
        Assert.Equal("CAR-000042", party.CariCode.Value);
        Assert.Equal("Örnek Taraf", party.LegalName);
        Assert.Equal("Örnek Ticari Ad", party.TradeName);
        Assert.True(party.IsActive);
    }

    [Fact]
    public void Cari_code_is_not_mutable_after_creation()
    {
        var property = typeof(Party).GetProperty(nameof(Party.CariCode));

        Assert.NotNull(property);
        Assert.False(property!.SetMethod?.IsPublic ?? false);
    }

    [Fact]
    public void Deactivate_and_reactivate_preserve_identity()
    {
        var id = Guid.NewGuid();
        var code = PartyCode.FromSequence(9);
        var party = new Party(id, code, PartyKind.LegalEntity, "Göymen Test A.Ş.");

        party.Deactivate();
        Assert.False(party.IsActive);
        Assert.Equal(id, party.Id);
        Assert.Equal(code, party.CariCode);

        party.Reactivate();
        Assert.True(party.IsActive);
        Assert.Equal(id, party.Id);
        Assert.Equal(code, party.CariCode);
    }

    [Fact]
    public void Approved_legal_name_change_is_explicitly_named()
    {
        var party = new Party(
            Guid.NewGuid(),
            PartyCode.FromSequence(10),
            PartyKind.LegalEntity,
            "Eski Unvan");

        party.ApplyApprovedLegalName("Yeni Unvan");

        Assert.Equal("Yeni Unvan", party.LegalName);
    }
}
