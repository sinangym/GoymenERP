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

public sealed class FinanceOpeningBalanceApprovalSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("ambient")] [InlineData("dirty")] [InlineData("audit")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Four_decisions_refuse_unowned_entry_without_cleaning_caller_work(string boundary)
    {
        foreach (var operation in Enumerable.Range(0, 4))
        {
            await using var rig = new Rig(boundary);
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var original = Transaction.Current;
            var result = await rig.Decide(operation);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Equal([FinanceAction.ApproveOpeningBalance], rig.Authority.Actions);
            Assert.Same(original, Transaction.Current); Assert.Equal(0, rig.Writer.Writes); Assert.Equal(0, rig.Writer.Discards);
            Assert.Equal(boundary is "audit" or "nonrelational-audit", rig.Writer.HasPendingDrafts);
            Assert.Equal(boundary == "dirty", rig.Database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(rig.CallerRow, Assert.Single(rig.Database.ChangeTracker.Entries()).Entity);
        }
    }

    [Theory] [InlineData("dirty")] [InlineData("audit")]
    public async Task Work_added_during_first_authority_await_remains_callers_work(string work)
    {
        foreach (var operation in Enumerable.Range(0, 4))
        {
            await using var rig = new Rig("clean");
            rig.Authority.DuringRead = () => { if (work == "dirty") rig.Database.OpeningBalances.Add(rig.CallerRow); else rig.Writer.HasPendingDrafts = true; };
            Assert.Equal(CommandOutcome.Unavailable, (await rig.Decide(operation)).Outcome);
            Assert.Single(rig.Authority.Actions); Assert.Equal(0, rig.Writer.Writes); Assert.Equal(0, rig.Writer.Discards);
            Assert.Equal(work == "audit", rig.Writer.HasPendingDrafts); Assert.Equal(work == "dirty", rig.Database.ChangeTracker.HasChanges());
            if (work == "dirty") Assert.Same(rig.CallerRow, Assert.Single(rig.Database.ChangeTracker.Entries()).Entity);
        }
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Native_authority_refusal_precedes_persistence_without_audit_cleanup(PermissionDecision decision, CommandOutcome outcome)
    {
        foreach (var operation in Enumerable.Range(0, 4))
        {
            await using var rig = new Rig("nonrelational-audit"); rig.Authority.Decision = decision;
            Assert.Equal(outcome, (await rig.Decide(operation)).Outcome);
            Assert.True(rig.Writer.HasPendingDrafts); Assert.Equal(0, rig.Writer.Discards); Assert.Equal(0, rig.Writer.Writes);
        }
    }

    [Fact]
    public async Task Unresolved_company_preserves_native_denial_without_reading_an_owner_or_discarding_work()
    {
        foreach (var operation in Enumerable.Range(0, 4))
        {
            await using var rig = new Rig("unresolved");
            Assert.Equal(CommandOutcome.Denied, (await rig.Decide(operation)).Outcome);
            Assert.Empty(rig.Authority.Actions); Assert.Equal(0, rig.Writer.Discards);
            Assert.IsNotType<FinanceOpeningBalanceService>(rig.Service);
        }
    }

    [Fact]
    public async Task Native_key_null_version_and_canonical_rejection_reason_limits_are_not_replaced_by_persistence_refusal()
    {
        foreach (var operation in Enumerable.Range(0, 4))
        {
            await using var rig = new Rig("audit");
            Assert.Equal(CommandOutcome.ValidationFailed, (await rig.Decide(operation, key: " padded ")).Outcome);
            Assert.Equal("identity_required", Assert.Single((await rig.Decide(operation, version: null!)).Issues).Code);
            if (operation is 1 or 3)
                foreach (var reason in new[] { "", " padded ", new string('x', 501) })
                    Assert.Equal(CommandOutcome.ValidationFailed, (await rig.Decide(operation, reason: reason)).Outcome);
            Assert.True(rig.Writer.HasPendingDrafts); Assert.Equal(0, rig.Writer.Discards); Assert.Equal(0, rig.Writer.Writes);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly Guid companyId = Guid.NewGuid();
        public readonly CompanyContext Company = new(); public readonly Authority Authority = new(); public readonly Writer Writer = new();
        public FinanceDbContext Database { get; } public OpeningBalanceRow CallerRow { get; } public IFinanceOpeningBalanceService Service { get; }
        public Rig(string boundary)
        {
            if (boundary != "unresolved") Company.Set(companyId);
            var options = new DbContextOptionsBuilder<FinanceDbContext>();
            if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-opening-entry-" + Guid.NewGuid());
            else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
            Database = new(options.Options, Company); CallerRow = new() { Id = Guid.NewGuid(), CompanyId = companyId };
            if (boundary == "dirty") Database.OpeningBalances.Add(CallerRow);
            Writer.HasPendingDrafts = boundary is "audit" or "nonrelational-audit";
            var envelope = new FinanceAuditEnvelope("SENTETIK", [], "Web");
            Service = new FinanceOpeningBalanceService(Database, Company, new User(companyId), Authority,
                new NoCandidates(), new NoParty(), new NoPeriod(), new AuditEventCollector(), new(envelope, envelope),
                TimeProvider.System, auditOwner: boundary == "missing-writer" ? null : Writer).WithApprovalSafety();
        }
        public Task<OpeningBalanceCommandResult> Decide(int operation, string key = "SENTETIK-key", string version = "1", string reason = "SENTETIK ret")
        {
            var c = new FinanceContext(companyId, "SENTETIK-checker", "SENTETIK", null); var m = new CommandMetadata(key);
            return operation switch
            {
                0 => Service.ApproveAsync(c, m, new(CallerRow.Id, version), default),
                1 => Service.RejectAsync(c, m, new(CallerRow.Id, version, reason), default),
                2 => Service.ApproveReversalAsync(c, m, new(CallerRow.Id, version), default),
                _ => Service.RejectReversalAsync(c, m, new(CallerRow.Id, version, reason), default),
            };
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    private sealed class User(Guid company) : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId => "SENTETIK-checker"; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company }; public bool CanAccessCompany(Guid id) => id == company; }
    private sealed class Authority : IFinancePermissionPort
    {
        public readonly List<FinanceAction> Actions = []; public Action? DuringRead; public PermissionDecision Decision = PermissionDecision.Allowed;
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
        { Actions.Add(a); await Task.Yield(); DuringRead?.Invoke(); return Decision; }
    }
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    { public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken t = default) => throw new InvalidOperationException("No Settings read at entry."); }
    private sealed class NoParty : IFinancePartyAccountPort
    { public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext c, Guid party, Guid account, CancellationToken t) => throw new InvalidOperationException("No Cari read at entry."); }
    private sealed class NoPeriod : IFinanceOpeningPeriodReader
    { public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid company, CancellationToken t) => throw new InvalidOperationException("No opening-period read at entry."); }
    private sealed class Writer : IAuditTransactionWriterV1
    { public bool HasPendingDrafts { get; set; } public int Writes, Discards;
      public Task WritePendingAsync(IDbTransaction tx, CancellationToken t = default) { Writes++; throw new InvalidOperationException("No Audit write at entry."); }
      public void DiscardPending() { Discards++; HasPendingDrafts = false; } }
}
