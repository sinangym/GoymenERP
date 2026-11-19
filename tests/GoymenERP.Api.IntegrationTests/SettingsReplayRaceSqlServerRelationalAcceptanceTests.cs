using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL unique-key races through the actual administrations and Audit owner. Authority/context mutations are explicit
/// synthetic port faults at the final winner read, not HTTP/Identity authorization acceptance evidence.
/// </summary>
public sealed partial class SettingsReplayRaceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Actor = "20000000-0000-0000-0000-0000000000aa";

    [SqlServerFact]
    public Task Collateral_change_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.CollateralChange);

    [SqlServerFact]
    public Task Collateral_refusal_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.CollateralRefusal);

    [SqlServerFact]
    public Task Contract_limit_change_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.ContractChange);

    [SqlServerFact]
    public Task Company_policy_refusal_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.PolicyRefusal);

    [SqlServerFact]
    public Task Legacy_company_policy_change_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.LegacyPolicyChange);

    [SqlServerFact]
    public Task Reference_list_change_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.ReferenceChange);

    [SqlServerFact]
    public Task Reference_list_refusal_race_revalidates_the_winner_after_the_final_sql_read() => CheckAsync(Branch.ReferenceRefusal);

    private static async Task CheckAsync(Branch branch)
    {
        await using var run = await Run.StartAsync();
        if (branch == Branch.CollateralRefusal)
        {
            await using var initial = run.Request();
            await initial.Collateral.CreateAsync(new("TAKEN", "SENTETIK collateral", SettingsCollateralKindV1.Dbs, "SENTETIK initial"),
                Metadata("SENTETIK-initial", "SENTETIK-initial"));
        }
        if (branch == Branch.ReferenceRefusal)
        {
            await using var initial = run.Request();
            await initial.ReferenceLists.CreateAsync(SettingsReferenceListKindV1.ExpenseType,
                new("TAKEN", "SENTETIK expense", "SENTETIK initial"),
                Metadata("SENTETIK-initial", "SENTETIK-initial"));
        }

        var index = 0;
        foreach (var fault in Enum.GetValues<Fault>())
        {
            var key = $"SENTETIK-{branch}-{fault}";
            var winnerCorrelation = $"SENTETIK-winner-{branch}-{fault}";
            var loserCorrelation = $"SENTETIK-loser-{branch}-{fault}";
            var control = new Control();
            Answer? winner = null;
            var race = new Race(control, fault, async () =>
            {
                await using var winningRequest = run.Request();
                winner = await InvokeAsync(winningRequest, branch, index, Metadata(key, winnerCorrelation));
                Assert.False(winner.IsReplay);
            });
            await using (var loser = run.Request(control, race))
            {
                var command = InvokeAsync(loser, branch, index, Metadata(key, loserCorrelation));
                if (fault == Fault.None)
                {
                    var answer = await command;
                    Assert.True(answer.IsReplay);
                    Assert.Equal(winnerCorrelation, answer.CorrelationId);
                    Assert.Equal((winner!.Status, winner.Code), (answer.Status, answer.Code));
                }
                else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => command);
            }

            Assert.Equal(1, race.SaveAttempts);
            Assert.True(race.UniqueFailure);
            Assert.Equal(1, race.WinnerReads);
            Assert.NotNull(winner);
            Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation",
                winnerCorrelation));
            Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation",
                loserCorrelation));
            Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", loserCorrelation));
            var changes = branch is Branch.CollateralChange or Branch.ContractChange or Branch.LegacyPolicyChange or Branch.ReferenceChange;
            Assert.Equal(changes ? 1 : 0,
                await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", winnerCorrelation));
            var (businessSql, businessCount) = branch switch
            {
                Branch.CollateralChange => ("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions] WHERE [CompanyId]=@company", index + 1),
                Branch.CollateralRefusal => ("SELECT COUNT(*) FROM [settings].[CollateralTypeVersions] WHERE [CompanyId]=@company", 1),
                Branch.ContractChange => ("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [CompanyId]=@company AND [PolicyKey]='sales.contract-discount-limit'", index + 1),
                Branch.PolicyRefusal => ("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [CompanyId]=@company AND [PolicyKey]='sales.contract-discount-limit'", 0),
                Branch.ReferenceChange => ("SELECT COUNT(*) FROM [settings].[ReferenceListEntryVersions] WHERE [CompanyId]=@company", index + 1),
                Branch.ReferenceRefusal => ("SELECT COUNT(*) FROM [settings].[ReferenceListEntryVersions] WHERE [CompanyId]=@company", 1),
                _ => ("SELECT COUNT(*) FROM [settings].[CompanyPolicyVersions] WHERE [CompanyId]=@company AND [PolicyKey]='finance.opening-period'", index + 1),
            };
            Assert.Equal(businessCount, await run.CountAsync(businessSql, winnerCorrelation));
            // Restoring authority allows the same key/body to replay the winner's original correlation, with no new write.
            await using var restored = run.Request();
            var replay = await InvokeAsync(restored, branch, index, Metadata(key, $"SENTETIK-restored-{fault}"));
            Assert.Equal(winner! with { IsReplay = true }, replay);
            index++;
        }
    }

    private static async Task<Answer> InvokeAsync(Request request, Branch branch, int index, SettingsCommandMetadataV1 metadata)
    {
        if (branch is Branch.ReferenceChange or Branch.ReferenceRefusal)
        {
            var result = await request.ReferenceLists.CreateAsync(SettingsReferenceListKindV1.ExpenseType,
                new(branch == Branch.ReferenceRefusal ? "TAKEN" : $"RACE-{index}", "SENTETIK expense", "SENTETIK race"), metadata);
            return new(result.Answer.StatusCode, result.Answer.Code, result.IsReplay, result.OriginalCorrelationId);
        }
        if (branch is Branch.CollateralChange or Branch.CollateralRefusal)
        {
            var result = await request.Collateral.CreateAsync(new(branch == Branch.CollateralRefusal ? "TAKEN" : $"RACE-{index}",
                "SENTETIK collateral", SettingsCollateralKindV1.Dbs, "SENTETIK race"), metadata);
            return new(result.Answer.StatusCode, result.Answer.Code, result.IsReplay, result.OriginalCorrelationId);
        }
        var policy = branch == Branch.LegacyPolicyChange
            ? await request.Policy.RecordFinanceOpeningPeriodAsync(new(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                index % 2 == 0, "SENTETIK race"), metadata)
            : await request.Policy.RecordSalesContractDiscountLimitAsync(new(branch == Branch.PolicyRefusal ? "999" : index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "SENTETIK race"), metadata);
        return new(policy.Answer.StatusCode, policy.Answer.Code, policy.IsReplay, policy.OriginalCorrelationId);
    }

    private static SettingsCommandMetadataV1 Metadata(string key, string correlation) =>
        new(CompanyA, Actor, correlation, null, "Web", null, null, key);

    private enum Branch { CollateralChange, CollateralRefusal, ContractChange, PolicyRefusal, LegacyPolicyChange, ReferenceChange, ReferenceRefusal }
    private enum Fault { None, PermissionAfterLookup, CompanyAfterLookup, ActorAfterLookup, CompanyDuringRefresh, ActorDuringRefresh }
    private sealed record Answer(int Status, string? Code, bool IsReplay, string CorrelationId);

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; set; } = CompanyA;
        public void Set(Guid id) => CompanyId = id;
    }

    private sealed class MutableActor : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }

    private sealed class Control : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public MutableCompany Company { get; } = new();
        public MutableActor User { get; } = new();
        public bool Granted { get; set; } = true;
        public Action? BeforeRefresh { get; set; }
        private PermissionScope Scope => Granted
            ? new(true, true, new HashSet<string> { "SENTETIK-settings-race" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
            : PermissionScope.Denied;
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope);
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            BeforeRefresh?.Invoke(); BeforeRefresh = null;
            return Task.FromResult(Scope);
        }
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Granted);
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) => Task.FromResult(new AuthorizationSnapshot(Actor,
                new HashSet<Guid> { CompanyA, CompanyB }, permissions.ToDictionary(value => value, _ => Scope)));
    }

    private sealed class Race(Control control, Fault fault, Func<Task> winner) : SaveChangesInterceptor
    {
        public int SaveAttempts { get; private set; }
        public bool UniqueFailure { get; private set; }
        public int WinnerReads { get; private set; }
        public DbCommandInterceptor Reader { get; } = new WinnerRead();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            Assert.Equal(1, SaveAttempts);
            await winner(); // Another real connection commits AFTER the loser's reads and BEFORE its SQL insert.
            ((WinnerRead)Reader).AfterFailure = () =>
            {
                Assert.True(UniqueFailure);
                WinnerReads++;
                switch (fault)
                {
                    case Fault.PermissionAfterLookup: control.Granted = false; break;
                    case Fault.CompanyAfterLookup: control.Company.CompanyId = CompanyB; break;
                    case Fault.ActorAfterLookup: control.User.UserId = "SENTETIK-different-actor"; break;
                    case Fault.CompanyDuringRefresh: control.BeforeRefresh = () => control.Company.CompanyId = CompanyB; break;
                    case Fault.ActorDuringRefresh: control.BeforeRefresh = () => control.User.UserId = "SENTETIK-different-actor"; break;
                }
            };
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            UniqueFailure = eventData.Exception.GetBaseException() is SqlException { Number: 2601 or 2627 };
            return Task.CompletedTask;
        }

        private sealed class WinnerRead : DbCommandInterceptor
        {
            public Action? AfterFailure { get; set; }
            public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                DbDataReader result, CancellationToken cancellationToken = default)
            {
                if (AfterFailure is { } fault && command.CommandText.Contains("FROM [idempotency].[CommandExecutions]", StringComparison.Ordinal))
                {
                    AfterFailure = null;
                    fault();
                }
                return ValueTask.FromResult(result);
            }
        }
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly SettingsDbContext database;
        private readonly IAuditTransactionWriterV1 owner;
        public SettingsCollateralTypeAdministration Collateral { get; }
        public SettingsCompanyPolicyAdministration Policy { get; }
        public SettingsReferenceListAdministration ReferenceLists { get; }
        public Request(Run run, Control control, Race? race, SaveChangesInterceptor? saveFault = null)
        {
            var audit = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<SettingsDbContext>().UseSqlServer(run.Connection)
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, control.Company));
            if (race is not null) options.AddInterceptors(race, race.Reader);
            if (saveFault is not null) options.AddInterceptors(saveFault);
            database = new SettingsDbContext(options.Options, control.Company);
            owner = (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(run.Services, run.AuditWriterType, audit, control.Company);
            Collateral = new(database, control.Company, control.User, control, control, audit, owner, TimeProvider.System,
                NullLogger<SettingsCollateralTypeAdministration>.Instance);
            Policy = new(database, control.Company, control.User, control, control, audit, TimeProvider.System,
                NullLogger<SettingsCompanyPolicyAdministration>.Instance, owner);
            ReferenceLists = new(database, control.Company, control.User, control, control, audit, TimeProvider.System,
                NullLogger<SettingsReferenceListAdministration>.Instance);
        }
        public async ValueTask DisposeAsync() { owner.DiscardPending(); await database.DisposeAsync(); }
    }

    private sealed class Run(string baseConnectionString, SqlServerRelationalApiFactory factory, Type auditWriterType) : IAsyncDisposable
    {
        public string Connection => factory.DatabaseConnectionString;
        public IServiceProvider Services => factory.Services;
        public Type AuditWriterType { get; } = auditWriterType;
        public Request Request(Control? control = null, Race? race = null, SaveChangesInterceptor? saveFault = null) =>
            new(this, control ?? new Control(), race, saveFault);
        public static async Task<Run> StartAsync()
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(connection);
            using var client = factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
            using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
            await using var scope = factory.Services.CreateAsyncScope();
            return new(connection, factory, scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>().GetType());
        }
        public async Task<int> CountAsync(string sql, string correlation)
        {
            await using var connection = new SqlConnection(Connection); await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@correlation", correlation);
            command.Parameters.AddWithValue("@company", CompanyA);
            return (int)(await command.ExecuteScalarAsync())!;
        }
        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync(); SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
