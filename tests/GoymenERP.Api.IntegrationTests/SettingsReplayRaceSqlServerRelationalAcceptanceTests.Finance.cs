using System.Data.Common;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsReplayRaceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Finance_policy_unique_recovery_revalidates_after_the_final_winner_read() => FinanceRecoveryAsync(false);

    [SqlServerFact]
    public Task Finance_policy_concurrency_recovery_revalidates_after_the_final_winner_read() => FinanceRecoveryAsync(true);

    private static async Task FinanceRecoveryAsync(bool concurrency)
    {
        await using var run = await Run.StartAsync();
        var index = 0;
        foreach (var fault in Enum.GetValues<Fault>())
        {
            var key = $"SENTETIK-finance-{concurrency}-{fault}";
            var original = $"SENTETIK-winner-{key}";
            var rejected = $"SENTETIK-loser-{key}";
            var body = new PrepareSettingsBankInstitutionsV1(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null, [new(null, "SENTETIK bank", $"S{index}", true)], "SENTETIK recovery");
            var control = new Control();
            SettingsFinancePolicyCommandResultV1? winner = null;
            var race = new FinanceRecoveryRace(control, fault, concurrency, async () =>
            {
                await using var request = new FinanceRecoveryRequest(run, new Control());
                winner = await request.Service.PrepareBankInstitutionsAsync(body, Metadata(key, original));
                Assert.False(winner.IsReplay);
                Assert.Equal(204, winner.Answer.StatusCode);
            });
            await using (var loser = new FinanceRecoveryRequest(run, control, race))
            {
                var command = loser.Service.PrepareBankInstitutionsAsync(body, Metadata(key, rejected));
                if (fault == Fault.None)
                {
                    var replay = await command;
                    Assert.True(replay.IsReplay);
                    Assert.Equal(original, replay.OriginalCorrelationId);
                    Assert.Equal(winner!.Answer, replay.Answer);
                }
                else if (fault == Fault.PermissionAfterLookup)
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => command);
                else
                    Assert.Equal(SettingsFinancePolicyCodesV1.AccessDenied,
                        (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => command)).Code);
            }
            Assert.Equal(1, race.SaveAttempts);
            Assert.True(race.RecoveryFailure);
            Assert.Equal(1, race.WinnerReads);
            Assert.Equal(index + 1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[BankInstitutionPolicies] WHERE [CompanyId]=@company", original));
            Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation", original));
            Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation", rejected));
            Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", original));
            Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", rejected));
            await using var restored = new FinanceRecoveryRequest(run, new Control());
            var restoredAnswer = await restored.Service.PrepareBankInstitutionsAsync(body, Metadata(key, $"SENTETIK-restored-{key}"));
            Assert.True(restoredAnswer.IsReplay);
            Assert.Equal(original, restoredAnswer.OriginalCorrelationId);
            Assert.Equal(winner!.Answer, restoredAnswer.Answer);
            index++;
        }
    }

    private sealed class FinanceRecoveryRace(Control control, Fault fault, bool concurrency, Func<Task> winner) : SaveChangesInterceptor
    {
        public int SaveAttempts { get; private set; }
        public int WinnerReads { get; private set; }
        public bool RecoveryFailure { get; private set; }
        private readonly FinanceWinnerRead reader = new();
        public DbCommandInterceptor Reader => reader;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, ++SaveAttempts);
            // Explicit synthetic scheduling fault: release the loser's serializable range locks and audit write.
            // This proves recovery disclosure fences, not a production serializable-race schedule.
            var transaction = Assert.IsAssignableFrom<IDbContextTransaction>(eventData.Context!.Database.CurrentTransaction);
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            await winner(); // Actual administration, independent connection, real committed command/audit/business rows.
            reader.AfterFailure = () =>
            {
                Assert.True(RecoveryFailure);
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
            if (concurrency)
            {
                RecoveryFailure = true;
                throw new DbUpdateConcurrencyException("SENTETIK recovery scheduling fault");
            }
            return result; // Actual duplicate command/policy insert must produce SQL 2601/2627.
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            RecoveryFailure = concurrency || eventData.Exception.GetBaseException() is SqlException { Number: 2601 or 2627 };
            return Task.CompletedTask;
        }

        private sealed class FinanceWinnerRead : DbCommandInterceptor
        {
            public Action? AfterFailure { get; set; }
            public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                DbDataReader result, CancellationToken cancellationToken = default)
            {
                if (AfterFailure is { } mutate && command.CommandText.Contains("FROM [idempotency].[CommandExecutions]", StringComparison.Ordinal))
                { AfterFailure = null; mutate(); }
                return ValueTask.FromResult(result);
            }
        }
    }

    private sealed class FinanceRecoveryRequest : IAsyncDisposable
    {
        private readonly SettingsDbContext database;
        private readonly IAuditTransactionWriterV1 owner;
        public SettingsFinancePolicyAdministration Service { get; }
        public FinanceRecoveryRequest(Run run, Control control, FinanceRecoveryRace? race = null)
        {
            var audit = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<SettingsDbContext>().UseSqlServer(run.Connection)
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, control.Company));
            if (race is not null) options.AddInterceptors(race, race.Reader);
            database = new(options.Options, control.Company);
            owner = (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(run.Services, run.AuditWriterType, audit, control.Company);
            Service = new(database, control.Company, control.User, control, control, audit, owner, new UnusedFinanceReferences(),
                new UnusedCollateralCatalog(), TimeProvider.System, NullLogger<SettingsFinancePolicyAdministration>.Instance);
        }
        public async ValueTask DisposeAsync() { owner.DiscardPending(); await database.DisposeAsync(); }
    }

    // Bank authoring has no Finance/reference dependency. Fail if that boundary changes; never fake available data.
    private sealed class UnusedFinanceReferences : ISettingsFinancePolicyReferencesV1
    {
        public Task<SettingsFinancePolicyReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Bank authoring must not read Finance references.");
    }
    private sealed class UnusedCollateralCatalog : ISettingsCollateralTypeCatalogV1
    {
        public Task<SettingsCollateralTypesViewV1> ListAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Bank authoring must not read collateral types.");
    }
}
