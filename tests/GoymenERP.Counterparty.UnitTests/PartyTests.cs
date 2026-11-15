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
        // AT-009: the one Party mutation (its VKN/TCKN) is internal to the module; the write guard and the maker-checker
        // command are its only way in.
        Assert.False(typeof(Party).GetProperty(nameof(Party.TaxIdentityNumber))!.SetMethod?.IsPublic ?? false);
    }

    [Fact]
    public void Trade_name_change_trims_moves_the_version_once_and_leaves_the_legal_name()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(7), PartyKind.LegalEntity, "Örnek Taraf A.Ş.");
        var at = DateTimeOffset.UnixEpoch.AddDays(1);

        Assert.True(party.ChangeTradeName("  Örnek  ", at));

        Assert.Equal("Örnek", party.TradeName);
        Assert.Equal("Örnek Taraf A.Ş.", party.LegalName);
        Assert.Equal(2, party.Version);
        Assert.Equal(at, party.UpdatedAtUtc);
    }

    [Fact]
    public void Trade_name_change_to_the_stored_value_is_a_no_op_without_a_new_version()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(8), PartyKind.LegalEntity, "Örnek", "Ticari");

        Assert.False(party.ChangeTradeName(" Ticari ", DateTimeOffset.UnixEpoch.AddDays(1)));

        Assert.Equal("Ticari", party.TradeName);
        Assert.Equal(1, party.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_trade_name_clears_it(string? cleared)
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(9), PartyKind.LegalEntity, "Örnek", "Ticari");

        Assert.True(party.ChangeTradeName(cleared, DateTimeOffset.UnixEpoch.AddDays(1)));

        Assert.Null(party.TradeName);
        Assert.Equal(2, party.Version);
    }

    [Fact]
    public void Trade_name_past_the_limit_is_refused_and_nothing_changes()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(10), PartyKind.LegalEntity, "Örnek", "Ticari");
        var tooLong = new string('x', Party.TradeNameMaximumLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => party.ChangeTradeName(tooLong, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => Party.NormalizeTradeName(tooLong));

        Assert.Equal("Ticari", party.TradeName);
        Assert.Equal(1, party.Version);
        Assert.Equal(
            new string('x', Party.TradeNameMaximumLength),
            Party.NormalizeTradeName(new string('x', Party.TradeNameMaximumLength)));
    }

    [Fact]
    public void Legal_name_change_trims_moves_the_version_once_and_leaves_the_trade_name()
    {
        // ALS-D95 / 19.22: one step, no approval; the version moves once.
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(11), PartyKind.LegalEntity, "Eski Unvan A.Ş.", "Ticari");
        var at = DateTimeOffset.UnixEpoch.AddDays(1);

        Assert.True(party.ChangeLegalName("  Yeni Unvan A.Ş.  ", at));

        Assert.Equal("Yeni Unvan A.Ş.", party.LegalName);
        Assert.Equal("Ticari", party.TradeName);
        Assert.Null(party.TaxIdentityNumber);
        Assert.Equal(2, party.Version);
        Assert.Equal(at, party.UpdatedAtUtc);
    }

    [Fact]
    public void Legal_name_change_to_the_stored_value_is_a_no_op_without_a_new_version()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(12), PartyKind.LegalEntity, "Örnek A.Ş.");

        Assert.False(party.ChangeLegalName(" Örnek A.Ş. ", DateTimeOffset.UnixEpoch.AddDays(1)));

        Assert.Equal("Örnek A.Ş.", party.LegalName);
        Assert.Equal(1, party.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_legal_name_is_refused_and_nothing_changes(string blank)
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(13), PartyKind.LegalEntity, "Örnek A.Ş.");

        var refused = Assert.ThrowsAny<ArgumentException>(() => party.ChangeLegalName(blank, DateTimeOffset.UnixEpoch));
        Assert.IsNotType<ArgumentOutOfRangeException>(refused);
        Assert.ThrowsAny<ArgumentException>(() => Party.NormalizeLegalName(null));

        Assert.Equal("Örnek A.Ş.", party.LegalName);
        Assert.Equal(1, party.Version);
    }

    [Fact]
    public void Legal_name_past_the_limit_is_refused_and_nothing_changes()
    {
        var party = new Party(Guid.NewGuid(), PartyCode.FromSequence(14), PartyKind.LegalEntity, "Örnek A.Ş.");
        var tooLong = new string('x', Party.LegalNameMaximumLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => party.ChangeLegalName(tooLong, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => Party.NormalizeLegalName(tooLong));

        Assert.Equal("Örnek A.Ş.", party.LegalName);
        Assert.Equal(1, party.Version);
        Assert.Equal(
            new string('x', Party.LegalNameMaximumLength),
            Party.NormalizeLegalName(new string('x', Party.LegalNameMaximumLength)));
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
