using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCollateralSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task An_explicit_bank_is_frozen_on_each_revision_replay_keeps_the_original_proof_and_release_needs_no_current_bank()
    {
        await using var run = await Run.StartAsync();
        var bank = new K6Policies(CompanyA);
        PartyCollateralRecordView recorded;
        await using (var scope = run.OpenWithPolicies(CompanyA, Recorder, bank))
        {
            var request = Record("1000") with { BankInstitutionId = bank.Id, BankSourceVersion = bank.Source };
            var first = await scope.Collaterals.RecordAsync(scope.Context, Key("k6-record"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome); recorded = first.Resource!;
            Assert.Equal(bank.Proof, recorded.BankInstitution);
            var account = (await scope.Collaterals.GetAccountAsync(scope.Context, Account, default)).Resource!;
            Assert.Equal("400", Assert.Single(account.WeightedCollateral.Amounts).Amount);
            var company = new CompanyContext(); company.Set(CompanyA);
            var risk = await new FinanceSalesPartyRiskReadPortV1(scope.Database, company, new SyntheticCatalog(true), TimeProvider.System, bank)
                .ReadPartyRiskAsync(Party, Account);
            Assert.Equal(400m, Assert.Single(risk.Collateral.Amounts).Amount);
            var panel = await new FinancePartyCreditPanelService(scope.Database, company, new FixedCurrentUser(Recorder, CompanyA),
                new SyntheticPermissions(), new SyntheticParty(), new SyntheticCatalog(true), TimeProvider.System, bank)
                .GetAsync(scope.Context, Party, Account, default);
            Assert.Equal("400", Assert.Single(panel.Resource!.WeightedCollateral.Amounts).Amount);
            var rateProof = Assert.Single(risk.Collateral.PolicyProofs!);
            Assert.Equal((recorded.CollateralId, "1", bank.Id, bank.Source, "40"),
                (rateProof.CollateralId, rateProof.CollateralRevision, rateProof.BankInstitutionId, rateProof.BankSourceVersion, rateProof.RatePercent));
            bank.Available = false;
            var replay = await scope.Collaterals.RecordAsync(scope.Context, Key("k6-record"), request, default);
            Assert.True(replay.IsReplay); Assert.Equal(recorded.BankInstitution, replay.Resource!.BankInstitution);
            var released = await scope.Collaterals.ReleaseAsync(scope.Context, Key("k6-release"), new(recorded.CollateralId, "1", Reason), default);
            Assert.Equal(CommandOutcome.Succeeded, released.Outcome); Assert.Equal(recorded.BankInstitution, released.Resource!.BankInstitution);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals WHERE CollateralId=@id AND BankProofJson IS NOT NULL", ("@id", recorded.CollateralId)));
        Assert.Equal(bank.Source, await run.ScalarAsync<string>("SELECT JSON_VALUE(AfterJson,'$.bankInstitution.sourceVersion') FROM audit.AuditEvents WHERE EntityId=@id", ("@id", recorded.Id.ToString())));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task A_bank_source_changing_before_the_save_or_belonging_to_another_company_saves_no_collateral_attempt_or_audit()
    {
        await using var run = await Run.StartAsync();
        var bank = new K6Policies(CompanyA) { ChangeAfterFirstRead = true };
        await using (var scope = run.OpenWithPolicies(CompanyA, Recorder, bank))
        {
            var result = await scope.Collaterals.RecordAsync(scope.Context, Key("k6-stale"),
                Record("1") with { BankInstitutionId = bank.Id, BankSourceVersion = bank.Source }, default);
            Assert.NotEqual(CommandOutcome.Succeeded, result.Outcome);
        }
        var foreign = new K6Policies(CompanyB);
        await using (var scope = run.OpenWithPolicies(CompanyA, Recorder, foreign))
        {
            var result = await scope.Collaterals.RecordAsync(scope.Context, Key("k6-foreign"),
                Record("1") with { BankInstitutionId = foreign.Id, BankSourceVersion = foreign.Source }, default);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollaterals"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCollateralCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'finance.collateral.%'"));
    }

    // This fixture isolates the external read owner. Settings' real publish/remaining-day queries are exercised by
    // SettingsCollateralPolicySqlServerRelationalAcceptanceTests; these facts prove Finance's actual SQL/audit/replay.
    private sealed class K6Policies(Guid company) : IFinanceCollateralPolicyReaderV1
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Source { get; } = "settings.bank-institutions:synthetic:1";
        public bool Available { get; set; } = true;
        public bool ChangeAfterFirstRead { get; init; }
        private int reads;
        public FinanceCollateralBankProofV1 Proof => new(company, Id, "Synthetic bank", "0007", Source);
        public Task<FinanceCollateralBankCatalogV1> GetBankInstitutionsAsync(Guid companyId, DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            var proof = ChangeAfterFirstRead && ++reads > 1 ? Proof with { SourceVersion = Source + ":changed" } : Proof;
            return Task.FromResult(new FinanceCollateralBankCatalogV1(Available, company, Available ? [proof] : []));
        }
        public Task<FinanceCollateralRateReadV1> GetRateAsync(Guid companyId, Guid collateralTypeId, Guid? bankInstitutionId,
            string? bankSourceVersion, DateOnly? validUntil, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FinanceCollateralRateReadV1(Available, Available ? "40" : null,
                Available ? "settings.collateral-rate:synthetic:1" : null, Available ? "TypeBank" : null, Available ? null : "unavailable", Available ? "1" : null));
    }
}
