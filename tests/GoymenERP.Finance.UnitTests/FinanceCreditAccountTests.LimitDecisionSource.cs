using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Bank_limit_purpose_source_reuses_native_formatter_and_approve_only_owner_for_pending_and_terminal(bool terminal)
    {
        await using var run = new Run(); var id = await LimitDecisionPrepare(run);
        if (terminal)
        {
            var decided = await run.Limit(Checker, service => service.DecideAsync(Ctx(Checker), new("limit-source-decide"), new(id, "1", true, null), default));
            Assert.Equal(CommandOutcome.Succeeded, decided.Outcome);
        }
        await using var db = run.Memory(CompanyA); var company = new LimitSafetyCompany(CompanyA); var user = new LimitSafetyUser();
        var permission = new LimitDecisionReadPermission(); var core = LimitSafetyCore(db, company, user, permission, null);
        var authority = new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true);
        var source = new FinanceBankCreditLimitWorkInboxSourceV1(db, new(company, user, authority), core.WithApprovalSafety());
        var context = new WorkInboxSourceContextV1(CompanyA, Checker, "limit-source-purpose", null);
        var snapshot = await source.ReadDecisionAsync(context, id, default); Assert.NotNull(snapshot);
        Assert.Equal(!terminal, snapshot.Pending); Assert.Equal(terminal ? "2" : "1", snapshot.SourceVersion);
        Assert.Equal(Maker, snapshot.Item.PreparedBy); Assert.True(snapshot.Item.Excludes(Maker)); Assert.Null(snapshot.Item.ExcludedDeciders);
        Assert.Equal($"/onaylarim/finance.bank-credit-limit.v1/{id:D}", snapshot.Item.Link);
        Assert.Equal("/finans/krediler", FinanceBankCreditLimitWorkInboxSourceV1.ScreenPath);
        var human = await core.ReadDecisionAsync(Ctx(Checker), id, _ => Task.CompletedTask, default); Assert.Equal(ReadOutcome.Found, human.Outcome);
        Assert.True(FinanceCreditLimitServiceV1.MatchesDecisionItem(snapshot.Item, human.Resource));
        var queue = await source.ListPendingAsync(context, 10, default);
        if (terminal) Assert.Empty(queue.Items!); else Assert.True(FinanceCreditLimitServiceV1.MatchesDecisionItem(Assert.Single(queue.Items!), human.Resource));
        Assert.Equal(human.Resource!.SourceFactsDigest, snapshot.BankCreditLimitProof!.SourceFactsDigest);
        Assert.Equal(FinanceCreditLimitServiceV1.DecisionPreviewDigest(human.Resource), snapshot.BankCreditLimitProof.PreviewDigest);
        Assert.DoesNotContain("bankCreditLimitProof", JsonSerializer.Serialize(snapshot, FinanceJson.CreateOptions()));
        Assert.All(permission.Actions, action => Assert.Equal(FinanceAction.ApproveCredit, action));
        Assert.Equal(ReadOutcome.Denied, (await core.ReadAsync(Ctx(Checker), human.Resource.Limit.Source.CreditAccountId, default)).Outcome);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("caption")] [InlineData("actual")] [InlineData("threshold")] [InlineData("deviation")] [InlineData("unit")]
    [InlineData("reference")] [InlineData("reference-label")] [InlineData("maker")] [InlineData("time")] [InlineData("link")] [InlineData("excluded")]
    public void Bank_limit_owner_matcher_binds_all_existing_rule_reference_and_caption_facts(string fault)
    {
        var (_, row, _, _, _, result) = LimitReceiptFixture(true); var view = result.Resource!;
        var detail = new FinanceBankCreditLimitDecisionDetailV1(view, row.ProposedBy, FinanceCreditLimitServiceV1.SourceFactsDigest(view, row.ProposedBy));
        var item = FinanceBankCreditLimitWorkInboxSourceV1.DecisionItem(detail); Assert.NotNull(item); Assert.True(FinanceCreditLimitServiceV1.MatchesDecisionItem(item, detail));
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
        Assert.False(FinanceCreditLimitServiceV1.MatchesDecisionItem(altered, detail));
        Assert.False(FinanceCreditLimitServiceV1.MatchesDecisionItem(item, detail with { SourceFactsDigest = "wrong" }));
    }

    [Fact]
    public async Task Bank_limit_source_has_no_raw_table_fallback_when_guarded_owner_is_missing()
    {
        await using var run = new Run(); var id = await LimitDecisionPrepare(run); await using var db = run.Memory(CompanyA);
        var source = new FinanceBankCreditLimitWorkInboxSourceV1(db, new(new LimitSafetyCompany(CompanyA), new LimitSafetyUser(),
            new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(new(CompanyA, Checker, "source-no-owner", null), id, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }
}
