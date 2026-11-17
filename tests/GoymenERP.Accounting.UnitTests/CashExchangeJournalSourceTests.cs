using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Accounting.UnitTests;

public sealed class CashExchangeJournalSourceTests
{
    private static readonly Guid Company = Guid.Parse("b2980000-0000-4000-8000-000000000001");
    private static readonly Guid Usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
    private static readonly DateOnly Day = new(2026, 11, 17);
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("3100", "3300", "Credit", "200")]
    [InlineData("3400", "3300", "Debit", "100")]
    [InlineData("3300", "3300", null, null)]
    public void Only_actual_settlement_and_approved_carrying_value_determine_balanced_lines(string carrying, string receipt, string? side, string? difference)
    {
        var lines = Assert.IsAssignableFrom<IReadOnlyList<NormalJournalExactSourceLineV1>>(CashExchangeJournalAmountsV1.TryCreate(carrying, receipt));
        Assert.Equal(new(CashExchangeJournalAmountsV1.TargetReceipt, "Debit", receipt), lines[0]);
        Assert.Equal(new(CashExchangeJournalAmountsV1.SourceCarrying, "Credit", carrying), lines[1]);
        Assert.Equal(side is null ? 2 : 3, lines.Count);
        if (side is not null) Assert.Equal((side, difference), (lines[2].Side, lines[2].Amount));
        Assert.True(NormalJournalExactSourceAmountsV1.Valid(lines));
    }

    [Fact]
    public void Exact_constraints_do_not_hide_a_cent_in_decimal_sum_or_overflow_at_maximum()
    {
        const decimal third = 333333333333333333333333333.33m;
        Assert.False(NormalJournalExactSourceAmountsV1.TotalMatches([third, third, third], 1000000000000000000000000000m));
        Assert.True(NormalJournalExactSourceAmountsV1.TotalMatches([decimal.MaxValue - 1, 1m], decimal.MaxValue));
        Assert.Null(CashExchangeJournalAmountsV1.TryCreate("0.01", "79228162514264337593543950335")); // Exact difference cannot fit Decimal.
        Assert.Null(CashExchangeJournalAmountsV1.TryCreate("0", "3300"));
        Assert.Null(CashExchangeJournalAmountsV1.TryCreate("100.00000000000000000000000000001", "3300"));
    }

    [Fact]
    public void Balanced_but_changed_leg_amounts_cannot_replace_approved_source_components()
    {
        var lines = CashExchangeJournalAmountsV1.TryCreate("3100", "3300")!;
        var proposed = lines.Select(x => new ProposedLine(x.MappingKey, new AccountRef(Company, Guid.NewGuid()),
            Enum.Parse<PostingSide>(x.Side), decimal.Parse(x.Amount, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Assert.True(NormalJournalExactSourceAmountsV1.Matches(lines, proposed));
        proposed[1] = proposed[1] with { Amount = 3200m };
        proposed[2] = proposed[2] with { Amount = 100m };
        Assert.False(NormalJournalExactSourceAmountsV1.Matches(lines, proposed)); // debit and credit both still 3300.
    }

    [Fact]
    public void Raw_exchange_is_nonpostable_and_approved_book_value_retains_real_proof_instead_of_tcmb_value()
    {
        var company = new CompanyContext(); company.Set(Company);
        var source = Source(); var value = BookValue(source);
        var raw = FinanceJournalSourceReaderV1.Read(Envelope(source), company);
        Assert.Equal("100", raw.Amount); Assert.Equal(Usd, raw.CurrencyId);
        Assert.Equal("SourceBookValuePending", raw.CashExchange!.PostingState);
        Assert.False(NormalJournalSourceKindsV1.IsSupported(raw.Kind));
        var approved = FinanceJournalSourceReaderV1.Read(Envelope(source, value), company);
        Assert.Equal("3100", approved.Amount); Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, approved.CurrencyId);
        Assert.Equal("ApprovedSourceBookValue", approved.CashExchange!.PostingState);
        Assert.Equal("3400", source.Exchange!.SourceReferenceTl!.Amount);
        Assert.Equal(value.TransferSourceHash, FinanceJournalSourceReaderV1.TransferHash(source));
        Assert.True(NormalJournalSourceKindsV1.IsSupported(approved.Kind));
    }

    [Fact]
    public void Actual_target_foreign_currency_does_not_become_tl_from_its_comparison()
    {
        var source = Source(SettingsCurrencyIdentitiesV1.Euro, "90"); var value = BookValue(source);
        var company = new CompanyContext(); company.Set(Company);
        var fact = FinanceJournalSourceReaderV1.Read(Envelope(source, value), company);
        Assert.Equal("TargetBookValueUnavailable", fact.CashExchange!.PostingState);
        Assert.Equal("90", fact.CashExchange.TargetAmount);
        Assert.NotEqual("90", source.Exchange!.TargetReferenceTl!.Amount);
    }

    [Fact]
    public void Two_documented_functional_values_allow_foreign_target_without_using_either_tcmb_reference()
    {
        var source = Source(SettingsCurrencyIdentitiesV1.Euro, "90");
        var value = BookValue(source) with { TargetFunctionalValueTl = "3330" };
        var company = new CompanyContext(); company.Set(Company);
        var fact = FinanceJournalSourceReaderV1.Read(Envelope(source, value), company);
        Assert.Equal("ApprovedSourceBookValue", fact.CashExchange!.PostingState);
        Assert.Equal("3330", fact.CashExchange.TargetFunctionalValueTl);
        Assert.Equal("3330", FinanceJournalSourceReaderV1.TargetFunctionalAmount(source, value));
        Assert.Equal("90", fact.CashExchange.TargetAmount);
        Assert.Equal("3400", source.Exchange!.SourceReferenceTl!.Amount);
        Assert.Equal("3240", source.Exchange.TargetReferenceTl!.Amount);
        Assert.Equal("230", CashExchangeJournalAmountsV1.TryCreate(value.SourceBookValueTl, value.TargetFunctionalValueTl!)![2].Amount);
        var legacy = JsonSerializer.Serialize(BookValue(source), FinanceJson.CreateOptions());
        Assert.DoesNotContain("targetFunctionalValueTl", legacy);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1e2")]
    [InlineData("3301")]
    public void Documented_target_tl_must_be_positive_exact_and_match_an_actual_try_leg(string value)
    {
        var source = Source(); var company = new CompanyContext(); company.Set(Company);
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(
            Envelope(source, BookValue(source) with { TargetFunctionalValueTl = value }), company));
        Assert.True(FinanceJournalSourceReaderV1.ValidApprovedExchange(source,
            BookValue(source) with { TargetFunctionalValueTl = "3300.00" }, Company));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("maker")]
    [InlineData("company")]
    [InlineData("transfer")]
    [InlineData("amount")]
    [InlineData("status")]
    [InlineData("evidence")]
    [InlineData("time")]
    [InlineData("unknown")]
    [InlineData("comparison")]
    public void Broken_approved_source_cannot_enter_the_accounting_inbox(string change)
    {
        var source = Source(); var envelope = Envelope(source, BookValue(source));
        var payload = JsonNode.Parse(envelope.PayloadJson)!; var book = payload["bookValue"]!;
        switch (change)
        {
            case "hash": book["transferSourceHash"] = new string('f', 64); break;
            case "maker": book["decidedBy"] = book["preparedBy"]!.GetValue<string>(); break;
            case "company": book["companyId"] = Guid.NewGuid(); break;
            case "transfer": book["transferId"] = Guid.NewGuid(); break;
            case "amount": book["sourceBookValueTl"] = "0"; break;
            case "status": book["status"] = "Pending"; break;
            case "evidence": book["evidence"]!["versionNo"] = 0; break;
            case "time": book["decidedAtUtc"] = At.AddDays(-1); break;
            case "unknown": payload["invented"] = true; break;
            case "comparison": payload["source"]!["exchange"]!["sourceReferenceTl"]!["amount"] = "1"; break;
        }
        var company = new CompanyContext(); company.Set(Company);
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(envelope with { PayloadJson = payload.ToJsonString() }, company));
        Assert.Throws<OutboxPermanentFailureException>(() => FinanceJournalSourceReaderV1.Read(envelope with { CompanyId = Guid.NewGuid() }, company));
    }

    private static CashTransferView Source(Guid? targetCurrency = null, string targetAmount = "3300")
    {
        var target = targetCurrency ?? SettingsCurrencyIdentitiesV1.TurkishLira;
        var preparation = new CashTransferPreparation(CashTransferKind.CurrencyExchange, CashTransferAccountKind.Bank, Guid.NewGuid(),
            new("100", Usd), new("SENTETIK-usd", "1"), CashTransferAccountKind.Bank, Guid.NewGuid(),
            new(targetAmount, target), new("SENTETIK-target", "1"), null, Day, null, null, "SENTETIK exchange");
        var comparison = new CashTransferComparisonSourceV1(Usd, "34", Day, "TCMB", Guid.NewGuid(), "1", Day.AddDays(-1), "SENTETIK / NOT_ENV");
        var targetComparison = target == SettingsCurrencyIdentitiesV1.TurkishLira
            ? new CashTransferComparisonSourceV1(target, "1", Day, "FunctionalCurrency", null, null, null, null)
            : new(target, "36", Day, "TCMB", Guid.NewGuid(), "1", Day.AddDays(-1), "SENTETIK / NOT_ENV");
        return new(Guid.NewGuid(), Company, preparation, Guid.NewGuid().ToString("D"), At, true, "1",
            CashTransferExchangeCalculatorV1.Calculate(Company, Day, preparation.SourceMoney, preparation.TargetMoney,
                preparation.SourceProfile, preparation.TargetProfile, comparison, targetComparison));
    }
    private static CashExchangeBookValueViewV1 BookValue(CashTransferView source) => new(Guid.NewGuid(), Company, source.Id, source.Version,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source, FinanceJson.CreateOptions())))).ToLowerInvariant(),
        "3100", new(Guid.NewGuid(), 1, new string('a', 64)), "SENTETIK actual carrying proof", Guid.NewGuid().ToString("D"), At.AddMinutes(1),
        CashExchangeBookValueStatusV1.Approved, "2", Guid.NewGuid().ToString("D"), At.AddMinutes(2), null);
    private static WorkInboxEventV1 Envelope(CashTransferView source, CashExchangeBookValueViewV1? value = null)
    {
        var id = Guid.NewGuid();
        return new(FinanceJournalSourceInboxV1.SourceName, id, Company, value is null ? NormalJournalSourceKindsV1.RecordedCashExchange
            : NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue, 1,
            value is null ? JsonSerializer.Serialize(new FinanceCashExchangeRecordedV1(id, source), FinanceJson.CreateOptions())
                : JsonSerializer.Serialize(new FinanceCashExchangeBookValueApprovedV1(id, source, value), FinanceJson.CreateOptions()),
            value?.DecidedAtUtc ?? source.RecordedAtUtc, "SENTETIK-exchange-source", null);
    }
}
