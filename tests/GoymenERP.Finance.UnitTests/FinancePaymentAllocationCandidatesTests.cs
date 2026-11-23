using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>SENTETIK / NOT_ENV candidate selection; real SQL isolation is covered separately.</summary>
public sealed class FinancePaymentAllocationCandidatesTests
{
    [Fact]
    public async Task Recorded_payment_can_select_only_available_same_currency_payable_positions_of_its_actual_beneficiary()
    {
        await using var run = new Run(); await run.SeedAsync();
        var read = await run.Reader.ReadAsync(run.Context, run.Payment, 0, default);
        Assert.Equal(ReadOutcome.Found, read.Outcome);
        Assert.Equal("0.00", read.Resource!.Positions.Single(x => x.CompanyAccountId == run.Account).AvailableAmount);
        var held = Assert.Single(read.Resource.Positions, x => x.CompanyAccountId == run.TargetAccount);
        Assert.Equal("12.00", held.AvailableAmount);
        var item = Assert.Single(read.Resource.OpenItems);
        Assert.Equal(run.GoodItem, item.OpenItemId); Assert.Equal(run.Account, item.OriginalAccountId);
        Assert.Equal(run.TargetAccount, Assert.Single(item.Positions).CompanyAccountId);
        Assert.Empty(read.Resource.Allocations);
    }

    [Fact]
    public async Task Missing_amount_permission_context_change_and_foreign_payment_return_no_financial_data()
    {
        await using var run = new Run(); await run.SeedAsync();
        run.Permission.DenyAmount = true;
        var denied = await run.Reader.ReadAsync(run.Context, run.Payment, 0, default);
        Assert.Equal(ReadOutcome.Denied, denied.Outcome); Assert.Null(denied.Resource);
        run.Permission.DenyAmount = false;
        run.Permission.ChangeAfterNextRead = () => run.Company.Set(Guid.NewGuid());
        var changed = await run.Reader.ReadAsync(run.Context, run.Payment, 0, default);
        Assert.Equal(ReadOutcome.Denied, changed.Outcome); Assert.Null(changed.Resource);
        run.Company.Set(run.Context.CompanyId);
        var absent = await run.Reader.ReadAsync(run.Context, Guid.NewGuid(), 0, default);
        Assert.Equal(ReadOutcome.NotFound, absent.Outcome); Assert.Null(absent.Resource);
    }

    private sealed class Run : IAsyncDisposable
    {
        public Guid Payment { get; } = Guid.NewGuid(); public Guid Party { get; } = Guid.NewGuid();
        public Guid Account { get; } = Guid.NewGuid(); public Guid Target { get; } = Guid.NewGuid();
        public Guid TargetAccount { get; } = Guid.NewGuid(); public Guid Currency { get; } = Guid.NewGuid();
        public Guid GoodItem { get; } = Guid.NewGuid(); public AdversarialCompanyContext Company { get; } = new();
        public FinanceDbContext Db { get; } public Permission Permission { get; } = new();
        public FinanceContext Context { get; } public FinancePaymentAllocationCandidatesV1 Reader { get; }
        public Run()
        {
            Company.Set(Guid.NewGuid()); Context = new(Company.CompanyId, "SENTETIK", "SENTETIK-candidates", null);
            Db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Company);
            Reader = new(Db, Company, new User(Context.CompanyId), Permission);
        }
        public async Task SeedAsync()
        {
            Db.ManualBankPayments.Add(new() { Id = Payment, CompanyId = Context.CompanyId, PartyId = Party, CompanyAccountId = Account,
                CurrencyId = Currency, AmountText = "12.00", Status = ManualBankPaymentStatuses.Recorded, Version = 3 });
            AddAssignment(Payment, FinanceBalanceAssignmentKinds.Payment);
            AddItem(GoodItem, FinanceOpenItemDirectionCodes.Payable, Currency); AddAssignment(GoodItem, FinanceBalanceAssignmentKinds.OpenItem);
            var wrongDirection = Guid.NewGuid(); AddItem(wrongDirection, FinanceOpenItemDirectionCodes.Receivable, Currency);
            AddAssignment(wrongDirection, FinanceBalanceAssignmentKinds.OpenItem);
            AddItem(Guid.NewGuid(), FinanceOpenItemDirectionCodes.Payable, Guid.NewGuid());
            AddItem(Guid.NewGuid(), FinanceOpenItemDirectionCodes.Payable, Currency); // Original account has no payment remainder.
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
        }
        private void AddAssignment(Guid source, int kind) => Db.PartyBalanceAssignments.Add(new() {
            CompanyId = Context.CompanyId, Kind = kind, SourceId = source, OriginalPartyId = Party, OriginalAccountId = Account,
            CurrentPartyId = Target, CurrentAccountId = TargetAccount, CurrencyId = Currency, AvailableAmountText = "12.00", Version = 1, LastTransferId = Guid.NewGuid() });
        private void AddItem(Guid id, int direction, Guid currency) => Db.OpenItems.Add(new() { Id = id, CompanyId = Context.CompanyId,
            Direction = direction, SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1", SourceBatchId = Guid.NewGuid(),
            SourceBatchVersion = "1", SourceBatchChecksum = new string('a', 64), PartyId = Party, CompanyAccountId = Account,
            CurrencyId = currency, OriginalAmountText = "12.00", OutstandingAmountText = "12.00", Status = FinanceOpenItemStatuses.Open, Version = 1 });
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Permission : IFinancePermissionPort
    {
        public bool DenyAmount { get; set; } public Action? ChangeAfterNextRead { get; set; }
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token)
        { var change = ChangeAfterNextRead; ChangeAfterNextRead = null; change?.Invoke();
          return Task.FromResult(DenyAmount && action == FinanceAction.ReadPartyLedgerAmount ? PermissionDecision.Denied : PermissionDecision.Allowed); }
    }
    private sealed class User(Guid company) : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId => "SENTETIK";
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company }; public bool CanAccessCompany(Guid id) => company == id; }
    // SENTETIK adversarial port: production CompanyContext intentionally rejects this drift at Set().
    private sealed class AdversarialCompanyContext : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) { CompanyId = companyId; IsResolved = true; }
    }

}
