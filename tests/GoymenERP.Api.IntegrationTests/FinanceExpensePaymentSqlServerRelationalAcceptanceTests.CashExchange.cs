using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ExchangeUsd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
    private static readonly Guid ExchangeTry = SettingsCurrencyIdentitiesV1.TurkishLira;

    [SqlServerFact]
    public async Task Exchange_snapshot_Down_allows_legacy_null_evidence_and_refuses_first_while_a_recorded_snapshot_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCashTransferExchangeSnapshots().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var source = await run.ApprovedExchangeBankAsync(ExchangeUsd);
        var legacyTarget = await run.ApprovedExchangeBankAsync(ExchangeUsd);
        var target = await run.ApprovedExchangeBankAsync(ExchangeTry);
        await run.FundExchangeBankAsync(source, "150");
        // Explicit synthetic legacy fixture. Its null evidence is never enriched with today's rate.
        await using (var legacy = run.OpenExchange(CompanyA, new SyntheticExchangeComparison()))
        {
            legacy.Db.CashTransfers.Add(new CashTransferRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1, Kind = CashTransferKinds.SameCurrency,
                SourceKind = CashTransferAccountKinds.Bank, SourceAccountId = source, SourceCurrencyId = ExchangeUsd,
                SourceAmountText = "1", SourceProfileId = Profile.ToString("D"), SourceProfileVersion = "1",
                TargetKind = CashTransferAccountKinds.Bank, TargetAccountId = legacyTarget, TargetCurrencyId = ExchangeUsd,
                TargetAmountText = "1", TargetProfileId = Profile.ToString("D"), TargetProfileVersion = "1",
                OccurredOn = OccurredOn, TransferConfirmed = true, RecordedBy = Recorder, RecordedAtUtc = DateTimeOffset.UtcNow,
            });
            await legacy.Db.SaveChangesAsync();
        }
        await run.ExecuteAsync(guard);
        await using (var current = run.OpenExchange(CompanyA, new SyntheticExchangeComparison()))
        {
            var result = await current.Service.CommitAsync(current.Context, Key("exchange-down"), ExchangeRequest(source, target), default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        }
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51933, error.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers WHERE ExchangeSnapshotJson IS NULL"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers WHERE ExchangeSnapshotJson IS NOT NULL"));
    }

    /// <summary>Real Finance ledger, SQL persistence and Audit. Permission, period, profile and TCMB ports
    /// remain explicit SENTETIK fixtures; this does not claim live Settings/TCMB or financial P&amp;L acceptance.</summary>
    [SqlServerFact]
    public async Task Exchange_quote_keeps_actual_legs_and_source_versions_once_and_replay_does_not_requote_history()
    {
        await using var run = await Run.StartAsync();
        var source = await run.ApprovedExchangeBankAsync(ExchangeUsd);
        var target = await run.ApprovedExchangeBankAsync(ExchangeTry);
        await run.FundExchangeBankAsync(source, "150");
        var request = ExchangeRequest(source, target);
        CashTransferView original;
        var comparison = new SyntheticExchangeComparison();
        await using (var scope = run.OpenExchange(CompanyA, comparison))
        {
            var preview = await scope.Service.PreviewAsync(scope.Context, request.Preparation, default);
            Assert.Equal(CommandOutcome.Succeeded, preview.Outcome);
            Assert.Equal(("33.00", "34.00", "-1.00"),
                (preview.Exchange!.TransactionRate, preview.Exchange.ComparisonRate, preview.Exchange.RateDeviation));
            Assert.Null(preview.Preparation!.Rate);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
            var committed = await scope.Service.CommitAsync(scope.Context, Key("exchange-record"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, committed.Outcome);
            original = Assert.IsType<CashTransferView>(committed.Resource);
        }
        Assert.Equal(("100", "3300", "33.00"),
            (original.Preparation.SourceMoney.Amount, original.Preparation.TargetMoney.Amount, original.Preparation.Rate));
        Assert.Equal(comparison.RecordId, original.Exchange!.SourceComparison.RecordId);
        Assert.Equal("7", original.Exchange.SourceComparison.RecordVersion);
        var sourceReference = Assert.IsType<CashTransferTlReferenceV1>(original.Exchange.SourceReferenceTl);
        var targetReference = Assert.IsType<CashTransferTlReferenceV1>(original.Exchange.TargetReferenceTl);
        Assert.Equal("3400", sourceReference.Amount); Assert.Equal("3300", targetReference.Amount);
        Assert.Equal(ExchangeTry, sourceReference.CurrencyId);
        Assert.Equal(CashTransferExchangeCalculatorV1.TlReferenceSourceVersion, targetReference.SourceVersion);
        await using (var retry = run.OpenExchange(CompanyA, new SyntheticExchangeComparison { Unavailable = true }))
        {
            var replay = await retry.Service.CommitAsync(retry.Context, Key("exchange-record"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
            Assert.True(replay.IsReplay);
            Assert.Equal(original, replay.Resource);
            var changed = await retry.Service.CommitAsync(retry.Context, Key("exchange-record"),
                request with { Preparation = request.Preparation with { TargetMoney = new("3301", ExchangeTry) } }, default);
            Assert.Equal(CommandOutcome.IdempotencyConflict, changed.Outcome);
        }
        await using (var otherCompany = run.OpenExchange(CompanyB, comparison))
        {
            var hidden = await otherCompany.Service.DetailAuthorizedAsync(otherCompany.Context, original.Id, default);
            Assert.Equal(ReadOutcome.NotFound, hidden.Outcome);
            Assert.Null(hidden.Resource);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransferCommands"));
        var persisted = await run.ScalarAsync<string>("SELECT ExchangeSnapshotJson FROM finance.CashTransfers WHERE Id=@id", ("@id", original.Id));
        Assert.Equal(original.Exchange, JsonSerializer.Deserialize<CashTransferExchangeSnapshotV1>(persisted,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.cash-transfer.recorded.v1' AND EntityId=@id",
            ("@id", original.Id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        // A benchmark is not an accounting exchange difference: no unapproved posting event is emitted.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", original.Id)));
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ExchangeBalanceAsync(source, ExchangeUsd, "50")).Outcome);
        Assert.Equal(TreasuryBalanceOutcome.Insufficient, (await run.ExchangeBalanceAsync(source, ExchangeUsd, "50.01")).Outcome);
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ExchangeBalanceAsync(target, ExchangeTry, "3300")).Outcome);
    }

    [SqlServerFact]
    public async Task Exchange_with_missing_comparison_or_insufficient_balance_writes_no_record_audit_or_key()
    {
        await using var run = await Run.StartAsync();
        var source = await run.ApprovedExchangeBankAsync(ExchangeUsd);
        var target = await run.ApprovedExchangeBankAsync(ExchangeTry);
        await run.FundExchangeBankAsync(source, "50");
        var request = ExchangeRequest(source, target);
        foreach (var unavailable in new[] { true, false })
        {
            await using var scope = run.OpenExchange(CompanyA, new SyntheticExchangeComparison { Unavailable = unavailable });
            var refused = await scope.Service.CommitAsync(scope.Context, Key("exchange-retryable"), request, default);
            Assert.Equal(unavailable ? CommandOutcome.Unavailable : CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.False(refused.IsReplay);
            Assert.Null(refused.Resource);
            Assert.Contains(refused.Issues, issue => issue.Code == (unavailable
                ? CurrencyExchangeRateAnswer.UnavailableCode : TreasuryBalanceAnswer.InsufficientCode));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransferCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.cash-transfer.recorded.v1'"));
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ExchangeBalanceAsync(source, ExchangeUsd, "50")).Outcome);
    }

    [SqlServerFact]
    public async Task Exchange_SQL_failure_after_a_real_insert_rolls_back_both_legs_snapshot_audit_and_key_before_retry()
    {
        await using var run = await Run.StartAsync();
        var source = await run.ApprovedExchangeBankAsync(ExchangeUsd);
        var target = await run.ApprovedExchangeBankAsync(ExchangeTry);
        await run.FundExchangeBankAsync(source, "150");
        var request = ExchangeRequest(source, target);
        var fault = new FailAfterExchangeInsert();
        await using (var failing = run.OpenExchange(CompanyA, new SyntheticExchangeComparison(), fault))
        {
            var failed = await failing.Service.CommitAsync(failing.Context, Key("exchange-fault"), request, default);
            Assert.True(fault.Fired);
            Assert.Equal(IsolationLevel.Serializable, fault.Isolation);
            Assert.Equal(CommandOutcome.Unavailable, failed.Outcome);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransferCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.cash-transfer.recorded.v1'"));
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ExchangeBalanceAsync(source, ExchangeUsd, "150")).Outcome);
        await using var retry = run.OpenExchange(CompanyA, new SyntheticExchangeComparison());
        var saved = await retry.Service.CommitAsync(retry.Context, Key("exchange-fault"), request, default);
        Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
        Assert.False(saved.IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
    }

    private static CommitCashTransferRequest ExchangeRequest(Guid source, Guid target) => new(new(
        CashTransferKind.CurrencyExchange, CashTransferAccountKind.Bank, source, new("100", ExchangeUsd),
        new(Profile.ToString("D"), "1"), CashTransferAccountKind.Bank, target, new("3300", ExchangeTry),
        new(Profile.ToString("D"), "1"), null, OccurredOn, null, "SENTETIK exchange", null), true);

    private sealed partial class Run
    {
        public async Task<Guid> ApprovedExchangeBankAsync(Guid currency)
        {
            var id = Guid.NewGuid();
            await ExecuteAsync("INSERT INTO finance.BankAccounts (Id,CompanyId,CurrencyId,DisplayName,MoneyMovementEligible,Iban,"
                + "Status,Version,ProposedBy,ProposedAtUtc,ApprovedBy,ApprovedAtUtc,UpdatedAtUtc) VALUES "
                + "(@id,@company,@currency,N'SENTETIK exchange bank',1,N'TR330006100519786457841326',2,2,N'SENTETIK-maker',"
                + "SYSDATETIMEOFFSET(),N'SENTETIK-checker',SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET())",
                ("@id", id), ("@company", CompanyA), ("@currency", currency));
            return id;
        }

        public async Task FundExchangeBankAsync(Guid bank, string amount)
        {
            OpeningBalanceView proposed;
            await using (var maker = OpenLedgerOpenings(Preparer))
            {
                var result = await maker.Service.ProposeAsync(maker.Context, Key("exchange-opening-p"),
                    new(OpeningBalanceTargetKind.BankAccount, bank, null, ExchangeUsd, null, amount, OccurredOn), default);
                Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                proposed = Assert.IsType<OpeningBalanceView>(result.Resource);
            }
            await using var checker = OpenLedgerOpenings(Approver);
            var approved = await checker.Service.ApproveAsync(checker.Context, Key("exchange-opening-a"), new(proposed.Id, proposed.Version), default);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        }

        public ExchangeScope OpenExchange(Guid companyId, SyntheticExchangeComparison rates, params IInterceptor[] interceptors)
        {
            var company = new CompanyContext(); company.Set(companyId);
            var user = new FixedCurrentUser(Recorder, companyId);
            var audit = new AuditEventCollector();
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql =>
                { sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema); sql.MaxBatchSize(1); })
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).AddInterceptors(interceptors).Options, company);
            var permission = new SyntheticPermissions();
            var profiles = new SyntheticSettings(companyId);
            var actualComposition = new SettingsFinanceCashExchangeRateRuleV1(profiles, rates, company, user, permission);
            var service = new CashTransferService(db, company, user, permission, new InternalLedgerTreasuryBalanceGuard(db, company),
                actualComposition, profiles, new SyntheticOpenPeriod(), audit, new(FinanceMoneyOperations.CashTransferV1),
                new(Envelope(FinancePermissionNames.CashTransferCommit)), TimeProvider.System, documentNumbers: new SyntheticFinanceRecordNumbers());
            return new(db, service, new(companyId, Recorder, Correlation, null));
        }

        public async Task<TreasuryBalanceAnswer> ExchangeBalanceAsync(Guid bank, Guid currency, string amount)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            await using var db = new FinanceDbContext(Options(Factory), company);
            return await FinanceTreasuryTransaction.ExecuteAsync(db,
                () => new InternalLedgerTreasuryBalanceGuard(db, company).CheckOutgoingAsync(new(CompanyA, Recorder, Correlation, null),
                    ExpensePaymentSourceKind.Bank, bank, new(amount, currency), default),
                () => Task.FromResult(new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Unavailable, TreasuryBalanceAnswer.UnavailableCode)),
                auditOwner: null, CancellationToken.None);
        }
    }

    private sealed record ExchangeScope(FinanceDbContext Db, CashTransferService Service, FinanceContext Context) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class SyntheticExchangeComparison : ISettingsExchangeRateResolverV1
    {
        public Guid RecordId { get; } = Guid.NewGuid();
        public bool Unavailable { get; init; }
        public Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly day, Guid source, Guid target, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ExchangeRateResolutionV1(day, source, target, "USD",
                Unavailable ? ExchangeRateResolutionStatesV1.Unavailable : ExchangeRateResolutionStatesV1.Resolved,
                ExchangeRateSourcesV1.Tcmb, false, day.AddDays(-1), day.AddDays(-1), Unavailable ? null : "34",
                "SENTETIK / NOT_ENV", Unavailable ? null : RecordId, Unavailable ? null : "7", null, null));
        }
    }

    private sealed class FailAfterExchangeInsert : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        public IsolationLevel? Isolation { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("INSERT INTO [finance].[CashTransfers]", StringComparison.Ordinal))
            {
                Fired = true; Isolation = command.Transaction?.IsolationLevel;
                await result.DisposeAsync();
                throw new DbUpdateConcurrencyException("SENTETIK fault after real exchange insert");
            }
            return result;
        }
    }
}
