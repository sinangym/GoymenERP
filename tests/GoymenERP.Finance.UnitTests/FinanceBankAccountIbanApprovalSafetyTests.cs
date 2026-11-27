using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceBankAccountIbanApprovalSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("ambient")] [InlineData("dirty")] [InlineData("audit")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Iban_approval_refuses_unowned_boundaries_without_reading_or_discarding_caller_work(string boundary)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        var permissions = new Permissions(); var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<FinanceDbContext>();
        if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-iban-entry-" + Guid.NewGuid());
        else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
        await using var database = new FinanceDbContext(options.Options, company);
        var writer = new Writer { HasPendingDrafts = boundary is "audit" or "nonrelational-audit" };
        var row = new BankAccountRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId };
        if (boundary == "dirty") database.BankAccounts.Add(row);
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var ambientBefore = Transaction.Current;
        var service = new FinanceBankAccountService(database, company, new User(company.CompanyId), permissions,
            new NoCandidates(), collector, new(new("finance.bank-account.propose", [], "Web"), new("finance.bank-account.approve", [], "Web")),
            TimeProvider.System, boundary == "missing-writer" ? null : writer);
        var answer = await service.ApproveIbanChangeAsync(new(company.CompanyId, "SENTETIK-checker", "SENTETIK", null),
            new("SENTETIK-iban-key"), new(row.Id, "3"), default);
        Assert.Equal(CommandOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
        Assert.Equal([FinanceAction.ApproveBankAccountIbanChange], permissions.Actions);
        Assert.Same(ambientBefore, Transaction.Current); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(boundary is "audit" or "nonrelational-audit", writer.HasPendingDrafts);
        Assert.Equal(boundary == "dirty", database.ChangeTracker.HasChanges());
        if (boundary == "dirty") Assert.Same(row, Assert.Single(database.ChangeTracker.Entries()).Entity);
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-checker";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }
    private sealed class Permissions : IFinancePermissionPort
    {
        public List<FinanceAction> Actions { get; } = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token)
        { Actions.Add(action); return Task.FromResult(PermissionDecision.Allowed); }
    }
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    { public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken token = default) => throw new InvalidOperationException("No Settings read at entry."); }
    // Entry-only probe; real audit participation and rollback are asserted in the native SQL partial.
    private sealed class Writer : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public int Writes, Discards;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException(); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
}
