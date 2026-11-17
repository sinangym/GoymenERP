using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Fact]
    public async Task Bank_group_limit_keeps_source_revision_maker_checker_replay_and_original_approved_record()
    {
        await using var run = new Run(); var account = await ApprovedCredit(run);
        var source = Limit(account);
        var proposed = await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker), new("limit-p"), source, default));
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.Limit(Maker, service => service.DecideAsync(Ctx(Maker), new("limit-own"), new(proposed.Resource!.Id, "1", true, null), default))).Outcome);
        var decision = new CreditLimitDecisionV1(proposed.Resource!.Id, "1", true, null);
        var approved = await run.Limit(Checker, service => service.DecideAsync(Ctx(Checker), new("limit-a"), decision, default));
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.True((await run.Limit(Checker, service => service.DecideAsync(Ctx(Checker), new("limit-a"), decision, default))).IsReplay);
        var next = source with { Amount = "200.123", ExpectedCurrentLimitId = approved.Resource!.Id, ExpectedCurrentLimitVersion = "2", SourceDocument = source.SourceDocument with { Revision = "2" } };
        var nextProposal = await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker), new("limit-p2"), next, default));
        Assert.Equal(CommandOutcome.Succeeded, nextProposal.Outcome);
        Assert.Equal(CommandOutcome.Succeeded, (await run.Limit(Checker, service => service.DecideAsync(Ctx(Checker), new("limit-a2"), new(nextProposal.Resource!.Id, "1", true, null), default))).Outcome);
        var view = await run.LimitRead(Maker, account.Id);
        Assert.Equal("100.00", view.Resource!.Items.Single(x => x.Id == approved.Resource.Id).Source.Amount);
        Assert.Equal(nextProposal.Resource!.Id, view.Resource.Items.Single(x => x.Id == approved.Resource.Id).SupersededById);
        await using var db = run.Memory(CompanyA);
        Assert.Equal(2, await db.CreditLimits.CountAsync()); Assert.Empty(await db.OutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task Limit_stale_base_pending_duplicate_and_foreign_company_are_not_accepted()
    {
        await using var run = new Run(); var account = await ApprovedCredit(run);
        var source = Limit(account);
        Assert.Equal(CommandOutcome.VersionConflict, (await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker), new("limit-stale"), source with { ExpectedCreditVersion = "999" }, default))).Outcome);
        Assert.Equal(CommandOutcome.Succeeded, (await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker), new("limit-first"), source, default))).Outcome);
        Assert.Equal(CommandOutcome.VersionConflict, (await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker), new("limit-duplicate"), source, default))).Outcome);
        Assert.Equal(CommandOutcome.Denied, (await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker) with { CompanyId = CompanyB }, new("limit-foreign"), source, default))).Outcome);
    }

    [Fact]
    public async Task Exceeded_limit_is_an_advisory_and_does_not_prevent_actual_source_approval()
    {
        await using var run = new Run(); var account = await ApprovedCredit(run);
        var proposed = await run.Limit(Maker, service => service.ProposeAsync(Ctx(Maker), new("cap-p"), Limit(account) with { Amount = "50" }, default));
        await run.Limit(Checker, service => service.DecideAsync(Ctx(Checker), new("cap-a"), new(proposed.Resource!.Id, "1", true, null), default));
        var actual = await run.Movement(Maker, service => service.ProposeAsync(Ctx(Maker), new("limit-actual-p"), Movement(account), default));
        var before = (await run.LimitRead(Checker, account.Id)).Resource!;
        Assert.Equal("0", before.Assessment.RecordedPrincipalNet);
        Assert.True(Assert.Single(before.Assessment.ProposedMovements).ExceedsLimit);
        Assert.Equal(CommandOutcome.Succeeded, (await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("limit-actual-a"), new(actual.Resource!.Id, "1", true, null), default))).Outcome);
        var after = (await run.LimitRead(Maker, account.Id)).Resource!;
        Assert.Equal("100.00", after.Assessment.RecordedPrincipalNet); Assert.True(after.Assessment.ExceedsLimit);
    }

    [Fact]
    public void Limit_assessment_uses_actual_principal_only_and_preserves_unknown_limit_and_signed_source_net()
    {
        var source = new CreditMovementProposeV1(Guid.NewGuid(), "2", CreditMovementKindV1.Repayment,
            new("SENTETIK", Start, "1"), Start, Start, [new("principal", "7.125", null), new("interest", "90", null)], null, null, null, "SENTETIK");
        var row = new FinanceCreditMovementRow { Id = Guid.NewGuid(), CompanyId = CompanyA, CreditAccountId = source.CreditAccountId,
            CurrencyId = Currency, Version = 2, Kind = 2, Status = 2, SourceJson = JsonSerializer.Serialize(source, FinanceJson.CreateOptions()),
            SourceIdentityHash = new('a', 64), MoneyProfileJson = "{}", ProposedBy = Maker };
        var answer = FinanceCreditLimitServiceV1.Assess([row], null)!;
        Assert.Equal("-7.125", answer.RecordedPrincipalNet); Assert.Null(answer.ExceedsLimit);
    }

    private static CreditLimitProposeV1 Limit(CreditAccountView account) => new(account.Id, account.Version, null, null,
        "100.00", new("SENTETIK-LIMIT", Start, "1"), "SENTETIK banka kaynak limiti");
    private sealed partial class Run
    {
        public Task<CreditLimitResultV1> Limit(string actor, Func<FinanceCreditLimitServiceV1, Task<CreditLimitResultV1>> call) => LimitCall(actor, call);
        public Task<ReadResult<CreditLimitWorkspaceV1>> LimitRead(string actor, Guid id) => LimitCall(actor, service => service.ReadAsync(Ctx(actor), id, default));
        private async Task<T> LimitCall<T>(string actor, Func<FinanceCreditLimitServiceV1, Task<T>> call)
        {
            var company = new CompanyContext(); company.Set(CompanyA); var collector = new AuditEventCollector();
            await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            return await call(new(db, company, new FixedUser(actor, CompanyA), new RecordingPermission(PermissionDecision.Allowed),
                new SyntheticCreditSources(CompanyA), collector, Envelopes(), new FixedClock()));
        }
    }
}
