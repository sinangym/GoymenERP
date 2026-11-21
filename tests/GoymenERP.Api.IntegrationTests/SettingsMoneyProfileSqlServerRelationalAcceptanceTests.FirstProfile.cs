using System.Net;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsMoneyProfileSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task First_credit_currency_profile_needs_explicit_human_values_and_is_atomic_replayable_prospective_and_company_scoped()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Manage);
        using var editor = run.Client(subject, CompanyA);
        var before = await run.ReadAsync(editor, Root);
        const string operation = MoneyProfileAuthoringCatalogV1.CreditPlanOperation;
        var usd = Assert.Single(before.Profiles, row => row.CurrencyId == SettingsCurrencyIdentitiesV1.UnitedStatesDollar && row.Operation == operation);
        Assert.Equal("0", usd.Version);
        Assert.Empty(usd.Versions);
        Assert.Empty(await run.CandidatesAsync(CompanyA, operation));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
        var request = new RecordMoneyProfileRequestV1("0", usd.CurrencyId, operation, 2, null, true,
            "SENTETIK banka belgesi para birimi", before.Today);
        await run.CommandAsync(editor, Root + "/versions", "first-usd", request);
        await run.CommandAsync(editor, Root + "/versions", "first-usd", request, replay: true);
        using (var collision = await editor.SendAsync(Json(Root + "/versions", "first-usd", request with { Scale = 3 })))
            await ProblemAsync(collision, HttpStatusCode.Conflict, MoneyProfileProblemCodesV1.IdempotencyConflict);
        using (var stale = await editor.SendAsync(Json(Root + "/versions", "another-first-usd", request)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, MoneyProfileProblemCodesV1.VersionConflict);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileBaselineTransfers]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.MoneyProfile'"));
        Assert.Equal(usd.CurrencyId, Assert.Single(await run.CandidatesAsync(CompanyA, operation)).CurrencyId);
        await using (var port = run.Port(CompanyA))
        {
            var profile = await port.Value.GetMoneyProfileAsync(usd.CurrencyId, operation, before.Today);
            Assert.Equal((CompanyA, "USD", 1L, 2, true, before.Today),
                (profile.CompanyId, profile.CurrencyCode, profile.Profile.Version, profile.Scale, profile.IsActive, profile.EffectiveFrom));
            var earlier = await port.Value.GetMoneyProfileAsync(usd.CurrencyId, operation, before.Today.AddDays(-1));
            Assert.True(earlier.EffectiveFrom > before.Today.AddDays(-1));
        }
        await using (var other = run.Port(CompanyB))
            await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => other.Value.GetMoneyProfileAsync(usd.CurrencyId, operation, before.Today));
        await run.CommandAsync(editor, Root + "/versions", "disable-usd", request with { ExpectedVersion = "1", IsActive = false });
        Assert.Empty(await run.CandidatesAsync(CompanyA, operation));
        await using (var port = run.Port(CompanyA))
            Assert.False((await port.Value.GetMoneyProfileAsync(usd.CurrencyId, operation, before.Today)).IsActive);
        // A disabled stored profile never silently falls back to a compiled or guessed active value.
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[MoneyProfileVersions]"));
    }
}
