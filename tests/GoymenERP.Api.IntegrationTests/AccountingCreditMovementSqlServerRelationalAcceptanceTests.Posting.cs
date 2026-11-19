using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

// Real Accounting queue/draft/journal SQL. Finance owner and approved package data are explicit synthetic seams.
public sealed partial class AccountingCreditMovementSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Approved_credit_components_create_one_normal_journal_only_after_a_different_accounting_actor_approves()
    {
        await using var run = await Run.Start(); var source = PostedSource();
        await using var scope = run.Open(); await scope.Store.AcceptAsync(Envelope(source), default);
        var user = new CreditUser(); var owner = new CreditOwner(source); var permissions = new CreditPermission();
        var binding = CreditBinding(source, owner.Value); var provider = CreditProvider(scope, user, owner, binding, permissions);
        var queue = new CreditMovementProposalStoreV1(scope.Db, scope.Company, new MissingMappings(), scope.Audit, TimeProvider.System, journalSource: provider);
        var ready = Assert.Single(await queue.ListAsync(default)); Assert.Equal("ReadyForPreparation", ready.Status);
        Assert.Equal("MappingReady", ready.MappingStatus); Assert.Empty(ready.WaitingReasons);
        Assert.Null(ready.Source.Components[0].FunctionalAmount); // original currency, never inferred TL.
        var actual = Assert.IsType<NormalJournalDraftServerSourceV1>(await provider.ResolveAsync(source.CompanyId, source.EventId));
        Assert.Equal(source.CurrencyId, actual.CurrencyId); Assert.Equal(owner.Value.MoneyProfile.ProfileId.ToString("D"), actual.OwnerProfileId);
        var preparation = NormalJournalDraftSourcePreparationV1.TryCreate(source.CompanyId, source.EventId, actual)!;
        var exactLines = Assert.IsAssignableFrom<IReadOnlyList<NormalJournalExactSourceLineV1>>(actual.ExactSourceLines);
        var lines = exactLines.Select(x => new ProposedLine(x.MappingKey,
            actual.Rule.Mappings.Single(m => m.MappingKey == x.MappingKey && m.Side.ToString() == x.Side).Account,
            Enum.Parse<PostingSide>(x.Side), decimal.Parse(x.Amount, CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(new[] { 98.5m, 1.5m, 100m }, lines.Select(x => Assert.IsType<decimal>(x.Amount)));
        var changed = lines.ToArray(); changed[0] = changed[0] with { Amount = 98m }; changed[1] = changed[1] with { Amount = 2m };
        Assert.False(NormalJournalExactSourceAmountsV1.Matches(exactLines, changed)); // still balanced; source facts differ.
        var store = new AccountingStore(scope.Db, scope.Company, scope.Audit, TimeProvider.System,
            new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(source.CompanyId)));
        var period = Guid.NewGuid(); var checker = Guid.NewGuid();
        await store.ProposePeriodAsync(period, new(2026, 11, 1), new(2026, 11, 30), user.Actor, CreditMetadata(user.Actor));
        await store.ApprovePeriodAsync(period, 1, checker, CreditMetadata(checker));
        var id = Guid.NewGuid();
        var request = new PostingRequest("SENTETIK-credit-posting", id.ToString("D"), id,
            new(source.CompanyId, source.EventId, actual.Rule.PostingPurpose), actual.Rule.Version, actual.CurrencyId,
            new(source.TransactionDate, source.AccountingDate, null, At), lines);
        var draft = NormalJournalDraftV1.Prepare(JournalKind.Original, request, 1, user.Actor, actual.Rule.Mirror, actual.Rule).Draft!;
        await store.CreateNormalDraftAsync(draft, _ => Task.CompletedTask, CreditMetadata(user.Actor));
        var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => store.ApproveAndPostNormalDraftAsync(id, 1, period,
            user.Actor, _ => Task.CompletedTask, CreditMetadata(user.Actor)));
        Assert.Equal(NormalJournalDraftStoreCodesV1.CreditSelfApproval, refused.Code);
        Assert.Equal(0, await run.Count("accounting.Journals"));
        var posted = await store.ApproveAndPostNormalDraftAsync(id, 1, period, checker, _ => Task.CompletedTask, CreditMetadata(checker));
        Assert.NotEqual(posted.PreparedByUserId, posted.ApprovedByUserId);
        await queue.AcceptAsync(Envelope(source), default);
        Assert.Equal(1, await run.Count("accounting.CreditMovementProposals")); Assert.Equal(0, await run.Count("accounting.FinanceJournalSources"));
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(3, await run.Count("accounting.JournalLines"));
        Assert.Equal(64, preparation.PreparationVersion.Length); Assert.Equal("98.50", owner.Value.Source.BankEffect!.Amount);
    }

    [SqlServerFact]
    public async Task Credit_source_resolution_refuses_missing_mapping_changed_owner_amount_profile_and_permission_or_context_loss()
    {
        await using var run = await Run.Start(); var source = PostedSource();
        await using (var accept = run.Open()) await accept.Store.AcceptAsync(Envelope(source), default);
        foreach (var change in new[] { "mapping", "amount", "profile", "status", "reversed", "owner-denied", "permission", "company", "actor", "membership" })
        {
            await using var scope = run.Open(); var user = new CreditUser(); var owner = new CreditOwner(source); var permissions = new CreditPermission();
            var consumerCompany = new CreditConsumerCompany(source.CompanyId);
            var binding = CreditBinding(source, owner.Value);
            if (change == "amount") owner.Value = owner.Value with { Source = owner.Value.Source with { Components = [new("principal", "101", null), new("fee", "1.50", null)] } };
            if (change == "profile") owner.Value = owner.Value with { MoneyProfile = owner.Value.MoneyProfile with { ProfileVersion = 2 } };
            if (change == "status") owner.Value = owner.Value with { Status = CreditMovementStatusV1.Proposed };
            if (change == "reversed") owner.Value = owner.Value with { ReversedByMovementId = Guid.NewGuid() };
            if (change == "owner-denied") owner.Outcome = ReadOutcome.Denied;
            if (change == "permission") permissions.Decision = PermissionDecision.Denied;
            if (change == "company") owner.DuringRead = () => consumerCompany.Set(TestCompanyIds.CompanyB);
            if (change == "actor") owner.DuringRead = () => user.Actor = Guid.NewGuid();
            if (change == "membership") owner.DuringRead = () => user.Member = false;
            Assert.Null(await CreditProvider(scope, user, owner, change == "mapping" ? null : binding, permissions, consumerCompany)
                .ResolveAsync(source.CompanyId, source.EventId));
            if (change == "company")
            {
                Assert.Equal(TestCompanyIds.CompanyB, consumerCompany.CompanyId);
                Assert.Equal(source.CompanyId, scope.Company.CompanyId);
                Assert.Equal(1, owner.ReadCalls);
            }
        }
        Assert.Equal(0, await run.Count("accounting.Journals"));
    }
    private static readonly DateTimeOffset At = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static FinanceCreditMovementRecordedV1 PostedSource() => Source() with { Components = [new("principal", "100", null), new("fee", "1.50", null)] };
    private static AccountingCommandMetadata CreditMetadata(Guid actor) => new(actor.ToString("D"), "SENTETIK-accounting", [], "SENTETIK-credit", null, Guid.NewGuid().ToString("D"), SourceChannels.Integration);
    private static CreditMovementNormalJournalSourceProviderV1 CreditProvider(Scope scope, CreditUser user, CreditOwner owner,
        ApprovedNormalJournalSourceBindingV1? binding, CreditPermission permission, ICompanyContext? consumerCompany = null)
    {
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-credit", null, SourceChannels.Integration, null, null);
        return new(scope.Db, consumerCompany ?? scope.Company, user, correlation, owner,
            new ApprovedNormalJournalSourceBindingCatalogV1(binding is null ? [] : [binding]), permission);
    }
    private static ApprovedNormalJournalSourceBindingV1 CreditBinding(FinanceCreditMovementRecordedV1 source, CreditMovementViewV1 value)
    {
        var accounts = Enumerable.Range(1, 3).Select(_ => new AccountRef(source.CompanyId, Guid.NewGuid())).ToArray(); var actor = Guid.NewGuid();
        var mirror = new AccountPlanMirrorV1(source.CompanyId, "SENTETIK plan", new string('a', 64), actor, At,
            accounts.Select((x, i) => new AccountPlanMirrorRowV1(x, "SENTETIK-" + i, "SENTETIK account")).ToArray());
        var rule = new NormalJournalPostingRuleV1(mirror, CreditMovementJournalComponentsV1.Purpose(source.Kind.ToString())!, "1", actor, At,
            [new("credit.bank", PostingSide.Debit, accounts[0]), new("credit.component.fee", PostingSide.Debit, accounts[1]),
             new("credit.component.principal", PostingSide.Credit, accounts[2])]);
        return new(source.CompanyId, source.EventId, NormalJournalSourceKindsV1.RecordedCreditMovement, value.Version,
            value.MoneyProfile.ProfileId.ToString("D"), value.MoneyProfile.ProfileVersion.ToString(CultureInfo.InvariantCulture), source.CurrencyId, rule);
    }
    // The real SQL scope keeps its original company; only the provider's consumer context drifts.
    private sealed class CreditConsumerCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class CreditUser : ICurrentUser
    {
        public Guid Actor { get; set; } = Guid.NewGuid(); public bool Member { get; set; } = true;
        public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA };
        public bool CanAccessCompany(Guid id) => Member && id == TestCompanyIds.CompanyA;
    }
    private sealed class CreditPermission : IFinancePermissionPort
    {
        public PermissionDecision Decision { get; set; } = PermissionDecision.Allowed;
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? resourceId, CancellationToken ct) => Task.FromResult(Decision);
    }
    private sealed class CreditOwner(FinanceCreditMovementRecordedV1 source) : IFinanceCreditMovementServiceV1
    {
        public ReadOutcome Outcome { get; set; } = ReadOutcome.Found; public Action? DuringRead { get; set; }
        public int ReadCalls { get; private set; }
        public CreditMovementViewV1 Value { get; set; } = new(source.MovementId, source.CompanyId, "2", CreditMovementStatusV1.Approved,
            new(source.CreditAccountId, "2", source.Kind, source.SourceDocument, source.TransactionDate, source.AccountingDate,
                source.Components, source.BankEffect, null, null, source.Reason), source.CurrencyId, false, At, At.AddMinutes(1), null, null, null,
            new(Guid.NewGuid(), 1, 2, "0.01"));
        public Task<ReadResult<CreditMovementPageV1>> ListAsync(FinanceContext context, Guid id, CancellationToken ct)
        { ReadCalls++; DuringRead?.Invoke(); return Task.FromResult(new ReadResult<CreditMovementPageV1>(Outcome, Outcome == ReadOutcome.Found ? new(Value.CompanyId, [Value]) : null)); }
        public Task<CreditMovementResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata, CreditMovementProposeV1 request, CancellationToken ct) => throw new NotSupportedException();
        public Task<CreditMovementResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata, CreditMovementDecisionV1 request, CancellationToken ct) => throw new NotSupportedException();
    }
}
