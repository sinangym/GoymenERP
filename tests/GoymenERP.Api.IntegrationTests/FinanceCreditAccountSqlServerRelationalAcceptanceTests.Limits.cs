using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Bank_group_limit_keeps_two_person_source_audit_replay_and_Down_refuses_loss()
    {
        await using var run = await Run.StartAsync(); var account = await ApprovedMovementAccount(run);
        var input = new CreditLimitProposeV1(account.Id, account.Version, null, null, "50.00", new("SENTETIK-LIMIT", account.StartDate, "1"), "SENTETIK limit kaynağı");
        CreditLimitViewV1 proposal;
        await using (var maker = run.OpenLimit(Maker))
            proposal = (await maker.Service.ProposeAsync(maker.Context, Key("limit-p"), input, default)).Resource!;
        Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditLimits SET [Status]=2, [Version]=2, DecidedBy=ProposedBy, DecidedAtUtc=SYSDATETIMEOFFSET() WHERE Id=@id", ("@id", proposal.Id)))).Number);
        await using (var checker = run.OpenLimit(Checker))
        {
            var command = new CreditLimitDecisionV1(proposal.Id, "1", true, null);
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.DecideAsync(checker.Context, Key("limit-a"), command, default)).Outcome);
            Assert.True((await checker.Service.DecideAsync(checker.Context, Key("limit-a"), command, default)).IsReplay);
        }
        CreditMovementViewV1 actual;
        await using (var maker = run.OpenMovement(Maker)) actual = (await maker.Service.ProposeAsync(maker.Context, Key("limit-actual-p"), ActualInput(account), default)).Resource!;
        await using (var checker = run.OpenMovement(Checker))
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.DecideAsync(checker.Context, Key("limit-actual-a"), new(actual.Id, "1", true, null), default)).Outcome);
        await using (var reader = run.OpenLimit(Checker))
            Assert.True((await reader.Service.ReadAsync(reader.Context, account.Id, default)).Resource!.Assessment.ExceedsLimit);
        await using (var foreign = run.OpenLimit(Checker, CompanyB))
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.ReadAsync(foreign.Context, account.Id, default)).Outcome);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditLimit' AND EntityId=@id", ("@id", proposal.Id.ToString("D"))));
        var guard = Assert.IsType<SqlOperation>(new AddCreditBankGroupLimits().DownOperations[0]).Sql;
        Assert.Equal(51974, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal("50.00", await run.ScalarAsync<string>("SELECT AmountText FROM finance.CreditLimits WHERE Id=@id", ("@id", proposal.Id)));
    }

    [SqlServerFact]
    public async Task Concurrent_bank_group_limit_proposals_keep_one_pending_source_slot()
    {
        await using var run = await Run.StartAsync(); var account = await ApprovedMovementAccount(run);
        var input = new CreditLimitProposeV1(account.Id, account.Version, null, null, "50", new("SENTETIK", account.StartDate, "1"), "SENTETIK");
        await using var first = run.OpenLimit(Maker); await using var second = run.OpenLimit(Maker);
        var answers = await Task.WhenAll(first.Service.ProposeAsync(first.Context, Key("parallel-a"), input, default),
            second.Service.ProposeAsync(second.Context, Key("parallel-b"), input, default));
        Assert.Single(answers, x => x.Outcome == CommandOutcome.Succeeded);
        Assert.Single(answers, x => x.Outcome is CommandOutcome.VersionConflict or CommandOutcome.Unavailable);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditLimits WHERE CompanyId=@company AND [Status]=1", ("@company", CompanyA)));
    }

    private sealed partial class Run
    {
        public LimitScope OpenLimit(string actor, Guid? companyId = null)
        {
            var company = new CompanyContext(); company.Set(companyId ?? CompanyA); var audit = new AuditEventCollector();
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            var service = new FinanceCreditLimitServiceV1(db, company, new FixedCurrentUser(actor, company.CompanyId), new SyntheticPermissions(),
                new SyntheticCreditSources(company.CompanyId), audit, new(new(ErpPermissions.FinanceCreditsPropose, ["SENTETIK"], "Web"),
                    new(ErpPermissions.FinanceCreditsApprove, ["SENTETIK"], "Web")), TimeProvider.System);
            return new(db, service, new(company.CompanyId, actor, Correlation, null));
        }
    }
    private sealed class LimitScope(FinanceDbContext db, FinanceCreditLimitServiceV1 service, FinanceContext context) : IAsyncDisposable
    {
        public FinanceCreditLimitServiceV1 Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
