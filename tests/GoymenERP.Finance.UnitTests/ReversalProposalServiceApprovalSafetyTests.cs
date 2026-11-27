using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Finance.UnitTests;

// SENTETIK entry boundaries only; SQL/actual Audit behavior belongs to the relational fixture.
public sealed class ReversalProposalServiceApprovalSafetyTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Theory]
    [InlineData("missing-writer")] [InlineData("nonrelational")] [InlineData("dirty")]
    [InlineData("pending-audit")] [InlineData("ambient")]
    public async Task Guarded_entry_preserves_unowned_work_and_never_saves(string boundary)
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var db = Database(company); var writer = new EntryWriter { HasPendingDrafts = boundary == "pending-audit" };
        if (boundary == "dirty") db.Commands.Add(new() { CompanyId = Company, CommandName = "SENTETIK-caller" });
        var before = db.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray();
        var service = Core(db, company, new User(), new Permissions(), boundary == "missing-writer" ? null : writer).WithApprovalSafety();
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        foreach (var approve in new[] { true, false })
        {
            var result = await Decide(service, approve);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Equal(before, db.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray());
            Assert.Equal(boundary == "pending-audit", writer.HasPendingDrafts);
            Assert.Equal((0, 0), (writer.Writes, writer.Discards));
        }
    }

    [Theory]
    [InlineData("dirty")] [InlineData("audit")]
    public async Task Caller_work_created_during_first_authority_await_is_not_owned_or_discarded(string work)
    {
        var company = new CompanyContext(); company.Set(Company); await using var db = Database(company);
        var writer = new EntryWriter(); var permissions = new Permissions();
        permissions.BeforeReturn = () => { if (work == "dirty") db.Commands.Add(new() { CompanyId = Company, CommandName = "SENTETIK-caller" }); else writer.HasPendingDrafts = true; };
        var service = Core(db, company, new User(), permissions, writer).WithApprovalSafety();
        Assert.Equal(CommandOutcome.Unavailable, (await Decide(service, true)).Outcome);
        Assert.Equal(work == "dirty", db.ChangeTracker.HasChanges()); Assert.Equal(work == "audit", writer.HasPendingDrafts);
        Assert.Equal((0, 0), (writer.Writes, writer.Discards));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Native_authority_refusal_precedes_persistence_refusal_without_discarding_caller_audit(PermissionDecision permission, CommandOutcome expected)
    {
        var company = new CompanyContext(); company.Set(Company); await using var db = Database(company);
        var writer = new EntryWriter { HasPendingDrafts = true };
        var service = Core(db, company, new User(), new Permissions { Result = permission }, writer).WithApprovalSafety();
        Assert.Equal(expected, (await Decide(service, false)).Outcome);
        Assert.True(writer.HasPendingDrafts); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("membership")]
    public async Task Foreign_or_lost_context_is_denied_before_any_owned_boundary(string boundary)
    {
        var company = new CompanyContext(); company.Set(boundary == "company" ? Guid.NewGuid() : Company);
        await using var db = Database(company); var user = new User { Actor = boundary == "actor" ? "SENTETIK-other" : "SENTETIK-checker", Member = boundary != "membership" };
        var writer = new EntryWriter { HasPendingDrafts = true };
        db.Commands.Add(new() { CompanyId = Company, CommandName = "SENTETIK-caller" });
        var service = Core(db, company, user, new Permissions(), writer).WithApprovalSafety();
        Assert.Equal(CommandOutcome.Denied, (await Decide(service, true)).Outcome);
        Assert.True(db.ChangeTracker.HasChanges()); Assert.True(writer.HasPendingDrafts); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
    }

    [Fact]
    public async Task Actual_module_factory_exposes_only_guarded_receipt_reversal_runtime()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var database = Database(company);
        var services = new ServiceCollection(); services.AddFinanceServices();
        services.AddSingleton<ICompanyContext>(company); services.AddSingleton<ICurrentUser>(new User());
        services.AddSingleton<IErpAuthorityRevalidationService>(new RegistrationAuthority()); services.AddSingleton(database);
        services.AddSingleton<ICorrelationContext>(new CorrelationContext()); services.AddSingleton<IAuditEventCollector>(new AuditEventCollector());
        services.AddSingleton<IAuditTransactionWriterV1>(new EntryWriter()); services.AddSingleton<IFinanceRecordPeriodRule>(new OpenPeriod()); services.AddSingleton(TimeProvider.System);
        Assert.Single(services, item => item.ServiceType == typeof(IReversalProposalService));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var service = Assert.IsType<GuardedReversalProposalService>(scope.ServiceProvider.GetRequiredService<IReversalProposalService>());
        Assert.Null(scope.ServiceProvider.GetService<ReversalProposalService>());
        var result = await service.ApproveAsync(new(Company, "SENTETIK-checker", "SENTETIK-original", null),
            new("SENTETIK-key"), new(Guid.NewGuid(), "1"), default);
        Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.Null(result.Resource);
        Assert.False(database.ChangeTracker.HasChanges()); Assert.Null(database.Database.CurrentTransaction);
    }

    private static FinanceDbContext Database(ICompanyContext company) => new(new DbContextOptionsBuilder<FinanceDbContext>()
        .UseInMemoryDatabase("receipt-reversal-entry-" + Guid.NewGuid()).Options, company);
    private static ReversalProposalService Core(FinanceDbContext db, ICompanyContext company, ICurrentUser user, IFinancePermissionPort permission, IAuditTransactionWriterV1? writer) =>
        new(db, company, user, permission, new OpenPeriod(), new NoAllocations(), new AuditEventCollector(),
            new(new(FinancePermissionNames.ReversePropose, [], "Web"), new(FinancePermissionNames.ReverseApprove, [], "Web")), TimeProvider.System, auditOwner: writer);
    private static Task<ReversalProposalCommandResult> Decide(IReversalProposalService service, bool approve) => approve
        ? service.ApproveAsync(new(Company, "SENTETIK-checker", "SENTETIK-original", null), new("SENTETIK-key"), new(Guid.NewGuid(), "1"), default)
        : service.RejectAsync(new(Company, "SENTETIK-checker", "SENTETIK-original", null), new("SENTETIK-key"), new(Guid.NewGuid(), "1", " reason "), default);
    private sealed class OpenPeriod : IFinanceRecordPeriodRule
    { public Task<RecordPeriodAnswer> CheckAsync(FinanceContext c, DateOnly d, DateOnly? v, DateTimeOffset n, CancellationToken t) => Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null)); }
    private sealed class NoAllocations : IFinanceReversalAllocationGate
    { public Task<ReversalAllocationAnswer> CheckAsync(FinanceContext c, Guid s, CancellationToken t) => Task.FromResult(new ReversalAllocationAnswer(ReversalAllocationOutcome.NoAllocation)); }
    private sealed class User : ICurrentUser
    { public string Actor = "SENTETIK-checker"; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid c) => Member && AllowedCompanyIds.Contains(c); }
    private sealed class Permissions : IFinancePermissionPort
    { public Action? BeforeReturn; public PermissionDecision Result = PermissionDecision.Allowed;
      public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t) { BeforeReturn?.Invoke(); return Task.FromResult(Result); } }
    private sealed class EntryWriter : IAuditTransactionWriterV1
    { public bool HasPendingDrafts { get; set; } public int Writes, Discards;
      public Task WritePendingAsync(IDbTransaction tx, CancellationToken t = default) { Writes++; throw new InvalidOperationException("Entry cannot write."); }
      public void DiscardPending() { Discards++; HasPendingDrafts = false; } }
    private sealed class RegistrationAuthority : IErpAuthorityRevalidationService
    { public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken t = default) => Task.FromResult(PermissionScope.Denied); }
}
