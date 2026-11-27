using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Theory]
    [InlineData(CreditAccountGroup.Cash)]
    [InlineData(CreditAccountGroup.NonCash)]
    [InlineData(CreditAccountGroup.InstrumentAndFactoring)]
    [InlineData(CreditAccountGroup.Leasing)]
    public async Task Approved_group_source_fields_survive_native_store_approval_read_and_replay_without_financial_events(CreditAccountGroup group)
    {
        await using var run = new Run();
        var input = ProfileFieldsRequest(group);
        var json = JsonSerializer.Serialize(input.Profile, FinanceJson.CreateOptions());
        var first = await run.ProposeAsync(Maker, input, key: "SENTETIK-fields", correlation: "SENTETIK-fields-original");
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.Equal(json, (await run.RowAsync(first.Resource!.Id)).SourceProfileJson);
        Assert.Equal(CommandOutcome.Succeeded, (await run.ApproveAsync(Checker, new(first.Resource.Id, "1"))).Outcome);
        var read = await run.GetAsync(Checker, first.Resource.Id);
        Assert.Equal(json, JsonSerializer.Serialize(read.Resource!.Profile, FinanceJson.CreateOptions()));
        var replay = await run.ProposeAsync(Maker, input, key: "SENTETIK-fields", correlation: "SENTETIK-other");
        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-fields-original"),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Equal(json, JsonSerializer.Serialize(replay.Resource!.Profile, FinanceJson.CreateOptions()));
        Assert.Equal(json, (await run.RowAsync(first.Resource.Id)).SourceProfileJson);
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(Checker, first.Resource.Id, company: CompanyB)).Outcome);
        await using var db = run.Memory(CompanyA);
        Assert.Empty(await db.CreditMovements.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData("index-missing", "profile.cash.indexCurrencyId")]
    [InlineData("index-empty", "profile.cash.indexCurrencyId")]
    [InlineData("export-blank", "profile.cash.exportCommitmentReference")]
    [InlineData("risk-missing", "profile.nonCash.riskAmount")]
    [InlineData("risk-negative", "profile.nonCash.riskAmount")]
    [InlineData("risk-invalid", "profile.nonCash.riskAmount")]
    [InlineData("commission-negative", "profile.nonCash.commission")]
    [InlineData("commission-date", "profile.nonCash.commission")]
    [InlineData("commission-document", "profile.nonCash.commission")]
    [InlineData("commission-period", "profile.nonCash.commission")]
    [InlineData("related-reference", "profile.nonCash.relatedCommercialDocumentReference")]
    [InlineData("return-date", "profile.nonCash.returnEvidence")]
    [InlineData("return-document", "profile.nonCash.returnEvidence")]
    [InlineData("instrument-maturity-missing", "profile.instrumentAndFactoring.maturityDate")]
    [InlineData("instrument-maturity-before-start", "profile.instrumentAndFactoring.maturityDate")]
    [InlineData("deductions-missing", "profile.instrumentAndFactoring.deductions")]
    [InlineData("deductions-negative", "profile.instrumentAndFactoring.deductions")]
    [InlineData("deductions-invalid", "profile.instrumentAndFactoring.deductions")]
    [InlineData("contract-date-missing", "profile.leasing.contractDate")]
    [InlineData("contract-date-default", "profile.leasing.contractDate")]
    [InlineData("purchase-option-negative", "profile.leasing.purchaseOptionAmount")]
    [InlineData("lease-without-plan", "profile.planNature")]
    public async Task Missing_or_malformed_documented_profile_fields_are_refused_without_source_backfill(string fault, string field)
    {
        await using var run = new Run();
        var input = ProfileFieldFault(fault);
        AssertIssue(await run.ProposeAsync(Maker, input), CommandOutcome.ValidationFailed, field, "credit_source_profile_invalid");
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task Index_currency_uses_the_existing_authorized_settings_candidates_instead_of_accepting_an_arbitrary_id()
    {
        await using var run = new Run();
        var input = ProfileFieldsRequest(CreditAccountGroup.Cash);
        input = input with { Profile = input.Profile! with { Cash = input.Profile.Cash! with { IndexCurrencyId = OtherCurrency } } };
        AssertIssue(await run.ProposeAsync(Maker, input), CommandOutcome.ValidationFailed,
            "profile.cash.indexCurrencyId", "currency_not_a_candidate");
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Theory]
    [InlineData(CreditAccountGroup.Cash, "{\"kind\":\"Installment\",\"maturityDate\":\"2027-11-15\"}")]
    [InlineData(CreditAccountGroup.NonCash, "{\"kind\":\"Guarantee\",\"beneficiaryReference\":\"SENTETIK\",\"isIndefinite\":true,\"expiryDate\":null}")]
    [InlineData(CreditAccountGroup.InstrumentAndFactoring, "{\"kind\":\"Factoring\",\"withRecourse\":true,\"instrumentIds\":[],\"receivableDocumentReferences\":[\"SENTETIK\"]}")]
    [InlineData(CreditAccountGroup.Leasing, "{\"assetReference\":\"SENTETIK\",\"leaseStartDate\":\"2026-11-15\",\"leaseEndDate\":\"2027-11-15\"}")]
    public void Legacy_v1_group_JSON_round_trips_without_emitting_new_null_fields(CreditAccountGroup group, string legacyJson)
    {
        var options = FinanceJson.CreateOptions();
        var type = group switch
        {
            CreditAccountGroup.Cash => typeof(CreditCashProfileV1),
            CreditAccountGroup.NonCash => typeof(CreditNonCashProfileV1),
            CreditAccountGroup.InstrumentAndFactoring => typeof(CreditInstrumentProfileV1),
            _ => typeof(CreditLeasingProfileV1)
        };
        var decoded = JsonSerializer.Deserialize(legacyJson, type, options);
        Assert.NotNull(decoded);
        Assert.Equal(legacyJson, JsonSerializer.Serialize(decoded, type, options));
        using var original = JsonDocument.Parse(legacyJson);
        Assert.Equal(FinanceCreditAccountService.PayloadHash("SENTETIK-legacy-profile", original.RootElement),
            FinanceCreditAccountService.PayloadHash("SENTETIK-legacy-profile", decoded));
    }

    [Fact]
    public async Task Legacy_required_field_absence_preserves_read_and_receipt_replay_but_refuses_new_approval_without_rewriting_source()
    {
        await using var run = new Run();
        var profile = Profile(CreditAccountGroup.NonCash) with { NonCash = new(CreditNonCashKindV1.Guarantee, "SENTETIK", true, null) };
        var input = Request() with { Group = CreditAccountGroup.NonCash, Profile = profile, Installments = [] };
        var sourceJson = JsonSerializer.Serialize(profile, FinanceJson.CreateOptions());
        var row = Account(FinanceCreditAccountService.Proposed);
        row.CreditGroup = FinanceCreditAccountService.NonCashGroup;
        row.SourceProfileJson = sourceJson;
        await using (var db = run.Memory(CompanyA))
        {
            db.CreditAccounts.Add(row);
            await db.SaveChangesAsync();
        }
        var view = (await run.GetAsync(Maker, row.Id)).Resource!;
        Assert.Null(view.Profile!.NonCash!.RiskAmount);
        Assert.Null(view.PlanTotals);
        const string key = "SENTETIK-old-v1-receipt";
        await using (var db = run.Memory(CompanyA))
        {
            // Existing receipt fixture only: a new proposal with these missing fields is refused below.
            db.CreditAccountCommands.Add(new CreditAccountCommandRow
            {
                CompanyId = CompanyA, CommandName = FinanceCreditAccountService.ProposeCommand,
                IdempotencyKeyHash = FinanceCommandKeys.Hash(key), ActorId = Maker, CreditAccountId = row.Id,
                PayloadHash = FinanceCreditAccountService.PayloadHash(FinanceCreditAccountService.ProposeCommand, input),
                ResultJson = JsonSerializer.Serialize(new CreditAccountCommandResult(CommandOutcome.Succeeded, view, false,
                    "SENTETIK-old-correlation", []), FinanceJson.CreateOptions()),
                CorrelationId = "SENTETIK-old-correlation", RecordedAtUtc = At
            });
            await db.SaveChangesAsync();
        }
        var replay = await run.ProposeAsync(Maker, input, key: key);
        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-old-correlation"),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId));
        AssertIssue(await run.ProposeAsync(Maker, input), CommandOutcome.ValidationFailed,
            "profile.nonCash.riskAmount", "credit_source_profile_invalid");
        AssertIssue(await run.ApproveAsync(Checker, new(row.Id, "1")), CommandOutcome.ValidationFailed,
            "profile.nonCash.riskAmount", "credit_source_profile_invalid");
        var stored = await run.RowAsync(row.Id);
        Assert.Equal(sourceJson, stored.SourceProfileJson);
        Assert.Equal((FinanceCreditAccountService.Proposed, 1L), (stored.Status, stored.Version));
        Assert.Single(await run.CommandsAsync());
        Assert.Empty(await run.AuditsAsync(row.Id));
    }

    private static CreditAccountProposeRequest ProfileFieldsRequest(CreditAccountGroup group)
    {
        var profile = Profile(group);
        profile = group switch
        {
            CreditAccountGroup.Cash => profile with { Cash = new(CreditCashKindV1.ForeignCurrencyIndexed, Start.AddYears(1),
                IndexCurrencyId: Currency, ExportCommitmentReference: "SENTETIK-export") },
            CreditAccountGroup.NonCash => profile with { NonCash = profile.NonCash! with
            {
                RiskAmount = "9999.00001", Commission = new("12.34567", Start, new("SENTETIK-commission", Start, "R1"), "SENTETIK-period"),
                RelatedCommercialDocumentReference = "SENTETIK-order", ReturnEvidence = new(Start.AddMonths(1), new("SENTETIK-return", Start.AddMonths(1), "R2"))
            } },
            CreditAccountGroup.InstrumentAndFactoring => profile with { InstrumentAndFactoring = profile.InstrumentAndFactoring! with
                { Deductions = new("1.23456", "0", "0.00001") } },
            _ => profile with { Leasing = profile.Leasing! with { PurchaseOptionAmount = "123.456789" } }
        };
        return Request() with { Group = group, Profile = profile, Installments = PlanFor(group) };
    }

    private static CreditAccountProposeRequest ProfileFieldFault(string fault)
    {
        var group = fault.StartsWith("index", StringComparison.Ordinal) || fault.StartsWith("export", StringComparison.Ordinal)
            ? CreditAccountGroup.Cash : fault.StartsWith("instrument", StringComparison.Ordinal) || fault.StartsWith("deductions", StringComparison.Ordinal)
                ? CreditAccountGroup.InstrumentAndFactoring : fault.StartsWith("contract", StringComparison.Ordinal)
                    || fault.StartsWith("purchase", StringComparison.Ordinal) || fault.StartsWith("lease", StringComparison.Ordinal)
                    ? CreditAccountGroup.Leasing : CreditAccountGroup.NonCash;
        var input = ProfileFieldsRequest(group);
        var profile = input.Profile!;
        profile = fault switch
        {
            "index-missing" => profile with { Cash = profile.Cash! with { IndexCurrencyId = null } },
            "index-empty" => profile with { Cash = profile.Cash! with { IndexCurrencyId = Guid.Empty } },
            "export-blank" => profile with { Cash = profile.Cash! with { ExportCommitmentReference = " " } },
            "risk-missing" => profile with { NonCash = profile.NonCash! with { RiskAmount = null } },
            "risk-negative" => profile with { NonCash = profile.NonCash! with { RiskAmount = "-1" } },
            "risk-invalid" => profile with { NonCash = profile.NonCash! with { RiskAmount = "1,25" } },
            "commission-negative" => profile with { NonCash = profile.NonCash! with { Commission = profile.NonCash.Commission! with { Amount = "-1" } } },
            "commission-date" => profile with { NonCash = profile.NonCash! with { Commission = profile.NonCash.Commission! with { CommissionDate = default } } },
            "commission-document" => profile with { NonCash = profile.NonCash! with { Commission = profile.NonCash.Commission! with { SourceDocument = new(" ", Start, "1") } } },
            "commission-period" => profile with { NonCash = profile.NonCash! with { Commission = profile.NonCash.Commission! with { Period = " " } } },
            "related-reference" => profile with { NonCash = profile.NonCash! with { RelatedCommercialDocumentReference = " " } },
            "return-date" => profile with { NonCash = profile.NonCash! with { ReturnEvidence = profile.NonCash.ReturnEvidence! with { ReturnedOn = default } } },
            "return-document" => profile with { NonCash = profile.NonCash! with { ReturnEvidence = profile.NonCash.ReturnEvidence! with { SourceDocument = new("SENTETIK", default, "1") } } },
            "instrument-maturity-missing" => profile with { InstrumentAndFactoring = profile.InstrumentAndFactoring! with { MaturityDate = null } },
            "instrument-maturity-before-start" => profile with { InstrumentAndFactoring = profile.InstrumentAndFactoring! with { MaturityDate = Start.AddDays(-1) } },
            "deductions-missing" => profile with { InstrumentAndFactoring = profile.InstrumentAndFactoring! with { Deductions = null } },
            "deductions-negative" => profile with { InstrumentAndFactoring = profile.InstrumentAndFactoring! with { Deductions = new("-1", "0", "0") } },
            "deductions-invalid" => profile with { InstrumentAndFactoring = profile.InstrumentAndFactoring! with { Deductions = new("0", "1,00", "0") } },
            "contract-date-missing" => profile with { Leasing = profile.Leasing! with { ContractDate = null } },
            "contract-date-default" => profile with { Leasing = profile.Leasing! with { ContractDate = default(DateOnly) } },
            "purchase-option-negative" => profile with { Leasing = profile.Leasing! with { PurchaseOptionAmount = "-1" } },
            _ => profile with { PlanNature = CreditPlanNatureV1.SourceMovements }
        };
        return input with { Profile = profile, Installments = fault == "lease-without-plan" ? [] : input.Installments };
    }
}
