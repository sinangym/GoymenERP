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

public sealed class FinancePartyCreditLimitApprovalSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("ambient")] [InlineData("dirty")] [InlineData("audit")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Both_decisions_refuse_unowned_entry_without_reading_or_discarding_caller_work(string boundary)
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig(boundary);
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var original = Transaction.Current;
            var answer = await rig.Decide(approve);
            Assert.Equal(CommandOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
            Assert.Equal([FinanceAction.ApproveCreditLimit], rig.Permissions.Actions);
            Assert.Same(original, Transaction.Current); Assert.Equal(0, rig.Writer.Writes); Assert.Equal(0, rig.Writer.Discards);
            Assert.Equal(boundary is "audit" or "nonrelational-audit", rig.Writer.HasPendingDrafts);
            Assert.Equal(boundary == "dirty", rig.Database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(rig.CallerRow, Assert.Single(rig.Database.ChangeTracker.Entries()).Entity);
        }
    }

    [Theory]
    [InlineData("dirty")] [InlineData("audit")]
    public async Task Caller_work_added_during_the_first_fresh_authority_await_is_preserved(string work)
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig("clean");
            rig.Permissions.DuringRead = () =>
            {
                if (work == "dirty") rig.Database.PartyCreditLimits.Add(rig.CallerRow);
                else rig.Writer.HasPendingDrafts = true;
            };
            var answer = await rig.Decide(approve);
            Assert.Equal(CommandOutcome.Unavailable, answer.Outcome);
            Assert.Equal([FinanceAction.ApproveCreditLimit], rig.Permissions.Actions);
            Assert.Equal(0, rig.Writer.Discards); Assert.Equal(0, rig.Writer.Writes);
            Assert.Equal(work == "audit", rig.Writer.HasPendingDrafts);
            Assert.Equal(work == "dirty", rig.Database.ChangeTracker.HasChanges());
            if (work == "dirty") Assert.Same(rig.CallerRow, Assert.Single(rig.Database.ChangeTracker.Entries()).Entity);
        }
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Native_authority_refusal_precedes_unavailable_persistence_without_cleaning_caller_audit(
        PermissionDecision decision, CommandOutcome outcome)
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig("nonrelational-audit"); rig.Permissions.Decision = decision;
            var answer = await rig.Decide(approve);
            Assert.Equal(outcome, answer.Outcome); Assert.True(rig.Writer.HasPendingDrafts);
            Assert.Equal(0, rig.Writer.Discards); Assert.Equal(0, rig.Writer.Writes);
        }
    }

    [Fact]
    public async Task An_unresolved_company_preserves_the_native_denial_without_dereferencing_its_company_id()
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig("unresolved");
            var answer = await rig.Decide(approve);
            Assert.Equal(CommandOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource);
            Assert.Empty(rig.Permissions.Actions); Assert.Equal(0, rig.Writer.Writes); Assert.Equal(0, rig.Writer.Discards);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public readonly CompanyContext Company = new(); public readonly Permissions Permissions = new();
        public readonly Writer Writer = new(); public FinanceDbContext Database { get; }
        public PartyCreditLimitRow CallerRow { get; }
        private readonly Guid companyId = Guid.NewGuid();
        private readonly IFinancePartyCreditLimitMaster service;
        public Rig(string boundary)
        {
            if (boundary != "unresolved") Company.Set(companyId);
            var options = new DbContextOptionsBuilder<FinanceDbContext>();
            if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-limit-entry-" + Guid.NewGuid());
            else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
            Database = new(options.Options, Company);
            CallerRow = new PartyCreditLimitRow { Id = Guid.NewGuid(), CompanyId = companyId };
            if (boundary == "dirty") Database.PartyCreditLimits.Add(CallerRow);
            Writer.HasPendingDrafts = boundary is "audit" or "nonrelational-audit";
            var core = new FinancePartyCreditLimitService(Database, Company, new User(companyId), Permissions,
                new NoCandidates(), new NoParties(), new AuditEventCollector(), new(new("SENTETIK", [], "Web"), new("SENTETIK", [], "Web")), TimeProvider.System);
            service = new GuardedFinancePartyCreditLimitService(core, boundary == "missing-writer" ? null : Writer);
        }
        public Task<PartyCreditLimitCommandResult> Decide(bool approve) => approve
            ? service.ApproveAsync(new(companyId, "SENTETIK-checker", "SENTETIK", null), new("SENTETIK-key"), new(CallerRow.Id, "1"), default)
            : service.RejectAsync(new(companyId, "SENTETIK-checker", "SENTETIK", null), new("SENTETIK-key"), new(CallerRow.Id, "1", "SENTETIK reason"), default);
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-checker";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }
    private sealed class Permissions : IFinancePermissionPort
    {
        public List<FinanceAction> Actions { get; } = []; public Action? DuringRead;
        public PermissionDecision Decision = PermissionDecision.Allowed;
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken token)
        { Actions.Add(a); await Task.Yield(); DuringRead?.Invoke(); return Decision; }
    }
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    { public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken t = default) => throw new InvalidOperationException("No owner read at entry."); }
    private sealed class NoParties : IFinancePartyAccountPort
    { public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext c, Guid party, Guid account, CancellationToken t) => throw new InvalidOperationException("No owner read at entry."); }
    private sealed class Writer : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public int Writes, Discards;
        public Task WritePendingAsync(IDbTransaction tx, CancellationToken t = default) { Writes++; throw new InvalidOperationException("No write at entry."); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
}
