using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingInventoryCostRoundingTests
{
    private static readonly Guid Currency = new("24355a72-efb6-46ee-a6f6-3bc797b73047");
    private static readonly Guid Profile = new("24355a72-efb6-46ee-a6f6-3bc797b73048");

    [Theory]
    [InlineData("1", "3", 2, "0.33", "1", "300")]
    [InlineData("-1", "3", 2, "-0.33", "-1", "300")]
    [InlineData("1", "8", 2, "0.13", "-1", "200")]
    [InlineData("-1", "8", 2, "-0.13", "1", "200")]
    [InlineData("1", "250", 2, "0", "1", "250")]
    [InlineData("-1", "250", 2, "0", "-1", "250")]
    [InlineData("10001", "100", 2, "100.01", "0", "1")]
    [InlineData("1", "3", 4, "0.3333", "1", "30000")]
    [InlineData("79228162514264337593543950335", "1", 0, "79228162514264337593543950335", "0", "1")]
    public void One_component_is_rounded_once_and_the_signed_exact_residue_conserves_the_original_source(
        string numerator, string denominator, int scale, string expected, string residueN, string residueD)
    {
        var source = new AccountingInventoryCostRatioV1(numerator, denominator);
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(source, Currency, scale, out var amount, out var evidence));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), amount);
        Assert.Equal(new(residueN, residueD), evidence!.ExactSignedResidue);
        Assert.Equal(source, AccountingInventoryCostAmountsV1.Add(AccountingInventoryCostAmountsV1.FromDecimal(amount), evidence.ExactSignedResidue));
        Assert.Equal(source, evidence.ExactSignedAmount);
        Assert.Equal(expected, evidence.RoundedSignedAmount);
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(evidence, JsonSerializer.Deserialize<AccountingInventoryCostRoundingV1>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("79228162514264337593543950336", "1", 2)]
    [InlineData("1", "0", 2)]
    [InlineData("01", "1", 2)]
    [InlineData("2", "2", 2)]
    [InlineData("1", "1", 29)]
    public void Unreadable_or_out_of_range_source_is_refused_without_a_fabricated_posting(string n, string d, int scale)
    {
        Assert.False(AccountingInventoryCostRoundingV1Rules.TryRound(new(n, d), Currency, scale, out _, out var evidence));
        Assert.Null(evidence);
    }

    [Fact]
    public void An_owner_rounded_amount_preserves_its_source_proof_under_a_different_representable_journal_profile()
    {
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(new("1", "3"), Currency, 2, out var amount, out var rounding));
        var recorded = new AccountingInventoryCostRecordedRoundingV1(Currency, Profile, 7, 2, rounding!);
        var component = new AccountingInventoryCostComponentV1(AccountingInventoryCostTraceV1.PurchaseFirstComponent(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(), null, null, Guid.NewGuid(), AccountingInventoryCostAmountsV1.FromDecimal(amount), "{}", new string('a', 64), RecordedRounding: recorded);
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryResolve(component, Currency, Profile, 7, 2, out var kept, out var keptProof));
        Assert.Equal(amount, kept); Assert.Equal(rounding, keptProof);
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryResolve(component, Currency, Guid.NewGuid(), 8, 2, out var later, out var laterProof));
        Assert.Equal(amount, later); Assert.Equal(rounding, laterProof);
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryResolve(component, Currency, Profile, 7, 3, out var finer, out var finerProof));
        Assert.Equal(amount, finer); Assert.Equal(rounding, finerProof);
        Assert.False(AccountingInventoryCostRoundingV1Rules.TryResolve(component, Currency, Profile, 8, 1, out _, out _));
        Assert.False(AccountingInventoryCostRoundingV1Rules.TryResolve(component, Guid.NewGuid(), Profile, 7, 2, out _, out _));
        Assert.False(AccountingInventoryCostRoundingV1Rules.TryResolve(component, Currency, Guid.Empty, 8, 2, out _, out _));
        Assert.False(AccountingInventoryCostRoundingV1Rules.IsValidRecorded(component.SignedTlAmount,
            recorded with { Rounding = rounding! with { ExactSignedResidue = new("0", "1") } }));
    }
}
