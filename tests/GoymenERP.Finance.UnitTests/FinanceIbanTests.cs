using System.Reflection;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// K2 (Sinan #66 <c>5748258299</c>): an IBAN's STRUCTURE, and the two things structure is not.
/// <para>
/// The vectors below are the published ISO 13616 examples for four countries, including one whose
/// account part contains letters (FR). They are test data, not accounts: <b>SENTETIK / NOT_ENV</b>.
/// </para>
/// </summary>
public sealed class FinanceIbanTests
{
    // Published ISO 13616 examples. SENTETIK: no such account is expected to exist.
    private const string ValidGb = "GB82WEST12345698765432";
    private const string ValidDe = "DE89370400440532013000";
    private const string ValidTr = "TR330006100519786457841326";
    private const string ValidFrWithLetters = "FR1420041010050500013M02606";

    [Theory]
    [InlineData(ValidGb)]
    [InlineData(ValidDe)]
    [InlineData(ValidTr)]
    [InlineData(ValidFrWithLetters)]
    public void A_published_example_from_any_country_is_structurally_valid(string iban)
    {
        Assert.True(FinanceIban.IsStructurallyValid(iban));
    }

    /// <summary>
    /// The check digits have to DO something: one altered character makes the same-length, same-shape
    /// string invalid. Without this the rule could be length-and-charset only and still look green.
    /// </summary>
    [Theory]
    [InlineData("GB82WEST12345698765433")]
    [InlineData("TR330006100519786457841327")]
    public void One_altered_character_fails_the_checksum(string tampered)
    {
        Assert.Equal(22, ValidGb.Length);
        Assert.False(FinanceIban.IsStructurallyValid(tampered));
    }

    /// <summary>
    /// Normalize then validate, in that order, because the stored form must be the validated form: a
    /// value validated in one shape and stored in another stops matching the rule that admitted it.
    /// </summary>
    [Theory]
    [InlineData("TR33 0006 1005 1978 6457 8413 26")]
    [InlineData("tr33 0006 1005 1978 6457 8413 26")]
    [InlineData("  TR33-0006-1005-1978-6457-8413-26  ")]
    public void Spacing_hyphens_and_case_are_display_only_and_normalize_to_the_stored_form(string typed)
    {
        var normalized = FinanceIban.Normalize(typed);

        Assert.Equal(ValidTr, normalized);
        Assert.True(FinanceIban.IsStructurallyValid(normalized));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TR3300061")]                       // shorter than ISO 13616 allows
    [InlineData("TR33000610051978645784132612345")] // longer than the account part can be here
    [InlineData("T1330006100519786457841326")]      // country code must be two letters
    [InlineData("TRA30006100519786457841326")]      // check digits must be two digits
    [InlineData("TR33 0006*1005 1978 6457 8413 26")] // a character that is neither letter nor digit
    public void A_value_that_is_not_shaped_like_an_IBAN_is_refused(string? candidate)
    {
        Assert.False(FinanceIban.IsStructurallyValid(FinanceIban.Normalize(candidate)));
    }

    /// <summary>
    /// The arithmetic never converts the text to a number, so length cannot overflow anything: a 34
    /// character IBAN expands past 60 digits. Over-long input is refused by the bound, not by a throw.
    /// </summary>
    [Fact]
    public void An_absurdly_long_value_is_refused_without_arithmetic_and_without_throwing()
    {
        var absurd = "TR" + new string('9', 4000);

        Assert.False(FinanceIban.IsStructurallyValid(FinanceIban.Normalize(absurd)));
        Assert.Equal(34, FinanceIban.MaximumLength);
    }

    /// <summary>
    /// K2's two negative clauses, pinned as a DESIGN fact rather than left to prose: this type exposes
    /// normalization, validity and the two bounds — and nothing that reads a bank identifier out of an
    /// IBAN. <b>"IBAN biçim/checksum doğrulaması banka mülkiyeti/onayı yerine geçmez"</b> and
    /// <b>"eksik banka verisi icat edilmez"</b>: the bank name is typed by a person, never derived from
    /// the account number, and ownership is Ö4's approval, never a checksum.
    /// <para>
    /// A member added here that returns a bank code would turn this red. That is the point: the next
    /// person meets the clause instead of a convenience.
    /// </para>
    /// </summary>
    [Fact]
    public void The_type_offers_no_way_to_derive_bank_data_from_an_IBAN()
    {
        var members = typeof(FinanceIban)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "IsStructurallyValid", "MaximumLength", "MinimumLength", "Normalize" },
            members);
    }
}
