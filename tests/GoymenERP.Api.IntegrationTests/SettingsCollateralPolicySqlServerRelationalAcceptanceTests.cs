using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCollateralTypeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Bank_catalog_and_K6_rates_use_real_published_identity_proofs_inclusive_remaining_days_and_existing_type_precedence()
    {
        await using var run = await HostRun.StartAsync(); await GrantFinancePolicyAuthorAsync(run);
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        using var editor = run.Client(TestUsers.CompanyA, CompanyA);
        await run.CommandAsync(editor, Root, "k6-type", new CreateSettingsCollateralTypeRequestV1("K6", "Synthetic K6 type", SettingsCollateralKindV1.GuaranteeLetter, "Synthetic"));
        var first = (await editor.GetFromJsonAsync<SettingsBankInstitutionsWorkspaceV1>(FinancePolicyRoot + "/bank-institutions"))!;
        Assert.Empty(first.Versions);
        var bankRequest = new PrepareSettingsBankInstitutionsV1("0", first.BusinessDate, null, [new(null, "Synthetic bank", "0007", true)], "Synthetic explicit bank");
        await run.CommandAsync(editor, FinancePolicyRoot + "/bank-institutions", "k6-bank-draft", bankRequest);
        var bankDraft = Assert.Single((await editor.GetFromJsonAsync<SettingsBankInstitutionsWorkspaceV1>(FinancePolicyRoot + "/bank-institutions"))!.Versions);
        var draftsOnly = (await editor.GetFromJsonAsync<SettingsCollateralRatesWorkspaceV1>(FinancePolicyRoot + "/collateral-rates"))!;
        Assert.Equal(SettingsFinancePolicyReadOutcomeV1.Unavailable, draftsOnly.Banks.Outcome); Assert.Empty(draftsOnly.Banks.Entries);
        await run.CommandAsync(editor, $"{FinancePolicyRoot}/bank-institutions/{bankDraft.PolicyId}/publish", "k6-bank-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic publish bank"));
        var source = (await editor.GetFromJsonAsync<SettingsCollateralRatesWorkspaceV1>(FinancePolicyRoot + "/collateral-rates"))!;
        var bank = Assert.Single(source.Banks.Entries); var type = source.Types.Entries.Single(x => x.Code == "K6");
        Assert.Equal("0007", bank.EftCode); Assert.Equal("100", type.RatePercent);
        var basic = new PrepareSettingsCollateralRateV1("0", SettingsCollateralRateScopeV1.TypeBank, type.Id, type.Version,
            bank.BankInstitutionId, bank.SourceVersion, null, "80", first.BusinessDate, null, "Synthetic type bank rate");
        await run.CommandAsync(editor, FinancePolicyRoot + "/collateral-rates", "k6-bank-rate", basic);
        var basicDraft = Assert.Single((await editor.GetFromJsonAsync<SettingsCollateralRatesWorkspaceV1>(FinancePolicyRoot + "/collateral-rates"))!.Versions);
        await run.CommandAsync(editor, $"{FinancePolicyRoot}/collateral-rates/{basicDraft.PolicyId}/publish", "k6-bank-rate-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic publish rate"));
        var special = basic with { Scope = SettingsCollateralRateScopeV1.TypeBankRemainingDays, MaximumRemainingDays = 30, RatePercent = "40" };
        await run.CommandAsync(editor, FinancePolicyRoot + "/collateral-rates", "k6-days-rate", special);
        var specialDraft = (await editor.GetFromJsonAsync<SettingsCollateralRatesWorkspaceV1>(FinancePolicyRoot + "/collateral-rates"))!.Versions.Single(x => x.Scope == SettingsCollateralRateScopeV1.TypeBankRemainingDays);
        await run.CommandAsync(editor, $"{FinancePolicyRoot}/collateral-rates/{specialDraft.PolicyId}/publish", "k6-days-rate-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic publish days rate"));
        foreach (var (remaining, expected, expectedScope) in new[] { (20, "40", "TypeBankRemainingDays"), (30, "40", "TypeBankRemainingDays"), (31, "80", "TypeBank") })
        {
            var result = await ReadK6PreviewAsync(editor, new(type.Id, bank.BankInstitutionId, bank.SourceVersion, first.BusinessDate.AddDays(remaining), first.BusinessDate));
            Assert.Equal((SettingsFinancePolicyReadOutcomeV1.Available, expected, expectedScope), (result.Outcome, result.RatePercent, result.Scope));
        }
        var noMaturity = await ReadK6PreviewAsync(editor, new(type.Id, bank.BankInstitutionId, bank.SourceVersion, null, first.BusinessDate));
        Assert.Equal(("80", "TypeBank"), (noMaturity.RatePercent, noMaturity.Scope));
        var legacy = await ReadK6PreviewAsync(editor, new(type.Id, null, null, null, first.BusinessDate));
        Assert.Equal(("100", "Type"), (legacy.RatePercent, legacy.Scope));
        var forged = await ReadK6PreviewAsync(editor, new(type.Id, bank.BankInstitutionId, null, null, first.BusinessDate));
        Assert.Equal(SettingsFinancePolicyReadOutcomeV1.Unavailable, forged.Outcome); Assert.Null(forged.RatePercent);
        await run.CommandAsync(editor, FinancePolicyRoot + "/collateral-rates", "k6-overlap-draft", special with { ExpectedVersion = "1", MaximumRemainingDays = 60 });
        var overlap = (await editor.GetFromJsonAsync<SettingsCollateralRatesWorkspaceV1>(FinancePolicyRoot + "/collateral-rates"))!.Versions.Single(x => x.Scope == SettingsCollateralRateScopeV1.TypeBankRemainingDays && x.Version == 2);
        using (var response = await editor.SendAsync(Json($"{FinancePolicyRoot}/collateral-rates/{overlap.PolicyId}/publish", "k6-overlap-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic overlapping days"))))
            await ProblemAsync(response, HttpStatusCode.Conflict, SettingsFinancePolicyCodesV1.WindowConflict);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [finance].[MoneyEvents]"));
    }

    private static async Task<SettingsCollateralRateReadV1> ReadK6PreviewAsync(HttpClient editor, PreviewSettingsCollateralRateV1 request)
    {
        using var response = await editor.PostAsJsonAsync(FinancePolicyRoot + "/collateral-rates/preview", request);
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<SettingsCollateralRateReadV1>())!;
    }
}
