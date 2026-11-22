using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

/// <summary>WP12 / FNS-D52: SENTETIK files only. The parser reads exactly what the Settings mapping says, nothing else.</summary>
public sealed class FinanceBankStatementParserTests
{
    internal static SettingsBankCsvMappingV1 SignedMapping(Guid? company = null) => new(company ?? Guid.NewGuid(),
        new ProfileVersionV1(Guid.NewGuid(), 3), "SENTETIK-BANKA", "Sentetik Banka", ';', 1, true, ',', '.', "dd.MM.yyyy",
        SettingsBankCsvAmountLayoutV1.SignedAmount, SettingsBankCsvSignedAmountDirectionV1.Inflow,
        new SettingsBankCsvColumnsV1(1, 2, 3, 4, null, null, 5, 6, null));

    private static SettingsBankCsvMappingV1 InflowOutflowMapping() => new(Guid.NewGuid(),
        new ProfileVersionV1(Guid.NewGuid(), 1), "SENTETIK-IKI", "Sentetik Iki", ',', 0, false, '.', null, "yyyy-MM-dd",
        SettingsBankCsvAmountLayoutV1.InflowOutflowColumns, null,
        new SettingsBankCsvColumnsV1(1, null, 2, null, 3, 4, null, null, 5));

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Signed_csv_is_read_with_the_mapping_separators_skip_rows_header_and_sign_meaning()
    {
        var file = Utf8("SENTETIK BANKA EKSTRESI\nTarih;Valor;Aciklama;Tutar;Bakiye;Referans\n"
                        + "17.11.2026;18.11.2026;Gelen EFT SENTETIK;1.234,50;10.000,00;R-1\n"
                        + "18.11.2026;;=SUM(A1);-12,5;-2,50;\n\n");
        var result = FinanceBankStatementParser.ParseCsv(file, SignedMapping());
        Assert.True(result.IsValid);
        Assert.Equal(2, result.Lines.Count);
        var first = result.Lines[0];
        Assert.Equal((3, new DateOnly(2026, 11, 17), (DateOnly?)new DateOnly(2026, 11, 18), FinanceBankStatementDirectionV1.Inflow,
                "1234.50", "10000.00", "R-1", "Gelen EFT SENTETIK"),
            (first.LineNumber, first.TransactionOn, first.ValueOn, first.Direction, first.Amount, first.Balance, first.Reference,
                first.Description));
        var second = result.Lines[1];
        Assert.Equal((FinanceBankStatementDirectionV1.Outflow, "12.5", "-2.50", (string?)null, "'=SUM(A1)", (DateOnly?)null),
            (second.Direction, second.Amount, second.Balance, second.Reference, second.Description, second.ValueOn));
    }

    [Fact]
    public void Inflow_outflow_csv_takes_exactly_one_non_zero_side_and_a_currency_code()
    {
        var file = Utf8("2026-11-17,Havale SENTETIK,100.00,0.00,TRY\n2026-11-18,\"Odeme, SENTETIK\",,25.10,TRY\n");
        var result = FinanceBankStatementParser.ParseCsv(file, InflowOutflowMapping());
        Assert.True(result.IsValid);
        Assert.Equal([(FinanceBankStatementDirectionV1.Inflow, "100.00"), (FinanceBankStatementDirectionV1.Outflow, "25.10")],
            result.Lines.Select(line => (line.Direction, line.Amount)).ToArray());
        Assert.Equal("Odeme, SENTETIK", result.Lines[1].Description);
        Assert.All(result.Lines, line => Assert.Equal("TRY", line.CurrencyText));
    }

    [Theory]
    [InlineData("2026-11-17,Iki taraf,10.00,5.00,TRY\n", "amount")]
    [InlineData("2026-11-17,Hic,,,TRY\n", "amount")]
    [InlineData("17.11.2026,Tarih,10.00,,TRY\n", "transactionDate")]
    [InlineData("2026-11-17,Para,10.00,,tl\n", "currency")]
    [InlineData("2026-11-17,,10.00,,TRY\n", "description")]
    [InlineData("2026-11-17,Negatif,-10.00,,TRY\n", "amount")]
    public void An_invalid_value_refuses_the_whole_file_and_returns_no_line(string text, string field)
    {
        var result = FinanceBankStatementParser.ParseCsv(Utf8("2026-11-16,Gecerli,1.00,,TRY\n" + text), InflowOutflowMapping());
        Assert.False(result.IsValid);
        Assert.Empty(result.Lines);
        Assert.Contains(result.Issues, issue => issue.LineNumber == 2 && issue.Field == field);
    }

    [Theory]
    [InlineData("17.11.2026;;SENTETIK;12.34,50;;\n")]
    [InlineData("17.11.2026;;SENTETIK;1,2,3;;\n")]
    [InlineData("17.11.2026;;SENTETIK;0,00;;\n")]
    [InlineData("17.11.2026;;SENTETIK;;\n")]
    public void A_misgrouped_zero_or_missing_signed_amount_is_refused(string row)
    {
        var result = FinanceBankStatementParser.ParseCsv(Utf8("baslik\nTarih\n" + row), SignedMapping());
        Assert.False(result.IsValid);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Invalid_utf8_control_characters_and_broken_quotes_are_refused_before_any_line()
    {
        Assert.Contains(FinanceBankStatementParser.ParseCsv([0xC3, 0x28], SignedMapping()).Issues, issue => issue.Code == "utf8_invalid");
        Assert.Contains(FinanceBankStatementParser.ParseCsv(Utf8("a\u0001b"), SignedMapping()).Issues,
            issue => issue.Code == "control_character");
        Assert.Contains(FinanceBankStatementParser.ParseCsv(Utf8("x\ny\n17.11.2026;;\"acik;1,00;;\n"), SignedMapping()).Issues,
            issue => issue.Code == "csv_quote_invalid");
        Assert.Contains(FinanceBankStatementParser.ParseCsv([], SignedMapping()).Issues, issue => issue.Code == "file_empty");
    }

    [Fact]
    public void Mt940_lines_take_entry_date_mark_amount_references_and_information()
    {
        var file = Utf8("{1:F01SENTETIK}{2:I940}{4:\n:20:SENTETIK1\n:25:TR420001000000000000000001\n:28C:1/1\n"
                        + ":60F:C261116TRY1000,00\n"
                        + ":61:2611171117C1234,5NTRFREF-1//BANK-1\nEK BILGI\n:86:Gelen havale TR13 0006 2000 0000 0000 0000 02\n"
                        + ":61:2612310101D10,NMSCNONREF//BANK-2\n:86:Masraf\n"
                        + ":61:261118RC5,00NTRFNONREF//BANK-3\n"
                        + ":62F:C261118TRY2219,50\n-}");
        var result = FinanceBankStatementParser.ParseMt940(file);
        Assert.True(result.IsValid, string.Join(",", result.Issues.Select(issue => issue.Code)));
        Assert.Equal(["TR420001000000000000000001"], result.AccountIdentifiers);
        Assert.Equal(3, result.Lines.Count);
        var first = result.Lines[0];
        Assert.Equal((new DateOnly(2026, 11, 17), (DateOnly?)new DateOnly(2026, 11, 17), FinanceBankStatementDirectionV1.Inflow,
                "1234.5", "REF-1", "TRY", "Gelen havale TR13 0006 2000 0000 0000 0000 02 EK BILGI"),
            (first.TransactionOn, first.ValueOn, first.Direction, first.Amount, first.Reference, first.CurrencyText, first.Description));
        var yearEnd = result.Lines[1];
        Assert.Equal((new DateOnly(2027, 1, 1), (DateOnly?)new DateOnly(2026, 12, 31), FinanceBankStatementDirectionV1.Outflow, "10",
            "BANK-2"), (yearEnd.TransactionOn, yearEnd.ValueOn, yearEnd.Direction, yearEnd.Amount, yearEnd.Reference));
        var reversal = result.Lines[2];
        Assert.Equal((FinanceBankStatementDirectionV1.Outflow, "5.00", "BANK-3", "BANK-3"),
            (reversal.Direction, reversal.Amount, reversal.Reference, reversal.Description));
    }

    [Theory]
    [InlineData(":20:X\n:25:A\n:61:2611171117C10,00NTRFX\n:86:Bakiye yok\n")]
    [InlineData(":20:X\n:25:A\n:60F:C261116TRY1,00\n:61:2611171117X10,00NTRFX\n")]
    [InlineData(":20:X\n:25:A\n:60F:C261116TRY1,00\n:61:2613171117C10,00NTRFX\n")]
    [InlineData(":20:X\n:25:A\n:60F:C261116TRY1,00\n:61:2611171117C10.00NTRFX\n")]
    [InlineData("Serbest metin\n")]
    public void Malformed_mt940_is_refused_as_a_whole(string text)
    {
        var result = FinanceBankStatementParser.ParseMt940(Utf8(text));
        Assert.False(result.IsValid);
        Assert.Empty(result.Lines);
    }
}
