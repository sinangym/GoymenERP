using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Ai;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// WP-A3c Finance READ tools over SENTETIK owner reads: no database, no model, no network. Each tool reads only through the
/// Finance owner's authorized read; an owner refusal is a refusal, another company's row releases nothing, and missing
/// data (scan cap, unknown currency code, hidden amounts) is said, never guessed.
/// </summary>
public sealed class FinanceAiReadToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000f1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000f2");
    private static readonly Guid Party = Guid.Parse("cccccccc-0000-4000-8000-0000000000f3");
    private static readonly Guid UnknownCurrency = Guid.Parse("dddddddd-0000-4000-8000-0000000000f4");
    // 09:00 UTC = 12:00 Europe/Istanbul: the business day is 2026-11-22.
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 22);

    // ---- finance.due-open-items.v1 ---------------------------------------------------------------

    [Fact]
    public async Task Due_open_items_are_counted_by_direction_and_state_with_an_explicit_currency_code_and_no_total()
    {
        var owner = new OpenItems();
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today.AddDays(-3), "1250.50");
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "10.00");
        owner.Add(FinanceOpenItemDirectionV1.Payable, Today.AddDays(5), "99.90");
        owner.Add(FinanceOpenItemDirectionV1.Payable, Today.AddDays(30), "5.00");
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today.AddDays(-1), "0.00", FinanceOpenItemStatusV1.Closed);
        owner.Add(FinanceOpenItemDirectionV1.Receivable, null, "7.00");

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.True(root.GetProperty("currencyCodesResolved").GetBoolean());
        Assert.Equal("2026-11-22", root.GetProperty("businessDate").GetString());
        Assert.Equal("2026-11-29", root.GetProperty("dueUntil").GetString());
        Assert.Equal(1, root.GetProperty("receivableOverdueCount").GetInt32());
        Assert.Equal(1, root.GetProperty("receivableDueTodayCount").GetInt32());
        Assert.Equal(0, root.GetProperty("receivableUpcomingCount").GetInt32());
        Assert.Equal(0, root.GetProperty("payableOverdueCount").GetInt32());
        Assert.Equal(1, root.GetProperty("payableUpcomingCount").GetInt32());
        Assert.Equal(1, root.GetProperty("openWithoutDueDateCount").GetInt32());
        var first = root.GetProperty("items")[0];
        Assert.Equal("Overdue", first.GetProperty("dueState").GetString());
        Assert.Equal(3, first.GetProperty("daysOverdue").GetInt32());
        Assert.Equal("1250.50", first.GetProperty("outstandingAmount").GetString());
        Assert.Equal("TRY", first.GetProperty("currencyCode").GetString());
        Assert.False(root.TryGetProperty("total", out _));
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
        Assert.Equal(Company, result.Source.TargetId);
        Assert.All(owner.Contexts, context => Assert.Equal(Company, context.CompanyId));
    }

    [Fact]
    public async Task Due_open_items_filter_by_direction_and_keep_the_limit()
    {
        var owner = new OpenItems();
        for (var index = 0; index < 4; index++)
        {
            owner.Add(FinanceOpenItemDirectionV1.Payable, Today.AddDays(-index), "1.00");
        }

        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "1.00");

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{\"direction\":\"Payable\",\"limit\":2}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal(2, content.RootElement.GetProperty("listedCount").GetInt32());
        Assert.Equal(3, content.RootElement.GetProperty("payableOverdueCount").GetInt32());
        Assert.Equal(0, content.RootElement.GetProperty("receivableDueTodayCount").GetInt32());
        Assert.All(content.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("Payable", item.GetProperty("direction").GetString()));
    }

    [Fact]
    public async Task Without_the_open_item_read_the_owner_refusal_is_denied_and_nothing_is_released()
    {
        var owner = new OpenItems { Outcome = ReadOutcome.Denied };

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task Redacted_amounts_mean_the_amount_grant_is_gone_and_the_answer_is_denied()
    {
        var owner = new OpenItems();
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "1.00", disclosure: AmountDisclosure.Redacted);

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
    }

    [Fact]
    public async Task An_open_item_of_another_company_releases_nothing()
    {
        var owner = new OpenItems();
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "1.00");
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "2.00", company: OtherCompany);

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task An_unknown_currency_code_withholds_that_amount_and_says_the_codes_are_not_resolved()
    {
        var owner = new OpenItems();
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "3.00", currency: UnknownCurrency);

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("currencyCodesResolved").GetBoolean());
        var item = content.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("outstandingAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("currencyCode").ValueKind);
    }

    [Fact]
    public async Task A_currency_code_from_the_settings_candidates_is_used_for_a_non_published_identity()
    {
        var owner = new OpenItems();
        owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "3.00", currency: UnknownCurrency);
        var tool = new FinanceDueOpenItemsAiTool(owner, new FixedTime(Now),
            new Candidates([new MoneyCurrencyCandidateV1(UnknownCurrency, "GBP", "SENTETIK sterlin")]));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.True(content.RootElement.GetProperty("currencyCodesResolved").GetBoolean());
        Assert.Equal("GBP", content.RootElement.GetProperty("items")[0].GetProperty("currencyCode").GetString());
    }

    [Fact]
    public async Task At_the_scan_cap_the_counts_are_withheld_and_the_answer_is_not_complete()
    {
        var owner = new OpenItems();
        for (var index = 0; index <= FinanceDueOpenItemsAiTool.ScanLimit; index++)
        {
            owner.Add(FinanceOpenItemDirectionV1.Receivable, Today, "1.00");
        }

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("receivableDueTodayCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("openWithoutDueDateCount").ValueKind);
    }

    [Theory]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000f1\"}")]
    [InlineData("{\"direction\":\"Both\"}")]
    [InlineData("{\"withinDays\":91}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":\"5\"}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task Malformed_or_unscoped_due_open_item_arguments_are_invalid_and_the_owner_is_not_read(string arguments)
    {
        var owner = new OpenItems();

        var result = await DueOpenItems(owner).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(owner.Contexts);
    }

    // ---- finance.due-customer-instruments.v1 -------------------------------------------------------

    [Fact]
    public async Task Due_customer_instruments_count_only_held_instruments_and_release_no_serial_drawer_or_bank()
    {
        var owner = new Instruments();
        owner.Add(CustomerInstrumentStatus.InPortfolio, Today.AddDays(-2), "500.00");
        owner.Add(CustomerInstrumentStatus.AtBankForCollection, Today, "300.00");
        owner.Add(CustomerInstrumentStatus.InPortfolio, Today.AddDays(3), "100.00", CustomerInstrumentKind.PromissoryNote);
        owner.Add(CustomerInstrumentStatus.Collected, Today.AddDays(-5), "900.00");
        owner.Add(CustomerInstrumentStatus.Bounced, Today.AddDays(-5), "900.00");
        owner.Add(CustomerInstrumentStatus.InPortfolio, Today.AddDays(60), "900.00");

        var result = await new FinanceDueCustomerInstrumentsAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(1, root.GetProperty("overdueCount").GetInt32());
        Assert.Equal(1, root.GetProperty("dueTodayCount").GetInt32());
        Assert.Equal(1, root.GetProperty("upcomingCount").GetInt32());
        Assert.Equal(2, root.GetProperty("inPortfolioCount").GetInt32());
        Assert.Equal(1, root.GetProperty("atBankForCollectionCount").GetInt32());
        Assert.Equal("TRY", root.GetProperty("items")[0].GetProperty("currencyCode").GetString());
        Assert.DoesNotContain("SENTETIK-SERI", result.ContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK-KESIDECI", result.ContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK-BANKA", result.ContentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Due_customer_instruments_without_the_owner_grant_are_denied_and_another_company_is_unavailable()
    {
        var denied = new Instruments { Outcome = ReadOutcome.Denied };
        Assert.Equal(AiToolOutcomeV1.Denied, (await new FinanceDueCustomerInstrumentsAiTool(denied, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var foreign = new Instruments();
        foreign.Add(CustomerInstrumentStatus.InPortfolio, Today, "1.00", company: OtherCompany);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new FinanceDueCustomerInstrumentsAiTool(foreign, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task A_due_instrument_with_a_hidden_amount_refuses_the_whole_answer()
    {
        var owner = new Instruments();
        owner.Add(CustomerInstrumentStatus.InPortfolio, Today, "1.00", disclosure: AmountDisclosure.Redacted);

        var result = await new FinanceDueCustomerInstrumentsAiTool(owner, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
    }

    // ---- finance.treasury-accounts.v1 (version 2, AI-D43) -------------------------------------------

    [Fact]
    public async Task Treasury_accounts_show_each_ledger_balance_with_its_currency_code_and_no_cross_account_total()
    {
        var banks = new Banks();
        var tl = banks.Add("SENTETIK Banka TL", SettingsCurrencyIdentitiesV1.TurkishLira, true);
        var usd = banks.Add("SENTETIK Banka USD", SettingsCurrencyIdentitiesV1.UnitedStatesDollar, true);
        banks.Add("SENTETIK Banka eski", SettingsCurrencyIdentitiesV1.TurkishLira, false);
        var cash = new CashAccounts();
        var kasa = cash.Add("SENTETIK Merkez kasa", SettingsCurrencyIdentitiesV1.TurkishLira, CashAccountStatus.Approved);
        var balances = new Balances();
        balances.Set(tl, "1500.25");
        balances.Set(usd, "320.00");
        balances.Currencies[usd] = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
        balances.Set(kasa, "75.5");

        var result = await Treasury(banks, cash, balances).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("bankAccountCount").GetInt32());
        Assert.Equal(1, root.GetProperty("cashAccountCount").GetInt32());
        Assert.False(root.GetProperty("balancesKnown").GetBoolean());
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        var tlItem = Assert.Single(items, item => item.GetProperty("accountId").GetGuid() == tl);
        Assert.Equal(("Known", "1500.25", "TRY"), (tlItem.GetProperty("balanceState").GetString(),
            tlItem.GetProperty("balance").GetString(), tlItem.GetProperty("currencyCode").GetString()));
        var usdItem = Assert.Single(items, item => item.GetProperty("accountId").GetGuid() == usd);
        Assert.Equal(("320.00", "USD"), (usdItem.GetProperty("balance").GetString(), usdItem.GetProperty("currencyCode").GetString()));
        var kasaItem = Assert.Single(items, item => item.GetProperty("accountId").GetGuid() == kasa);
        Assert.Equal("75.5", kasaItem.GetProperty("balance").GetString());
        Assert.Equal(JsonValueKind.Null, kasaItem.GetProperty("statement").ValueKind);
        // The inactive account's ledger has no answer here: its balance is "Unavailable", never a zero.
        var old = Assert.Single(items, item => !item.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, old.GetProperty("balance").ValueKind);
        Assert.False(root.TryGetProperty("total", out _));
        Assert.All(balances.Asked, asked => Assert.Null(asked.Through));
    }

    [Fact]
    public async Task A_bank_balance_that_differs_from_its_latest_statement_carries_the_statement_balance_and_the_difference()
    {
        var banks = new Banks();
        var account = banks.Add("SENTETIK Banka TL", SettingsCurrencyIdentitiesV1.TurkishLira, true);
        var balances = new Balances();
        balances.Set(account, "1000.00");
        var statementDate = new DateOnly(2026, 11, 20);
        balances.Set(account, "940.00", statementDate);
        var statements = new Statements();
        statements.Add(account, new DateOnly(2026, 11, 10), "800.00");
        var latest = statements.Add(account, statementDate, "900.00");

        var result = await Treasury(banks, new CashAccounts(), balances, statements).ReadAsync(new AiToolInputV1("{\"kind\":\"Bank\"}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal(1, content.RootElement.GetProperty("statementDifferenceCount").GetInt32());
        var item = content.RootElement.GetProperty("items")[0];
        Assert.Equal("1000.00", item.GetProperty("balance").GetString());
        var statement = item.GetProperty("statement");
        Assert.Equal(FinanceTreasuryAccountsAiTool.StatementDifference, statement.GetProperty("state").GetString());
        Assert.Equal(latest, statement.GetProperty("importId").GetGuid());
        Assert.Equal("2026-11-20", statement.GetProperty("statementDate").GetString());
        Assert.Equal("900.00", statement.GetProperty("statementBalance").GetString());
        Assert.Equal("940.00", statement.GetProperty("ledgerBalanceAtStatementDate").GetString());
        Assert.Equal("40.00", statement.GetProperty("difference").GetString());
        Assert.Contains(balances.Asked, asked => asked.Id == account && asked.Through == statementDate);
    }

    [Fact]
    public async Task A_matching_statement_says_matched_and_an_absent_or_unreadable_statement_does_not_refuse_the_balance()
    {
        var banks = new Banks();
        var matched = banks.Add("SENTETIK A", SettingsCurrencyIdentitiesV1.TurkishLira, true);
        var none = banks.Add("SENTETIK B", SettingsCurrencyIdentitiesV1.TurkishLira, true);
        var balances = new Balances();
        balances.Set(matched, "10.00");
        balances.Set(matched, "10.0", new DateOnly(2026, 11, 21));
        balances.Set(none, "5.00");
        var statements = new Statements();
        statements.Add(matched, new DateOnly(2026, 11, 21), "10.00");

        var result = await Treasury(banks, new CashAccounts(), balances, statements).ReadAsync(new AiToolInputV1("{\"kind\":\"Bank\"}"), Context());

        using (var content = JsonDocument.Parse(result.ContentJson!))
        {
            var items = content.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(FinanceTreasuryAccountsAiTool.StatementMatched, Single(items, matched).GetProperty("statement").GetProperty("state").GetString());
            Assert.Equal(FinanceTreasuryAccountsAiTool.StatementNone, Single(items, none).GetProperty("statement").GetProperty("state").GetString());
            Assert.Equal("5.00", Single(items, none).GetProperty("balance").GetString());
        }

        var denied = new Statements { Outcome = ReadOutcome.Denied };
        var withoutStatementGrant = await Treasury(banks, new CashAccounts(), balances, denied).ReadAsync(new AiToolInputV1("{\"kind\":\"Bank\"}"), Context());
        Assert.Equal(AiToolOutcomeV1.Succeeded, withoutStatementGrant.Outcome);
        using (var content = JsonDocument.Parse(withoutStatementGrant.ContentJson!))
        {
            Assert.All(content.RootElement.GetProperty("items").EnumerateArray(), item =>
            {
                Assert.Equal(FinanceTreasuryAccountsAiTool.StatementNotAuthorized, item.GetProperty("statement").GetProperty("state").GetString());
                Assert.Equal(JsonValueKind.Null, item.GetProperty("statement").GetProperty("statementBalance").ValueKind);
            });
        }
    }

    [Fact]
    public async Task Without_the_balance_grant_the_answer_is_denied_and_another_company_releases_nothing()
    {
        var banks = new Banks();
        banks.Add("SENTETIK Banka TL", SettingsCurrencyIdentitiesV1.TurkishLira, true);
        var denied = new Balances { Outcome = ReadOutcome.Denied };
        Assert.Equal(AiToolOutcomeV1.Denied, (await Treasury(banks, new CashAccounts(), denied)
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var deniedCash = new CashAccounts { Outcome = ReadOutcome.Denied };
        Assert.Equal(AiToolOutcomeV1.Denied, (await Treasury(banks, deniedCash, new Balances())
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var foreignBanks = new Banks();
        foreignBanks.Add("SENTETIK Banka", SettingsCurrencyIdentitiesV1.TurkishLira, true, OtherCompany);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await Treasury(foreignBanks, new CashAccounts(), new Balances())
            .ReadAsync(new AiToolInputV1("{\"kind\":\"Bank\"}"), Context())).Outcome);

        var foreignBalance = new Banks();
        var account = foreignBalance.Add("SENTETIK Banka", SettingsCurrencyIdentitiesV1.TurkishLira, true);
        var balances = new Balances { AnswerCompany = OtherCompany };
        balances.Set(account, "1.00");
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await Treasury(foreignBalance, new CashAccounts(), balances)
            .ReadAsync(new AiToolInputV1("{\"kind\":\"Bank\"}"), Context())).Outcome);
    }

    [Fact]
    public async Task A_balance_in_a_currency_without_a_known_code_is_withheld_and_cash_only_reads_no_bank()
    {
        var banks = new Banks { Outcome = ReadOutcome.Denied };
        var cash = new CashAccounts();
        var kasa = cash.Add("SENTETIK kasa", UnknownCurrency, CashAccountStatus.Approved);
        var balances = new Balances();
        balances.Set(kasa, "12.00");
        balances.Currencies[kasa] = UnknownCurrency;

        var result = await Treasury(banks, cash, balances).ReadAsync(new AiToolInputV1("{\"kind\":\"Cash\"}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        Assert.Equal(0, banks.Calls);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("currencyCodesResolved").GetBoolean());
        var item = content.RootElement.GetProperty("items")[0];
        Assert.Equal(FinanceTreasuryAccountsAiTool.BalanceUnavailable, item.GetProperty("balanceState").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("balance").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("currencyCode").ValueKind);
    }

    [Fact]
    public void The_finance_descriptors_are_read_only_owner_tools_with_their_owner_permissions()
    {
        var descriptors = new[]
        {
            DueOpenItems(new OpenItems()).Descriptor,
            new FinanceDueCustomerInstrumentsAiTool(new Instruments(), new FixedTime(Now)).Descriptor,
            Treasury(new Banks(), new CashAccounts(), new Balances()).Descriptor,
        };

        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Equal("Finance", descriptor.OwnerModule);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.NotEmpty(descriptor.RequiredPermissions);
            Assert.DoesNotContain(descriptor.FieldAllowlist, field => field.Contains("company", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(AiDataClassV1.Confidential, descriptors[0].DataClass);
        Assert.Equal(AiDataClassV1.Confidential, descriptors[1].DataClass);
        Assert.Equal([FinancePermissionNames.OpenItemAmountRead, FinancePermissionNames.OpenItemRead], descriptors[0].RequiredPermissions);
    }

    // ---- fixtures -------------------------------------------------------------------------------

    private static FinanceDueOpenItemsAiTool DueOpenItems(OpenItems owner) => new(owner, new FixedTime(Now));

    private static FinanceTreasuryAccountsAiTool Treasury(Banks banks, CashAccounts cash, Balances balances, Statements? statements = null) =>
        new(banks, cash, balances, new FixedTime(Now), null, statements);

    private static JsonElement Single(JsonElement[] items, Guid accountId) =>
        Assert.Single(items, item => item.GetProperty("accountId").GetGuid() == accountId);

    private static AiServerContextV1 Context() => new(Company, "11111111-1111-4111-8111-1111111111f1", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-wpa3c-sentetik", null, null, null);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Candidates(IReadOnlyList<MoneyCurrencyCandidateV1> items) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(items);
    }

    /// <summary>A SENTETIK FI04 owner read: pages of 50, newest first, as the owner pages them.</summary>
    private sealed class OpenItems : IFinanceOpenItemReadServiceV1
    {
        private readonly List<FinanceOpenItemViewV1> rows = [];

        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;
        public List<FinanceContext> Contexts { get; } = [];

        public void Add(FinanceOpenItemDirectionV1 direction, DateOnly? dueOn, string outstanding,
            FinanceOpenItemStatusV1 status = FinanceOpenItemStatusV1.Open, Guid? currency = null, Guid? company = null,
            AmountDisclosure disclosure = AmountDisclosure.Visible)
        {
            var currencyId = currency ?? SettingsCurrencyIdentitiesV1.TurkishLira;
            var amount = disclosure == AmountDisclosure.Visible
                ? new AmountView(AmountDisclosure.Visible, new MoneyText(outstanding, currencyId))
                : new AmountView(disclosure, null);
            rows.Add(new FinanceOpenItemViewV1(Guid.NewGuid(), company ?? Company, "SENTETIK-kaynak", Guid.NewGuid(), Guid.NewGuid(),
                "1", Party, Guid.NewGuid(), currencyId, "1", dueOn, disclosure == AmountDisclosure.Visible ? status : null,
                Now.AddMinutes(-rows.Count), amount, amount, direction));
        }

        public Task<ReadResult<FinanceOpenItemPageV1>> ListAuthorizedAsync(FinanceContext context, SourceListRequest request,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            if (Outcome != ReadOutcome.Found)
            {
                return Task.FromResult(new ReadResult<FinanceOpenItemPageV1>(Outcome, null));
            }

            var skip = request.Cursor is null ? 0 : int.Parse(request.Cursor, System.Globalization.CultureInfo.InvariantCulture);
            var page = rows.Skip(skip).Take(50).ToArray();
            var next = skip + page.Length < rows.Count ? (skip + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new ReadResult<FinanceOpenItemPageV1>(ReadOutcome.Found,
                new FinanceOpenItemPageV1(context.CompanyId, page, next)));
        }

        public Task<ReadResult<FinanceOpenItemViewV1>> DetailAuthorizedAsync(FinanceContext context, Guid openItemId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Instruments : ICustomerInstrumentService
    {
        private readonly List<CustomerInstrumentView> rows = [];

        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;

        public void Add(CustomerInstrumentStatus status, DateOnly dueOn, string amount,
            CustomerInstrumentKind kind = CustomerInstrumentKind.Cheque, Guid? company = null,
            AmountDisclosure disclosure = AmountDisclosure.Visible)
        {
            var money = disclosure == AmountDisclosure.Visible
                ? new AmountView(AmountDisclosure.Visible, new MoneyText(amount, SettingsCurrencyIdentitiesV1.TurkishLira))
                : new AmountView(disclosure, null);
            rows.Add(new CustomerInstrumentView(Guid.NewGuid(), company ?? Company, "1", kind, status, Party, Guid.NewGuid(),
                "SENTETIK-SERI-" + rows.Count, "SENTETIK-KESIDECI", kind == CustomerInstrumentKind.Cheque ? "SENTETIK-BANKA" : null,
                null, dueOn, Today.AddDays(-30), SettingsCurrencyIdentitiesV1.TurkishLira, money, "SENTETIK aciklama", null,
                Today.AddDays(-30), null, "SENTETIK", Now.AddDays(-30)));
        }

        public Task<ReadResult<CustomerInstrumentPage>> ListAuthorizedAsync(FinanceContext context, string? cursor,
            CancellationToken cancellationToken) =>
            Task.FromResult(Outcome == ReadOutcome.Found
                ? new ReadResult<CustomerInstrumentPage>(ReadOutcome.Found, new CustomerInstrumentPage(context.CompanyId, rows, null))
                : new ReadResult<CustomerInstrumentPage>(Outcome, null));

        public Task<CustomerInstrumentCommandResult> ReceiveAsync(FinanceContext context, CommandMetadata metadata,
            CustomerInstrumentReceiveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CustomerInstrumentCommandResult> ChangeStatusAsync(FinanceContext context, CommandMetadata metadata,
            CustomerInstrumentStatusChangeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CustomerInstrumentCommandResult> CollectAsync(FinanceContext context, CommandMetadata metadata,
            CustomerInstrumentCollectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadResult<CustomerInstrumentDetail>> DetailAuthorizedAsync(FinanceContext context, Guid instrumentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadResult<CustomerInstrumentRiskView>> RiskAuthorizedAsync(FinanceContext context,
            CustomerInstrumentRiskRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Banks : IFinanceBankAccountReader
    {
        private readonly List<BankAccountView> rows = [];

        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;
        public int Calls { get; private set; }

        public Guid Add(string name, Guid currency, bool active, Guid? company = null)
        {
            var id = Guid.NewGuid();
            rows.Add(new BankAccountView(id, company ?? Company, currency, name, active, active, "1"));
            return id;
        }

        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Outcome == ReadOutcome.Found
                ? new ReadResult<BankAccountPage>(ReadOutcome.Found, new BankAccountPage(context.CompanyId, rows))
                : new ReadResult<BankAccountPage>(Outcome, null));
        }

        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CashAccounts : IFinanceCashAccountMaster
    {
        private readonly List<CashAccountMasterItem> rows = [];

        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;

        public Guid Add(string name, Guid currency, CashAccountStatus status)
        {
            var id = Guid.NewGuid();
            rows.Add(new CashAccountMasterItem(new CashAccountView(id, Company, currency, name, status,
                status == CashAccountStatus.Approved, null, null, "1"), false, false));
            return id;
        }

        public Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Outcome == ReadOutcome.Found
                ? new ReadResult<CashAccountMasterPage>(ReadOutcome.Found, new CashAccountMasterPage(context.CompanyId, rows))
                : new ReadResult<CashAccountMasterPage>(Outcome, null));

        public Task<CashAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountProposeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CashAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountApproveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CashAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountRejectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CashAccountCommandResult> ProposeStateChangeAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountStateChange change, CashAccountStateChangeRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CashAccountCommandResult> ApproveStateChangeAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountStateChange change, CashAccountStateChangeRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CashAccountCommandResult> RejectStateChangeAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountStateChange change, CashAccountStateChangeRejectRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadResult<CashAccountView>> GetAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>A SENTETIK AI-D43 ledger balance read: answers only the balances it was given, per (account, through date).</summary>
    private sealed class Balances : IFinanceTreasuryBalanceReadV1
    {
        private readonly Dictionary<(Guid, DateOnly?), string> answers = [];

        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;
        public Guid? AnswerCompany { get; init; }
        public List<(Guid Id, DateOnly? Through)> Asked { get; } = [];

        public void Set(Guid accountId, string balance, DateOnly? through = null) => answers[(accountId, through)] = balance;

        public Task<ReadResult<FinanceTreasuryBalanceViewV1>> ReadAuthorizedAsync(FinanceContext context, FinanceTreasuryAccountKindV1 kind,
            Guid accountId, DateOnly? throughDate, CancellationToken cancellationToken)
        {
            Asked.Add((accountId, throughDate));
            if (Outcome != ReadOutcome.Found)
            {
                return Task.FromResult(new ReadResult<FinanceTreasuryBalanceViewV1>(Outcome, null));
            }

            if (!answers.TryGetValue((accountId, throughDate), out var balance))
            {
                return Task.FromResult(new ReadResult<FinanceTreasuryBalanceViewV1>(ReadOutcome.Unavailable, null));
            }

            var currency = Currencies.TryGetValue(accountId, out var known) ? known : SettingsCurrencyIdentitiesV1.TurkishLira;
            return Task.FromResult(new ReadResult<FinanceTreasuryBalanceViewV1>(ReadOutcome.Found,
                new FinanceTreasuryBalanceViewV1(AnswerCompany ?? context.CompanyId, kind, accountId, currency, throughDate, balance)));
        }

        public Dictionary<Guid, Guid> Currencies { get; } = [];
    }

    /// <summary>A SENTETIK WP12 statement read: parsed imports per bank account, each with one last line and its balance.</summary>
    private sealed class Statements : IFinanceBankStatementImportServiceV1
    {
        private readonly List<(FinanceBankStatementImportViewV1 Import, FinanceBankStatementLineViewV1 Line)> imports = [];

        public ReadOutcome Outcome { get; init; } = ReadOutcome.Found;

        public Guid Add(Guid bankAccountId, DateOnly lastOn, string? balance, string? currency = "TRY")
        {
            var id = Guid.NewGuid();
            var import = new FinanceBankStatementImportViewV1(id, Company, "1", bankAccountId, FinanceBankStatementFormatV1.Csv, "SENTETIK",
                FinanceBankStatementImportStateV1.Parsed, Guid.NewGuid(), 1, new string('a', 64), null, null, 1, 0, lastOn.AddDays(-3), lastOn,
                "SENTETIK", Now.AddDays(-1), "SENTETIK", Now.AddDays(-1));
            var line = new FinanceBankStatementLineViewV1(Guid.NewGuid(), 1, lastOn, null, FinanceBankStatementDirectionV1.Inflow, "1.00",
                balance, currency, null, "SENTETIK aciklama", null,
                new FinanceBankStatementSuggestionV1(FinanceBankStatementMatchStatusV1.Manual, FinanceBankStatementManualReasonV1.NoExactCandidate, null));
            imports.Add((import, line));
            return id;
        }

        public Task<FinanceBankStatementListResultV1> ListAuthorizedAsync(FinanceContext context, Guid? bankAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Outcome == ReadOutcome.Found
                ? new FinanceBankStatementListResultV1(ReadOutcome.Found, new FinanceBankStatementImportListV1(context.CompanyId,
                    imports.Where(entry => entry.Import.BankAccountId == bankAccountId).Select(entry => entry.Import).ToArray()))
                : new FinanceBankStatementListResultV1(Outcome, null));

        public Task<FinanceBankStatementDetailResultV1> GetAuthorizedAsync(FinanceContext context, Guid importId,
            CancellationToken cancellationToken)
        {
            var entry = imports.Single(candidate => candidate.Import.Id == importId);
            return Task.FromResult(new FinanceBankStatementDetailResultV1(ReadOutcome.Found,
                new FinanceBankStatementImportDetailV1(entry.Import, [entry.Line], 0, 1)));
        }

        public Task<FinanceBankStatementCommandResultV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
            ReserveFinanceBankStatementImportV1 request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FinanceBankStatementCommandResultV1> ParseAsync(FinanceContext context, CommandMetadata metadata, Guid importId,
            ParseFinanceBankStatementImportV1 request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadOutcome> CheckDocumentOwnerAsync(FinanceContext context, Guid importId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinanceBankStatementMatchCommandResultV1> ConfirmMatchAsync(FinanceContext context, CommandMetadata metadata,
            Guid importId, ConfirmFinanceBankStatementMatchV1 request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FinanceBankStatementMatchCommandResultV1> ReverseMatchAsync(FinanceContext context, CommandMetadata metadata,
            Guid matchId, ReverseFinanceBankStatementMatchV1 request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
