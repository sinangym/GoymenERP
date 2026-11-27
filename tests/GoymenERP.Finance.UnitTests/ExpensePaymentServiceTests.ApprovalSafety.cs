using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class ExpensePaymentServiceTests
{
    // Entry/refusal-only probes. No fake EF transaction provider and no synthetic relational-success claim.
    [Theory]
    [InlineData("missing-writer")]
    [InlineData("nonrelational")]
    [InlineData("dirty")]
    [InlineData("pending")]
    [InlineData("ambient")]
    [InlineData("await-dirty")]
    [InlineData("await-pending")]
    public async Task Approval_boundary_preserves_all_caller_work_before_entering_an_owned_transaction(string boundary)
    {
        await using var run = await Run.StartAsync(); await using var db = run.Context();
        var company = new ApprovalCompany(Company); var user = new ApprovalUser(); var permission = new ApprovalPermission();
        var writer = new ApprovalEntryWriter { HasPendingDrafts = boundary == "pending" };
        var caller = new ExpensePaymentRow { CompanyId = Company, Id = Guid.NewGuid(), Version = 1 };
        if (boundary == "dirty") db.ExpensePayments.Add(caller);
        permission.AfterRead = () =>
        {
            if (boundary == "await-dirty" && !db.ChangeTracker.HasChanges()) db.ExpensePayments.Add(caller);
            if (boundary == "await-pending") writer.HasPendingDrafts = true;
        };
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var service = ApprovalCore(db, company, user, permission, boundary == "missing-writer" ? null : writer).WithApprovalSafety();
        var result = await service.ApproveAsync(new(Company, "checker", "entry", null), Key("entry"), new(Guid.NewGuid(), "1"), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.False(result.IsReplay); Assert.Null(result.Resource);
        Assert.Equal(boundary is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        if (boundary is "dirty" or "await-dirty") Assert.Contains(db.ChangeTracker.Entries<ExpensePaymentRow>(), e => ReferenceEquals(e.Entity, caller) && e.State == EntityState.Added);
        Assert.Equal(boundary is "pending" or "await-pending", writer.HasPendingDrafts);
        Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes); Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("membership")]
    public async Task Approval_native_authority_refusal_precedes_owned_cleanup_and_does_not_clear_the_callers_draft(string failure)
    {
        await using var run = await Run.StartAsync(); await using var db = run.Context();
        var company = new ApprovalCompany(Company); var user = new ApprovalUser(); var permission = new ApprovalPermission();
        var writer = new ApprovalEntryWriter { HasPendingDrafts = true };
        db.ExpensePayments.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, Version = 1 });
        if (failure == "permission") permission.Decision = PermissionDecision.Denied;
        if (failure == "actor") user.Actor = "other";
        if (failure == "company") company.Set(Guid.NewGuid());
        if (failure == "membership") user.Member = false;
        var result = await ApprovalCore(db, company, user, permission, writer).WithApprovalSafety()
            .ApproveAsync(new(Company, "checker", "denied", null), Key("denied"), new(Guid.NewGuid(), "1"), default);
        Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.True(db.ChangeTracker.HasChanges());
        Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
    }

    private static ExpensePaymentService ApprovalCore(FinanceDbContext db, ICompanyContext company, ICurrentUser user,
        IFinancePermissionPort permission, IAuditTransactionWriterV1? writer) => new(db, company, user, permission,
        new SingleExpenseItemCatalog(), new FixedBalance(TreasuryBalanceOutcome.Sufficient), new SettingsPort(), new OpenPeriod(),
        new AuditEventCollector(), new(FinanceMoneyOperations.ExpensePaymentV1),
        new(new(FinancePermissionNames.ExpensePaymentDraftSave, [], "Web"), new(FinancePermissionNames.ExpensePaymentApprove, [], "Web"),
            new(FinancePermissionNames.ExpensePaymentRecord, [], "Web")), TimeProvider.System, writer);
    private sealed class ApprovalCompany(Guid company) : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } = company; public void Set(Guid value) => CompanyId = value; }
    private sealed class ApprovalUser : ICurrentUser
    { public string Actor = "checker"; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class ApprovalPermission : IFinancePermissionPort
    { public Action? AfterRead; public PermissionDecision Decision = PermissionDecision.Allowed;
      public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { var answer = Decision; AfterRead?.Invoke(); return Task.FromResult(answer); } }
    private sealed class ApprovalEntryWriter : IAuditTransactionWriterV1
    { public bool HasPendingDrafts { get; set; } public int Writes, Discards;
      public Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry refusal cannot flush."); }
      public void DiscardPending() { Discards++; HasPendingDrafts = false; } }
}
