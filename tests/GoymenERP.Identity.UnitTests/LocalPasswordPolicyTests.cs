using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-2 password policy (KIM-D4, KIM-D12): the configured length window counted in Unicode scalar values and the
/// blocklist of <c>admin</c>, the user name and company name derivatives. SENTETIK; passwords are made at run time.
/// </summary>
public sealed class LocalPasswordPolicyTests
{
    private const int MinimumLength = 8;
    private const int MaximumLength = 128;

    [Fact]
    public void A_password_inside_the_window_and_off_the_blocklist_has_no_violation()
    {
        var policy = Policy();

        Assert.Empty(policy.Evaluate(SyntheticPasswords.New(), ["kullanici", "Ornek Sirket"]));
    }

    [Fact]
    public void Length_is_counted_in_unicode_scalar_values_not_utf16_units()
    {
        var policy = new LocalPasswordPolicy(4, 4, blocklistEnabled: false, []);
        var fourScalars = string.Concat(Enumerable.Repeat("\U0001F600", 4));
        var threeScalars = string.Concat(Enumerable.Repeat("\U0001F600", 3));

        Assert.Equal(8, fourScalars.Length);
        Assert.Empty(policy.Evaluate(fourScalars, []));
        Assert.Equal([LocalPasswordPolicyViolationCodes.TooShort], policy.Evaluate(threeScalars, []));
    }

    [Fact]
    public void A_password_outside_the_window_reports_the_length_rule()
    {
        var policy = Policy();

        Assert.Equal(
            [LocalPasswordPolicyViolationCodes.TooShort],
            policy.Evaluate(SyntheticPasswords.New()[..(MinimumLength - 1)], []));
        Assert.Equal(
            [LocalPasswordPolicyViolationCodes.TooLong],
            policy.Evaluate(string.Concat(Enumerable.Repeat(SyntheticPasswords.New(), 5))[..(MaximumLength + 1)], []));
    }

    [Fact]
    public void The_fixed_admin_term_and_its_derivatives_are_blocked()
    {
        var policy = Policy();
        var digits = Digits();

        Assert.Equal(
            [LocalPasswordPolicyViolationCodes.Blocklisted],
            policy.Evaluate(LocalPasswordBlocklist.FixedTerm.ToUpperInvariant() + digits + "!", []));
        Assert.Equal(
            [LocalPasswordPolicyViolationCodes.TooShort, LocalPasswordPolicyViolationCodes.Blocklisted],
            policy.Evaluate(LocalPasswordBlocklist.FixedTerm, []));
    }

    [Fact]
    public void The_user_name_and_a_company_name_are_blocked_with_their_derivatives()
    {
        var policy = Policy();
        var digits = Digits();

        Assert.Equal(
            [LocalPasswordPolicyViolationCodes.Blocklisted],
            policy.Evaluate("Kurucu.Kullanici" + digits, ["kurucukullanici"]));
        Assert.Equal(
            [LocalPasswordPolicyViolationCodes.Blocklisted],
            policy.Evaluate("ÖrnekGıdaAş-" + digits, ["Örnek Gıda A.Ş."]));
        Assert.Empty(policy.Evaluate("ÖrnekGıdaAşKasa-" + digits, ["Örnek Gıda A.Ş."]));
    }

    [Fact]
    public void A_configured_term_is_normalized_and_blocked()
    {
        var policy = new LocalPasswordPolicy(MinimumLength, MaximumLength, blocklistEnabled: true, ["  Sentetik Kelime  ", "SENTETIK KELIME", " "]);

        Assert.Equal(["sentetik kelime"], policy.BlockedTerms);
        Assert.Equal([LocalPasswordPolicyViolationCodes.Blocklisted], policy.Evaluate("SentetikKelime" + Digits(), []));
    }

    [Fact]
    public void A_disabled_blocklist_checks_only_the_length_window()
    {
        var policy = new LocalPasswordPolicy(MinimumLength, MaximumLength, blocklistEnabled: false, []);

        Assert.Empty(policy.Evaluate(LocalPasswordBlocklist.FixedTerm + Digits(), ["kullanici"]));
    }

    [Theory]
    [InlineData(0, 128)]
    [InlineData(8, 7)]
    public void An_invalid_window_is_refused(int minimumLength, int maximumLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalPasswordPolicy(minimumLength, maximumLength, true, []));
    }

    private static LocalPasswordPolicy Policy() => new(MinimumLength, MaximumLength, blocklistEnabled: true, []);

    /// <summary>Six random digits, so no derivative in this file is a fixed password.</summary>
    private static string Digits() =>
        System.Security.Cryptography.RandomNumberGenerator.GetInt32(100_000, 1_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
}
