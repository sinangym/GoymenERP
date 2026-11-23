using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// B-05 / M-03: the party-account read port (Cari Hesaplar list and one account's summary with its open items) over
/// stored open items, in memory. Only OPEN items count; receivable and payable are summed exactly per currency and never
/// across currencies (OPEN-012); another company's rows are never read; amounts and open items need the amount
/// authority; a revoked or unavailable authority withholds everything; a broken stored row withholds the answer.
/// SENTETIK / NOT_ENV; the SQL evidence is <c>FinancePartyAccountSqlServerRelationalAcceptanceTests</c>.
/// </summary>
public sealed class PartyLedgerReadServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Try = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Usd = Guid.Parse("23232323-2323-4232-8232-232323232323");
    private static readonly Guid Party = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Account = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid OtherParty = Guid.Parse("46464646-4646-4464-8464-464646464646");
    private static readonly Guid OtherAccount = Guid.Parse("57575757-5757-4575-8575-575757575757");
    private const string Actor = "SENTETIK-muhasebe";

    [Fact]
    public async Task The_list_sums_only_open_items_per_account_and_currency_exactly_and_never_reads_another_company()
    {
        await using var run = new Run();
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "1000.10", "600.05");
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "200", "200");
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Payable, "50.5", "50.5");
        await run.SeedAsync(Company, Party, Account, Usd, FinanceOpenItemDirectionCodes.Receivable, "75.25", "75.25");
        // A closed and a cancelled item carry no open amount and do not count.
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "900", "0",
            FinanceOpenItemStatuses.Closed);
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "800", "800",
            FinanceOpenItemStatuses.Cancelled);
        await run.SeedAsync(Company, OtherParty, OtherAccount, Try, FinanceOpenItemDirectionCodes.Payable, "10", "10");
        await run.SeedAsync(OtherCompany, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "5000", "5000");

        await using var scope = run.Read(Company);
        var result = await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None);

        Assert.Equal(PartyAccountListOutcome.Found, result.Outcome);
        Assert.NotNull(result.List);
        Assert.Equal(Company, result.List.CompanyId);
        Assert.Equal(2, result.List.Accounts.Count);
        var account = Assert.Single(result.List.Accounts, row => row.PartyId == Party);
        Assert.Equal(Account, account.CompanyAccountId);
        Assert.Equal(2, account.Currencies.Count);
        var lira = Assert.Single(account.Currencies, currency => currency.CurrencyId == Try);
        Assert.Equal(2, lira.ReceivableCount);
        Assert.Equal(new MoneyText("800.05", Try), lira.OpenReceivable);
        Assert.Equal(1, lira.PayableCount);
        Assert.Equal(new MoneyText("50.5", Try), lira.OpenPayable);
        var dollar = Assert.Single(account.Currencies, currency => currency.CurrencyId == Usd);
        Assert.Equal(new MoneyText("75.25", Usd), dollar.OpenReceivable);
        Assert.Equal(0, dollar.PayableCount);
        Assert.Equal(new MoneyText("0", Usd), dollar.OpenPayable);
        var other = Assert.Single(result.List.Accounts, row => row.PartyId == OtherParty);
        Assert.Equal(new MoneyText("10", Try), Assert.Single(other.Currencies).OpenPayable);
        Assert.DoesNotContain("5000", JsonSerializer.Serialize(result.List, FinanceJson.CreateOptions()),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_is_refused_without_the_amount_authority_or_the_ledger_authority_and_withheld_when_revoked()
    {
        await using var run = new Run();
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "10", "10");

        await using (var scope = run.Read(Company, new DenyAmount()))
        {
            var result = await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None);
            Assert.Equal(new PartyAccountListResultV1(PartyAccountListOutcome.Denied, null), result);
        }

        await using (var scope = run.Read(Company, new DenyAll()))
        {
            Assert.Equal(PartyAccountListOutcome.Denied,
                (await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None)).Outcome);
        }

        await using (var scope = run.Read(Company, new UnavailableAll()))
        {
            Assert.Equal(PartyAccountListOutcome.Unavailable,
                (await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None)).Outcome);
        }

        await using (var scope = run.Read(Company, new RevokedAfterFirstLedgerRead()))
        {
            var result = await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None);
            Assert.Equal(PartyAccountListOutcome.Denied, result.Outcome);
            Assert.Null(result.List);
        }
    }

    [Fact]
    public async Task The_list_answers_an_empty_list_for_a_company_without_open_items_and_refuses_a_foreign_context()
    {
        await using var run = new Run();
        await run.SeedAsync(OtherCompany, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "10", "10");

        await using (var scope = run.Read(Company))
        {
            var result = await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None);
            Assert.Equal(PartyAccountListOutcome.Found, result.Outcome);
            Assert.Empty(result.List!.Accounts);
        }

        // The context names a company the actor's resolved company is not: refused before anything is read.
        await using (var scope = run.Read(Company))
        {
            var foreign = scope.Context with { CompanyId = OtherCompany };
            Assert.Equal(PartyAccountListOutcome.Denied,
                (await scope.Service.ListAuthorizedAsync(foreign, CancellationToken.None)).Outcome);
        }
    }

    [Fact]
    public async Task The_list_withholds_a_broken_stored_amount_or_direction_as_unavailable()
    {
        await using var run = new Run();
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "10", "1,5");

        await using var scope = run.Read(Company);
        Assert.Equal(new PartyAccountListResultV1(PartyAccountListOutcome.Unavailable, null),
            await scope.Service.ListAuthorizedAsync(scope.Context, CancellationToken.None));
    }

    [Fact]
    public async Task The_account_view_keeps_the_balance_summary_and_lists_only_its_open_items_by_due_date()
    {
        await using var run = new Run();
        var later = await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "300",
            "120.5", dueOn: new DateOnly(2026, 12, 1));
        var sooner = await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Payable, "40", "40",
            dueOn: new DateOnly(2026, 11, 1));
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "70", "0",
            FinanceOpenItemStatuses.Closed, new DateOnly(2026, 10, 1));
        await run.SeedAsync(Company, OtherParty, OtherAccount, Try, FinanceOpenItemDirectionCodes.Receivable, "9", "9");
        await run.SeedAsync(OtherCompany, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "8", "8");

        await using var scope = run.Read(Company);
        var result = await scope.Service.GetAuthorizedAsync(scope.Context, new PartyAccountRequestV1(Party, Account),
            CancellationToken.None);

        Assert.Equal(PartyAccountReadOutcome.Found, result.Outcome);
        var view = result.Account!;
        Assert.Equal((Company, Party, Account), (view.CompanyId, view.PartyId, view.CompanyAccountId));
        Assert.Equal(AmountDisclosure.Visible, view.OpenItemsDisclosure);
        Assert.Equal(new[] { sooner, later }, view.OpenItems.Select(item => item.OpenItemId));
        var payable = view.OpenItems[0];
        Assert.Equal(FinanceOpenItemDirectionV1.Payable, payable.Direction);
        Assert.Equal(new DateOnly(2026, 11, 1), payable.DueOn);
        Assert.Equal(new MoneyText("40", Try), payable.OutstandingAmount);
        var receivable = view.OpenItems[1];
        Assert.Equal(new MoneyText("300", Try), receivable.OriginalAmount);
        Assert.Equal(new MoneyText("120.5", Try), receivable.OutstandingAmount);
        // The API-04 summary is carried as it is: the open receivable and payable agree with the listed items.
        var currency = Assert.Single(view.Currencies);
        Assert.Equal(new AmountView(AmountDisclosure.Visible, new MoneyText("120.5", Try)), currency.OpenReceivable);
        Assert.Equal(new AmountView(AmountDisclosure.Visible, new MoneyText("40", Try)), currency.OpenPayable);
    }

    [Fact]
    public async Task The_account_view_redacts_its_open_items_without_the_amount_authority_and_refuses_without_the_ledger()
    {
        await using var run = new Run();
        await run.SeedAsync(Company, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "300", "300");

        await using (var scope = run.Read(Company, new DenyAmount()))
        {
            var result = await scope.Service.GetAuthorizedAsync(scope.Context,
                new PartyAccountRequestV1(Party, Account), CancellationToken.None);
            Assert.Equal(PartyAccountReadOutcome.Found, result.Outcome);
            Assert.Equal(AmountDisclosure.Redacted, result.Account!.OpenItemsDisclosure);
            Assert.Empty(result.Account.OpenItems);
            Assert.DoesNotContain("300", JsonSerializer.Serialize(result.Account, FinanceJson.CreateOptions()),
                StringComparison.Ordinal);
        }

        await using (var scope = run.Read(Company, new DenyAll()))
        {
            Assert.Equal(new PartyAccountReadResultV1(PartyAccountReadOutcome.Denied, null),
                await scope.Service.GetAuthorizedAsync(scope.Context, new PartyAccountRequestV1(Party, Account),
                    CancellationToken.None));
        }

        await using (var scope = run.Read(Company, new UnavailableAll()))
        {
            Assert.Equal(PartyAccountReadOutcome.Unavailable, (await scope.Service.GetAuthorizedAsync(scope.Context,
                new PartyAccountRequestV1(Party, Account), CancellationToken.None)).Outcome);
        }

        await using (var scope = run.Read(Company))
        {
            Assert.Equal(PartyAccountReadOutcome.Invalid, (await scope.Service.GetAuthorizedAsync(scope.Context,
                new PartyAccountRequestV1(Guid.Empty, Account), CancellationToken.None)).Outcome);
        }
    }

    [Fact]
    public async Task The_account_view_of_an_account_without_facts_is_an_empty_visible_view()
    {
        await using var run = new Run();
        await run.SeedAsync(OtherCompany, Party, Account, Try, FinanceOpenItemDirectionCodes.Receivable, "8", "8");

        await using var scope = run.Read(Company);
        var result = await scope.Service.GetAuthorizedAsync(scope.Context, new PartyAccountRequestV1(Party, Account),
            CancellationToken.None);

        Assert.Equal(PartyAccountReadOutcome.Found, result.Outcome);
        Assert.Empty(result.Account!.Currencies);
        Assert.Empty(result.Account.OpenItems);
        Assert.Equal(AmountDisclosure.Visible, result.Account.OpenItemsDisclosure);
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-party-accounts-{Guid.NewGuid():N}";

        private FinanceDbContext Context(Guid company, out CompanyContext companyContext)
        {
            companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public Scope Read(Guid company, IFinancePermissionPort? permissions = null)
        {
            var context = Context(company, out var companyContext);
            var user = new User(Actor, company);
            var port = permissions ?? new AllowAll();
            var ledger = new PartyLedgerService(context, companyContext, user, port);
            var service = new PartyLedgerReadService(context, companyContext, user, port, ledger);
            return new Scope(context, service, new FinanceContext(company, Actor, $"accounts-{Guid.NewGuid():N}", null));
        }

        public async Task<Guid> SeedAsync(Guid company, Guid party, Guid account, Guid currency, int direction,
            string original, string outstanding, int status = FinanceOpenItemStatuses.Open, DateOnly? dueOn = null)
        {
            await using var context = Context(company, out _);
            var admitted = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
            var row = new FinanceOpenItemRow
            {
                Direction = direction,
                Id = Guid.NewGuid(), CompanyId = company,
                SourceKind = direction == FinanceOpenItemDirectionCodes.Payable
                    ? FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment
                    : FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1",
                SourceBatchId = Guid.NewGuid(), SourceBatchVersion = "synthetic-v1", SourceBatchChecksum = new string('f', 64),
                PartyId = party, CompanyAccountId = account, CurrencyId = currency,
                OriginalAmountText = original, OutstandingAmountText = outstanding, DueOn = dueOn ?? new DateOnly(2026, 10, 10),
                Status = status, Version = 1,
                CreatedBy = "synthetic", CreatedAtUtc = admitted, UpdatedBy = "synthetic", UpdatedAtUtc = admitted,
            };
            context.OpenItems.Add(row);
            await context.SaveChangesAsync();
            return row.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context(Company, out _);
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class Scope(FinanceDbContext db, IPartyLedgerReadServiceV1 service, FinanceContext context)
        : IAsyncDisposable
    {
        public IPartyLedgerReadServiceV1 Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class DenyAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Denied);
    }

    private sealed class UnavailableAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Unavailable);
    }

    private sealed class DenyAmount : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action == FinanceAction.ReadPartyLedgerAmount
            ? PermissionDecision.Denied
            : PermissionDecision.Allowed);
    }

    /// <summary>Allows the first ledger measurement and denies every later one (a grant revoked mid-request).</summary>
    private sealed class RevokedAfterFirstLedgerRead : IFinancePermissionPort
    {
        private int reads;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(action switch
        {
            FinanceAction.ReadPartyLedger => Interlocked.Increment(ref reads) == 1
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied,
            FinanceAction.ReadPartyLedgerAmount => PermissionDecision.Allowed,
            _ => PermissionDecision.Denied,
        });
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
