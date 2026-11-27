using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    // Actual SQL, existing native Propose/Decide and SAME scoped writer. Bank master fixture is seeded as in native movement goldens.
    [SqlServerFact]
    public async Task Credit_movement_bank_caption_comes_from_exact_company_owner_query_without_changing_original_native_receipt()
    {
        await using var run = await Run.StartAsync(); var (id, bank) = await MovementPurposeBankPrepare(run);
        await using var scope = new MovementDecisionScope(run); var answer = await scope.Decide(id, "bank-caption-decide", true);
        Assert.Equal(CommandOutcome.Succeeded, answer.Outcome); var receipt = Assert.IsType<FinanceCreditMovementDecisionReceiptV1>(answer.DecisionReceipt);
        Assert.Equal("SENTETIK purpose bank", receipt.BankDisplayName); Assert.Equal(bank, answer.Resource!.Source.BankEffect!.BankAccountId);
        var reader = Assert.IsAssignableFrom<IFinanceCreditMovementDecisionReadV1>(scope.Service);
        var human = await reader.ReadDecisionAsync(new(CompanyA, Checker, scope.OriginalCorrelation, null), id, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Found, human.Outcome); Assert.Equal(receipt.BankDisplayName, human.Resource!.BankDisplayName);
        var item = GoymenERP.Modules.Finance.Infrastructure.WorkInbox.FinanceCreditMovementWorkInboxSourceV1.DecisionItem(human.Resource);
        Assert.True(FinanceCreditMovementServiceV1.MatchesDecisionItem(item, new(answer.Resource, Maker, receipt.BankDisplayName, receipt.SourceFactsDigest)));
        Assert.False(FinanceCreditMovementServiceV1.MatchesDecisionItem(item, new(answer.Resource, Maker, "WRONG snapshot caption", receipt.SourceFactsDigest)));
        var originalJson = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.CreditMovementCommands WHERE CompanyId=@c AND Command=@command AND KeyHash=@key",
            ("@c", CompanyA), ("@command", FinanceCreditMovementServiceV1.DecideCommand), ("@key", FinanceCommandKeys.Hash("bank-caption-decide")));
        Assert.Equal(originalJson, JsonSerializer.Serialize(answer, FinanceJson.CreateOptions()));
        Assert.DoesNotContain("bankDisplayName", originalJson); Assert.DoesNotContain("bankDisplayName", JsonSerializer.Serialize(receipt, FinanceJson.CreateOptions()));
        Assert.Equal(receipt, (await scope.Decide(id, "bank-caption-decide", true)).DecisionReceipt);
        scope.AssertClean(); await MovementSafetyDurable(run, id, true);
    }

    [SqlServerFact]
    public async Task Credit_movement_exact_bank_caption_read_fences_roll_back_before_commit_and_same_key_decides_once()
    {
        foreach (var boundary in new[] { 4, 5, 7 }) // Save=1, Commands2/3, bank owner read4/5, post-native6, precommit7.
        foreach (var approve in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); var (id, _) = await MovementPurposeBankPrepare(run); var save = new MovementDecisionSave(id);
            await using var scope = new MovementDecisionScope(run, save: save); var deny = true; var savedFences = 0;
            scope.Permission.Check = () => save.Saved && ++savedFences == boundary && deny ? PermissionDecision.Denied : PermissionDecision.Allowed;
            var refused = await scope.Decide(id, "bank-caption-late", approve);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.DecisionReceipt); Assert.True(save.Saved); Assert.Equal(boundary, savedFences);
            scope.AssertClean(); await MovementSafetyDurable(run, id, null);
            deny = false; save.Saved = false;
            var success = await scope.Decide(id, "bank-caption-late", approve); var replay = await scope.Decide(id, "bank-caption-late", approve);
            Assert.Equal(CommandOutcome.Succeeded, success.Outcome); Assert.Equal("SENTETIK purpose bank", success.DecisionReceipt!.BankDisplayName);
            Assert.True(replay.IsReplay); Assert.Equal(success.DecisionReceipt, replay.DecisionReceipt); scope.AssertClean(); await MovementSafetyDurable(run, id, approve);
        }
    }

    private static async Task<(Guid Movement, Guid Bank)> MovementPurposeBankPrepare(Run run)
    {
        var account = await ApprovedMovementAccount(run); var bank = Guid.NewGuid();
        await using (var seed = run.OpenMovement(Maker))
        {
            seed.Database.BankAccounts.Add(new BankAccountRow { Id = bank, CompanyId = CompanyA, CurrencyId = Currency, DisplayName = "SENTETIK purpose bank",
                Version = 2, Status = 2, ProposedBy = Maker, ProposedAtUtc = DateTimeOffset.UtcNow, ApprovedBy = Checker, ApprovedAtUtc = DateTimeOffset.UtcNow,
                MoneyMovementEligible = true, Iban = "TR330006100519786457841326" });
            await seed.Database.SaveChangesAsync();
        }
        await using var maker = run.OpenMovement(Maker);
        var proposed = await maker.Service.ProposeAsync(maker.Context, Key("purpose-bank-propose"), ActualInput(account) with
            { BankEffect = new(bank, "95.00", "SENTETIK actual bank leg") }, default);
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); return (proposed.Resource!.Id, bank);
    }
}
