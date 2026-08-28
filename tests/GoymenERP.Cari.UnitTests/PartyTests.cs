using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

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
        Assert.Equal("CAR-000042", party.Code.Value);
        Assert.Equal("Örnek Taraf", party.LegalName);
        Assert.Equal("Örnek Ticari Ad", party.TradeName);
        Assert.True(party.IsActive);
    }

    [Fact]
    public void Group_party_identity_has_no_public_mutation_before_cross_company_policy()
    {
        var publicMutationMethods = typeof(Party)
            .GetMethods()
            .Where(method =>
                method.DeclaringType == typeof(Party)
                && !method.IsStatic
                && !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(publicMutationMethods);
        Assert.False(typeof(Party).GetProperty(nameof(Party.Code))!.SetMethod?.IsPublic ?? false);
        Assert.False(typeof(Party).GetProperty(nameof(Party.LegalName))!.SetMethod?.IsPublic ?? false);
        Assert.False(typeof(Party).GetProperty(nameof(Party.TradeName))!.SetMethod?.IsPublic ?? false);
        Assert.False(typeof(Party).GetProperty(nameof(Party.IsActive))!.SetMethod?.IsPublic ?? false);
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
}
