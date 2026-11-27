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
    public async Task Credit_movement_decision_entry_preserves_caller_state_and_requires_a_real_clean_owned_boundary(string boundary)
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new LimitSafetyPermission();
        var writer = new LimitEntryWriter { HasPendingDrafts = boundary == "pending" };
        var caller = MovementSafetyCaller();
        if (boundary == "dirty") db.CreditMovements.Add(caller);
        permission.AfterRead = () =>
        {
            if (boundary == "await-dirty" && !db.ChangeTracker.HasChanges()) db.CreditMovements.Add(caller);
            if (boundary == "await-pending") writer.HasPendingDrafts = true;
        };
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var service = MovementSafetyCore(db, company, user, permission, boundary == "missing-writer" ? null : writer).WithApprovalSafety();
        var result = await service.DecideAsync(Ctx(Checker), new("movement-entry"), new(Guid.NewGuid(), "1", true, null), default);
        Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.False(result.IsReplay); Assert.Null(result.Resource);
        Assert.Equal(boundary is "dirty" or "await-dirty", db.ChangeTracker.HasChanges());
        if (boundary is "dirty" or "await-dirty") Assert.Contains(db.ChangeTracker.Entries<FinanceCreditMovementRow>(), e => ReferenceEquals(e.Entity, caller) && e.State == EntityState.Added);
        Assert.Equal(boundary is "pending" or "await-pending", writer.HasPendingDrafts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.Null(db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")]
    public async Task Credit_movement_native_authority_refusal_does_not_clean_the_callers_draft_or_audit(string fault)
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new LimitSafetyPermission();
        var writer = new LimitEntryWriter { HasPendingDrafts = true };
        db.CreditMovements.Add(MovementSafetyCaller());
        if (fault == "permission") permission.Decision = PermissionDecision.Denied;
        if (fault == "actor") user.Actor = Maker;
        if (fault == "company") company.Set(CompanyB);
        if (fault == "membership") user.Member = false;
        var result = await MovementSafetyCore(db, company, user, permission, writer).WithApprovalSafety()
            .DecideAsync(Ctx(Checker), new("movement-denied"), new(Guid.NewGuid(), "1", false, "SENTETIK literal ret"), default);
        Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.True(db.ChangeTracker.HasChanges());
        Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
    }

    [Theory]
    [InlineData("key")] [InlineData("reason-null")] [InlineData("reason-long")]
    [InlineData("version-null")] [InlineData("version-empty")] [InlineData("version-padded")] [InlineData("version-long")]
    public async Task Credit_movement_malformed_native_input_keeps_validation_precedence_and_preserves_pending_caller_work(string fault)
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA);
        var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser(); var permission = new LimitSafetyPermission();
        var writer = new LimitEntryWriter { HasPendingDrafts = true };
        var caller = MovementSafetyCaller(); db.CreditMovements.Add(caller);
        var request = new CreditMovementDecisionV1(Guid.NewGuid(), "1", false, "SENTETIK ret");
        request = fault switch
        {
            "reason-null" => request with { Reason = null }, "reason-long" => request with { Reason = new string('x', 1001) },
            "version-null" => request with { ExpectedVersion = null! }, "version-empty" => request with { ExpectedVersion = "" },
            "version-padded" => request with { ExpectedVersion = " 1 " }, "version-long" => request with { ExpectedVersion = new string('1', 65) },
            _ => request,
        };
        var context = Ctx(Checker); var metadata = new CommandMetadata(fault == "key" ? " padded " : "movement-malformed");
        var core = MovementSafetyCore(db, company, user, permission, writer);
        // Raw native golden refuses before transaction/read; the guarded interface must retain the SAME result/code.
        var original = await core.DecideAsync(context, metadata, request, default);
        var guarded = await core.WithApprovalSafety().DecideAsync(context, metadata, request, default);
        Assert.Equal(CommandOutcome.ValidationFailed, original.Outcome); Assert.Equal(original.Outcome, guarded.Outcome);
        Assert.Equal(fault == "key" ? "credit_movement_metadata_invalid" : "credit_movement_decision_invalid", Assert.Single(guarded.Issues).Code);
        Assert.Equal(original.Issues.ToArray(), guarded.Issues.ToArray()); Assert.False(guarded.IsReplay); Assert.Null(guarded.Resource);
        Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.True(writer.HasPendingDrafts);
        Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Credit_movement_cancellation_during_first_authority_read_preserves_pending_caller_work()
    {
        await using var run = new Run(); await using var db = run.Memory(CompanyA); using var cancellation = new CancellationTokenSource();
        var writer = new LimitEntryWriter { HasPendingDrafts = true }; var caller = MovementSafetyCaller(); db.CreditMovements.Add(caller);
        var permission = new LimitSafetyPermission { AfterRead = () => cancellation.Cancel() };
        var service = MovementSafetyCore(db, new LimitSafetyCompany(CompanyA), new LimitSafetyUser(), permission, writer).WithApprovalSafety();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DecideAsync(Ctx(Checker), new("movement-cancel-entry"), new(Guid.NewGuid(), "1", true, null), cancellation.Token));
        Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity); Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
    }

    private static FinanceCreditMovementServiceV1 MovementSafetyCore(FinanceDbContext db, ICompanyContext company, ICurrentUser user,
        IFinancePermissionPort permission, IAuditTransactionWriterV1? writer)
    {
        var periods = new MovementPeriods();
        return new(db, company, user, permission, new SyntheticCreditSources(CompanyA), periods, periods, new SyntheticMovementBalance(),
            new AuditEventCollector(), Envelopes(), new FixedClock(), writer, OpenGroups);
    }
    private static FinanceCreditMovementRow MovementSafetyCaller() => new() { Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1,
        SourceJson = "{}", SourceIdentityHash = new string('a', 64), MoneyProfileJson = "{}", ProposedBy = "SENTETIK caller" };
}
