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

public sealed class FinanceOfficialSalesInvoiceApprovalSafetyTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("ambient")] [InlineData("dirty")] [InlineData("audit")]
    [InlineData("nonrelational-clean")] [InlineData("nonrelational-audit")]
    public async Task Both_native_decisions_refuse_unowned_entry_without_discarding_caller_work(string boundary)
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig(boundary);
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var original = Transaction.Current;
            var answer = await rig.Decide(approve);
            Assert.Equal(CommandOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
            Assert.Equal([approve ? FinanceOfficialSalesInvoiceAuthorityActionV1.Approve : FinanceOfficialSalesInvoiceAuthorityActionV1.Reject], rig.Authority.Actions);
            Assert.Same(original, Transaction.Current); Assert.Equal(0, rig.Writer.Writes); Assert.Equal(0, rig.Writer.Discards);
            Assert.Equal(boundary is "audit" or "nonrelational-audit", rig.Writer.HasPendingDrafts);
            Assert.Equal(boundary == "dirty", rig.Database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(rig.CallerRow, Assert.Single(rig.Database.ChangeTracker.Entries()).Entity);
        }
    }

    [Theory] [InlineData("dirty")] [InlineData("audit")]
    public async Task Caller_work_added_during_the_first_fresh_authority_await_is_not_owned_or_cleaned(string work)
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig("clean");
            rig.Authority.DuringRead = () => { if (work == "dirty") rig.Database.OfficialSalesInvoices.Add(rig.CallerRow); else rig.Writer.HasPendingDrafts = true; };
            Assert.Equal(CommandOutcome.Unavailable, (await rig.Decide(approve)).Outcome);
            Assert.Single(rig.Authority.Actions); Assert.Equal(0, rig.Writer.Writes); Assert.Equal(0, rig.Writer.Discards);
            Assert.Equal(work == "audit", rig.Writer.HasPendingDrafts); Assert.Equal(work == "dirty", rig.Database.ChangeTracker.HasChanges());
            if (work == "dirty") Assert.Same(rig.CallerRow, Assert.Single(rig.Database.ChangeTracker.Entries()).Entity);
        }
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Native_authority_refusal_precedes_persistence_and_preserves_caller_audit(PermissionDecision decision, CommandOutcome outcome)
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig("nonrelational-audit"); rig.Authority.Decision = decision;
            Assert.Equal(outcome, (await rig.Decide(approve)).Outcome); Assert.True(rig.Writer.HasPendingDrafts);
            Assert.Equal(0, rig.Writer.Discards); Assert.Equal(0, rig.Writer.Writes);
        }
    }

    [Fact]
    public async Task Unresolved_company_keeps_native_denial_and_the_readonly_candidates_alias_is_also_guarded()
    {
        foreach (var approve in new[] { true, false })
        {
            await using var rig = new Rig("unresolved");
            Assert.Equal(CommandOutcome.Denied, (await rig.Decide(approve)).Outcome);
            Assert.Empty(rig.Authority.Actions); Assert.Equal(0, rig.Writer.Discards);
            Assert.IsAssignableFrom<IFinanceOfficialSalesInvoiceCandidatesV1>(rig.Service);
            Assert.IsNotType<FinanceOfficialSalesInvoiceServiceV1>(rig.Service);
        }
    }

    [Fact]
    public async Task Native_key_and_canonical_reason_refusals_precede_unavailable_persistence_without_caller_cleanup()
    {
        await using var rig = new Rig("audit");
        Assert.Equal(CommandOutcome.ValidationFailed, (await rig.Decide(true, key: " padded ")).Outcome);
        foreach (var reason in new[] { "", " padded ", new string('x', 1001) })
            Assert.Equal(CommandOutcome.ValidationFailed, (await rig.Decide(false, reason: reason)).Outcome);
        Assert.True(rig.Writer.HasPendingDrafts); Assert.Equal(0, rig.Writer.Discards); Assert.Equal(0, rig.Writer.Writes);
    }

    private sealed class Rig : IAsyncDisposable
    {
        public readonly CompanyContext Company = new(); public readonly Authority Authority = new(); public readonly Writer Writer = new();
        public FinanceDbContext Database { get; } public FinanceOfficialSalesInvoiceRow CallerRow { get; }
        public IFinanceOfficialSalesInvoiceServiceV1 Service { get; }
        private readonly Guid companyId = Guid.NewGuid();
        public Rig(string boundary)
        {
            if (boundary != "unresolved") Company.Set(companyId);
            var options = new DbContextOptionsBuilder<FinanceDbContext>();
            if (boundary.StartsWith("nonrelational", StringComparison.Ordinal)) options.UseInMemoryDatabase("SENTETIK-official-entry-" + Guid.NewGuid());
            else options.UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;");
            Database = new(options.Options, Company); CallerRow = new() { Id = Guid.NewGuid(), CompanyId = companyId };
            if (boundary == "dirty") Database.OfficialSalesInvoices.Add(CallerRow);
            Writer.HasPendingDrafts = boundary is "audit" or "nonrelational-audit";
            var envelope = new FinanceAuditEnvelope("SENTETIK", [], "Web");
            var core = new FinanceOfficialSalesInvoiceServiceV1(Database, Company, new User(companyId), Authority,
                new NoDocuments(), new NoCandidates(), new AuditEventCollector(), new(envelope, envelope, envelope, envelope), TimeProvider.System, new NoCustomers());
            Service = core.WithApprovalSafety(boundary == "missing-writer" ? null : Writer);
        }
        public Task<FinanceOfficialSalesInvoiceResultV1> Decide(bool approve, string key = "SENTETIK-key", string reason = "SENTETIK ret") => approve
            ? Service.ApproveAsync(new(companyId, "SENTETIK-checker", "SENTETIK", null), new(key), new(CallerRow.Id, "1"), default)
            : Service.RejectAsync(new(companyId, "SENTETIK-checker", "SENTETIK", null), new(key), new(CallerRow.Id, "1", reason), default);
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    private sealed class User(Guid company) : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId => "SENTETIK-checker"; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company }; public bool CanAccessCompany(Guid id) => id == company; }
    private sealed class Authority : IFinanceOfficialSalesInvoiceAuthorityV1
    {
        public List<FinanceOfficialSalesInvoiceAuthorityActionV1> Actions { get; } = []; public Action? DuringRead; public PermissionDecision Decision = PermissionDecision.Allowed;
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceOfficialSalesInvoiceAuthorityActionV1 a, Guid id, CancellationToken t)
        { Actions.Add(a); await Task.Yield(); DuringRead?.Invoke(); return Decision; }
    }
    private sealed class NoDocuments : IFinanceOfficialSalesInvoiceDocumentPortV1
    { public Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext c, Guid document, Guid invoice, int? version, CancellationToken t) => throw new InvalidOperationException("No document read at entry."); }
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    { public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken t = default) => throw new InvalidOperationException("No Settings read at entry."); }
    private sealed class NoCustomers : IFinanceOfficialSalesInvoiceCustomerPortV1
    { public Task<ReadOutcome> CheckCompanyPartyAsync(FinanceContext c, Guid id, CancellationToken t) => throw new InvalidOperationException("No Cari read at entry."); }
    private sealed class Writer : IAuditTransactionWriterV1
    { public bool HasPendingDrafts { get; set; } public int Writes, Discards;
      public Task WritePendingAsync(IDbTransaction tx, CancellationToken t = default) { Writes++; throw new InvalidOperationException("No audit write at entry."); }
      public void DiscardPending() { Discards++; HasPendingDrafts = false; } }
}
