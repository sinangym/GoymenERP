using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

// Same native disposable SQL fixture and real scoped Audit writer as tax entry. No production grants or activation.
public sealed partial class CounterpartyTaxIdentitySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Atomic_create_records_initial_tax_and_two_audits_with_a_private_receipt_and_replays_after_later_changes()
    {
        await using var test = new TaxTest(); using var maker = test.Client();
        var command = AtomicCreateCommand("SENTETIK atomic native", $" {Vkn} ");
        NewKey(maker, "atomic-original");
        var correlation = maker.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();
        using var created = await maker.PostAsJsonAsync("/api/v1/counterparties", command);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(original); var view = json.RootElement;
        var party = view.GetProperty("id").GetGuid();
        Assert.Equal("2", view.GetProperty("version").GetString());
        Assert.Equal(8, view.EnumerateObject().Count());
        Assert.Equal("1", view.GetProperty("companyAccounts")[0].GetProperty("version").GetString());
        Assert.DoesNotContain(Vkn, original, StringComparison.Ordinal);
        Assert.False(view.TryGetProperty("createdTaxIdentityEvidence", out _));
        Assert.Equal(Vkn, await test.ScalarAsync<string>("SELECT TaxIdentityNumber FROM counterparty.Parties WHERE Id=@Party", party));
        Assert.Equal(1, await test.ScalarAsync<int>("SELECT COUNT(*) FROM counterparty.PartyIdentityRevisions WHERE PartyId=@Party AND Sequence=2 AND EffectiveFrom IS NULL", party));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=CONVERT(nvarchar(36),@Party) AND EventType IN (N'counterparty.party.created.v1',N'counterparty.party.tax-identity.entered.v1')", party));
        Assert.Equal(2, await test.ScalarAsync<int>("SELECT SchemaVersion FROM idempotency.CommandExecutions WHERE CommandName=N'counterparty.party.create.v1' AND JSON_VALUE(ResponseJson,'$.Evidence.PartyId')=CONVERT(nvarchar(36),@Party)", party));
        var retained = await test.ScalarAsync<string>("SELECT ResponseJson FROM idempotency.CommandExecutions WHERE JSON_VALUE(ResponseJson,'$.Evidence.PartyId')=CONVERT(nvarchar(36),@Party)", party);
        Assert.DoesNotContain(Vkn, retained, StringComparison.Ordinal); // History owns the value; proof only binds the canonical hash.

        await RequestAsync(maker, party, "2", OtherVkn);
        var pending = (await ReadAsync(maker, PathFor(party))).GetProperty("pendingChange").GetProperty("id").GetGuid();
        using (var checker = test.Client(TestCompanyIds.CompanyA, TestUsers.CompanyA))
        {
            NewKey(checker);
            using var changed = await checker.PutAsJsonAsync($"{PathFor(party)}/change-request/decision",
                new { expectedVersion = "2", changeRequestId = pending, decision = "approve", note = (string?)null });
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }
        NewKey(maker, "atomic-original");
        using (var replay = await maker.PostAsJsonAsync("/api/v1/counterparties", command))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(original, await replay.Content.ReadAsStringAsync());
            Assert.Equal(correlation, replay.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
        }
        NewKey(maker, "atomic-original");
        using (var changedPayload = await maker.PostAsJsonAsync("/api/v1/counterparties", command with { TaxIdentityNumber = OtherVkn }))
            Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
        // AT-009 remains a warning: a distinct create key may store the same number on another card.
        NewKey(maker);
        using var duplicate = await maker.PostAsJsonAsync("/api/v1/counterparties", AtomicCreateCommand("SENTETIK allowed duplicate", OtherVkn));
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
    }

    [SqlServerFact]
    public async Task Atomic_create_keeps_legacy_and_explicit_null_at_schema_one_and_refuses_invalid_or_ungranted_initial_tax()
    {
        await using var test = new TaxTest(); using var maker = test.Client();
        foreach (var body in new object[] {
            new { kind = 1, legalName = "SENTETIK old four", tradeName = (string?)null, roles = 2 },
            new { kind = 1, legalName = "SENTETIK explicit null", tradeName = (string?)null, roles = 2, taxIdentityNumber = (string?)null } })
        {
            NewKey(maker);
            using var response = await maker.PostAsJsonAsync("/api/v1/counterparties", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var view = await JsonAsync(response); var party = view.GetProperty("id").GetGuid();
            Assert.Equal("1", view.GetProperty("version").GetString());
            Assert.Equal(1, await test.ScalarAsync<int>("SELECT SchemaVersion FROM idempotency.CommandExecutions WHERE JSON_VALUE(ResponseJson,'$.Resource.Id')=CONVERT(nvarchar(36),@Party)", party));
            Assert.Equal(0, await test.ScalarAsync<int>("SELECT COUNT(*) FROM counterparty.PartyIdentityRevisions WHERE PartyId=@Party", party));
        }
        var before = await AtomicCreateFootprintAsync(test);
        NewKey(maker);
        using (var invalid = await maker.PostAsJsonAsync("/api/v1/counterparties", AtomicCreateCommand("SENTETIK invalid", "1234567891")))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before, await AtomicCreateFootprintAsync(test));
        await using var noGrant = new TaxTest(grant: false); using var ungranted = noGrant.Client();
        var ungrantedBefore = await AtomicCreateFootprintAsync(noGrant); NewKey(ungranted);
        using var denied = await ungranted.PostAsJsonAsync("/api/v1/counterparties", AtomicCreateCommand("SENTETIK no grant", Vkn));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(ungrantedBefore, await AtomicCreateFootprintAsync(noGrant));
    }

    [SqlServerFact]
    public async Task Atomic_create_rolls_back_saved_party_receipt_history_and_audit_on_late_context_or_permission_loss()
    {
        await using var test = new TaxTest(); var probe = new AtomicCreateProbe();
        await using var harness = new AtomicCreateHarness(test, probe);
        var before = await AtomicCreateFootprintAsync(test);
        foreach (var fault in new[] { "create", "read", "update", "scope", "actor", "company", "membership", "authentication" })
        {
            harness.Restore();
            probe.AfterSaved = () =>
            {
                if (fault == "create") harness.Authority.DeniedPermission = ErpPermissions.CounterpartyPartiesCreate;
                if (fault == "read") harness.Authority.DeniedPermission = ErpPermissions.CounterpartyPartiesRead;
                if (fault == "update") harness.Authority.DeniedPermission = CounterpartyTaxIdentityPermissionsV1.Update;
                if (fault == "scope") harness.Authority.NarrowPermission = CounterpartyTaxIdentityPermissionsV1.Update;
                if (fault == "actor") harness.User.UserId = "SENTETIK changed creator";
                if (fault == "company") harness.Company.Set(TestCompanyIds.CompanyB);
                if (fault == "membership") harness.User.Access = false;
                if (fault == "authentication") harness.User.Authenticated = false;
            };
            await using var run = harness.Scope();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => run.CreateAsync("atomic-late", "SENTETIK-late"));
            run.AssertClean(); harness.Restore(); probe.AfterSaved = null;
            Assert.Equal(before, await AtomicCreateFootprintAsync(test));
        }
        await using var retry = harness.Scope();
        var answer = await retry.CreateAsync("atomic-late", "SENTETIK-restored");
        Assert.Equal((CounterpartyCommandResult.Created, false, 2L), (answer.Response.Result, answer.IsReplay, answer.Response.Resource!.Version));
        Assert.False(retry.Writer.HasPendingDrafts); Assert.Null(retry.Database.Database.CurrentTransaction);
        var durable = await AtomicCreateFootprintAsync(test);
        harness.User.UserId = "SENTETIK different creator";
        await using (var differentActor = harness.Scope())
        {
            var conflict = await differentActor.CreateAsync("atomic-late", "SENTETIK-other-actor");
            Assert.Equal(CounterpartyCommandResult.IdempotencyConflict, conflict.Response.Result);
            Assert.Null(conflict.Response.Resource);
        }
        harness.Restore();
        await using (var replayScope = harness.Scope())
        {
            var replay = await replayScope.CreateAsync("atomic-late", "SENTETIK-replay");
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-restored", replay.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(answer.Response.Resource), JsonSerializer.Serialize(replay.Response.Resource));
        }
        Assert.Equal(durable, await AtomicCreateFootprintAsync(test));
    }

    [SqlServerFact]
    public async Task Atomic_create_real_audit_failure_and_owner_save_race_clean_up_before_same_key_retry()
    {
        await using var test = new TaxTest(); var probe = new AtomicCreateProbe();
        await using var harness = new AtomicCreateHarness(test, probe);
        var before = await AtomicCreateFootprintAsync(test);
        foreach (var fault in new[] { "audit", "owner-race" })
        {
            probe.FailAudit = fault == "audit"; probe.FailOwner = fault == "owner-race";
            var saves = probe.OwnerSaves;
            await using (var run = harness.Scope())
            {
                if (fault == "audit") await Assert.ThrowsAsync<InvalidOperationException>(() => run.CreateAsync("atomic-failure", "SENTETIK-audit"));
                else await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => run.CreateAsync("atomic-failure", "SENTETIK-race"));
                run.AssertClean();
            }
            Assert.Equal(saves + 1, probe.OwnerSaves); // One mutation attempt, never a mutation retry after absent winner.
            Assert.Equal(before, await AtomicCreateFootprintAsync(test));
        }
        await using var retry = harness.Scope();
        var answer = await retry.CreateAsync("atomic-failure", "SENTETIK-retry");
        Assert.Equal(CounterpartyCommandResult.Created, answer.Response.Result); Assert.False(answer.IsReplay);
        Assert.False(retry.Writer.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Atomic_create_entry_refusals_preserve_caller_transaction_dirty_entities_and_audit_drafts()
    {
        await using var test = new TaxTest(); await using var harness = new AtomicCreateHarness(test, new());
        var before = await AtomicCreateFootprintAsync(test);
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
            if (boundary == "audit") run.Collector.Enqueue(AtomicCreateCallerDraft(harness));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var ambientBefore = Transaction.Current;
            await Assert.ThrowsAsync<InvalidOperationException>(() => run.CreateAsync("caller-entry", "SENTETIK-entry"));
            Assert.Same(ambientBefore, Transaction.Current);
            if (borrowed is not null) Assert.Same(borrowed, run.Database.Database.CurrentTransaction);
            if (caller is not null) Assert.Equal(EntityState.Added, run.Database.Entry(caller).State);
            if (boundary == "audit") { Assert.True(run.Writer.HasPendingDrafts); run.Writer.DiscardPending(); }
        }
        Assert.Equal(before, await AtomicCreateFootprintAsync(test));
    }

    [SqlServerFact]
    public async Task Atomic_create_refuses_corrupt_private_receipts_without_rewriting_or_fabricating_a_current_resource()
    {
        await using var test = new TaxTest(); using var maker = test.Client();
        NewKey(maker, "atomic-proof"); var body = AtomicCreateCommand("SENTETIK corrupt proof", Vkn);
        using var created = await maker.PostAsJsonAsync("/api/v1/counterparties", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var party = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var original = await test.ScalarAsync<string>("SELECT ResponseJson FROM idempotency.CommandExecutions WHERE JSON_VALUE(ResponseJson,'$.Evidence.PartyId')=CONVERT(nvarchar(36),@Party)", party);
        foreach (var mutation in new[] {
            "JSON_MODIFY(ResponseJson,'$.Evidence.PayloadHash',N'wrong')",
            "JSON_MODIFY(ResponseJson,'$.Evidence.PartyVersion',1)",
            "JSON_MODIFY(ResponseJson,'$.Evidence.CompanyId',N'00000000-0000-0000-0000-000000000001')",
            "JSON_MODIFY(ResponseJson,'$.Evidence',NULL)" })
        {
            // Insert an independent malformed-fixture row cloned from real native evidence.
            // The original immutable receipt and the SQL append-only trigger are never changed or disabled.
            var badKey = Guid.NewGuid().ToString("N");
            await SeedAtomicReceiptVariantAsync(test, badKey, mutation, 2);
            var before = await AtomicCreateFootprintAsync(test);
            NewKey(maker, badKey);
            using var refused = await maker.PostAsJsonAsync("/api/v1/counterparties", body);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.DoesNotContain(Vkn, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(before, await AtomicCreateFootprintAsync(test));
        }
        await SeedAtomicReceiptVariantAsync(test, "atomic-unknown-schema", "ResponseJson", 99);
        var unknownBefore = await AtomicCreateFootprintAsync(test);
        NewKey(maker, "atomic-unknown-schema");
        using var unknown = await maker.PostAsJsonAsync("/api/v1/counterparties", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unknown.StatusCode);
        Assert.Equal(unknownBefore, await AtomicCreateFootprintAsync(test));
        Assert.Equal(original, await test.ScalarAsync<string>("SELECT ResponseJson FROM idempotency.CommandExecutions WHERE CompanyId=@Company AND CommandName=N'counterparty.party.create.v1' AND OriginalCorrelationId NOT LIKE N'SENTETIK-malformed-%'", party));
    }

    private static async Task SeedAtomicReceiptVariantAsync(TaxTest test, string key, string expression, int schema)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(test.Factory.DatabaseConnectionString);
        await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO idempotency.CommandExecutions
                (Id,CompanyId,CommandName,IdempotencyKeyHash,RequestHash,ActorId,Outcome,HttpStatusCode,ResponseJson,OriginalCorrelationId,CompletedAtUtc,SchemaVersion)
            SELECT NEWID(),CompanyId,CommandName,@newKey,RequestHash,ActorId,Outcome,HttpStatusCode,{expression},@correlation,CompletedAtUtc,@schema
            FROM idempotency.CommandExecutions WHERE CompanyId=@company AND CommandName=N'counterparty.party.create.v1' AND IdempotencyKeyHash=@originalKey;
            """;
        command.Parameters.AddWithValue("@newKey", IdempotencyHash.ComputeKey(key));
        command.Parameters.AddWithValue("@originalKey", IdempotencyHash.ComputeKey("atomic-proof"));
        command.Parameters.AddWithValue("@company", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@correlation", "SENTETIK-malformed-" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@schema", schema);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static CreateCounterparty AtomicCreateCommand(string name, string? number) =>
        new(PartyKind.LegalEntity, name, null, PartyRole.Supplier, number);
    private static Task<string> AtomicCreateFootprintAsync(TaxTest test) => test.ScalarAsync<string>("""
        SELECT CONCAT((SELECT COUNT(*) FROM counterparty.Parties),N'/',
          (SELECT COUNT(*) FROM counterparty.CompanyPartyAccounts),N'/',
          (SELECT COUNT(*) FROM counterparty.PartyIdentityRevisions),N'/',
          (SELECT COUNT(*) FROM idempotency.CommandExecutions),N'/',
          (SELECT COUNT(*) FROM audit.AuditEvents));
        """, Guid.Empty);
    private static AuditEventDraft AtomicCreateCallerDraft(AtomicCreateHarness harness) => new(harness.Company.CompanyId,
        "synthetic.atomic.caller.v1", "Counterparty.Party", Guid.NewGuid().ToString("D"), "Caller", null, 1,
        AuditActorTypes.User, harness.User.UserId!, ErpPermissions.CounterpartyPartiesRead, [], "{}", "{}", "SENTETIK caller", null,
        AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-caller", null, "caller-key");

    private sealed class AtomicCreateHarness : IAsyncDisposable
    {
        public readonly PreCreateProbeUser User = new(); public readonly PreCreateProbeCompany Company = new();
        public readonly PreCreateProbeAuthority Authority = new();
        public readonly WebApplicationFactory<Program> Factory;
        public AtomicCreateHarness(TaxTest data, AtomicCreateProbe probe)
        {
            Factory = data.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentUser>(); services.AddSingleton<ICurrentUser>(User);
                services.RemoveAll<ICompanyContext>(); services.AddSingleton<ICompanyContext>(Company);
                services.RemoveAll<IErpAuthorizationService>(); services.AddSingleton<IErpAuthorizationService>(Authority);
                services.RemoveAll<IErpAuthorityRevalidationService>(); services.AddSingleton<IErpAuthorityRevalidationService>(Authority);
                services.AddDbContext<CounterpartyDbContext>((_, options) => options.AddInterceptors(probe));
                services.AddDbContext<AuditDbContext>((_, options) => options.AddInterceptors(probe));
            }));
            _ = Factory.Services; // Materialize the real disposable schema before direct-owner SQL assertions.
        }
        public AtomicCreateScope Scope() => new(this);
        public void Restore() { User.Reset(); Company.Set(TestCompanyIds.CompanyA); Authority.Reset(); }
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
    private sealed class AtomicCreateScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope; private readonly AtomicCreateHarness harness;
        public CounterpartyDbContext Database { get; }
        public IAuditTransactionWriterV1 Writer { get; }
        public AuditEventCollector Collector { get; }
        public AtomicCreateScope(AtomicCreateHarness harness)
        {
            this.harness = harness; scope = harness.Factory.Services.CreateAsyncScope();
            Database = scope.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
            Writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            Collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        }
        public Task<CounterpartyCommandExecution> CreateAsync(string key, string correlation) =>
            scope.ServiceProvider.GetRequiredService<ICounterpartyCommandService>().CreateAsync(
                AtomicCreateCommand("SENTETIK atomic owner", Vkn),
                new(harness.Company.CompanyId, harness.User.UserId!, correlation, null, "Web", null, null, key), default);
        public void AssertClean()
        { Assert.Empty(Database.ChangeTracker.Entries()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Database.Database.CurrentTransaction); }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
    private sealed class AtomicCreateProbe : SaveChangesInterceptor
    {
        public Action? AfterSaved; public bool FailOwner, FailAudit; public int OwnerSaves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is CounterpartyDbContext database && database.ChangeTracker.Entries<CommandExecution>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.CommandName == "counterparty.party.create.v1"))
            {
                OwnerSaves++;
                if (FailOwner) { FailOwner = false; throw new DbUpdateConcurrencyException("SENTETIK atomic create save race"); }
            }
            if (data.Context is AuditDbContext && FailAudit)
            { FailAudit = false; throw new InvalidOperationException("SENTETIK actual scoped Audit append failure"); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (data.Context is CounterpartyDbContext) AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
}
