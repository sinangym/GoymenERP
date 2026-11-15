using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// FNS-D52: the bank CSV column mapping rules are closed and exact — a code is accepted as written or refused, never trimmed,
/// folded or defaulted — the layout's own columns are required and the other layout's absent, every column is in range and
/// no column is used twice. The column numbers and codes below are rule inputs, not a mapping of any real bank.
/// </summary>
public sealed class BankCsvMappingRulesV1Tests
{
    private static readonly BankCsvMappingValuesV1 Signed = new(
        BankCsvMappingStatesV1.Active,
        BankCsvDelimitersV1.Semicolon,
        SkipLeadingRows: 0,
        HasHeaderRow: true,
        BankCsvDecimalSeparatorsV1.Comma,
        BankCsvThousandsSeparatorsV1.Period,
        "dd.MM.yyyy",
        BankCsvAmountLayoutsV1.SignedAmount,
        BankCsvSignedAmountDirectionsV1.Inflow,
        TransactionDateColumn: 1,
        ValueDateColumn: null,
        DescriptionColumn: 2,
        AmountColumn: 3,
        InflowColumn: null,
        OutflowColumn: null,
        BalanceColumn: null,
        ReferenceColumn: null,
        CurrencyColumn: null);

    private static readonly BankCsvMappingValuesV1 TwoColumns = Signed with
    {
        AmountLayout = BankCsvAmountLayoutsV1.InflowOutflowColumns,
        SignedAmountPositiveMeans = null,
        AmountColumn = null,
        InflowColumn = 3,
        OutflowColumn = 4,
    };

    [Theory]
    [InlineData("A")]
    [InlineData("0")]
    [InlineData("BANK_A-1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345")]
    public void A_bank_key_of_upper_case_ascii_letters_digits_underscore_and_hyphen_is_accepted(string key)
    {
        Assert.True(BankCsvMappingRulesV1.IsBankKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bank")]
    [InlineData("Bank")]
    [InlineData(" BANK")]
    [InlineData("BANK ")]
    [InlineData("_BANK")]
    [InlineData("-BANK")]
    [InlineData("BANK.A")]
    [InlineData("BANKİ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void A_bank_key_that_is_not_exact_is_refused(string? key)
    {
        Assert.False(BankCsvMappingRulesV1.IsBankKey(key));
    }

    [Fact]
    public void A_display_name_is_trimmed_and_bounded_and_refused_with_control_characters()
    {
        Assert.True(BankCsvMappingRulesV1.TryNormalizeDisplayName("  SENTETIK Banka  ", out var normalized));
        Assert.Equal("SENTETIK Banka", normalized);
        Assert.False(BankCsvMappingRulesV1.TryNormalizeDisplayName(null, out _));
        Assert.False(BankCsvMappingRulesV1.TryNormalizeDisplayName("   ", out _));
        Assert.False(BankCsvMappingRulesV1.TryNormalizeDisplayName("SENTETIK\nBanka", out _));
        Assert.True(BankCsvMappingRulesV1.TryNormalizeDisplayName(
            new string('B', BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength), out _));
        Assert.False(BankCsvMappingRulesV1.TryNormalizeDisplayName(
            new string('B', BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength + 1), out _));
    }

    [Fact]
    public void Every_published_code_parses_to_its_character_and_nothing_else_does()
    {
        Assert.Equal(new[] { ';', ',', '\t' }, BankCsvDelimitersV1.All.Select(code =>
        {
            Assert.True(BankCsvMappingRulesV1.TryParseDelimiter(code, out var delimiter));
            return delimiter;
        }));
        Assert.Equal(new[] { ',', '.' }, BankCsvDecimalSeparatorsV1.All.Select(code =>
        {
            Assert.True(BankCsvMappingRulesV1.TryParseDecimalSeparator(code, out var separator));
            return separator;
        }));
        Assert.Equal(new char?[] { null, '.', ',', ' ' }, BankCsvThousandsSeparatorsV1.All.Select(code =>
        {
            Assert.True(BankCsvMappingRulesV1.TryParseThousandsSeparator(code, out var separator));
            return separator;
        }));
        Assert.All(BankCsvDateFormatsV1.All, code => Assert.True(BankCsvMappingRulesV1.IsDateFormat(code)));
        Assert.All(BankCsvMappingStatesV1.All, code => Assert.True(BankCsvMappingRulesV1.IsState(code)));

        Assert.False(BankCsvMappingRulesV1.TryParseDelimiter("semicolon", out _));
        Assert.False(BankCsvMappingRulesV1.TryParseDelimiter(";", out _));
        Assert.False(BankCsvMappingRulesV1.TryParseDecimalSeparator(" Comma", out _));
        Assert.False(BankCsvMappingRulesV1.TryParseThousandsSeparator(null, out _));
        Assert.False(BankCsvMappingRulesV1.IsDateFormat("d.M.yyyy"));
        Assert.False(BankCsvMappingRulesV1.IsDateFormat("DD.MM.YYYY"));
        Assert.False(BankCsvMappingRulesV1.IsState("active"));
    }

    [Fact]
    public void The_amount_layout_and_signed_direction_codes_map_one_to_one_onto_the_typed_answer()
    {
        Assert.True(BankCsvMappingRulesV1.TryParseAmountLayout(BankCsvAmountLayoutsV1.SignedAmount, out var signed));
        Assert.Equal(SettingsBankCsvAmountLayoutV1.SignedAmount, signed);
        Assert.True(BankCsvMappingRulesV1.TryParseAmountLayout(BankCsvAmountLayoutsV1.InflowOutflowColumns, out var two));
        Assert.Equal(SettingsBankCsvAmountLayoutV1.InflowOutflowColumns, two);
        Assert.False(BankCsvMappingRulesV1.TryParseAmountLayout("Signed", out _));
        Assert.True(BankCsvMappingRulesV1.TryParseSignedAmountDirection(BankCsvSignedAmountDirectionsV1.Inflow, out var inflow));
        Assert.Equal(SettingsBankCsvSignedAmountDirectionV1.Inflow, inflow);
        Assert.True(BankCsvMappingRulesV1.TryParseSignedAmountDirection(BankCsvSignedAmountDirectionsV1.Outflow, out var outflow));
        Assert.Equal(SettingsBankCsvSignedAmountDirectionV1.Outflow, outflow);
        Assert.False(BankCsvMappingRulesV1.TryParseSignedAmountDirection(null, out _));
        Assert.False(BankCsvMappingRulesV1.TryParseSignedAmountDirection("Debit", out _));
    }

    [Fact]
    public void Column_and_skipped_row_bounds_are_the_published_limits()
    {
        Assert.True(BankCsvMappingRulesV1.IsColumn(1));
        Assert.True(BankCsvMappingRulesV1.IsColumn(BankCsvMappingInputLimitsV1.MaximumColumnNumber));
        Assert.False(BankCsvMappingRulesV1.IsColumn(0));
        Assert.False(BankCsvMappingRulesV1.IsColumn(null));
        Assert.False(BankCsvMappingRulesV1.IsColumn(BankCsvMappingInputLimitsV1.MaximumColumnNumber + 1));
        Assert.True(BankCsvMappingRulesV1.IsSkipLeadingRows(0));
        Assert.True(BankCsvMappingRulesV1.IsSkipLeadingRows(BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows));
        Assert.False(BankCsvMappingRulesV1.IsSkipLeadingRows(-1));
        Assert.False(BankCsvMappingRulesV1.IsSkipLeadingRows(null));
        Assert.False(BankCsvMappingRulesV1.IsSkipLeadingRows(BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows + 1));
    }

    [Fact]
    public void A_complete_mapping_of_either_layout_has_no_refusal()
    {
        Assert.Null(BankCsvMappingRulesV1.Refusal(Signed));
        Assert.Null(BankCsvMappingRulesV1.Refusal(TwoColumns));
        Assert.Null(BankCsvMappingRulesV1.Refusal(Signed with
        {
            State = BankCsvMappingStatesV1.Inactive,
            ThousandsSeparator = BankCsvThousandsSeparatorsV1.None,
            ValueDateColumn = 5,
            BalanceColumn = 6,
            ReferenceColumn = 7,
            CurrencyColumn = 8,
        }));
        Assert.Equal(new[] { 1, 2, 3, 4 }, BankCsvMappingRulesV1.Columns(TwoColumns));
    }

    private static readonly Dictionary<string, BankCsvMappingValuesV1> BrokenMappings = new(StringComparer.Ordinal)
    {
        { "state", Signed with { State = null } },
        { "delimiter", Signed with { Delimiter = "Pipe" } },
        { "skip rows", Signed with { SkipLeadingRows = null } },
        { "header", Signed with { HasHeaderRow = null } },
        { "decimal", Signed with { DecimalSeparator = null } },
        { "thousands", Signed with { ThousandsSeparator = "Apostrophe" } },
        { "same separators", Signed with { ThousandsSeparator = BankCsvThousandsSeparatorsV1.Comma } },
        { "date format", Signed with { DateFormat = "dd.MM.yy" } },
        { "layout", Signed with { AmountLayout = null } },
        { "signed without amount", Signed with { AmountColumn = null } },
        { "signed with inflow", Signed with { InflowColumn = 4 } },
        { "signed without direction", Signed with { SignedAmountPositiveMeans = null } },
        { "two columns with amount", TwoColumns with { AmountColumn = 5 } },
        { "two columns without outflow", TwoColumns with { OutflowColumn = null } },
        { "two columns with direction", TwoColumns with { SignedAmountPositiveMeans = BankCsvSignedAmountDirectionsV1.Outflow } },
        { "no transaction date", Signed with { TransactionDateColumn = null } },
        { "no description", Signed with { DescriptionColumn = 0 } },
        { "optional out of range", Signed with { BalanceColumn = BankCsvMappingInputLimitsV1.MaximumColumnNumber + 1 } },
        { "column used twice", Signed with { ReferenceColumn = 3 } },
        { "inflow equals outflow", TwoColumns with { OutflowColumn = 3 } },
    };

    public static TheoryData<string> BrokenRules => new(BrokenMappings.Keys);

    [Theory]
    [MemberData(nameof(BrokenRules))]
    public void A_broken_mapping_is_refused_with_a_turkish_detail(string rule)
    {
        var refusal = BankCsvMappingRulesV1.Refusal(BrokenMappings[rule]);
        Assert.False(string.IsNullOrWhiteSpace(refusal), rule);
    }
}
