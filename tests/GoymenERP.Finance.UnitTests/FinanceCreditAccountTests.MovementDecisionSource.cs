using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Credit_movement_purpose_source_reuses_native_formatter_and_approve_only_owner_for_pending_and_terminal(bool terminal)
    {
        await using var run = new Run(); var id = await MovementDecisionPrepare(run);
        if (terminal)
        {
            var decided = await run.Movement(Checker, service => service.DecideAsync(Ctx(Checker), new("movement-source-decide"), new(id, "1", true, null), default));
            Assert.Equal(CommandOutcome.Succeeded, decided.Outcome);
        }
        await using var db = run.Memory(CompanyA); var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser();
        var permission = new MovementDecisionReadPermission(); var core = MovementSafetyCore(db, company, user, permission, null);
        var authority = new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true);
        var source = new FinanceCreditMovementWorkInboxSourceV1(db, new(company, user, authority), core.WithApprovalSafety());
        var context = new WorkInboxSourceContextV1(CompanyA, Checker, "movement-source-purpose", null);
        var snapshot = await source.ReadDecisionAsync(context, id, default); Assert.NotNull(snapshot);
        Assert.Equal(!terminal, snapshot.Pending); Assert.Equal(terminal ? "2" : "1", snapshot.SourceVersion);
        Assert.Equal(Maker, snapshot.Item.PreparedBy); Assert.True(snapshot.Item.Excludes(Maker)); Assert.Null(snapshot.Item.ExcludedDeciders);
        Assert.Equal("/finans/krediler", snapshot.Item.Link);
        Assert.Equal("/finans/krediler", FinanceCreditMovementWorkInboxSourceV1.ScreenPath);
        var human = await core.ReadDecisionAsync(Ctx(Checker), id, _ => Task.CompletedTask, default); Assert.Equal(ReadOutcome.Found, human.Outcome);
        Assert.True(FinanceCreditMovementServiceV1.MatchesDecisionItem(snapshot.Item, human.Resource));
        var queue = await source.ListPendingAsync(context, 10, default);
        if (terminal) Assert.Empty(queue.Items!); else Assert.True(FinanceCreditMovementServiceV1.MatchesDecisionItem(Assert.Single(queue.Items!), human.Resource));
        Assert.Equal(human.Resource!.SourceFactsDigest, snapshot.CreditMovementProof!.SourceFactsDigest);
        Assert.Equal(FinanceCreditMovementServiceV1.DecisionPreviewDigest(human.Resource), snapshot.CreditMovementProof.PreviewDigest);
        Assert.DoesNotContain("creditMovementProof", JsonSerializer.Serialize(snapshot, FinanceJson.CreateOptions()));
        Assert.All(permission.Actions, action => Assert.Equal(FinanceAction.ApproveCredit, action));
        Assert.Equal(ReadOutcome.Denied, (await core.ListAsync(Ctx(Checker), human.Resource.Movement.Source.CreditAccountId, default)).Outcome);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("caption")] [InlineData("actual")] [InlineData("threshold")] [InlineData("deviation")] [InlineData("unit")]
    [InlineData("reference")] [InlineData("reference-label")] [InlineData("maker")] [InlineData("time")] [InlineData("link")] [InlineData("excluded")]
    public void Credit_movement_owner_matcher_binds_all_existing_rule_reference_and_caption_facts(string fault)
    {
        var (_, row, _, _, _, result) = MovementReceiptFixture(true); var view = result.Resource!;
        var detail = new FinanceCreditMovementDecisionDetailV1(view, row.ProposedBy, null, FinanceCreditMovementServiceV1.SourceFactsDigest(view, row.ProposedBy));
        var item = FinanceCreditMovementWorkInboxSourceV1.DecisionItem(detail); Assert.NotNull(item); Assert.True(FinanceCreditMovementServiceV1.MatchesDecisionItem(item, detail));
        var altered = item with
        {
            DocumentLabel = fault == "caption" ? "other" : item.DocumentLabel, PreparedBy = fault == "maker" ? "other" : item.PreparedBy,
            WaitingSinceUtc = fault == "time" ? item.WaitingSinceUtc.AddSeconds(1) : item.WaitingSinceUtc, Link = fault == "link" ? "/onaylarim" : item.Link,
            ExcludedDeciders = fault == "excluded" ? ["other"] : item.ExcludedDeciders,
            Rules = [item.Rules[0] with { Actual = fault == "actual" ? "999" : item.Rules[0].Actual, Threshold = fault == "threshold" ? "1" : null,
                Deviation = fault == "deviation" ? "1" : null, Unit = fault == "unit" ? "kg" : null }],
            References = item.References.Select(x => x.Role == "currency" ? x with { RecordId = fault == "reference" ? Guid.NewGuid() : x.RecordId,
                Label = fault == "reference-label" ? "other" : x.Label } : x).ToArray(),
        };
        Assert.False(FinanceCreditMovementServiceV1.MatchesDecisionItem(altered, detail));
        Assert.False(FinanceCreditMovementServiceV1.MatchesDecisionItem(item, detail with { SourceFactsDigest = "wrong" }));
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("SENTETIK owner bank")]
    public void Movement_bank_caption_matcher_reuses_the_existing_owner_name_fallback_and_keeps_caption_out_of_stable_facts(string? bankName)
    {
        var (_, row, _, _, _, result) = MovementReceiptFixture(true); var original = result.Resource!;
        var bankId = Guid.NewGuid(); var view = original with { Source = original.Source with { Kind = CreditMovementKindV1.Drawdown,
            BankEffect = new(bankId, "00095.00", "SENTETIK bank leg") }, OpeningPeriodVersion = null };
        var detail = new FinanceCreditMovementDecisionDetailV1(view, row.ProposedBy, bankName, FinanceCreditMovementServiceV1.SourceFactsDigest(view, row.ProposedBy));
        var item = FinanceCreditMovementWorkInboxSourceV1.DecisionItem(detail); Assert.NotNull(item);
        Assert.Equal(FinanceWorkInboxRead.Name(bankName, bankId), Assert.Single(item.References, x => x.Role == "bank-account").Label);
        Assert.Equal(FinanceWorkInboxRead.Amount(view.Source.BankEffect.Amount), Assert.Single(item.Rules).Actual);
        Assert.True(FinanceCreditMovementServiceV1.MatchesDecisionItem(item, detail));
        Assert.False(FinanceCreditMovementServiceV1.MatchesDecisionItem(item, detail with { BankDisplayName = "DIFFERENT owner bank" }));
        Assert.False(FinanceCreditMovementServiceV1.MatchesDecisionItem(item with { References = item.References.Select(x => x.Role == "bank-account"
            ? x with { RecordId = Guid.NewGuid() } : x).ToArray() }, detail));
        Assert.Equal(detail.SourceFactsDigest, FinanceCreditMovementServiceV1.SourceFactsDigest(view, row.ProposedBy));
        Assert.NotEqual(FinanceCreditMovementServiceV1.DecisionPreviewDigest(detail), FinanceCreditMovementServiceV1.DecisionPreviewDigest(detail with { BankDisplayName = "DIFFERENT owner bank" }));
        var receipt = new FinanceCreditMovementDecisionReceiptV1(CompanyA, view.Id, "SENTETIK", "key", "hash", Checker, "1", "2", "corr", row.DecidedAtUtc!.Value,
            detail.SourceFactsDigest, "digest") { BankDisplayName = bankName };
        Assert.DoesNotContain("bankDisplayName", JsonSerializer.Serialize(receipt, FinanceJson.CreateOptions()));
    }

    [Fact]
    public async Task Credit_movement_source_has_no_raw_table_fallback_when_guarded_owner_is_missing()
    {
        await using var run = new Run(); var id = await MovementDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var source = new FinanceCreditMovementWorkInboxSourceV1(db, new(new LimitSafetyCompany(CompanyA), new LimitSafetyUser(),
            new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(new(CompanyA, Checker, "source-no-owner", null), id, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }
}
