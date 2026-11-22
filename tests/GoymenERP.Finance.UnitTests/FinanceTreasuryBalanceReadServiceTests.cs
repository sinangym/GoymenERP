using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// AI-D43 (WP-A3c): the authorized treasury ledger balance read. SENTETIK InMemory rows; InMemory proves no SQL lock.
/// The balance is the internal ledger's, read only with <c>finance.treasury-balance.read</c> for the server's actor and
/// company; another company's account is not found and a ledger that cannot be told is unavailable, never a zero.
/// </summary>
public sealed class FinanceTreasuryBalanceReadServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-1111111111b1");
    private static readonly Guid OtherCompany = Guid.Parse("11111111-1111-4111-8111-1111111111b2");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-2222222222b1");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-3333333333b1");
    private const string Actor = "44444444-4444-4444-8444-4444444444b1";

    [Fact]
    public async Task The_ledger_balance_is_incoming_minus_outgoing_and_a_dated_read_stops_at_its_day()
    {
        var (db, company) = await SeedAsync();
        db.OpeningBalanceMovements.Add(Opening("1000.00", new DateOnly(2026, 1, 1)));
        db.CashTransfers.Add(Transfer(outgoing: true, "250.50", new DateOnly(2026, 11, 25)));
        await db.SaveChangesAsync();
        var permissions = new Permissions(PermissionDecision.Allowed);
        var service = new FinanceTreasuryBalanceReadService(db, company, new User(), permissions);

        var current = await service.ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Bank, Bank, null, default);
        var dated = await service.ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Bank, Bank, new DateOnly(2026, 11, 20), default);

        Assert.Equal(ReadOutcome.Found, current.Outcome);
        Assert.Equal(("749.50", Currency, Company), (current.Resource!.Balance, current.Resource.CurrencyId, current.Resource.CompanyId));
        Assert.Equal("1000.00", dated.Resource!.Balance);
        Assert.Equal(new DateOnly(2026, 11, 20), dated.Resource.ThroughDate);
        Assert.Equal(4, permissions.Checked.Count);
        Assert.All(permissions.Checked, action => Assert.Equal(FinanceAction.ReadTreasuryBalances, action));
    }

    [Fact]
    public async Task Without_the_balance_grant_the_read_is_denied_and_an_unregistered_name_is_unavailable()
    {
        var (db, company) = await SeedAsync();
        Assert.Equal(ReadOutcome.Denied, (await new FinanceTreasuryBalanceReadService(db, company, new User(),
            new Permissions(PermissionDecision.Denied)).ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Bank, Bank, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Unavailable, (await new FinanceTreasuryBalanceReadService(db, company, new User(),
            new Permissions(PermissionDecision.Unavailable)).ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Bank, Bank, null, default)).Outcome);
    }

    [Fact]
    public async Task Another_company_account_or_context_releases_nothing()
    {
        var database = Guid.NewGuid().ToString("N");
        var (db, company) = await SeedAsync(database);
        // The other company's account is written by that company's own context on the same SENTETIK store.
        var foreignAccount = Guid.NewGuid();
        var other = new CompanyContext();
        other.Set(OtherCompany);
        await using (var foreign = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                         .UseInMemoryDatabase(database).Options, other))
        {
            foreign.BankAccounts.Add(new BankAccountRow { Id = foreignAccount, CompanyId = OtherCompany, CurrencyId = Currency,
                DisplayName = "SENTETIK baska sirket", Status = 2, Version = 2 });
            await foreign.SaveChangesAsync();
        }

        var service = new FinanceTreasuryBalanceReadService(db, company, new User(), new Permissions(PermissionDecision.Allowed));

        Assert.Equal(ReadOutcome.NotFound,
            (await service.ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Bank, foreignAccount, null, default)).Outcome);
        Assert.Equal(ReadOutcome.NotFound,
            (await service.ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Cash, Bank, null, default)).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await service.ReadAuthorizedAsync(
            new FinanceContext(OtherCompany, Actor, "corr-sentetik", null), FinanceTreasuryAccountKindV1.Bank, Bank, null, default)).Outcome);
    }

    [Fact]
    public async Task A_ledger_fact_in_another_currency_makes_the_balance_unavailable_not_zero()
    {
        var (db, company) = await SeedAsync();
        var opening = Opening("5.00", new DateOnly(2026, 1, 1));
        opening.CurrencyId = Guid.NewGuid();
        db.OpeningBalanceMovements.Add(opening);
        await db.SaveChangesAsync();

        var result = await new FinanceTreasuryBalanceReadService(db, company, new User(), new Permissions(PermissionDecision.Allowed))
            .ReadAuthorizedAsync(Context(), FinanceTreasuryAccountKindV1.Bank, Bank, null, default);

        Assert.Equal(ReadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public void The_balance_read_maps_to_its_own_registered_name()
    {
        Assert.Equal("finance.treasury-balance.read", FinancePermissionNames.TreasuryBalanceRead);
        Assert.Equal(FinancePermissionNames.TreasuryBalanceRead, FinancePermissionPort.NameOf(FinanceAction.ReadTreasuryBalances));
        Assert.Equal(ErpPermissions.FinanceTreasuryBalanceRead, FinancePermissionNames.TreasuryBalanceRead);
    }

    private static async Task<(FinanceDbContext Db, CompanyContext Company)> SeedAsync(string? database = null)
    {
        var company = new CompanyContext();
        company.Set(Company);
        var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(database ?? Guid.NewGuid().ToString("N")).Options, company);
        db.BankAccounts.Add(new BankAccountRow { Id = Bank, CompanyId = Company, CurrencyId = Currency,
            DisplayName = "SENTETIK banka", Status = 2, Version = 2 });
        await db.SaveChangesAsync();
        return (db, company);
    }

    private static FinanceContext Context() => new(Company, Actor, "corr-sentetik", null);

    private static OpeningBalanceMovementRow Opening(string amount, DateOnly occurredOn) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, OpeningBalanceId = Guid.NewGuid(), Kind = FinanceOpeningBalanceService.OpeningMovement,
        TargetKind = FinanceOpeningBalanceService.BankAccountKind, TargetId = Bank, CurrencyId = Currency,
        AmountText = amount, RecordedBy = "SENTETIK", OccurredOn = occurredOn,
    };

    private static CashTransferRow Transfer(bool outgoing, string amount, DateOnly occurredOn) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, OccurredOn = occurredOn,
        SourceKind = CashTransferAccountKinds.Bank, SourceAccountId = outgoing ? Bank : Guid.NewGuid(),
        SourceCurrencyId = Currency, SourceAmountText = amount,
        TargetKind = CashTransferAccountKinds.Bank, TargetAccountId = outgoing ? Guid.NewGuid() : Bank,
        TargetCurrencyId = Currency, TargetAmountText = amount,
    };

    private sealed class Permissions(PermissionDecision decision) : IFinancePermissionPort
    {
        public List<FinanceAction> Checked { get; } = [];

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            Checked.Add(action);
            return Task.FromResult(decision);
        }
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, OtherCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
