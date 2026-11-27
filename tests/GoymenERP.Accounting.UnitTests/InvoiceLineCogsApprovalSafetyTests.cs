using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class InvoiceLineCogsApprovalSafetyTests
{
    private static readonly Guid Company = Guid.Parse("c0540000-0000-4000-8000-000000000001");
    private static readonly Guid Maker = Guid.Parse("c0540000-0000-4000-8000-000000000002");
    private static readonly Guid Checker = Guid.Parse("c0540000-0000-4000-8000-000000000003");
    private static readonly Guid Invoice = Guid.Parse("c0540000-0000-4000-8000-000000000004");
    private static readonly Guid Line = Guid.Parse("c0540000-0000-4000-8000-000000000005");
    private static readonly Guid Proposal = Guid.Parse("c0540000-0000-4000-8000-000000000006");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("missing-owner")] [InlineData("missing-writer")] [InlineData("other-context")] [InlineData("other-collector")]
    [InlineData("other-writer")] [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending")]
    [InlineData("ambient")] [InlineData("await-dirty")] [InlineData("await-pending")]
    public async Task Production_boundary_never_adopts_caller_state_or_reads_the_native_owner(string fault)
    {
        var company = new CompanyContextDouble(); await using var db = Db(company); await using var other = Db(company);
        var collector = new Collector(); var writer = new Writer(collector); var alternate = new Writer(collector);
        var core = new AccountingStore(fault == "other-context" ? other : db, company,
            fault == "other-collector" ? new Collector() : collector, TimeProvider.System, null!,
            normalApprovalAuditWriter: fault == "missing-writer" ? null : fault == "other-writer" ? alternate : writer);
        var store = new InvoiceLineCogsCorrectionStoreV1(db, company, null!, collector, writer, TimeProvider.System,
            approvalSafetyOwner: fault == "missing-owner" ? null : core);
        var caller = Caller(); if (fault == "dirty") db.Commands.Add(caller); if (fault == "pending") collector.Enqueue(Draft());
        using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var attempts = 0;
        var answer = await store.RunApprovalAsync(_ => { attempts++; throw new InvalidOperationException("No native I/O at rejected entry"); }, _ =>
        {
            if (fault == "await-dirty") db.Commands.Add(caller); if (fault == "await-pending") collector.Enqueue(Draft()); return Task.CompletedTask;
        });
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(fault is "dirty" or "await-dirty", db.ChangeTracker.HasChanges()); Assert.Equal(fault is "pending" or "await-pending", writer.HasPendingDrafts);
        if (db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Unimplemented_approval_boundary_fails_closed_without_starting_a_delegate()
    {
        IInvoiceLineCogsCorrectionStoreV1 store = new MissingBoundaryStore(); var attempts = 0;
        var answer = await store.RunApprovalAsync(_ => { attempts++; throw new InvalidOperationException(); }, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Equal(0, attempts);
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task Public_approval_captures_before_first_authority_await_and_preserves_unowned_audit(string fault)
    {
        var company = new CompanyContextDouble(); var user = new User(); var authority = new Authority();
        await using var db = Db(company); var collector = new Collector(); collector.Enqueue(Draft()); var writer = new Writer(collector);
        var core = new AccountingStore(db, company, collector, TimeProvider.System, null!, normalApprovalAuditWriter: writer);
        var store = new InvoiceLineCogsCorrectionStoreV1(db, company, null!, collector, writer, TimeProvider.System, approvalSafetyOwner: core);
        var source = new Source(); using var cancellation = new CancellationTokenSource();
        authority.AfterRead = () => Drift(fault, company, user, authority, cancellation);
        var service = Service(store, new NeverCommands(), source, authority, company, user);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Approve(service, "capture", cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await Approve(service, "capture")).Status);
        Assert.Equal(1, authority.Reads); Assert.Equal(0, source.Reads); Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, writer.Writes);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")] [InlineData("cancel")]
    public async Task Synthetic_source_ignoring_cancellation_or_drifting_context_cannot_enter_native_mutation(string fault)
    {
        var store = new Protocol(); var source = new Source(); var authority = new Authority(); var company = new CompanyContextDouble(); var user = new User();
        using var cancellation = new CancellationTokenSource(); source.AfterRead = () => Drift(fault, company, user, authority, cancellation);
        var service = Service(store, store, source, authority, company, user);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Approve(service, "source-await", cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await Approve(service, "source-await")).Status);
        Assert.Equal(1, source.Reads); Assert.Equal(0, store.Effects); Assert.Empty(store.Commands);
    }

    [Fact]
    public async Task Synthetic_original_receipt_uses_the_unchanged_native_hash_and_replays_before_a_changed_source()
    {
        var store = new Protocol(); var source = new Source(); var user = new User(); var service = Service(store, store, source, new Authority(), new CompanyContextDouble(), user);
        var first = await Approve(service, "original"); source.Unavailable = true; var replay = await Approve(service, "original");
        Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-cogs-approval", replay.OriginalCorrelationId);
        Assert.Equal(1, source.Reads); Assert.Equal(1, store.Effects); var record = Assert.Single(store.Commands);
        var command = "accounting.invoice-line-cogs.approve.v1";
        var payload = JsonSerializer.Serialize(new { command, invoiceId = Invoice, lineId = Line, payload = new { proposalId = Proposal, request = new ApproveInvoiceLineCogsCorrectionV1(1) } }, Json);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))), record.RequestHash);
        Assert.Equal(8, JsonSerializer.SerializeToElement(first, Json).EnumerateObject().Count()); Assert.Equal(22, JsonSerializer.SerializeToElement(first.Resource, Json).EnumerateObject().Count());
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await service.ApproveAsync(Invoice, Line, Proposal, new(2), "original")).Code);
        user.Actor = Maker; Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await Approve(service, "original")).Code); Assert.Equal(1, store.Effects);
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Synthetic_replay_rechecks_after_token_ignoring_original_key_read(string fault)
    {
        var store = new Protocol(); var source = new Source(); var company = new CompanyContextDouble(); var user = new User(); var authority = new Authority();
        var service = Service(store, store, source, authority, company, user); await Approve(service, "original");
        using var cancellation = new CancellationTokenSource(); store.AfterFind = () => Drift(fault, company, user, authority, cancellation);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Approve(service, "original", cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await Approve(service, "original")).Status);
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands); Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task Synthetic_readonly_winner_pass_never_calls_source_mutation_or_terminal_refusal_writer()
    {
        var store = new Protocol { ReplayOnly = true }; var source = new Source(); var service = Service(store, store, source, new Authority(), new CompanyContextDouble(), new User());
        var result = await Approve(service, "no-winner"); Assert.Equal(AccountingApiStatus.Conflict, result.Status); Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code);
        Assert.Equal(1, store.Finds); Assert.Equal(0, source.Reads); Assert.Equal(0, store.Effects); Assert.Empty(store.Commands); Assert.Equal(0, store.Refusals);
    }

    [Theory] [InlineData("not-found")] [InlineData("version")]
    public async Task Synthetic_native_terminal_refusal_remains_durable_and_replays_without_mutation(string fault)
    {
        var store = new Protocol { Fault = fault }; var source = new Source(); var service = Service(store, store, source, new Authority(), new CompanyContextDouble(), new User());
        var first = await Approve(service, "terminal"); var replay = await Approve(service, "terminal");
        Assert.Equal(fault == "not-found" ? AccountingApiStatus.NotFound : AccountingApiStatus.Conflict, first.Status);
        Assert.True(replay.IsReplay); Assert.Equal(first.Code, replay.Code); Assert.Single(store.Commands); Assert.Equal(1, store.Refusals); Assert.Equal(0, store.Effects);
    }

    [Fact]
    public async Task Synthetic_native_maker_denial_keeps_code_without_fabricating_a_success_or_consuming_a_key()
    {
        var store = new Protocol { Fault = "maker" }; var source = new Source(); var service = Service(store, store, source, new Authority(), new CompanyContextDouble(), new User());
        var first = await Approve(service, "maker"); Assert.Equal(AccountingApiStatus.Denied, first.Status); Assert.Equal(InvoiceLineCogsCorrectionCodesV1.SelfApproval, first.Code);
        Assert.Empty(store.Commands); Assert.Equal(0, store.Effects);
    }

    private static void Drift(string fault, CompanyContextDouble company, User user, Authority authority, CancellationTokenSource cancellation)
    { if (fault == "company") company.Set(Guid.NewGuid()); if (fault == "actor") user.Actor = Maker; if (fault == "member") user.Member = false; if (fault == "permission") authority.Granted = false; if (fault == "scope") authority.CompanyWide = false; if (fault == "null-scope") authority.Null = true; if (fault == "cancel") cancellation.Cancel(); }
    private static Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> Approve(InvoiceLineCogsCorrectionServiceV1 service, string key, CancellationToken token = default) => service.ApproveAsync(Invoice, Line, Proposal, new(1), key, token);
    private static InvoiceLineCogsCorrectionServiceV1 Service(IInvoiceLineCogsCorrectionStoreV1 store, IAccountingCommandLog commands, Source source, Authority authority, ICompanyContext company, User user)
    { var correlation = new CorrelationContext(); correlation.Set("SENTETIK-cogs-approval", null, "Web", null, null); return new(store, source, source, commands, authority, company, user, correlation); }
    private static AccountingDbContext Db(ICompanyContext company) => new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("cogs-entry-" + Guid.NewGuid()).Options, company);
    private static AccountingCommandRow Caller() => new() { CompanyId = Company, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft Draft() => new(Company, "SENTETIK caller", "Accounting.InvoiceLineCogsCorrection", "caller", "Approve", 1, 2, "User", Checker.ToString("D"), AccountingJournalReversalPermissionNamesV1.Approve,
        [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "UnitTest", null, null, "SENTETIK-caller", null, "SENTETIK-key");
    private sealed class CompanyContextDouble : ICompanyContext { public Guid CompanyId { get; private set; } = Company; public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid value) => CompanyId = value; }
    private sealed class User : ICurrentUser { public Guid Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService
    { public int Reads; public bool Granted = true, CompanyWide = true, Null; public Action? AfterRead; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid id, CancellationToken token = default) { Reads++; AfterRead?.Invoke(); return Task.FromResult(Null ? null! : new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Collector : IAuditEventCollector { public List<AuditEventDraft> Entries { get; } = []; public void Enqueue(AuditEventDraft draft) => Entries.Add(draft); }
    private sealed class Writer(Collector collector) : IAuditTransactionWriterV1
    { public int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0; public Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default) { Writes++; throw new InvalidOperationException("Entry-only writer cannot append Audit"); } public void DiscardPending() { Discards++; collector.Entries.Clear(); } }
    private sealed class NeverCommands : IAccountingCommandLog
    { public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => throw new InvalidOperationException("No command I/O at denied entry"); public Task RecordCommandAsync(AccountingCommandRecord value, CancellationToken token = default) => throw new NotSupportedException(); }
    private sealed class Source : IAccountingFinalizedInvoiceLineSourceV1, IAccountingInvoiceLineProductReferenceV1
    {
        public int Reads; public bool Unavailable; public Action? AfterRead;
        public Task<FinalizedInvoiceLineSourceReadV1> GetAsync(Guid company, Guid invoice, Guid line, CancellationToken token = default)
        { Reads++; var value = new FinalizedInvoiceLineSourceReadV1(Unavailable ? FinalizedInvoiceLineSourceOutcomeV1.Unavailable : FinalizedInvoiceLineSourceOutcomeV1.Available, company, invoice, line,
            Unavailable ? null : new(company, invoice, line, Proposal, 7, Proposal, Proposal, Proposal, 1, 1, new(2026, 11, 17), "SENTETIK", Proposal, "{}", new string('A', 64))); AfterRead?.Invoke(); return Task.FromResult(value); }
        public Task<FinalizedInvoiceCogsSelectionReadV1> FindAsync(Guid company, string number, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsProductReferenceV1> GetAsync(Guid company, Guid product, CancellationToken token = default) => throw new NotSupportedException();
    }
    private class MissingBoundaryStore : IInvoiceLineCogsCorrectionStoreV1
    {
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public virtual Task RecordAttemptAsync(AccountingCommandRecord value, Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsHistoryV1> ReadAsync(FinalizedInvoiceLineSourceV1 source, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsCorrectionViewV1?> FindAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsCorrectionViewV1> PrepareAsync(FinalizedInvoiceLineSourceV1 source, PrepareInvoiceLineCogsCorrectionV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public virtual Task<InvoiceLineCogsCorrectionViewV1> ApproveAsync(FinalizedInvoiceLineSourceV1 source, Guid proposal, long expected, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
    // Pure native command protocol only: no relational transaction, SQL rollback or durable Audit is claimed here.
    private sealed class Protocol : MissingBoundaryStore, IInvoiceLineCogsCorrectionStoreV1, IAccountingCommandLog
    {
        public bool ReplayOnly; public string? Fault; public Action? AfterFind; public int Effects, Finds, Refusals; public List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(ReplayOnly); await fence(token); return answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string hash, CancellationToken token = default) { Finds++; var value = Commands.SingleOrDefault(x => x.CommandName == command && x.IdempotencyKeyHash == hash); AfterFind?.Invoke(); return Task.FromResult(value); }
        public Task RecordCommandAsync(AccountingCommandRecord value, CancellationToken token = default) { Commands.Add(value); return Task.CompletedTask; }
        public override async Task RecordAttemptAsync(AccountingCommandRecord value, Func<CancellationToken, Task> fence, CancellationToken token = default) { await fence(token); Refusals++; Commands.Add(value); }
        public override async Task<InvoiceLineCogsCorrectionViewV1> ApproveAsync(FinalizedInvoiceLineSourceV1 source, Guid proposal, long expected, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            await fence(token); if (Fault == "not-found") throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.NotFound, "SENTETIK");
            if (Fault == "version") throw new AccountingVersionConflictException(expected, 2); if (Fault == "maker") throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SelfApproval, "SENTETIK");
            Effects++; var value = new InvoiceLineCogsCorrectionViewV1(proposal, Company, Invoice, Line, 7, 2, 0, "Approved", InvoiceLineCogsClassificationV1.Manufactured,
                new(Proposal, 2, 1, "SENTETIK", new string('A', 64), new(Proposal, "620", "SENTETIK")), Proposal, new(2026, 11, 17), new(2026, 11, 17), null, Maker, DateTimeOffset.UtcNow, actor, DateTimeOffset.UtcNow, "SENTETIK", null, null, null);
            Commands.Add(metadata.Completion!(value)); return value;
        }
    }
}
