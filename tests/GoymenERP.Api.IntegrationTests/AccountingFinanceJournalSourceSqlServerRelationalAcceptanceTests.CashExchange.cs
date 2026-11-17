using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingFinanceJournalSourceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Approved_exchange_and_recorded_transfer_can_arrive_in_either_order_without_a_second_money_event_or_automatic_journal()
    {
        await using var run = await Run.Start();
        var (source, value) = ExchangeSource(); var approved = ExchangeEnvelope(source, value); var raw = ExchangeEnvelope(source);
        await using (var scope = run.Open())
        {
            await scope.Store.AcceptAsync(approved, default); await scope.Store.AcceptAsync(raw, default);
            await scope.Store.AcceptAsync(raw, default); await scope.Store.AcceptAsync(approved, default);
            var rows = await scope.Store.ListAsync(default); Assert.Equal(2, rows.Count);
            Assert.All(rows, x => Assert.Equal(("Received", "BindingUnavailable"), (x.Status, x.MappingStatus)));
            Assert.Equal("SourceBookValuePending", rows.Single(x => x.SourceId == source.Id).CashExchange!.PostingState);
            Assert.Equal("3100", rows.Single(x => x.SourceId == value.Id).Amount);
        }
        Assert.Equal(2, await run.Count("accounting.FinanceJournalSources"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
        Assert.Equal(0, await run.Count("accounting.NormalJournalDrafts"));
        await using (var foreign = run.Open(TestCompanyIds.CompanyB))
        { Assert.Empty(await foreign.Store.ListAsync(default)); await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => foreign.Store.AcceptAsync(approved, default)); }
        var (second, secondValue) = ExchangeSource();
        await using var reordered = run.Open();
        await reordered.Store.AcceptAsync(ExchangeEnvelope(second), default);
        await reordered.Store.AcceptAsync(ExchangeEnvelope(second, secondValue), default);
        Assert.Equal(4, await run.Count("accounting.FinanceJournalSources"));
        Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Approved_exchange_uses_actual_owner_carrying_and_settlement_with_exact_mapping_before_one_normal_journal_posts()
    {
        await using var run = await Run.Start(); var (source, value) = ExchangeSource();
        await using (var accept = run.Open()) await accept.Store.AcceptAsync(ExchangeEnvelope(source, value), default);
        await using var scope = run.Open(); var user = new ExchangeUser(); var owner = new ExchangeOwner(source, value);
        var provider = ExchangeProvider(scope, user, owner, ExchangeBinding(source, value));
        var actual = Assert.IsType<NormalJournalDraftServerSourceV1>(await provider.ResolveAsync(source.CompanyId, value.Id, default));
        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, actual.CurrencyId);
        Assert.Equal("3300", actual.ExactSourceAmount);
        Assert.Collection(actual.ExactSourceLines!, x => Assert.Equal("3300", x.Amount), x => Assert.Equal("3100", x.Amount), x => Assert.Equal("200", x.Amount));
        var preparation = NormalJournalDraftSourcePreparationV1.TryCreate(source.CompanyId, value.Id, actual)!;
        var posting = new AccountingStore(scope.Db, scope.Company, scope.Audit, TimeProvider.System,
            new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(source.CompanyId)));
        var periodId = Guid.NewGuid(); var checker = Guid.NewGuid();
        await posting.ProposePeriodAsync(periodId, new(2026, 11, 1), new(2026, 11, 30), user.Actor, ExchangeMetadata(user.Actor));
        await posting.ApprovePeriodAsync(periodId, 1, checker, ExchangeMetadata(checker));
        var lines = actual.ExactSourceLines!.Select(line => new ProposedLine(line.MappingKey,
            actual.Rule.Mappings.Single(x => x.MappingKey == line.MappingKey && x.Side.ToString() == line.Side).Account,
            Enum.Parse<PostingSide>(line.Side), decimal.Parse(line.Amount, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var draftId = Guid.NewGuid();
        var request = new PostingRequest("SENTETIK-exchange", draftId.ToString("D"), draftId,
            new(source.CompanyId, value.Id, actual.Rule.PostingPurpose), actual.Rule.Version, actual.CurrencyId,
            new(new(2026, 11, 17), new(2026, 11, 17), null, At), lines);
        var draft = NormalJournalDraftV1.Prepare(JournalKind.Original, request, 1, user.Actor, actual.Rule.Mirror, actual.Rule).Draft!;
        Assert.NotNull(draft); Assert.Equal(64, preparation.PreparationVersion.Length);
        await posting.CreateNormalDraftAsync(draft, _ => Task.CompletedTask, ExchangeMetadata(user.Actor));
        var posted = await posting.ApproveAndPostNormalDraftAsync(draftId, 1, periodId, checker, _ => Task.CompletedTask, ExchangeMetadata(checker));
        Assert.Equal(NormalJournalDraftStatusV1.Approved, posted.Status);
        Assert.NotEqual(posted.PreparedByUserId, posted.ApprovedByUserId);
        Assert.Equal(1, await run.Count("accounting.Journals")); Assert.Equal(3, await run.Count("accounting.JournalLines"));
        Assert.Equal(1, await run.Count("accounting.FinanceJournalSources"));
        // Owner facts remain original-currency amounts; neither the journal nor its approval creates another transfer.
        Assert.Equal("100", owner.Value.Transfer.Preparation.SourceMoney.Amount);
        Assert.Equal("3400", owner.Value.Transfer.Exchange!.SourceReferenceTl!.Amount);
    }

    [SqlServerFact]
    public async Task Exchange_resolution_refuses_missing_mapping_changed_owner_proof_and_authority_context_loss_after_await()
    {
        await using var run = await Run.Start(); var (source, value) = ExchangeSource();
        await using (var accept = run.Open()) await accept.Store.AcceptAsync(ExchangeEnvelope(source, value), default);
        foreach (var change in new[] { "mapping", "amount", "target-amount", "denied", "company", "actor", "membership", "second-approved" })
        {
            await using var scope = run.Open(); var user = new ExchangeUser(); var owner = new ExchangeOwner(source, value);
            if (change == "amount") owner.Value = owner.Value with { Proposals = [value with { SourceBookValueTl = "3200" }] };
            if (change == "target-amount") owner.Value = owner.Value with { Proposals = [value with { TargetFunctionalValueTl = "3330" }] };
            if (change == "second-approved") owner.Value = owner.Value with { Proposals = [value, value with { Id = Guid.NewGuid() }] };
            if (change == "denied") owner.Outcome = ReadOutcome.Denied;
            if (change == "company") owner.DuringRead = () => scope.Company.Set(TestCompanyIds.CompanyB);
            if (change == "actor") owner.DuringRead = () => user.Actor = Guid.NewGuid();
            if (change == "membership") owner.DuringRead = () => user.Member = false;
            var provider = ExchangeProvider(scope, user, owner, change == "mapping" ? null : ExchangeBinding(source, value));
            Assert.Null(await provider.ResolveAsync(source.CompanyId, value.Id, default));
        }
        Assert.Equal(0, await run.Count("accounting.Journals"));
    }

    [SqlServerFact]
    public async Task Exchange_source_Down_passes_with_only_old_sources_and_refuses_before_raw_or_approved_exchange_evidence_is_lost()
    {
        var guard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations.AllowCashExchangeJournalSources().DownOperations[0]);
        foreach (var approved in new[] { false, true })
        {
            await using var run = await Run.Start();
            Assert.Equal(1, await run.Scalar<int>(guard.Sql + "\nSELECT 1;"));
            await using (var old = run.Open()) await old.Store.AcceptAsync(Envelope(Source()), default);
            Assert.Equal(1, await run.Scalar<int>(guard.Sql + "\nSELECT 1;"));
            var (source, value) = ExchangeSource();
            await using (var exchange = run.Open())
                await exchange.Store.AcceptAsync(ExchangeEnvelope(source, approved ? value : null), default);
            Assert.Equal(51810, (await Assert.ThrowsAsync<SqlException>(() => run.Scalar<int>(guard.Sql + "\nSELECT 1;"))).Number);
            Assert.Equal(2, await run.Count("accounting.FinanceJournalSources"));
            Assert.Equal(0, await run.Count("accounting.Journals"));
        }
    }

    private static FinanceCashExchangeJournalSourceProviderV1 ExchangeProvider(Scope scope, ExchangeUser user, ExchangeOwner owner,
        ApprovedNormalJournalSourceBindingV1? binding)
    {
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-exchange", null, "API", null, null);
        return new(scope.Db, scope.Company, user, correlation, owner, new ApprovedNormalJournalSourceBindingCatalogV1(binding is null ? [] : [binding]));
    }
    private static AccountingCommandMetadata ExchangeMetadata(Guid actor) => new(actor.ToString("D"), "SENTETIK-accounting", [],
        "SENTETIK-exchange", null, Guid.NewGuid().ToString("D"), "API");
    [SqlServerFact]
    public async Task Foreign_target_uses_both_documented_tl_values_and_preserves_legacy_missing_target_as_unavailable()
    {
        await using var run = await Run.Start();
        var (source, legacy) = ExchangeSource(SettingsCurrencyIdentitiesV1.Euro);
        var value = legacy with { TargetFunctionalValueTl = "3330" };
        await using (var accept = run.Open()) await accept.Store.AcceptAsync(ExchangeEnvelope(source, value), default);
        await using var scope = run.Open(); var user = new ExchangeUser(); var owner = new ExchangeOwner(source, value);
        var actual = Assert.IsType<NormalJournalDraftServerSourceV1>(await ExchangeProvider(scope, user, owner,
            ExchangeBinding(source, value)).ResolveAsync(source.CompanyId, value.Id, default));
        Assert.Equal("3330", actual.ExactSourceAmount);
        Assert.Collection(actual.ExactSourceLines!, x => Assert.Equal("3330", x.Amount),
            x => Assert.Equal("3100", x.Amount), x => Assert.Equal("230", x.Amount));
        var posting = new AccountingStore(scope.Db, scope.Company, scope.Audit, TimeProvider.System,
            new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(source.CompanyId)));
        var periodId = Guid.NewGuid(); var checker = Guid.NewGuid();
        await posting.ProposePeriodAsync(periodId, new(2026, 11, 1), new(2026, 11, 30), user.Actor, ExchangeMetadata(user.Actor));
        await posting.ApprovePeriodAsync(periodId, 1, checker, ExchangeMetadata(checker));
        var lines = actual.ExactSourceLines!.Select(line => new ProposedLine(line.MappingKey,
            actual.Rule.Mappings.Single(x => x.MappingKey == line.MappingKey && x.Side.ToString() == line.Side).Account,
            Enum.Parse<PostingSide>(line.Side), decimal.Parse(line.Amount, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var draftId = Guid.NewGuid();
        var request = new PostingRequest("SENTETIK-exchange", draftId.ToString("D"), draftId,
            new(source.CompanyId, value.Id, actual.Rule.PostingPurpose), actual.Rule.Version, actual.CurrencyId,
            new(new(2026, 11, 17), new(2026, 11, 17), null, At), lines);
        var draft = NormalJournalDraftV1.Prepare(JournalKind.Original, request, 1, user.Actor, actual.Rule.Mirror, actual.Rule).Draft!;
        await posting.CreateNormalDraftAsync(draft, _ => Task.CompletedTask, ExchangeMetadata(user.Actor));
        await posting.ApproveAndPostNormalDraftAsync(draftId, 1, periodId, checker, _ => Task.CompletedTask, ExchangeMetadata(checker));
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal("90", owner.Value.Transfer.Preparation.TargetMoney.Amount);
        Assert.Equal("3240", owner.Value.Transfer.Exchange!.TargetReferenceTl!.Amount);
        var (older, oldValue) = ExchangeSource(SettingsCurrencyIdentitiesV1.Euro);
        await scope.Store.AcceptAsync(ExchangeEnvelope(older, oldValue), default);
        Assert.Null(await ExchangeProvider(scope, user, new(older, oldValue), ExchangeBinding(older, oldValue))
            .ResolveAsync(older.CompanyId, oldValue.Id, default));
        Assert.Equal(1, await run.Count("accounting.Journals"));
    }

    private static (CashTransferView, CashExchangeBookValueViewV1) ExchangeSource(Guid? targetCurrency = null)
    {
        var company = TestCompanyIds.CompanyA; var day = new DateOnly(2026, 11, 17); var usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
        var target = targetCurrency ?? SettingsCurrencyIdentitiesV1.TurkishLira;
        var preparation = new CashTransferPreparation(CashTransferKind.CurrencyExchange, CashTransferAccountKind.Bank, Guid.NewGuid(),
            new("100", usd), new("SENTETIK-usd", "1"), CashTransferAccountKind.Bank, Guid.NewGuid(),
            new(target == SettingsCurrencyIdentitiesV1.TurkishLira ? "3300" : "90", target), new("SENTETIK-target", "1"), null, day, null, null, "SENTETIK exchange");
        var exchange = CashTransferExchangeCalculatorV1.Calculate(company, day, preparation.SourceMoney, preparation.TargetMoney,
            preparation.SourceProfile, preparation.TargetProfile, new(usd, "34", day, "TCMB", Guid.NewGuid(), "1", day.AddDays(-1), "SENTETIK / NOT_ENV"),
            target == SettingsCurrencyIdentitiesV1.TurkishLira
                ? new(target, "1", day, "FunctionalCurrency", null, null, null, null)
                : new(target, "36", day, "TCMB", Guid.NewGuid(), "1", day.AddDays(-1), "SENTETIK / NOT_ENV"));
        var source = new CashTransferView(Guid.NewGuid(), company, preparation, Guid.NewGuid().ToString("D"), At, true, "1", exchange);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source, FinanceJson.CreateOptions())))).ToLowerInvariant();
        return (source, new(Guid.NewGuid(), company, source.Id, "1", hash, "3100", new(Guid.NewGuid(), 1, new string('a', 64)),
            "SENTETIK actual carrying proof", Guid.NewGuid().ToString("D"), At.AddMinutes(1), CashExchangeBookValueStatusV1.Approved, "2",
            Guid.NewGuid().ToString("D"), At.AddMinutes(2), null));
    }
    private static WorkInboxEventV1 ExchangeEnvelope(CashTransferView source, CashExchangeBookValueViewV1? value = null)
    {
        var id = Guid.NewGuid();
        return new(FinanceJournalSourceInboxV1.SourceName, id, source.CompanyId,
            value is null ? NormalJournalSourceKindsV1.RecordedCashExchange : NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue, 1,
            value is null ? JsonSerializer.Serialize(new FinanceCashExchangeRecordedV1(id, source), FinanceJson.CreateOptions())
                : JsonSerializer.Serialize(new FinanceCashExchangeBookValueApprovedV1(id, source, value), FinanceJson.CreateOptions()),
            value?.DecidedAtUtc ?? At, "SENTETIK-exchange", null);
    }
    private static ApprovedNormalJournalSourceBindingV1 ExchangeBinding(CashTransferView source, CashExchangeBookValueViewV1 value)
    {
        var accounts = Enumerable.Range(1, 3).Select(_ => new AccountRef(source.CompanyId, Guid.NewGuid())).ToArray(); var actor = Guid.NewGuid();
        var mirror = new AccountPlanMirrorV1(source.CompanyId, "SENTETIK-exchange-plan", new string('a', 64), actor, At,
            accounts.Select((account, index) => new AccountPlanMirrorRowV1(account, "SENTETIK-" + index, "SENTETIK account")).ToArray());
        var rule = new NormalJournalPostingRuleV1(mirror, CashExchangeJournalAmountsV1.PostingPurpose, "1", actor, At,
            [new(CashExchangeJournalAmountsV1.TargetReceipt, PostingSide.Debit, accounts[0]),
             new(CashExchangeJournalAmountsV1.SourceCarrying, PostingSide.Credit, accounts[1]),
             new(CashExchangeJournalAmountsV1.RealizedGain, PostingSide.Credit, accounts[2])]);
        return new(source.CompanyId, value.Id, NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue, value.Version,
            source.Preparation.SourceProfile.ProfileId, source.Preparation.SourceProfile.Version, SettingsCurrencyIdentitiesV1.TurkishLira, rule);
    }
    private sealed class ExchangeUser : ICurrentUser
    {
        public Guid Actor { get; set; } = Guid.NewGuid(); public bool Member { get; set; } = true;
        public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA };
        public bool CanAccessCompany(Guid companyId) => Member && companyId == TestCompanyIds.CompanyA;
    }
    // The Finance owner and approved package in these Accounting-focused cases are explicit synthetic seams.
    // Real Finance evidence/approval/outbox production has its own owner SQL acceptance source.
    private sealed class ExchangeOwner(CashTransferView source, CashExchangeBookValueViewV1 value) : ICashExchangeBookValueServiceV1
    {
        public CashExchangeBookValueWorkspaceV1 Value { get; set; } = new(source.CompanyId, source, [value]);
        public ReadOutcome Outcome { get; set; } = ReadOutcome.Found; public Action? DuringRead { get; set; }
        public Task<ReadResult<CashExchangeBookValueWorkspaceV1>> GetAsync(FinanceContext context, Guid id, CancellationToken ct)
        { DuringRead?.Invoke(); return Task.FromResult(new ReadResult<CashExchangeBookValueWorkspaceV1>(Outcome, Outcome == ReadOutcome.Found ? Value : null)); }
        public Task<ReadResult<CashExchangeBookValueCandidatePageV1>> ListAsync(FinanceContext context, Guid? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<CashExchangeBookValueCommandResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata, ProposeCashExchangeBookValueV1 request, CancellationToken ct) => throw new NotSupportedException();
        public Task<CashExchangeBookValueCommandResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata, DecideCashExchangeBookValueV1 request, CancellationToken ct) => throw new NotSupportedException();
    }
}
