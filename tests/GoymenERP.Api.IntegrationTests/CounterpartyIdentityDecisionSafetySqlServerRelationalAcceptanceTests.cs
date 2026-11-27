using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// Existing disposable SQL fixtures prepare real parties/proposals through native HTTP commands.
// The controlled principal/authority model only the later native decision boundary, not real grants or Work UI.
public sealed class CounterpartyIdentityDecisionSafetySqlServerRelationalAcceptanceTests
{
    [SqlServerTheory]
    [InlineData(true, true)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(false, false)]
    public async Task Identity_decisions_roll_back_late_authority_actor_company_and_delegation_loss_then_retry_and_replay_the_original_204(bool tax, bool approve)
    {
        await using var data = await Case.CreateAsync(tax);
        var probe = new SaveProbe(); await using var harness = new Harness(data, probe);
        var historyBefore = await data.CountHistoryAsync();
        foreach (var phase in new[] { "pending-audit", "saved" })
        foreach (var fault in new[] { "permission", "actor", "company", "delegation" })
        {
            await using var run = harness.Scope();
            void Revoke()
            {
                if (fault == "permission") harness.Authority.Granted = false;
                if (fault == "actor") harness.User.UserId = "SENTETIK-other";
                if (fault == "company") run.Company.Set(TestCompanyIds.CompanyB);
                if (fault == "delegation") harness.Authority.DelegationValid = false;
            }
            probe.AfterSaved = phase == "saved" ? Revoke : null;
            harness.Authority.BeforeFresh = () => { if (phase == "pending-audit" && run.Writer.HasPendingDrafts) Revoke(); return Task.CompletedTask; };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => run.DecideAsync(approve, "same-safe-key", "SENTETIK-abort"));
            Assert.Empty(run.Database.ChangeTracker.Entries()); Assert.False(run.Writer.HasPendingDrafts);
            Assert.Null(run.Database.Database.CurrentTransaction);
            harness.Restore(); probe.AfterSaved = null;
            Assert.Equal(data.Version, await data.VersionAsync());
            Assert.Equal("Pending", await data.StatusAsync()); Assert.Equal(historyBefore, await data.CountHistoryAsync());
            Assert.Equal(0, await data.CommandCountAsync()); Assert.Equal(0, await data.DecisionAuditCountAsync());
        }
        await using (var run = harness.Scope())
        {
            var first = await run.DecideAsync(approve, "same-safe-key", "SENTETIK-first", note: new string('n', 1000));
            Assert.Equal((CounterpartyCommandResult.Succeeded, 204, false, "SENTETIK-first"),
                (first.Response.Result, first.Response.StatusCode, first.IsReplay, first.OriginalCorrelationId));
            Assert.Null(first.Response.Resource);
        }
        Assert.Equal(approve ? data.Version + 1 : data.Version, await data.VersionAsync());
        Assert.Equal(approve ? "Approved" : "Rejected", await data.StatusAsync());
        Assert.Equal(1, await data.CommandCountAsync()); Assert.Equal(1, await data.DecisionAuditCountAsync());
        // A later independent change of the shared party never rewrites the original native response.
        using (var maker = data.MakerClient())
        {
            var proposed = await data.LaterProposalAsync(maker, approve ? data.Version + 1 : data.Version);
            Assert.NotEqual(data.RequestId, proposed);
            await using var later = harness.Scope();
            var laterAnswer = await later.DecideAsync(true, "later-key", "SENTETIK-later-change", requestId: proposed,
                expectedVersion: approve ? data.Version + 1 : data.Version);
            Assert.Equal(204, laterAnswer.Response.StatusCode);
        }
        await using (var run = harness.Scope())
        {
            var replay = await run.DecideAsync(approve, "same-safe-key", "SENTETIK-later", note: new string('n', 1000));
            Assert.Equal((204, true, "SENTETIK-first"), (replay.Response.StatusCode, replay.IsReplay, replay.OriginalCorrelationId));
            var changed = await run.DecideAsync(approve, "same-safe-key", "SENTETIK-changed", note: "changed");
            Assert.Equal(CounterpartyCommandResult.IdempotencyConflict, changed.Response.Result);
        }
        harness.User.UserId = Guid.NewGuid().ToString("D");
        await using (var otherActor = harness.Scope())
        {
            var conflict = await otherActor.DecideAsync(approve, "same-safe-key", "SENTETIK-actor", note: new string('n', 1000));
            Assert.Equal(CounterpartyCommandResult.IdempotencyConflict, conflict.Response.Result);
        }
        harness.Restore();
        harness.Authority.Granted = false;
        await using (var denied = harness.Scope())
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.DecideAsync(approve, "same-safe-key", "SENTETIK-revoked", note: new string('n', 1000)));
    }

    [SqlServerTheory]
    [InlineData(true)] [InlineData(false)]
    public async Task Identity_decision_entry_refusals_preserve_borrowed_handles_dirty_entities_and_real_caller_audit(bool tax)
    {
        await using var data = await Case.CreateAsync(tax); await using var harness = new Harness(data, new());
        foreach (var boundary in new[] { "borrowed", "dirty", "audit", "ambient" })
        {
            await using var run = harness.Scope();
            await using var borrowed = boundary == "borrowed" ? await run.Database.Database.BeginTransactionAsync() : null;
            Party? caller = null;
            if (boundary == "dirty")
            {
                caller = new Party(Guid.NewGuid(), PartyCode.FromSequence(99999), PartyKind.LegalEntity, "SENTETIK caller", null);
                run.Database.Parties.Add(caller);
            }
            if (boundary == "audit") run.Collector.Enqueue(CallerDraft(run));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var before = Transaction.Current;
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.DecideAsync(true, "entry", "SENTETIK-entry"));
            if (borrowed is not null) Assert.Same(borrowed, run.Database.Database.CurrentTransaction);
            if (caller is not null) Assert.Equal(EntityState.Added, run.Database.Entry(caller).State);
            if (boundary == "audit") { Assert.True(run.Writer.HasPendingDrafts); run.Writer.DiscardPending(); }
            Assert.Same(before, Transaction.Current);
        }
        Assert.Equal(0, await data.CommandCountAsync()); Assert.Equal("Pending", await data.StatusAsync());
    }

    [SqlServerTheory]
    [InlineData(true, false)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(false, true)]
    public async Task Identity_save_race_rolls_back_before_one_bounded_native_receipt_lookup_and_never_retries_the_mutation(bool tax, bool winner)
    {
        await using var data = await Case.CreateAsync(tax);
        var probe = new SaveProbe { FailNextSave = true }; await using var harness = new Harness(data, probe);
        await using var loser = harness.Scope(); var transactions = new HashSet<Guid>(); var winning = false;
        harness.Authority.BeforeFresh = async () =>
        {
            if (loser.Database.Database.CurrentTransaction is not { } transaction) return;
            transactions.Add(transaction.TransactionId);
            if (winner && transactions.Count == 2 && !winning)
            {
                winning = true;
                Assert.Empty(loser.Database.ChangeTracker.Entries()); Assert.False(loser.Writer.HasPendingDrafts);
                await using var actualWinner = harness.Scope();
                var written = await actualWinner.DecideAsync(true, "race-key", "SENTETIK-winner");
                Assert.Equal(204, written.Response.StatusCode); Assert.False(written.IsReplay);
            }
        };
        var answer = await loser.DecideAsync(true, "race-key", "SENTETIK-loser");
        Assert.Equal(2, transactions.Count); Assert.False(loser.Writer.HasPendingDrafts);
        Assert.Equal(winner ? 204 : 409, answer.Response.StatusCode); Assert.Equal(winner, answer.IsReplay);
        Assert.Equal(winner ? 1 : 0, await data.CommandCountAsync());
        Assert.Equal(winner ? 1 : 0, await data.DecisionAuditCountAsync());
        Assert.Equal(winner ? "Approved" : "Pending", await data.StatusAsync());
        harness.Restore();
        if (!winner)
        {
            Assert.Equal(1, probe.SaveAttempts);
            await using var retry = harness.Scope();
            var answerAfter = await retry.DecideAsync(true, "race-key", "SENTETIK-after-race");
            Assert.Equal(204, answerAfter.Response.StatusCode); Assert.False(answerAfter.IsReplay);
        }
        else Assert.Equal("SENTETIK-winner", answer.OriginalCorrelationId);
    }

    [SqlServerTheory]
    [InlineData(true)] [InlineData(false)]
    public async Task Identity_foreign_account_and_maker_refusals_remain_native_terminal_receipts_and_optional_note_is_not_mandatory(bool tax)
    {
        await using var data = await Case.CreateAsync(tax); await using var harness = new Harness(data, new());
        await using (var foreign = harness.Scope(TestCompanyIds.CompanyB))
        {
            var refused = await foreign.DecideAsync(false, "foreign-key", "SENTETIK-foreign");
            Assert.Equal((404, false), (refused.Response.StatusCode, refused.IsReplay));
            var replay = await foreign.DecideAsync(false, "foreign-key", "SENTETIK-foreign-replay");
            Assert.Equal((404, true, "SENTETIK-foreign"), (replay.Response.StatusCode, replay.IsReplay, replay.OriginalCorrelationId));
        }
        harness.User.UserId = data.RequestedBy;
        await using (var maker = harness.Scope())
        {
            var refused = await maker.DecideAsync(false, "maker-key", "SENTETIK-maker");
            Assert.Equal(409, refused.Response.StatusCode);
            var replay = await maker.DecideAsync(false, "maker-key", "SENTETIK-maker-replay");
            Assert.True(replay.IsReplay); Assert.Equal(refused.Response.Result, replay.Response.Result);
        }
        harness.Restore();
        await using var checker = harness.Scope();
        var rejected = await checker.DecideAsync(false, "reject-no-note", "SENTETIK-no-note");
        Assert.Equal(204, rejected.Response.StatusCode); Assert.Equal(data.Version, await data.VersionAsync());
    }

    private static AuditEventDraft CallerDraft(NativeScope run) => new(run.Company.CompanyId, "synthetic.counterparty.caller.v1",
        "Counterparty.Party", Guid.NewGuid().ToString("D"), "Caller", null, 1, AuditActorTypes.User, run.User.UserId,
        "counterparty.parties.read", [], "{}", "{}", "SENTETIK caller-owned", null, AuditAiInvolvement.None, null,
        "Web", null, null, "SENTETIK-caller", null, "caller-key");

    internal sealed record Case(bool Tax, Guid PartyId, Guid RequestId, long Version, string RequestedBy,
        SqlServerRelationalApiFactory Factory, IAsyncDisposable Fixture) : IAsyncDisposable
    {
        public string Table => Tax ? "PartyTaxIdentityChangeRequests" : "PartyLegalNameChangeRequests";
        public string Command => Tax ? "counterparty.party.tax-identity.change-decision.v1" : "counterparty.party.legal-name.change-decision.v1";
        public string EventPrefix => Tax ? "counterparty.party.tax-identity.change-" : "counterparty.party.legal-name.change-";
        public static Task<Case> CreateAsync(bool tax) => tax
            ? CounterpartyTaxIdentitySqlServerRelationalAcceptanceTests.PrepareSafetyCaseAsync()
            : CounterpartyLegalNameSqlServerRelationalAcceptanceTests.PrepareSafetyCaseAsync();
        public HttpClient MakerClient() => Factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        public Task<long> VersionAsync() => ScalarAsync<long>("SELECT Version FROM counterparty.Parties WHERE Id=@p");
        public Task<string> StatusAsync() => ScalarAsync<string>($"SELECT Status FROM counterparty.{Table} WHERE Id=@r");
        public Task<int> CommandCountAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM idempotency.CommandExecutions WHERE CommandName=@n AND CompanyId=@c");
        public Task<int> DecisionAuditCountAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE (EntityId=CONVERT(nvarchar(36),@p) OR EntityId=CONVERT(nvarchar(36),@r)) AND EventType IN (@approved,@rejected)");
        public Task<int> CountHistoryAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM counterparty.PartyIdentityRevisions WHERE PartyId=@p");
        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            command.Parameters.AddWithValue("@p", PartyId); command.Parameters.AddWithValue("@r", RequestId);
            command.Parameters.AddWithValue("@n", Command); command.Parameters.AddWithValue("@c", TestCompanyIds.CompanyA);
            command.Parameters.AddWithValue("@approved", EventPrefix + "approved.v1"); command.Parameters.AddWithValue("@rejected", EventPrefix + "rejected.v1");
            return Assert.IsType<T>(await command.ExecuteScalarAsync());
        }
        public async Task<Guid> LaterProposalAsync(HttpClient maker, long version)
        {
            maker.DefaultRequestHeaders.Remove("Idempotency-Key");
            maker.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var path = $"/api/v1/counterparties/{PartyId}/" + (Tax ? "tax-identity/change-request" : "legal-name/change-requests");
            object body = Tax ? new { expectedVersion = version.ToString(CultureInfo.InvariantCulture), proposedTaxIdentityNumber = "4567890128", reason = "SENTETIK later proposal" }
                : new { expectedVersion = version.ToString(CultureInfo.InvariantCulture), proposedLegalName = "SENTETIK later legal name", reason = "SENTETIK later proposal" };
            using var result = Tax ? await maker.PutAsJsonAsync(path, body) : await maker.PostAsJsonAsync(path, body);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, result.StatusCode);
            using var current = await maker.GetAsync($"/api/v1/counterparties/{PartyId}/" + (Tax ? "tax-identity" : "legal-name"));
            Assert.Equal(System.Net.HttpStatusCode.OK, current.StatusCode);
            using var json = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.TryGetProperty("pendingChange", out var pending));
            Assert.True(pending.TryGetProperty("id", out var id));
            return id.GetGuid();
        }
        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
    private sealed class Harness : IAsyncDisposable
    {
        public readonly Case Data; public readonly User User = new(); public readonly Authority Authority = new();
        public readonly WebApplicationFactory<Program> Factory;
        public Harness(Case data, SaveProbe probe)
        {
            Data = data;
            Factory = data.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentUser>(); services.AddSingleton<ICurrentUser>(User);
                services.RemoveAll<IErpAuthorityRevalidationService>(); services.AddSingleton<IErpAuthorityRevalidationService>(Authority);
                services.RemoveAll<IErpAuthorizationService>(); services.AddSingleton<IErpAuthorizationService>(Authority);
                services.AddDbContext<CounterpartyDbContext>((_, options) => options.AddInterceptors(probe));
            }));
        }
        public NativeScope Scope(Guid? company = null) => new(this, company ?? TestCompanyIds.CompanyA);
        public void Restore() { User.UserId = User.Original; Authority.Granted = true; Authority.DelegationValid = true; Authority.BeforeFresh = null; }
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
    private sealed class NativeScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope; private readonly Harness harness;
        public ICompanyContext Company { get; } public CounterpartyDbContext Database { get; }
        public IAuditTransactionWriterV1 Writer { get; } public AuditEventCollector Collector { get; }
        public User User => harness.User;
        public NativeScope(Harness harness, Guid company)
        {
            this.harness = harness; scope = harness.Factory.Services.CreateAsyncScope();
            Company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); Company.Set(company);
            Database = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            Writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            Collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        }
        public Task<CounterpartyCommandExecution> DecideAsync(bool approve, string key, string correlation, string? note = null,
            Guid? requestId = null, long? expectedVersion = null)
        {
            var data = harness.Data; var metadata = new CounterpartyCommandMetadata(Company.CompanyId, User.UserId, correlation, null, "Web", null, null, key);
            var version = (expectedVersion ?? data.Version).ToString(CultureInfo.InvariantCulture); var decision = approve ? "approve" : "reject";
            return data.Tax ? scope.ServiceProvider.GetRequiredService<ICounterpartyTaxIdentityService>()
                .DecideTaxIdentityChangeAsync(data.PartyId, new(version, requestId ?? data.RequestId, decision, note), metadata, default)
                : scope.ServiceProvider.GetRequiredService<ICounterpartyLegalNameService>()
                .DecideLegalNameChangeAsync(data.PartyId, new(version, requestId ?? data.RequestId, decision, note), metadata, default);
        }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private sealed class User : ICurrentUser
    {
        public readonly string Original = Guid.NewGuid().ToString("D"); public string UserId { get; set; }
        public User() => UserId = Original;
        public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA, TestCompanyIds.CompanyB]);
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Authority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public bool Granted = true, DelegationValid = true; public Func<Task>? BeforeFresh;
        private PermissionScope Scope() => new(Granted && DelegationValid, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Granted);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Scope());
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { if (BeforeFresh is not null) await BeforeFresh(); return Scope(); }
    }
    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public bool FailNextSave; public int SaveAttempts; public Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is CounterpartyDbContext database && (database.ChangeTracker.Entries<PartyTaxIdentityChangeRequest>().Any(entry => entry.State == EntityState.Modified)
                || database.ChangeTracker.Entries<PartyLegalNameChangeRequest>().Any(entry => entry.State == EntityState.Modified)))
            { SaveAttempts++; if (FailNextSave) { FailNextSave = false; throw new DbUpdateConcurrencyException("SENTETIK native decision save race"); } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (data.Context is CounterpartyDbContext) AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
}

public sealed partial class CounterpartyTaxIdentitySqlServerRelationalAcceptanceTests
{
    internal static async Task<CounterpartyIdentityDecisionSafetySqlServerRelationalAcceptanceTests.Case> PrepareSafetyCaseAsync()
    {
        var test = new TaxTest();
        try
        {
            using var maker = test.Client(); var party = await CreateAsync(maker, "SENTETIK tax decision boundary");
            await EnterAsync(maker, party, Tckn); await RequestAsync(maker, party, "2", Vkn);
            var json = await ReadAsync(maker, PathFor(party));
            Assert.True(json.TryGetProperty("pendingChange", out var pending));
            Assert.True(pending.TryGetProperty("id", out var id)); Assert.True(pending.TryGetProperty("requestedBy", out var actor));
            return new(true, party, id.GetGuid(), 2, actor.GetString()!, test.Factory, test);
        }
        catch { await test.DisposeAsync(); throw; }
    }
}
public sealed partial class CounterpartyLegalNameSqlServerRelationalAcceptanceTests
{
    internal static async Task<CounterpartyIdentityDecisionSafetySqlServerRelationalAcceptanceTests.Case> PrepareSafetyCaseAsync()
    {
        var test = new LegalNameTest();
        try
        {
            using var maker = test.Client(); var party = await CreatePartyAsync(maker, "SENTETIK legal decision boundary");
            var id = await ProposeAsync(maker, party, "1", "SENTETIK approved legal name");
            var json = await ReadAsync(maker, PathFor(party)); Assert.True(json.TryGetProperty("pendingChange", out var pending));
            Assert.True(pending.TryGetProperty("requestedBy", out var actor));
            return new(false, party, id, 1, actor.GetString()!, test.Factory, test);
        }
        catch { await test.DisposeAsync(); throw; }
    }
}
