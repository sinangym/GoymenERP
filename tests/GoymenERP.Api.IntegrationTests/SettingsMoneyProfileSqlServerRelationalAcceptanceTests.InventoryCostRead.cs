using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsMoneyProfileSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inventory_cost_profile_internal_read_uses_the_real_company_journal_timeline_without_user_impersonation_or_profile_creation()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        using var editor = run.Client(subject, CompanyA);
        var before = await run.ReadAsync(editor, Root);
        const string operation = "accounting.journal-amount.v1";
        var series = Assert.Single(before.Profiles, x => x.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira && x.Operation == operation);
        var original = await run.InventoryCostProfile(CompanyA, CompanyA, before.Today);
        Assert.Equal((CompanyA, series.CurrencyId, operation, 1L, 2),
            (original.CompanyId, original.CurrencyId, original.Operation, original.Profile.Version, original.Scale));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
        var request = new RecordMoneyProfileRequestV1(series.Version, series.CurrencyId, operation, 3, null,
            true, "SENTETIK ilk maliyet profil okuma", before.Today);
        await run.CommandAsync(editor, Root + "/versions", "inventory-cost-profile", request);
        var updated = await run.InventoryCostProfile(CompanyA, CompanyA, before.Today);
        Assert.Equal((2L, 3), (updated.Profile.Version, updated.Scale));
        Assert.Equal(1L, (await run.InventoryCostProfile(CompanyA, CompanyA, before.Today.AddDays(-1))).Profile.Version);
        // SAT-D18: GSP has no approved Accounting profile; a foreign company must not borrow GDC's baseline.
        var unavailable = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            run.InventoryCostProfile(CompanyB, CompanyB, before.Today));
        Assert.Equal("settings_money_profile_not_found", unavailable.Code);
        var mismatch = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            run.InventoryCostProfile(CompanyA, CompanyB, before.Today));
        Assert.Equal("settings_access_denied", mismatch.Code);
        await run.CommandAsync(editor, Root + "/versions", "inventory-cost-profile-disabled",
            request with { ExpectedVersion = "2", IsActive = false });
        await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => run.InventoryCostProfile(CompanyA, CompanyA, before.Today));
        // Reads never create a replacement, grant or zero-scale fallback for an inactive stored profile.
        Assert.Equal(3, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
    }
}
