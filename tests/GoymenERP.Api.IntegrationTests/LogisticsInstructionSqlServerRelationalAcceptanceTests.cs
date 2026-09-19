using System.Net;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOG-4a on real SQL Server (RCSI, the disposable database of the run): the instruction with its lines
/// is stored, issued and loaded in ONE save each (header, lines, share, attempt, audit); the §13 cap, a
/// blocked line and a stale version write nothing and are recorded / replayed as terminal refusals (§7);
/// a same-key barrier race stores one instruction, one attempt and one audit and the loser answers the
/// winner (§8); company B sees nothing and its same key is its own attempt; the routes answer only with
/// fresh grants and only for the context company. Nothing physical is written (D3). SENTETIK owners:
/// the customer and the order line are identities here — the owners' answers are the service's (unit
/// tests) and the browser suite's (LOG-4b) evidence.
/// </summary>
public sealed class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = Guid.Parse("8a1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");

    [SqlServerFact]
    public async Task An_instruction_with_lines_is_stored_issued_loaded_within_the_cap_replayed_and_rebuilt_in_one_save_each()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        StoredLoadingInstruction stored;
        Guid lineId;
        await using (var a = run.Store(CompanyA))
        {
            stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners, lines: 2))).Instruction;
            lineId = stored.Instruction.Lines[0].Id;
            var replay = await a.Store.StoreInstructionAsync(run.Envelope("k-create", correlation: "SENTETIK-second"), Hash("create-1"), Day,
                () => throw new InvalidOperationException("a replay never builds"));
            Assert.Equal((LogisticsWriteOutcome.Replayed, stored.Instruction.Id, run.Correlation), (replay.Outcome, replay.Instruction.Instruction.Id, replay.OriginalCorrelationId));
            var other = await Assert.ThrowsAsync<LogisticsStoreException>(() => a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-9"), Day,
                () => throw new InvalidOperationException("never built")));
            Assert.Equal(LogisticsStoreCodes.SameKeyPayloadMismatch, other.Code);
        }

        Assert.Equal((1, 2, 0, 1, 1, 3L, 1), await run.CountsAsync(stored.Instruction.Id));

        await using (var a = run.Store(CompanyA))
        {
            var issued = await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-3"), LogisticsCommands.IssueV1, stored.Instruction.Id,
                (instruction, now) => instruction.Issue(3, now), Issued);
            Assert.Equal((LoadingInstructionStatus.Issued, 4L), (issued!.Instruction.Instruction.Status, issued.Instruction.Instruction.Version));
            var loaded = await a.Store.MutateInstructionAsync(run.Envelope("k-load-1"), Hash("load-4"), LogisticsCommands.RecordLoadV1, stored.Instruction.Id,
                (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), lineId, Kg(4m), MeasurementKind.Measured, now), Loaded);
            Assert.Equal((5L, 4m, 2m), (loaded!.Instruction.Instruction.Version, loaded.Instruction.Instruction.Lines[0].LoadedQuantity.Value,
                loaded.Instruction.Instruction.Lines[0].RemainingToLoad.Value));
            // §13: the cap refuses the whole share; nothing below the domain ran, nothing was written.
            var over = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-load-2"), Hash("load-5"),
                LogisticsCommands.RecordLoadV1, stored.Instruction.Id, (instruction, now) => instruction.RecordLoad(5, Guid.NewGuid(), lineId, Kg(2.001m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal(LogisticsRuleCodes.LoadExceedsInstructed, over.Code);
        }

        Assert.Equal((1, 2, 1, 3, 3, 5L, 3), await run.CountsAsync(stored.Instruction.Id));
        Assert.Equal(4m, await run.ScalarAsync<decimal>("SELECT Loaded FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", lineId)));

        await using (var a = run.Store(CompanyA))
        {
            // Rebuilt through the domain in recording order: lines, issue, share; the same version, the same totals.
            var rebuilt = await a.Store.FindInstructionAsync(stored.Instruction.Id);
            Assert.NotNull(rebuilt);
            Assert.Equal((LoadingInstructionStatus.Issued, 5L, 2, 1, 4m, Day), (rebuilt.Instruction.Status, rebuilt.Instruction.Version, rebuilt.Instruction.Lines.Count,
                rebuilt.Instruction.Shares.Count, rebuilt.Instruction.Lines[0].LoadedQuantity.Value, rebuilt.EffectiveOn));
            Assert.Equal((owners.Order, 0), (rebuilt.Instruction.Lines[0].Source.OrderId, rebuilt.Instruction.Lines[0].Source.Revision));
            var page = await a.Store.ListInstructionsAsync(1, null, LoadingInstructionStatus.Issued);
            Assert.Equal(stored.Instruction.Id, Assert.Single(page.Items).Instruction.Id);
            Assert.Null(page.NextCursor);
            var replayed = await a.Store.MutateInstructionAsync(run.Envelope("k-load-1", correlation: "SENTETIK-retry"), Hash("load-4"), LogisticsCommands.RecordLoadV1,
                stored.Instruction.Id, (_, _) => throw new InvalidOperationException("a replay never mutates"), Loaded);
            Assert.Equal((LogisticsWriteOutcome.Replayed, run.Correlation, 5L), (replayed!.Outcome, replayed.OriginalCorrelationId, replayed.Instruction.Instruction.Version));
        }
    }

    [SqlServerFact]
    public async Task A_blocked_line_refuses_a_load_as_a_recorded_terminal_refusal_and_a_stale_version_replays()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        Guid lineId;
        await using (var a = run.Store(CompanyA))
        {
            var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction;
            id = stored.Id;
            lineId = stored.Lines[0].Id;
            await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-2"), LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(2, now), Issued);
        }

        // D2: the block's owner (Inventory/Quality) is not on main; the flag is set as its owner would set it, and the rebuild carries it.
        await run.ExecuteAsync("UPDATE logistics.LoadingInstructionLines SET IsBlocked = 1 WHERE LineId = @id", ("@id", lineId));
        await run.ExecuteAsync("UPDATE logistics.LoadingInstructions SET Version = Version + 1 WHERE Id = @id", ("@id", id));

        await using (var a = run.Store(CompanyA))
        {
            var envelope = run.Envelope("k-load-blocked");
            var blocked = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(envelope, Hash("load-4"), LogisticsCommands.RecordLoadV1, id,
                (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), lineId, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal(LogisticsRuleCodes.LineBlocked, blocked.Code);
            // §7: the service records the refusal under the key; the same key answers it again without touching the domain.
            await a.Store.RecordRefusalAsync(envelope, Hash("load-4"), LogisticsCommands.RecordLoadV1,
                new LogisticsTerminalRefusal(409, LogisticsProblemCodesV1.LineBlocked, [new("lineId", LogisticsRuleCodes.LineBlocked)]));
            var replay = await a.Store.FindReplayAsync(run.Envelope("k-load-blocked", correlation: "SENTETIK-retry"), Hash("load-4"), LogisticsCommands.RecordLoadV1);
            Assert.Equal((409, LogisticsProblemCodesV1.LineBlocked, run.Correlation), (replay!.Refusal!.Status, replay.Refusal.Code, replay.OriginalCorrelationId));
            Assert.Null(replay.Instruction);
            var stale = await Assert.ThrowsAsync<LogisticsVersionConflictException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-load-stale"), Hash("load-3"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(3, Guid.NewGuid(), lineId, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal((3L, 4L), (stale.ExpectedVersion, stale.CurrentVersion));
        }

        Assert.Equal((1, 1, 0, 3, 2, 4L, 2), await run.CountsAsync(id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE Outcome = 2 AND ISJSON(RefusalJson) = 1"));
    }

    [SqlServerFact]
    public async Task A_same_key_race_stores_one_instruction_one_attempt_and_one_audit_and_company_B_sees_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var barrier = new Barrier(2);
        async Task<object> WriteAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await writer.Store.StoreInstructionAsync(run.Envelope("k-race"), Hash("race"), Day, () => Instruction(owners))).Outcome;
            }
            catch (LogisticsStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(WriteAsync), Task.Run(WriteAsync));
        Assert.Single(results, result => result is LogisticsWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'logistics.loading-instruction.created.v1'"));

        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            var retry = await a.Store.FindReplayAsync(run.Envelope("k-race", correlation: "SENTETIK-loser"), Hash("race"), LogisticsCommands.CreateV1);
            Assert.Equal((LogisticsWriteOutcome.Recorded.ToString(), run.Correlation), ("Recorded", retry!.OriginalCorrelationId));
            id = retry.Instruction!.Instruction.Id;
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindInstructionAsync(id));
            Assert.Empty((await b.Store.ListInstructionsAsync(10, null)).Items);
            Assert.Null(await b.Store.FindReplayAsync(run.Envelope("k-race"), Hash("race"), LogisticsCommands.CreateV1));
            Assert.Null(await b.Store.MutateInstructionAsync(run.Envelope("k-b-issue"), Hash("issue-b"), LogisticsCommands.IssueV1, id,
                (instruction, now) => instruction.Issue(2, now), Issued));
        }

        Assert.Equal((1, 1, 0, 1, 1, 2L, 1), await run.CountsAsync(id));
    }

    [SqlServerFact]
    public async Task The_routes_answer_only_with_fresh_grants_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid id;
        await using (var a = run.Store(CompanyA))
        {
            id = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners))).Instruction.Instruction.Id;
        }

        // No grant: the fence refuses before the store (code-less 403); the instruction stays as it is.
        using (var ungranted = run.Factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
        {
            ungranted.DefaultRequestHeaders.Remove("Idempotency-Key");
            ungranted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.SendAsync(Command($"{id:D}/issue", "SENTETIK-ungranted", "{\"expectedVersion\":\"2\"}"))).StatusCode);
        }

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCreate);
        using (var granted = run.Factory.CreateClientFor(reader, CompanyA))
        {
            granted.DefaultRequestHeaders.Remove("Idempotency-Key");
            granted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            using var detail = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Contains("\"status\":\"Draft\"", await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var list = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}?status=Draft");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Contains(id.ToString("D"), await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // The create reaches the REAL Cari owner first: a synthetic customer is its NotFound → 409 account_state, nothing stored.
            using var create = await granted.SendAsync(Command(string.Empty, "SENTETIK-create", Create(owners)));
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
            Assert.Contains("\"code\":\"account_state\"", await create.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // The issue is a real command: 200 after the transition; the same key again is 200 + the replay headers.
            using var issued = await granted.SendAsync(Command($"{id:D}/issue", "SENTETIK-issue", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            Assert.Contains("\"status\":\"Issued\"", await issued.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var replay = await granted.SendAsync(Command($"{id:D}/issue", "SENTETIK-issue", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            using var stale = await granted.SendAsync(Command($"{id:D}/issue", "SENTETIK-issue-again", "{\"expectedVersion\":\"2\"}"));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCreate);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            clientOfB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command($"{id:D}/issue", "SENTETIK-b", "{\"expectedVersion\":\"3\"}"))).StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions"));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A Draft instruction of the company towards SYNTHETIC owner identities, its lines 6 kg each (the §13 example's shape).</summary>
    private static LoadingInstruction Instruction(Owners owners, int lines = 1)
    {
        var instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
        for (var i = 0; i < lines; i++)
        {
            instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6m), T0);
        }

        return instruction;
    }

    private static string Create(Owners owners) =>
        $$"""{"customerPartyId":"{{owners.Party:D}}","companyAccountId":"{{owners.Account:D}}","effectiveOn":"2026-09-19","lines":[{"lineId":"{{Guid.NewGuid():D}}","sourceOrderId":"{{owners.Order:D}}","sourceOrderLineId":"{{Guid.NewGuid():D}}","unitId":"{{KgUnit:D}}","quantity":"6"}]}""";

    private static HttpRequestMessage Command(string segment, string idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, segment.Length == 0 ? LogisticsEndpoints.InstructionsPath : $"{LogisticsEndpoints.InstructionsPath}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    private sealed record Owners(Guid Company, Guid Party, Guid Account, Guid Order)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StoreScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"log4-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // The host applies the module's migration after Organization's; applied again here idempotently.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new LogisticsDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public LogisticsWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], correlation ?? Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new LogisticsDbContext(builder.Options, companyContext);
            return new StoreScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Logistics user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"LOG-4A-{roleId:N}", "Synthetic Logistics test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        /// <summary>(instructions, lines, shares, attempts, audit events of the instruction, version, recorded attempts) as the real SQL holds them.</summary>
        public async Task<(int Instructions, int Lines, int Shares, int Attempts, int AuditEvents, long Version, int Recorded)> CountsAsync(Guid id) =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", id.ToString("D"))),
                await ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE Outcome = 1 AND InstructionId = @id", ("@id", id)));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<LogisticsDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options;
    }
}
