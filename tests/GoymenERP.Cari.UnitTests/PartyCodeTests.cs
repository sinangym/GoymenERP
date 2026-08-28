using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class PartyCodeTests
{
    [Fact]
    public void From_sequence_uses_group_wide_visible_format()
    {
        var code = PartyCode.FromSequence(1);

        Assert.Equal("CAR-000001", code.Value);
    }

    [Fact]
    public void Parse_normalizes_case_and_whitespace()
    {
        var code = PartyCode.Parse("  car-000125  ");

        Assert.Equal("CAR-000125", code.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CAR-000000")]
    [InlineData("CAR-12")]
    [InlineData("SUP-000001")]
    [InlineData("CAR-00A001")]
    [InlineData("CAR-0000001")]
    public void Invalid_or_noncanonical_codes_are_rejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => PartyCode.Parse(value));
    }
}
