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
    public void Sensitive_legal_name_has_no_public_mutation_before_approval_wiring()
    {
        var property = typeof(Party).GetProperty(nameof(Party.LegalName));

        Assert.NotNull(property);
        Assert.False(property!.SetMethod?.IsPublic ?? false);
        Assert.DoesNotContain(
            typeof(Party).GetMethods(),
            method => method.Name.Contains("LegalName", StringComparison.Ordinal)
                      && method.Name != $"get_{nameof(Party.LegalName)}");
    }

    [Fact]
    public void Default_party_code_cannot_create_a_party()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Party(
            Guid.NewGuid(),
            default,
            PartyKind.LegalEntity,
            "Geçersiz Cari"));
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
}
