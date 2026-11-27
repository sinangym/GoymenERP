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

public sealed partial class FinanceCreditAccountTests
{
    // Entry/refusal only: configured InMemory is not evidence of a successful SQL transaction.
    [Theory]
    [InlineData("missing-writer")] [InlineData("nonrelational")] [InlineData("dirty")]
    [InlineData("pending")] [InlineData("ambient")] [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Bank_limit_decision_entry_preserves_caller_state_and_requires_a_real_clean_owned_boundary(string boundary)
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new LimitSafetyPermission();
        var writer = new LimitEntryWriter { HasPendingDrafts = boundary == "pending" };
        var caller = new FinanceCreditLimitRow { Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1 };
        if (boundary == "dirty") db.CreditLimits.Add(caller);
        permission.AfterRead = () =>
        {
            if (boundary == "await-dirty" && !db.ChangeTracker.HasChanges()) db.CreditLimits.Add(caller);
            if (boundary == "await-pending") writer.HasPendingDrafts = true;
        };
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var service = LimitSafetyCore(db, company, user, permission, boundary == "missing-writer" ? null : writer).WithApprovalSafety();
        var result = await service.DecideAsync(Ctx(Checker), new("limit-entry"), new(Guid.NewGuid(), "1", true, null), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.False(result.IsReplay); Assert.Null(result.Resource);
        Assert.Equal(boundary is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        if (boundary is "dirty" or "await-dirty") Assert.Contains(db.ChangeTracker.Entries<FinanceCreditLimitRow>(), e => ReferenceEquals(e.Entity, caller) && e.State == EntityState.Added);
        Assert.Equal(boundary is "pending" or "await-pending", writer.HasPendingDrafts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")]
    public async Task Bank_limit_native_authority_refusal_does_not_clean_the_callers_draft_or_audit(string fault)
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new LimitSafetyPermission();
        var writer = new LimitEntryWriter { HasPendingDrafts = true };
        db.CreditLimits.Add(new() { Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1 });
        if (fault == "permission") permission.Decision = PermissionDecision.Denied;
        if (fault == "actor") user.Actor = Maker;
        if (fault == "company") company.Set(CompanyB);
        if (fault == "membership") user.Member = false;
        var result = await LimitSafetyCore(db, company, user, permission, writer).WithApprovalSafety()
            .DecideAsync(Ctx(Checker), new("limit-denied"), new(Guid.NewGuid(), "1", false, "SENTETIK literal ret"), default);
        Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.True(db.ChangeTracker.HasChanges());
        Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
    }

    private static FinanceCreditLimitServiceV1 LimitSafetyCore(FinanceDbContext db, ICompanyContext company, ICurrentUser user,
        IFinancePermissionPort permission, IAuditTransactionWriterV1? writer) => new(db, company, user, permission,
        new SyntheticCreditSources(CompanyA), new AuditEventCollector(), Envelopes(), new FixedClock(), writer);
    private sealed class LimitSafetyCompany(Guid id) : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } = id; public void Set(Guid value) => CompanyId = value; }
    private sealed class LimitSafetyUser : ICurrentUser
    { public string Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class LimitSafetyPermission : IFinancePermissionPort
    { public PermissionDecision Decision = PermissionDecision.Allowed; public Action? AfterRead;
      public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { var answer = Decision; AfterRead?.Invoke(); return Task.FromResult(answer); } }
    private sealed class LimitEntryWriter : IAuditTransactionWriterV1
    { public bool HasPendingDrafts { get; set; } public int Writes, Discards;
      public Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry cannot flush."); }
      public void DiscardPending() { Discards++; HasPendingDrafts = false; } }
}
