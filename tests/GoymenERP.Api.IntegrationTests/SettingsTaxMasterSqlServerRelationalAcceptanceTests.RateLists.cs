using System.Net;
using System.Text.Json;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-029 S9 (SAT-D181, EK26) against a REAL SQL Server: the VAT rate / withholding ratio lists as dated Settings data through the
/// REAL host — empty until entered (SAT-D148), 403 without the tax master name, validation, replay/conflict, the withholding
/// code check against the SAT-D148 code list, a scheduled version corrected and withdrawn, company isolation, audit, the
/// database CHECK, the <see cref="ISettingsTaxRateListReaderV1"/> port reading the stored rows and the forward-fix Down guard
/// (51377). Codes, names and percents are SENTETIK.
/// </summary>
public sealed partial class SettingsTaxMasterSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Rate_lists_are_dated_settings_data_read_by_the_rate_list_port_with_replay_isolation_audit_and_Down_guard_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Permission);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, "sales.drafts.read");
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Permission);
        using var client = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var clientB = run.Client(editorBSubject, CompanyB);
        var today = run.Today;
        var tomorrow = today.AddDays(1);
        var path = $"{Root}/rate-lists";
        var vatPath = $"{path}/{TaxRateListKindsV1.VatRate}/versions";
        var withholdingPath = $"{path}/{TaxRateListKindsV1.WithholdingRatio}/versions";
        var guard = Assert.IsType<SqlOperation>(new AddTaxRateLists().DownOperations[0]).Sql;

        // An empty store: the Down guard lets the rollback through.
        Assert.Equal(0, await run.ScalarAsync<int>(guard + "\nSELECT 0"));

        using (var forbidden = await viewer.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // SAT-D148: nothing is seeded — both lists answer "0" with no version in force.
        var initial = await run.JsonAsync(client, path);
        Assert.Equal(CompanyA, initial.GetProperty("companyId").GetGuid());
        Assert.Equal([TaxRateListKindsV1.VatRate, TaxRateListKindsV1.WithholdingRatio],
            initial.GetProperty("lists").EnumerateArray().Select(list => list.GetProperty("kind").GetString()));
        Assert.All(initial.GetProperty("lists").EnumerateArray(), list =>
        {
            Assert.Equal("0", list.GetProperty("version").GetString());
            Assert.Equal(JsonValueKind.Null, list.GetProperty("inForce").ValueKind);
            Assert.Empty(list.GetProperty("scheduled").EnumerateArray());
        });

        // Validation refuses before anything is written: an unknown kind, a VAT rate with a withholding code, a 0 withholding
        // ratio, a rounded percent and a duplicate code.
        foreach (var (target, key, entries) in new (string, string, TaxRateListEntryV1[])[]
                 {
                     ($"{path}/Other/versions", "k-rl-kind", [new("20", "SENTETIK", "20")]),
                     (vatPath, "k-rl-vat-code", [new("20", "SENTETIK", "20", "601")]),
                     (withholdingPath, "k-rl-zero", [new("0/10", "SENTETIK", "0")]),
                     (vatPath, "k-rl-scale", [new("20", "SENTETIK", "20.00001")]),
                     (vatPath, "k-rl-dup", [new("20", "SENTETIK", "20"), new("20", "SENTETIK", "10")]),
                 })
        {
            using var invalid = await client.SendAsync(Json(HttpMethod.Post, target, key,
                new PublishTaxRateListRequestV1("0", entries, "SENTETIK")));
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.RequestInvalid);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxRateListVersions]"));

        // The VAT rate list (SENTETIK) is entered; replay, the same list again (no-op) and a stale version.
        var vat = new PublishTaxRateListRequestV1("0",
            [new("20", "SENTETIK genel", "20"), new("10", "SENTETIK indirimli", "10"), new("1", "SENTETIK temel", "1")],
            "SENTETIK KDV listesi");
        await run.CommandAsync(client, HttpMethod.Post, vatPath, "k-rl-vat-1", vat, HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, vatPath, "k-rl-vat-1", vat, HttpStatusCode.NoContent, replayed: true);
        await run.CommandAsync(client, HttpMethod.Post, vatPath, "k-rl-vat-same", vat with { ExpectedVersion = "1" },
            HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxRateListVersions]"));
        using (var stale = await client.SendAsync(Json(HttpMethod.Post, vatPath, "k-rl-vat-stale", vat with { Reason = "x" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.VersionConflict);
        }

        // A withholding ratio's GİB code must be in the withholding code list in force (SAT-D148); then it is accepted.
        var withholding = new PublishTaxRateListRequestV1("0", [new("5/10", "SENTETIK tevkifat", "50", "601")],
            "SENTETIK tevkifat listesi");
        using (var unknownCode = await client.SendAsync(Json(HttpMethod.Post, withholdingPath, "k-rl-wh-unknown", withholding)))
        {
            await AssertProblemAsync(unknownCode, HttpStatusCode.UnprocessableEntity,
                TaxMasterProblemCodesV1.WithholdingCodeUnknown);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/code-lists/{TaxCodeListKindsV1.Withholding}/versions",
            "k-rl-wh-codes", new PublishTaxCodeListRequestV1("0", [new TaxCodeListEntryV1("601", "SENTETIK tevkifat kodu")],
                "SENTETIK kod listesi"), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, withholdingPath, "k-rl-wh-1", withholding, HttpStatusCode.NoContent);

        // A scheduled VAT version, corrected on its date (the earlier one is replaced), cannot replace the version in force.
        await run.CommandAsync(client, HttpMethod.Post, vatPath, "k-rl-vat-2",
            new PublishTaxRateListRequestV1("1", [new("20", "SENTETIK genel", "20")], "SENTETIK planlı", tomorrow),
            HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, vatPath, "k-rl-vat-3",
            new PublishTaxRateListRequestV1("2", [new("20", "SENTETIK genel", "20"), new("8", "SENTETIK", "8")],
                "SENTETIK düzeltme", tomorrow), HttpStatusCode.NoContent);
        var scheduled = await run.JsonAsync(client, path);
        var vatList = scheduled.GetProperty("lists").EnumerateArray()
            .Single(list => list.GetProperty("kind").GetString() == TaxRateListKindsV1.VatRate);
        Assert.Equal(("3", "1", "3"), (vatList.GetProperty("version").GetString(),
            vatList.GetProperty("inForce").GetProperty("listVersion").GetString(),
            Assert.Single(vatList.GetProperty("scheduled").EnumerateArray()).GetProperty("listVersion").GetString()));
        Assert.Equal(["1", "10", "20"], vatList.GetProperty("inForce").GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("code").GetString()));
        Assert.Equal(TaxMasterCalculatorV1.Format(20m), vatList.GetProperty("inForce").GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("code").GetString() == "20").GetProperty("percent").GetString());
        var whEntry = Assert.Single(scheduled.GetProperty("lists").EnumerateArray()
            .Single(list => list.GetProperty("kind").GetString() == TaxRateListKindsV1.WithholdingRatio)
            .GetProperty("inForce").GetProperty("entries").EnumerateArray());
        Assert.Equal(("5/10", "601"), (whEntry.GetProperty("code").GetString(), whEntry.GetProperty("taxCode").GetString()));

        using (var inEffect = await client.SendAsync(Json(HttpMethod.Post, $"{vatPath}/1/withdrawal", "k-rl-w1",
                   new WithdrawTaxCodeListRequestV1("3", "SENTETIK"))))
        {
            await AssertProblemAsync(inEffect, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordInEffect);
        }

        using (var replaced = await client.SendAsync(Json(HttpMethod.Post, $"{vatPath}/2/withdrawal", "k-rl-w2",
                   new WithdrawTaxCodeListRequestV1("3", "SENTETIK"))))
        {
            await AssertProblemAsync(replaced, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordWithdrawn);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{vatPath}/3/withdrawal", "k-rl-w3",
            new WithdrawTaxCodeListRequestV1("3", "SENTETIK geri çekme"), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, $"{vatPath}/3/withdrawal", "k-rl-w3",
            new WithdrawTaxCodeListRequestV1("3", "SENTETIK geri çekme"), HttpStatusCode.NoContent, replayed: true);
        Assert.Empty((await run.JsonAsync(client, path)).GetProperty("lists").EnumerateArray()
            .Single(list => list.GetProperty("kind").GetString() == TaxRateListKindsV1.VatRate)
            .GetProperty("scheduled").EnumerateArray());

        // The port reads the stored rows: version 1 today and tomorrow (the scheduled one is withdrawn); B has none.
        await using (var port = RateListPort(run, CompanyA))
        {
            var vatToday = await port.Value.GetRateListAsync(TaxRateListKindsV1.VatRate, today);
            Assert.Equal((TaxResolutionStatesV1.Resolved, 1L, today, 3, (string?)null),
                (vatToday.State, vatToday.ListVersion, vatToday.EffectiveFrom, vatToday.Entries.Count, vatToday.Reason));
            Assert.Equal(20m, vatToday.Entries.Single(entry => entry.Code == "20").Percent);
            Assert.Equal(1L, (await port.Value.GetRateListAsync(TaxRateListKindsV1.VatRate, tomorrow)).ListVersion);
            var ratio = Assert.Single((await port.Value.GetRateListAsync(TaxRateListKindsV1.WithholdingRatio, today)).Entries);
            Assert.Equal(("5/10", 50m, "601"), (ratio.Code, ratio.Percent, ratio.TaxCode));
            var yesterday = await port.Value.GetRateListAsync(TaxRateListKindsV1.VatRate, today.AddDays(-1));
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxRateListReasonsV1.RateListMissing),
                (yesterday.State, yesterday.Reason));
            Assert.Equal(TaxMasterProblemCodesV1.RequestInvalid, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetRateListAsync("Other", today))).Code);
        }

        await using (var portB = RateListPort(run, CompanyB))
        {
            var none = await portB.Value.GetRateListAsync(TaxRateListKindsV1.VatRate, today);
            Assert.Equal((CompanyB, TaxResolutionStatesV1.Unresolved, TaxRateListReasonsV1.RateListMissing, 0),
                (none.CompanyId, none.State, none.Reason, none.Entries.Count));
        }

        await using (var outsider = RateListPort(run, CompanyA, CompanyB))
        {
            Assert.Equal(TaxMasterProblemCodesV1.AccessDenied, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => outsider.Value.GetRateListAsync(TaxRateListKindsV1.VatRate, today))).Code);
        }

        // Company isolation: B sees its own empty lists; A's version is B's code-less 404.
        var listsB = await run.JsonAsync(clientB, path);
        Assert.Equal(CompanyB, listsB.GetProperty("companyId").GetGuid());
        Assert.All(listsB.GetProperty("lists").EnumerateArray(), list =>
            Assert.Equal(JsonValueKind.Null, list.GetProperty("inForce").ValueKind));
        using (var foreign = await clientB.SendAsync(Json(HttpMethod.Post, $"{vatPath}/1/withdrawal", "k-rl-foreign",
                   new WithdrawTaxCodeListRequestV1("1", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        // Audit: three versions published, one corrected (the same-date replacement), one withdrawn — under the tax master name.
        foreach (var (eventType, count) in new[]
                 {
                     ("settings.tax-rate-list.published.v1", 3),
                     ("settings.tax-rate-list.corrected.v1", 1),
                     ("settings.tax-rate-list.withdrawn.v1", 1),
                 })
        {
            Assert.Equal(count, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [CompanyId] = @c "
                + "AND [PermissionCode] = N'settings.tax-master.manage'", ("@e", eventType), ("@c", CompanyA)));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-rate-list.withdrawn.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK geri çekme'"));

        // The CHECK holds the kind rule in the database as well: a VAT rate carries no withholding code.
        var check = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [settings].[TaxRateListEntries] ([CompanyId], [Kind], [ListVersion], [Code], [Name], [Percent], [TaxCode]) "
            + "VALUES (@c, 'VatRate', 1, '18', N'SENTETIK', 18, '601'); SELECT 0", ("@c", CompanyA)));
        Assert.Equal(547, check.Number);

        // The rollback is refused while a rate list exists (forward fix, 51377); nothing is dropped.
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "\nSELECT 0"));
        Assert.Equal(51377, refused.Number);
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxRateListVersions]"));
    }

    private static PortScope<ISettingsTaxRateListReaderV1> RateListPort(HostRun run, Guid company, params Guid[] actorCompanies)
    {
        var database = NewSettingsContext(run.Factory.DatabaseConnectionString, company, out var companyContext);
        var companies = actorCompanies.Length == 0 ? new[] { company } : actorCompanies;
        return new PortScope<ISettingsTaxRateListReaderV1>(database,
            new SettingsTaxRateListReader(database, companyContext, new FixedActor("SENTETIK-sales-actor", companies)));
    }
}
