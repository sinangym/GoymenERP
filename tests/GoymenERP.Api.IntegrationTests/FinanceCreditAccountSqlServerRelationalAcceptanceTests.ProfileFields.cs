using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Approved_group_profile_metadata_remains_exact_through_native_SQL_approval_restart_and_replay_without_posting_money()
    {
        await using var run = await Run.StartAsync();
        foreach (var group in Enum.GetValues<CreditAccountGroup>())
        {
            var input = GroupFieldsSqlRequest(group);
            var json = JsonSerializer.Serialize(input.Profile, FinanceJson.CreateOptions());
            var key = $"SENTETIK-profile-fields-{group}";
            CreditAccountView proposed;
            await using (var maker = run.Open(CompanyA, Maker))
            {
                var result = await maker.Credits.ProposeAsync(maker.Context, Key(key), input, default);
                Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                proposed = result.Resource!;
            }
            await using (var checker = run.Open(CompanyA, Checker))
            {
                var approved = await checker.Credits.ApproveAsync(checker.Context, Key(key + "-approve"), new(proposed.Id, "1"), default);
                Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
                Assert.Equal(json, JsonSerializer.Serialize(approved.Resource!.Profile, FinanceJson.CreateOptions()));
            }
            await using (var restarted = run.Open(CompanyA, Maker))
            {
                var replay = await restarted.Credits.ProposeAsync(restarted.Context, Key(key), input, default);
                Assert.True(replay.IsReplay);
                Assert.Equal(proposed.Id, replay.Resource!.Id);
                Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
                Assert.Equal(json, JsonSerializer.Serialize(replay.Resource.Profile, FinanceJson.CreateOptions()));
            }
            await using (var foreign = run.Open(CompanyB, Checker))
            {
                Assert.Equal(ReadOutcome.NotFound, (await foreign.Credits.GetAsync(foreign.Context, proposed.Id, default)).Outcome);
            }
            Assert.Equal(json, await run.ScalarAsync<string>(
                "SELECT SourceProfileJson FROM finance.CreditAccounts WHERE CompanyId = @company AND Id = @id",
                ("@company", CompanyA), ("@id", proposed.Id)));
            Assert.Equal(0, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.CreditMovements WHERE CompanyId = @company AND CreditAccountId = @id",
                ("@company", CompanyA), ("@id", proposed.Id)));
        }
    }

    private static CreditAccountProposeRequest GroupFieldsSqlRequest(CreditAccountGroup group)
    {
        var input = Request();
        var date = input.StartDate;
        var profile = input.Profile! with
        {
            Cash = null,
            PlanNature = group == CreditAccountGroup.NonCash ? CreditPlanNatureV1.SourceMovements : CreditPlanNatureV1.Installments
        };
        profile = group switch
        {
            CreditAccountGroup.Cash => profile with { Cash = new(CreditCashKindV1.ForeignCurrencyIndexed, date.AddYears(1),
                IndexCurrencyId: Currency, ExportCommitmentReference: "SENTETIK-export") },
            CreditAccountGroup.NonCash => profile with { NonCash = new(CreditNonCashKindV1.Guarantee, "SENTETIK-beneficiary", true, null,
                RiskAmount: "12000.00001", Commission: new("12.34567", date, new("SENTETIK-commission", date, "1"), "SENTETIK-period"),
                RelatedCommercialDocumentReference: "SENTETIK-commercial", ReturnEvidence: new(date.AddDays(1), new("SENTETIK-return", date.AddDays(1), "1"))) },
            CreditAccountGroup.InstrumentAndFactoring => profile with { InstrumentAndFactoring = new(CreditInstrumentKindV1.Factoring,
                true, [], ["SENTETIK-receivable"], MaturityDate: date.AddMonths(2), Deductions: new("1.23456", "0", "0.00001")) },
            _ => profile with { Leasing = new("SENTETIK-asset", date, date.AddYears(2), ContractDate: date,
                PurchaseOptionAmount: "123.456789") }
        };
        IReadOnlyList<CreditPlanInstallmentInput> plan = group switch
        {
            CreditAccountGroup.NonCash => [],
            CreditAccountGroup.Leasing => [new(1, date.AddMonths(1), "1150.000", "1000.00", "50.000", "0", "0", Vat: "100.000")],
            _ => input.Installments
        };
        return input with { Group = group, ContractReference = $"SENTETIK-fields-{group}", Profile = profile, Installments = plan };
    }
}
