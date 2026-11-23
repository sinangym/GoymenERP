using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

// Synthetic persisted projections; transaction/constraint acceptance belongs to the real-SQL facts.
public sealed class PartyLedgerPeriodCarryTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Currency = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Actor = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateOnly Boundary = new(2026, 12, 1);

    [Fact]
    public async Task Public_contract_omits_new_optional_fields_without_carry_and_binds_them_when_carry_exists()
    {
        await using var run = new Run(); await run.ReceiptAsync("10.00", new(2026, 12, 5));
        var legacy = await run.ReadAsync();
        using var legacyJson = JsonDocument.Parse(JsonSerializer.Serialize(legacy.Ledger, FinanceJson.CreateOptions()));
        Assert.Equal("NoneOpen018", legacyJson.RootElement.GetProperty("carryForward").GetString());
        Assert.False(legacyJson.RootElement.TryGetProperty("periodCarry", out _));
        Assert.False(legacyJson.RootElement.GetProperty("currencies")[0].TryGetProperty("openingBalance", out _));
        await run.AddCarryAsync();
        var carried = await run.ReadAsync();
        using var carriedJson = JsonDocument.Parse(JsonSerializer.Serialize(carried.Ledger, FinanceJson.CreateOptions()));
        Assert.Equal("PeriodClosureVoucher", carriedJson.RootElement.GetProperty("carryForward").GetString());
        Assert.Equal(run.Voucher.Id, carriedJson.RootElement.GetProperty("periodCarry").GetProperty("voucherId").GetGuid());
        Assert.Equal("100.00", carriedJson.RootElement.GetProperty("currencies")[0].GetProperty("openingBalance")
            .GetProperty("amount").GetProperty("money").GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Carry_starts_the_balance_without_becoming_a_range_movement_or_doubling_all_time_balance()
    {
        await using var run = new Run();
        await run.AddCarryAsync();
        await run.ReceiptAsync("100.00", new(2026, 11, 12));
        await run.ReceiptAsync("10.00", new(2026, 12, 5));
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        Assert.Equal(PartyLedgerCarryForward.PeriodClosureVoucher, result.Ledger!.CarryForward);
        Assert.Equal(run.Voucher.Id, result.Ledger.PeriodCarry!.VoucherId);
        var currency = Assert.Single(result.Ledger.Currencies);
        Assert.Equal((PartyLedgerBalanceSide.Credit, "100.00"),
            (currency.OpeningBalance!.Side, currency.OpeningBalance.Amount.Money!.Amount));
        Assert.Single(currency.Entries);
        Assert.Equal(("0.00", "10.00", "110.00"),
            (currency.DebitTotal.Money!.Amount, currency.CreditTotal.Money!.Amount, currency.ClosingBalance.Amount.Money!.Amount));
        var summary = await run.Service.SummarizeAuthorizedAsync(run.Context, new(Party, Account), default);
        Assert.Equal(PartyBalanceOutcome.Found, summary.Outcome);
        Assert.Equal("110.00", Assert.Single(summary.Balance!.Currencies).Balance.Amount.Money!.Amount);
        Assert.Equal(2, await run.Db.MoneyEvents.CountAsync());
    }

    [Fact]
    public async Task A_range_after_the_boundary_uses_actual_intervening_facts_and_keeps_carry_only_currencies()
    {
        await using var run = new Run();
        var secondCurrency = Guid.NewGuid();
        await run.AddCarryAsync([
            new("Party", Account, Party, Currency, "0.00", "100.00"),
            new("Party", Account, Party, secondCurrency, "5.125", "0.000")]);
        await run.ReceiptAsync("12.500", new(2026, 12, 5));
        await run.ReceiptAsync("7.00", new(2026, 12, 12));
        var result = await run.ReadAsync(new(2026, 12, 10));
        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        var currency = result.Ledger!.Currencies.Single(x => x.CurrencyId == Currency);
        Assert.Equal("112.500", currency.OpeningBalance!.Amount.Money!.Amount);
        Assert.Equal("119.500", currency.ClosingBalance.Amount.Money!.Amount);
        Assert.Equal("7.000", currency.CreditTotal.Money!.Amount);
        var onlyCarry = result.Ledger.Currencies.Single(x => x.CurrencyId == secondCurrency);
        Assert.Empty(onlyCarry.Entries);
        Assert.Equal("5.125", onlyCarry.ClosingBalance.Amount.Money!.Amount);
        Assert.Equal("0.000", onlyCarry.DebitTotal.Money!.Amount);
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, AmountDisclosure.Redacted)]
    [InlineData(PermissionDecision.Unavailable, AmountDisclosure.Unavailable)]
    public async Task Missing_amount_authority_masks_carry_bridge_running_and_closing_balances(
        PermissionDecision decision, AmountDisclosure disclosure)
    {
        await using var run = new Run(new Permissions { Amount = decision });
        await run.AddCarryAsync();
        await run.ReceiptAsync("12.50", new(2026, 12, 5));
        await run.ReceiptAsync("7.00", new(2026, 12, 12));
        var result = await run.ReadAsync(new(2026, 12, 10));
        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        var currency = Assert.Single(result.Ledger!.Currencies);
        foreach (var balance in new[] { currency.OpeningBalance!, currency.ClosingBalance, Assert.Single(currency.Entries).RunningBalance })
        {
            Assert.Null(balance.Side); Assert.Null(balance.Amount.Money);
            Assert.Equal(disclosure, balance.Amount.Disclosure);
        }
        Assert.Null(currency.CreditTotal.Money);
        Assert.Null(Assert.Single(currency.Entries).Amount.Money);
    }

    [Fact]
    public async Task Reversed_carry_is_ignored_and_never_creates_a_reversal_ledger_movement()
    {
        await using var run = new Run(); await run.AddCarryAsync(); await run.ReverseCarryAsync();
        await run.ReceiptAsync("7.00", new(2026, 12, 12));
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerCarryForward.NoneOpen018, result.Ledger!.CarryForward);
        Assert.Null(result.Ledger.PeriodCarry);
        var currency = Assert.Single(result.Ledger.Currencies);
        Assert.Null(currency.OpeningBalance); Assert.Single(currency.Entries);
        Assert.Equal("7.00", currency.ClosingBalance.Amount.Money!.Amount);
    }

    [Fact]
    public async Task Another_company_carry_is_never_read_even_with_the_same_party_account()
    {
        await using var run = new Run(); await run.AddCarryAsync(company: Guid.NewGuid());
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        Assert.Equal(PartyLedgerCarryForward.NoneOpen018, result.Ledger!.CarryForward);
        Assert.Empty(result.Ledger.Currencies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Corrupt_hash_or_duplicate_exact_currency_source_refuses_the_whole_ledger(bool corruptHash)
    {
        await using var run = new Run();
        var line = new FinancePeriodCarryLineV1("Party", Account, Party, Currency, "0.00", "100.00");
        await run.AddCarryAsync(corruptHash ? [line] : [line, line], corruptHash: corruptHash);
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerOutcome.Unavailable, result.Outcome); Assert.Null(result.Ledger);
    }

    [Fact]
    public async Task Source_reversed_during_final_authority_measurement_is_withheld()
    {
        var permissions = new Permissions();
        await using var run = new Run(permissions); await run.AddCarryAsync();
        permissions.OnAmount = run.ReverseCarryAsync;
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerOutcome.Unavailable, result.Outcome); Assert.Null(result.Ledger);
    }

    [Fact]
    public async Task Fresh_read_denial_during_bridge_returns_no_ledger()
    {
        await using var run = new Run(new Permissions { DenyReadAfter = 2 });
        await run.AddCarryAsync(); await run.ReceiptAsync("1.00", new(2026, 12, 5));
        var result = await run.ReadAsync(new(2026, 12, 10));
        Assert.NotEqual(PartyLedgerOutcome.Found, result.Outcome); Assert.Null(result.Ledger);
    }

    [Fact]
    public async Task Fresh_read_denial_after_carry_proof_recheck_returns_no_ledger()
    {
        await using var run = new Run(new Permissions { DenyReadAfter = 2 });
        await run.AddCarryAsync();
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerOutcome.Denied, result.Outcome); Assert.Null(result.Ledger);
    }

    [Fact]
    public async Task Fresh_amount_denial_after_carry_proof_recheck_masks_every_balance()
    {
        await using var run = new Run(new Permissions { DenyAmountAfter = 1 });
        await run.AddCarryAsync(); await run.ReceiptAsync("7.00", new(2026, 12, 12));
        var result = await run.ReadAsync();
        Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
        var currency = Assert.Single(result.Ledger!.Currencies);
        foreach (var balance in new[] { currency.OpeningBalance!, currency.ClosingBalance, Assert.Single(currency.Entries).RunningBalance })
        {
            Assert.Equal(AmountDisclosure.Redacted, balance.Amount.Disclosure);
            Assert.Null(balance.Amount.Money); Assert.Null(balance.Side);
        }
        Assert.Null(currency.CreditTotal.Money); Assert.Null(Assert.Single(currency.Entries).Amount.Money);
    }

    [Fact]
    public async Task Complete_boundary_read_uses_summary_cap_while_public_range_still_refuses_1001_rows()
    {
        await using var run = new Run();
        for (var index = 0; index < 1001; index++) run.Db.MoneyEvents.Add(run.Event("1.00", new(2026, 11, 12)));
        await run.Db.SaveChangesAsync();
        var publicResult = await run.Service.ReadAuthorizedAsync(run.Context,
            new(Party, Account, new(2026, 11, 1), new(2026, 11, 30)), default);
        Assert.Equal(PartyLedgerOutcome.TooManyEntries, publicResult.Outcome);
        var boundary = await run.Service.ReadBoundaryAuthorizedAsync(run.Context, Party, Account, new(2026, 11, 30), default);
        Assert.Equal(PartyLedgerOutcome.Found, boundary.Outcome);
        Assert.Equal("1001.00", Assert.Single(boundary.Ledger!.Currencies).ClosingBalance.Amount.Money!.Amount);
    }

    private sealed class Run : IAsyncDisposable
    {
        public FinanceDbContext Db { get; }
        public PartyLedgerService Service { get; }
        public FinanceContext Context { get; } = new(Company, Actor.ToString("D"), "synthetic-carry-ledger", null);
        public FinancePeriodCarryVoucherRow Voucher { get; private set; } = null!;
        private readonly DbContextOptions<FinanceDbContext> options;
        public Run(Permissions? permissions = null)
        {
            var company = new CompanyContext(); company.Set(Company);
            options = new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new(options, company);
            Service = new(Db, company, new User(), permissions ?? new());
        }
        public async Task AddCarryAsync(FinancePeriodCarryLineV1[]? lines = null, Guid? company = null, bool corruptHash = false)
        {
            var source = new FinancePeriodCarrySourceV1(Guid.NewGuid(), company ?? Company, Guid.NewGuid(), 2,
                new(2026, 11, 1), new(2026, 11, 30), Guid.NewGuid(), Boundary, new(2026, 12, 31), Actor,
                new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
            var json = FinanceJson.CreateOptions();
            var sourceJson = JsonSerializer.Serialize(source, json);
            var linesJson = JsonSerializer.Serialize(lines ?? [new("Party", Account, Party, Currency, "0.00", "100.00")], json);
            Voucher = new() { Id = Guid.NewGuid(), CompanyId = source.CompanyId, ClosureId = source.ClosureId,
                SourcePeriodId = source.SourcePeriodId, ClosedPeriodVersion = 2, SourceEndsOn = source.SourceEndsOn,
                TargetPeriodId = source.TargetPeriodId, TargetStartsOn = Boundary, SourceJson = sourceJson,
                SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceJson + "|" + linesJson))),
                LinesJson = linesJson, RecordedBy = Actor, RecordedAtUtc = source.RecordedAtUtc };
            if (corruptHash) Voucher.SourceHash = new string('F', 64);
            var writerCompany = new CompanyContext(); writerCompany.Set(source.CompanyId);
            await using var writer = new FinanceDbContext(options, writerCompany);
            writer.PeriodCarryVouchers.Add(Voucher); await writer.SaveChangesAsync();
        }
        public async Task ReverseCarryAsync()
        {
            Db.PeriodCarryReversals.Add(new() { Id = Guid.NewGuid(), CompanyId = Company,
                ClosureId = Voucher.ClosureId, VoucherId = Voucher.Id, RecordedBy = Guid.NewGuid(),
                RecordedAtUtc = DateTimeOffset.UtcNow, Reason = "SENTETIK yeniden açma", SourceHash = Voucher.SourceHash });
            await Db.SaveChangesAsync();
        }
        public async Task ReceiptAsync(string amount, DateOnly date) { Db.MoneyEvents.Add(Event(amount, date)); await Db.SaveChangesAsync(); }
        public MoneyEventRow Event(string amount, DateOnly date) => new() {
            Id = Guid.NewGuid(), CompanyId = Company, SourceId = Guid.NewGuid(), Kind = MoneyEventKinds.ManualBankReceiptRecorded,
            PartyId = Party, CompanyAccountId = Account, TreasuryAccountId = Guid.NewGuid(), CurrencyId = Currency,
            AmountText = amount, ProfileId = Guid.NewGuid().ToString("D"), ProfileVersion = "1", OccurredOn = date,
            RecordedAtUtc = DateTimeOffset.UtcNow, RecordedBy = Actor.ToString("D"), CorrelationId = "SENTETIK-carry" };
        public Task<PartyLedgerResult> ReadAsync(DateOnly? from = null) => Service.ReadAuthorizedAsync(Context,
            new(Party, Account, from ?? Boundary, new(2026, 12, 31)), default);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Permissions : IFinancePermissionPort
    {
        public PermissionDecision Amount { get; init; } = PermissionDecision.Allowed;
        public int DenyReadAfter { get; init; } = int.MaxValue;
        public int DenyAmountAfter { get; init; } = int.MaxValue;
        public Func<Task>? OnAmount { get; set; }
        private int reads;
        private int amountReads;
        public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken token)
        {
            if (action == FinanceAction.ReadPartyLedger) return ++reads > DenyReadAfter ? PermissionDecision.Denied : PermissionDecision.Allowed;
            if (action != FinanceAction.ReadPartyLedgerAmount) return PermissionDecision.Allowed;
            if (OnAmount is { } callback) { OnAmount = null; await callback(); }
            return ++amountReads > DenyAmountAfter ? PermissionDecision.Denied : Amount;
        }
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
}
