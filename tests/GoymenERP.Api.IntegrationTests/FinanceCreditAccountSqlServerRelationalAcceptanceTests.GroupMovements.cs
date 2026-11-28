using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    // REF-CREDIT-01 / REF-JOURNAL-01: native source/outbox evidence, not an accounting posting,
    // instrument release policy or NonCash reimbursement policy. Existing synthetic ports remain unchanged.
    [SqlServerFact]
    public async Task Each_approved_credit_group_keeps_its_documented_actual_once_across_restart_replay_and_duplicate_source()
    {
        await using var run = await Run.StartAsync();
        foreach (var group in Enum.GetValues<CreditAccountGroup>())
        {
            var account = await ApprovedGroupMovementAccount(run, group);
            var prefix = $"SENTETIK-group-actual-{group}";
            // A supplied commission fact; no principal, interest, tax or FX calculation is performed.
            var input = ActualInput(account) with
            {
                Kind = CreditMovementKindV1.InterestAccrual,
                SourceDocument = new(prefix, new(2026, 11, 16), "1"),
                Components = [new("commission", "12.34567", null)]
            };
            var sourceJson = JsonSerializer.Serialize(input, FinanceJson.CreateOptions());
            CreditMovementViewV1 proposed;
            await using (var maker = run.OpenMovement(Maker))
            {
                var result = await maker.Service.ProposeAsync(maker.Context, Key(prefix + "-p"), input, default);
                Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                proposed = Assert.IsType<CreditMovementViewV1>(result.Resource);
                Assert.Equal((CompanyA, account.Id, "1", CreditMovementStatusV1.Proposed),
                    (proposed.CompanyId, proposed.Source.CreditAccountId, proposed.Version, proposed.Status));
            }
            var decision = new CreditMovementDecisionV1(proposed.Id, "1", true, null);
            await using (var checker = run.OpenMovement(Checker))
            {
                var result = await checker.Service.DecideAsync(checker.Context, Key(prefix + "-a"), decision, default);
                Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                Assert.False(result.IsReplay);
                Assert.Equal(("2", CreditMovementStatusV1.Approved), (result.Resource!.Version, result.Resource.Status));
                Assert.Equal(sourceJson, JsonSerializer.Serialize(result.Resource.Source, FinanceJson.CreateOptions()));
            }
            await using (var restartedMaker = run.OpenMovement(Maker))
            {
                var replay = await restartedMaker.Service.ProposeAsync(restartedMaker.Context, Key(prefix + "-p"), input, default);
                Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
                Assert.True(replay.IsReplay);
                Assert.Equal(proposed.Id, replay.Resource!.Id);
                Assert.Equal(sourceJson, JsonSerializer.Serialize(replay.Resource.Source, FinanceJson.CreateOptions()));
                var duplicate = await restartedMaker.Service.ProposeAsync(restartedMaker.Context, Key(prefix + "-duplicate"), input, default);
                Assert.Equal(CommandOutcome.ValidationFailed, duplicate.Outcome);
                Assert.Contains(duplicate.Issues, x => x.Code == "credit_movement_source_already_recorded");
            }
            await using (var restartedChecker = run.OpenMovement(Checker))
            {
                var replay = await restartedChecker.Service.DecideAsync(restartedChecker.Context, Key(prefix + "-a"), decision, default);
                Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
                Assert.True(replay.IsReplay);
                Assert.Equal((proposed.Id, "2", CreditMovementStatusV1.Approved),
                    (replay.Resource!.Id, replay.Resource.Version, replay.Resource.Status));
                Assert.Equal(restartedChecker.Context.CorrelationId, replay.OriginalCorrelationId);
                var events = await new FinanceCreditOutboxEventSourceV1(restartedChecker.Database, restartedChecker.Company)
                    .ReadAfterAsync(null, 10, default);
                var envelope = Assert.Single(events, x => JsonSerializer.Deserialize<FinanceCreditMovementRecordedV1>(x.PayloadJson,
                    FinanceJson.CreateOptions())!.MovementId == proposed.Id);
                var payload = JsonSerializer.Deserialize<FinanceCreditMovementRecordedV1>(envelope.PayloadJson, FinanceJson.CreateOptions())!;
                Assert.Equal((CompanyA, account.Id, Currency, CreditMovementKindV1.InterestAccrual, Checker),
                    (payload.CompanyId, payload.CreditAccountId, payload.CurrencyId, payload.Kind, payload.ApprovedBy));
                Assert.Equal(input.SourceDocument, payload.SourceDocument);
                Assert.Equal(input.Components, payload.Components);
                Assert.Null(payload.BankEffect);
                Assert.Null(Assert.Single(payload.Components).FunctionalAmount);
            }
            Assert.Equal(sourceJson, await run.ScalarAsync<string>(
                "SELECT SourceJson FROM finance.CreditMovements WHERE CompanyId=@company AND Id=@id",
                ("@company", CompanyA), ("@id", proposed.Id)));
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.CreditMovements WHERE CompanyId=@company AND CreditAccountId=@id",
                ("@company", CompanyA), ("@id", account.Id)));
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.OutboxMessages WHERE CompanyId=@company AND AggregateId=@id",
                ("@company", CompanyA), ("@id", proposed.Id)));
            Assert.Equal(2, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id",
                ("@id", proposed.Id.ToString("D"))));
        }
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE CompanyId=@company", ("@company", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }

    [SqlServerFact]
    public async Task NonCash_bank_effect_is_refused_without_an_actual_movement_outbox_or_bank_posting()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedGroupMovementAccount(run, CreditAccountGroup.NonCash);
        var input = ActualInput(account) with
        {
            SourceDocument = new("SENTETIK-noncash-bank-refusal", new(2026, 11, 16), "1"),
            BankEffect = new(Guid.NewGuid(), "95.00", "SENTETIK bank evidence")
        };
        // The group prohibition precedes bank lookup: this does not choose a tazmin bank/debt direction.
        await using (var maker = run.OpenMovement(Maker))
        {
            var result = await maker.Service.ProposeAsync(maker.Context, Key("noncash-bank-refusal"), input, default);
            Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
            Assert.Null(result.Resource);
            Assert.Contains(result.Issues, x => x.Field == "bankEffect" && x.Code == "credit_non_cash_has_no_bank_effect");
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovements WHERE CreditAccountId=@id", ("@id", account.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }

    private static async Task<CreditAccountView> ApprovedGroupMovementAccount(Run run, CreditAccountGroup group)
    {
        var input = GroupFieldsSqlRequest(group);
        Guid id;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            var proposed = await maker.Credits.ProposeAsync(maker.Context, Key($"group-{group}-p"), input, default);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
            id = proposed.Resource!.Id;
        }
        await using var checker = run.Open(CompanyA, Checker);
        var approved = await checker.Credits.ApproveAsync(checker.Context, Key($"group-{group}-a"), new(id, "1"), default);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Equal((group, "2", CreditAccountStatus.Approved), (approved.Resource!.Group, approved.Resource.Version, approved.Resource.Status));
        return approved.Resource;
    }
}
