using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class NormalJournalDraftLifecycleV1Tests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("nonrelational")] [InlineData("dirty")]
    [InlineData("pending")] [InlineData("ambient")] [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Both_period_approval_boundaries_refuse_caller_work_without_discarding_or_reading_native_state(string fault)
    {
        foreach (var command in new[] { AccountingCommandNames.ApprovePeriod, "accounting.period.reopening.approve.v1" })
        {
            var company = new ApprovalCompany(Company);
            await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
            var collector = new ApprovalCollector(); var writer = new ApprovalEntryWriter(collector);
            var core = new AccountingStore(db, company, collector, TimeProvider.System,
                new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)),
                normalApprovalAuditWriter: fault == "missing-writer" ? null : writer);
            var caller = new AccountingCommandRow { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "key",
                ActorId = "actor", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
            if (fault == "dirty") db.Commands.Add(caller);
            if (fault == "pending") collector.Enqueue(ApprovalCallerAudit());
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var calls = 0;
            var result = await core.RunPeriodApprovalAsync<PeriodViewV1>(command,
                _ => { calls++; throw new InvalidOperationException("Native reads cannot run in this unsupported scope."); }, _ =>
                {
                    if (fault == "await-dirty") db.Commands.Add(caller);
                    if (fault == "await-pending") collector.Enqueue(ApprovalCallerAudit());
                    return Task.CompletedTask;
                });
            Assert.Equal(AccountingApiStatus.Unavailable, result.Status); Assert.Null(result.Resource);
            Assert.Equal(0, calls); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
            Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
            Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
            if (db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
            Assert.Null(db.Database.CurrentTransaction);
        }
    }

    [Fact]
    public async Task Period_boundary_does_not_authorize_close_or_an_arbitrary_accounting_command()
    {
        var company = new ApprovalCompany(Company);
        await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var collector = new ApprovalCollector(); var writer = new ApprovalEntryWriter(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System,
            new AccountingAmountScaleVerifier(SyntheticSettings.Scale(Company, 2)), normalApprovalAuditWriter: writer);
        foreach (var command in new[] { AccountingCommandNames.ClosePeriod, NormalJournalCommandNamesV1.ApproveDraft, "unknown" })
            await Assert.ThrowsAsync<ArgumentException>(() => core.RunPeriodApprovalAsync<PeriodViewV1>(command,
                _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()));
        Assert.Equal(0, writer.Discards); Assert.Empty(db.ChangeTracker.Entries());
    }
}
