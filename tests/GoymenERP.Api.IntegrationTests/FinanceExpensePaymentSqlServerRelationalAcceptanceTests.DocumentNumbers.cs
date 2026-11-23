using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Actual Finance record + actual Settings reservation + actual SQL/Audit rollback.
/// Account seed, money profile, period and permission answers remain explicit synthetic seams.</summary>
public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Finance_document_number_Down_allows_unnumbered_drafts_and_refuses_51911_while_recorded_number_proof_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddFinanceRecordedDocumentNumbers().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK Down numbered bank");
        var approved = await NumberedApprovedAsync(run, bank);
        await run.ExecuteAsync(guard);
        await using var numbers = await FinanceNumbersAsync(run);
        await using (var scope = OpenNumbered(run, numbers.Numbers))
        {
            var recorded = await scope.Payments.RecordAsync(scope.Context, Key("SENTETIK-number-down"), new(approved.Id, approved.Version, true), default);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        }
        Assert.Equal(51911, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal("DEK-2026-000001", await run.ScalarAsync<string>("SELECT DocumentNumber FROM finance.ExpensePayments WHERE Id=@id", ("@id", approved.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
    }

    [SqlServerFact]
    public async Task A_finance_record_reserves_its_number_only_after_draft_and_approval_and_replays_its_frozen_format()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK numbered bank");
        var approved = await NumberedApprovedAsync(run, bank);
        Assert.Null(approved.DocumentNumber);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
        await using var numbers = await FinanceNumbersAsync(run);
        ExpensePaymentCommandResult recorded;
        await using (var scope = OpenNumbered(run, numbers.Numbers))
        {
            recorded = await scope.Payments.RecordAsync(scope.Context, Key("SENTETIK-record-number"), new(approved.Id, approved.Version, true), default);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
            Assert.Equal(new FinanceDocumentNumberV1("DEK-2026-000001", "1"), recorded.Resource!.DocumentNumber);
            Assert.Equal(approved.Preparation.Reference, recorded.Resource.Preparation.Reference);
        }
        await numbers.Administration.RecordFormatAsync(new(SettingsDocumentSeriesV1.FinanceVoucher, "1", "YDK", 6, "SENTETIK sonraki format"),
            NumberAdminMetadata("SENTETIK-format-two"));
        await using (var scope = OpenNumbered(run, numbers.Numbers))
        {
            var replay = await scope.Payments.RecordAsync(scope.Context, Key("SENTETIK-record-number"), new(approved.Id, approved.Version, true), default);
            Assert.True(replay.IsReplay); Assert.Equal(recorded.Resource, replay.Resource);
            Assert.Equal(recorded.OriginalCorrelationId, replay.OriginalCorrelationId);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'settings.document-number.reserved.v1'"));
        Assert.Equal("DEK-2026-000001", await run.ScalarAsync<string>("SELECT DocumentNumber FROM finance.ExpensePayments WHERE Id=@id", ("@id", approved.Id)));
    }

    [SqlServerFact]
    public async Task A_late_finance_denial_rolls_back_the_settings_number_audit_and_attempt_then_the_same_key_can_record()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK denied numbered bank");
        var approved = await NumberedApprovedAsync(run, bank);
        await using var numbers = await FinanceNumbersAsync(run);
        var permission = new NumberRecordPermission();
        await using (var scope = OpenNumbered(run, numbers.Numbers, permission,
            real => new NumberThenDeny(real, () => permission.Denied = true)))
        {
            var refused = await scope.Payments.RecordAsync(scope.Context, Key("SENTETIK-late-number"), new(approved.Id, approved.Version, true), default);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
        }
        await AssertNoNumberOrRecordAsync(run, approved.Id);
        await using (var scope = OpenNumbered(run, numbers.Numbers))
        {
            var retry = await scope.Payments.RecordAsync(scope.Context, Key("SENTETIK-late-number"), new(approved.Id, approved.Version, true), default);
            Assert.Equal(CommandOutcome.Succeeded, retry.Outcome);
            Assert.Equal("DEK-2026-000001", retry.Resource!.DocumentNumber!.Number);
        }
    }

    [SqlServerFact]
    public async Task A_finance_save_fault_after_number_reservation_leaves_no_number_gap_no_money_and_no_successful_attempt()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK fault numbered bank");
        var approved = await NumberedApprovedAsync(run, bank);
        await using var numbers = await FinanceNumbersAsync(run);
        await using (var scope = OpenNumbered(run, numbers.Numbers, interceptor: new FailNumberRecordAfterSave()))
            await Assert.ThrowsAsync<NumberRecordFault>(() => scope.Payments.RecordAsync(scope.Context,
                Key("SENTETIK-save-fault-number"), new(approved.Id, approved.Version, true), default));
        await AssertNoNumberOrRecordAsync(run, approved.Id);
        await using (var scope = OpenNumbered(run, numbers.Numbers))
        {
            var retry = await scope.Payments.RecordAsync(scope.Context, Key("SENTETIK-save-fault-number"), new(approved.Id, approved.Version, true), default);
            Assert.Equal(CommandOutcome.Succeeded, retry.Outcome);
            Assert.Equal("DEK-2026-000001", retry.Resource!.DocumentNumber!.Number);
        }
    }

    private static async Task<ExpensePaymentView> NumberedApprovedAsync(Run run, Guid bank)
    {
        var draft = await run.DraftAsync(CompanyA, bank, "SENTETIK-number-draft");
        await using var scope = run.Open(CompanyA, Approver);
        var result = await scope.Payments.ApproveAsync(scope.Context, Key("SENTETIK-number-approve"), new(draft.Id, draft.Version), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); return result.Resource!;
    }
    private static SettingsCommandMetadataV1 NumberAdminMetadata(string key) =>
        new(CompanyA, Recorder, "SENTETIK-number-admin", null, SourceChannels.Web, null, null, key);

    private static async Task<PurchaseDocumentNumberSqlTestOwner> FinanceNumbersAsync(Run run)
    {
        var owner = new PurchaseDocumentNumberSqlTestOwner(run.Factory.Services, CompanyA,
            new FixedCurrentUser(Recorder, CompanyA), new PurchaseDocumentNumberSqlTestOwner.DirectStoreAuthority());
        var answer = await owner.Administration.RecordFormatAsync(new(SettingsDocumentSeriesV1.FinanceVoucher, "0", "DEK", 6, "SENTETIK açık dekont biçimi"),
            NumberAdminMetadata("SENTETIK-number-format"));
        Assert.Equal(204, answer.Answer.StatusCode); return owner;
    }
    private static FinanceAuditEnvelope NumberEnvelope(string permission) => new(permission, ["SENTETIK-FINANCE"], "Web");
    private static Scope OpenNumbered(Run run, ISettingsDocumentNumberSeriesV1 numbers,
        IFinancePermissionPort? permission = null, Func<IFinanceDocumentNumberReservationV1, IFinanceDocumentNumberReservationV1>? decorate = null,
        IInterceptor? interceptor = null)
    {
        var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        var db = new FinanceDbContext(options.Options, company);
        IFinanceDocumentNumberReservationV1 real = new SettingsFinanceDocumentNumberReservationV1(db, numbers);
        var envelopes = new ExpensePaymentAuditEnvelopes(NumberEnvelope(FinancePermissionNames.ExpensePaymentDraftSave),
            NumberEnvelope(FinancePermissionNames.ExpensePaymentApprove), NumberEnvelope(FinancePermissionNames.ExpensePaymentRecord));
        var service = new ExpensePaymentService(db, company, new FixedCurrentUser(Recorder, CompanyA), permission ?? new SyntheticPermissions(),
            new SingleExpenseItemCatalog(), new SyntheticBalance(TreasuryBalanceOutcome.Sufficient), new SyntheticSettings(CompanyA),
            new SyntheticOpenPeriod(), audit, new(FinanceMoneyOperations.ExpensePaymentV1), envelopes, TimeProvider.System,
            documentNumbers: decorate?.Invoke(real) ?? real);
        return new(db, service, new(CompanyA, Recorder, run.Correlation, null));
    }
    private static async Task AssertNoNumberOrRecordAsync(Run run, Guid id)
    {
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePayments WHERE DocumentNumber IS NOT NULL"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName=N'finance.expense-payment.record'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberCounters"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'settings.document-number.reserved.v1',N'finance.expense-payment.recorded.v1')"));
    }
    private sealed class NumberRecordPermission : IFinancePermissionPort
    {
        internal bool Denied;
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken) =>
            Task.FromResult(Denied ? PermissionDecision.Denied : PermissionDecision.Allowed);
    }
    private sealed class NumberThenDeny(IFinanceDocumentNumberReservationV1 real, Action denied) : IFinanceDocumentNumberReservationV1
    {
        public async Task<SettingsDocumentNumberReservationV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
            FinanceAuditEnvelope envelope, SettingsFinanceDocumentPurposeV1 purpose, Guid sourceId, DateOnly businessDate, CancellationToken cancellationToken)
        { var answer = await real.ReserveAsync(context, metadata, envelope, purpose, sourceId, businessDate, cancellationToken); denied(); return answer; }
    }
    private sealed class NumberRecordFault : Exception { }
    private sealed class FailNumberRecordAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new NumberRecordFault();
    }
}
