using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-FIN-010 / KUR_GUNU SAT-D54/D55 / EK24 SAT-D169…D172 against a REAL SQL Server through the REAL host: the Settings
/// exchange-rate record imported from SYNTHETIC sources (the shipped TCMB and half-day sources stay disabled), the rate-day
/// resolution (Monday → Friday, arife → the business day before the previous full one, unverified / undetermined → no rate),
/// the reasoned manual correction (version, idempotency, audit, 422 on a day TCMB did not publish), append-only import that
/// never overwrites a definite answer, company isolation, 403 without the names and the rollback refusal (51201). Every grant is
/// a fresh synthetic role; every rate is SENTETIK.
/// </summary>
public sealed class SettingsExchangeRateSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Root = SettingsExchangeRateEndpoints.ExchangeRatesPath;
    private const string Migration = "20261102143743_AddExchangeRateRecords";
    private const string AmendmentMigration = "20261102192125_AmendExchangeRateRecordStates";
    private const string Read = SettingsExchangeRatePermissionManifestV1.Read;
    private const string Correct = SettingsExchangeRatePermissionManifestV1.Correct;
    private const string Import = SettingsExchangeRatePermissionManifestV1.Import;
    private const string MarkHalfDay = SettingsExchangeRatePermissionManifestV1.MarkHalfDay;

    // Thu 15 .. Thu 22 October 2026 (all before the run's today). Tue 20 is a synthetic arife; Wed 21 and Thu 22 unverified.
    private static readonly IReadOnlyDictionary<DateOnly, ExchangeRateBulletinFetchV1> Bulletins =
        new Dictionary<DateOnly, ExchangeRateBulletinFetchV1>
        {
            [Day("2026-10-15")] = Published("2026-10-15", ("USD", "41.5"), ("EUR", "48.25")),
            [Day("2026-10-16")] = Published("2026-10-16", ("USD", "41.6"), ("EUR", "48.3"), ("JPY", "0.274512")),
            [Day("2026-10-17")] = NoBulletin("2026-10-17"),
            [Day("2026-10-18")] = NoBulletin("2026-10-18"),
            [Day("2026-10-19")] = Published("2026-10-19", ("USD", "41.7"), ("EUR", "48.4")),
            [Day("2026-10-20")] = Published("2026-10-20", ("USD", "41.8"), ("EUR", "48.5")),
        };

    [SqlServerFact]
    public async Task Exchange_rate_record_import_resolution_correction_isolation_and_audit_on_real_sql()
    {
        var bulletins = new SyntheticBulletinSource(Bulletins);
        var halfDayAnswers = new Dictionary<DateOnly, string>
        {
            [Day("2026-10-15")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-16")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-17")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-18")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-19")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-20")] = ExchangeRateHalfDayStatesV1.HalfDay,
            [Day("2026-10-21")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-22")] = ExchangeRateHalfDayStatesV1.FullDay,
        };
        var halfDays = new SyntheticHalfDaySource(halfDayAnswers);
        await using var run = await HostRun.StartAsync(services =>
        {
            services.RemoveAll<IExchangeRateBulletinSourceV1>();
            services.AddSingleton<IExchangeRateBulletinSourceV1>(bulletins);
            services.RemoveAll<IExchangeRateHalfDaySourceV1>();
            services.AddSingleton<IExchangeRateHalfDaySourceV1>(halfDays);
        });
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Read, Correct, Import, MarkHalfDay);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Read, Correct, Import, MarkHalfDay);
        using var editor = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var editorB = run.Client(editorBSubject, CompanyB);
        var importBody = new ImportExchangeRatesRequestV1(Day("2026-10-15"), Day("2026-10-22"));

        // Without the names: 403; nothing is written and no source is asked.
        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, $"{Root}/imports", "k-forbidden", importBody)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, $"{Root}/days/2026-10-16/corrections", "k-forbidden-c",
                   new CorrectExchangeRateRequestV1("1", "USD", "41.9", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, bulletins.Calls);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords]"));

        // Only past days are imported (a day's bulletin is final only after it is announced).
        using (var future = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/imports", "k-future",
                   new ImportExchangeRatesRequestV1(run.Today, run.Today))))
        {
            await AssertProblemAsync(future, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.RequestInvalid);
        }

        // Import: every day recorded (Wed 21 / Thu 22 unverified: the source failed); the same key replays.
        var summary = await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-1", importBody, HttpStatusCode.OK);
        var outcomes = summary.GetProperty("days").EnumerateArray()
            .ToDictionary(day => day.GetProperty("date").GetString()!, day => (day.GetProperty("bulletinOutcome").GetString(),
                day.GetProperty("halfDayOutcome").GetString()));
        Assert.Equal(8, outcomes.Count);
        Assert.All(outcomes.Values, outcome => Assert.Equal(ExchangeRateImportOutcomesV1.Recorded, outcome.Item1));
        Assert.All(outcomes.Values, outcome => Assert.Equal(ExchangeRateImportOutcomesV1.Recorded, outcome.Item2));
        await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-1", importBody, HttpStatusCode.OK, replayed: true);
        Assert.Equal(8, bulletins.Calls);
        Assert.Equal(16, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords]"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ExchangeRateRecords] WHERE [Kind] = 'Bulletin' AND [State] = 'Unverified'"));
        // Review B P2: the first 404 (Sat 17 / Sun 18) is only NotFound — not yet a holiday; the resolver stops on it.
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ExchangeRateRecords] WHERE [Kind] = 'Bulletin' AND [State] = 'NotFound'"));
        Assert.Equal(RateDayCode("exchange_rate_day_unconfirmed", "2026-10-18"), Failure(await Resolve(editor, run, "2026-10-19", "USD")));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.exchange-rate.imported.v1' AND [CompanyId] = @c "
            + "AND [PermissionCode] = N'settings.exchange-rates.import'", ("@c", CompanyA)));

        // A second run sees the 404 again while a later bulletin (Mon 19) exists: now a final "no bulletin". Final days (Published)
        // are not asked again (B P3-1); the unverified days are.
        var second = await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-2", importBody, HttpStatusCode.OK);
        var secondOutcomes = second.GetProperty("days").EnumerateArray()
            .ToDictionary(day => day.GetProperty("date").GetString()!, day => day.GetProperty("bulletinOutcome").GetString());
        Assert.Equal((ExchangeRateImportOutcomesV1.Recorded, ExchangeRateImportOutcomesV1.Recorded),
            (secondOutcomes["2026-10-17"], secondOutcomes["2026-10-18"]));
        Assert.All(secondOutcomes.Where(pair => pair.Key is not ("2026-10-17" or "2026-10-18")),
            pair => Assert.Equal(ExchangeRateImportOutcomesV1.Unchanged, pair.Value));
        Assert.Equal([Day("2026-10-17"), Day("2026-10-18"), Day("2026-10-21"), Day("2026-10-22")], bulletins.Asked.Skip(8));
        Assert.Equal(18, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords]"));

        // The list: per day state, half-day state and the per-one-unit rates (JPY normalised by the source to one unit).
        var list = await run.JsonAsync(editor, $"{Root}?from=2026-10-14&to=2026-10-22");
        var days = list.GetProperty("days").EnumerateArray().ToDictionary(day => day.GetProperty("date").GetString()!);
        Assert.Equal(9, days.Count);
        Assert.True(list.GetProperty("bulletinSourceEnabled").GetBoolean());
        Assert.Equal((ExchangeRateBulletinStatesV1.NotRecorded, ExchangeRateHalfDayStatesV1.Undetermined, "0"),
            DayState(days["2026-10-14"]));
        Assert.Equal((ExchangeRateBulletinStatesV1.NoBulletin, ExchangeRateHalfDayStatesV1.FullDay, "3"), DayState(days["2026-10-17"]));
        Assert.Equal((ExchangeRateBulletinStatesV1.Published, ExchangeRateHalfDayStatesV1.HalfDay, "2"), DayState(days["2026-10-20"]));
        Assert.Equal((ExchangeRateBulletinStatesV1.Unverified, ExchangeRateHalfDayStatesV1.FullDay, "2"),
            DayState(days["2026-10-21"]));
        Assert.Equal(["EUR:48.3", "JPY:0.274512", "USD:41.6"], days["2026-10-16"].GetProperty("rates").EnumerateArray()
            .Select(rate => $"{rate.GetProperty("currencyCode").GetString()}:{rate.GetProperty("sellingRate").GetString()}"));

        // Resolution (the one calculator): Monday → Friday; Tuesday arife → Friday (full Monday's Resmî Gazete rate); fail-closed.
        var monday = await Resolve(editor, run, "2026-10-19", "USD");
        Assert.Equal(("Resolved", "2026-10-16", "41.6", "Official", false), Summary(monday));
        var arife = await Resolve(editor, run, "2026-10-20", "USD");
        Assert.Equal(("Resolved", "2026-10-16", "41.6", "Official", true), Summary(arife));
        Assert.Equal("2026-10-19", arife.GetProperty("previousFullBusinessDay").GetString());
        var wednesday = await Resolve(editor, run, "2026-10-21", "EUR");
        Assert.Equal(("Resolved", "2026-10-20", "48.5", "Official", false), Summary(wednesday));
        Assert.Equal(RateDayCode("exchange_rate_day_unconfirmed", "2026-10-21"), Failure(await Resolve(editor, run, "2026-10-22", "USD")));
        Assert.Equal(RateDayCode("exchange_rate_half_day_undetermined", "2026-10-14"),
            Failure(await Resolve(editor, run, "2026-10-14", "USD")));
        Assert.Equal(RateDayCode("exchange_rate_currency_missing", "2026-10-16"),
            Failure(await Resolve(editor, run, "2026-10-19", "GBP")));
        using (var badCurrency = await editor.GetAsync($"{Root}/resolution?rateDay=2026-10-19&currencyCode=TRY"))
        {
            await AssertProblemAsync(badCurrency, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.RequestInvalid);
        }

        // A third run asks only the still-unverified days; nothing changes (even though the source would now answer differently
        // for a final day), no audit.
        bulletins.Override(Day("2026-10-15"), Published("2026-10-15", ("USD", "99"), ("EUR", "48.25")));
        var again = await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-3", importBody, HttpStatusCode.OK);
        Assert.All(again.GetProperty("days").EnumerateArray(), day =>
        {
            Assert.Equal(ExchangeRateImportOutcomesV1.Unchanged, day.GetProperty("bulletinOutcome").GetString());
            Assert.Equal(ExchangeRateImportOutcomesV1.Unchanged, day.GetProperty("halfDayOutcome").GetString());
        });
        Assert.Equal([Day("2026-10-21"), Day("2026-10-22")], bulletins.Asked.Skip(12));
        Assert.Equal(18, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords]"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.exchange-rate.imported.v1'"));

        // SAT-D169 correction: reason, version, key; only on a day TCMB published.
        var friday = $"{Root}/days/2026-10-16/corrections";
        using (var noReason = await editor.SendAsync(Json(HttpMethod.Post, friday, "k-c-noreason",
                   new CorrectExchangeRateRequestV1("2", "USD", "41.65", " "))))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.ReasonRequired);
        }

        using (var rounded = await editor.SendAsync(Json(HttpMethod.Post, friday, "k-c-scale",
                   new CorrectExchangeRateRequestV1("2", "USD", "41.12345678901", "SENTETIK"))))
        {
            await AssertProblemAsync(rounded, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.RequestInvalid);
        }

        using (var holiday = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/days/2026-10-17/corrections", "k-c-holiday",
                   new CorrectExchangeRateRequestV1("3", "USD", "41.65", "SENTETIK"))))
        {
            await AssertProblemAsync(holiday, HttpStatusCode.UnprocessableEntity, ExchangeRateProblemCodesV1.DayNotPublished);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, friday, "k-c-stale",
                   new CorrectExchangeRateRequestV1("1", "USD", "41.65", "SENTETIK"))))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.VersionConflict);
        }

        var correction = new CorrectExchangeRateRequestV1("2", "USD", "41.65", "SENTETIK bülten düzeltmesi");
        await run.CommandAsync(editor, HttpMethod.Post, friday, "k-c-1", correction, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, HttpMethod.Post, friday, "k-c-1", correction, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, friday, "k-c-1", correction with { SellingRate = "41.7" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.IdempotencyConflict);
        }

        // The same value again is a no-op (no record, no audit).
        await run.CommandAsync(editor, HttpMethod.Post, friday, "k-c-same", correction with { ExpectedVersion = "3" },
            HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ExchangeRateRecords] WHERE [Provenance] = 'Manual' AND [Reason] = N'SENTETIK bülten düzeltmesi'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.exchange-rate.corrected.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK bülten düzeltmesi' AND JSON_VALUE([BeforeJson], '$.sellingRate') = N'41.6' "
            + "AND [PermissionCode] = N'settings.exchange-rates.correct' AND [ActorId] = @a", ("@a", editorActor)));

        // Documents reading afterwards get the corrected number with its provenance (SAT-D172 snapshot shape); EUR keeps TCMB's.
        var corrected = await Resolve(editor, run, "2026-10-19", "USD");
        Assert.Equal(("Resolved", "2026-10-16", "41.65", "Manual", false), Summary(corrected));
        Assert.Equal("3", corrected.GetProperty("recordVersion").GetString());
        Assert.Equal(("Resolved", "2026-10-16", "48.3", "Official", false), Summary(await Resolve(editor, run, "2026-10-19", "EUR")));

        // The read port Sales consumes: same answer; an actor without the company is refused.
        await using (var settings = run.SettingsContext(CompanyA, out var companyContext))
        {
            var port = new SettingsExchangeRateResolverV1(settings, companyContext, new FixedActor("SENTETIK-sales", CompanyA));
            var answer = await port.ResolveAsync(Day("2026-10-20"), ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"),
                SettingsCurrencyIdentitiesV1.TurkishLira);
            Assert.Equal((ExchangeRateResolutionStatesV1.Resolved, "41.65", true, "USD"),
                (answer.State, answer.SellingRate, answer.HalfDayRuleApplied, answer.CurrencyCode));
            var outsider = new SettingsExchangeRateResolverV1(settings, companyContext, new FixedActor("SENTETIK-other", CompanyB));
            var denied = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => outsider.ResolveAsync(Day("2026-10-20"),
                ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), SettingsCurrencyIdentitiesV1.TurkishLira));
            Assert.Equal(ExchangeRateProblemCodesV1.AccessDenied, denied.Code);
        }

        // Company isolation: B sees nothing of A and resolves nothing.
        var listB = await run.JsonAsync(editorB, $"{Root}?from=2026-10-15&to=2026-10-20");
        Assert.All(listB.GetProperty("days").EnumerateArray(), day =>
            Assert.Equal(ExchangeRateBulletinStatesV1.NotRecorded, day.GetProperty("bulletinState").GetString()));
        Assert.Equal(RateDayCode("exchange_rate_half_day_undetermined", "2026-10-19"),
            Failure(await Resolve(editorB, run, "2026-10-19", "USD")));
        using (var crossCompany = await editorB.SendAsync(Json(HttpMethod.Post, friday, "k-c-b",
                   new CorrectExchangeRateRequestV1("2", "USD", "1", "SENTETIK"))))
        {
            await AssertProblemAsync(crossCompany, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.VersionConflict);
        }

        // B P2-2: a 404 is final only when seen again in a later run while a LATER bulletin exists. Sat/Sun 24-25 alone: 404 →
        // NotFound (asked again); in the next run, with Monday 26 published, the second 404 makes them "no bulletin".
        bulletins.Override(Day("2026-10-24"), NoBulletin("2026-10-24"));
        bulletins.Override(Day("2026-10-25"), NoBulletin("2026-10-25"));
        var weekend = await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-weekend",
            new ImportExchangeRatesRequestV1(Day("2026-10-24"), Day("2026-10-25")), HttpStatusCode.OK);
        Assert.All(weekend.GetProperty("days").EnumerateArray(), day =>
            Assert.Equal(ExchangeRateImportOutcomesV1.Recorded, day.GetProperty("bulletinOutcome").GetString()));
        var weekendDays = (await run.JsonAsync(editor, $"{Root}?from=2026-10-24&to=2026-10-26")).GetProperty("days").EnumerateArray()
            .ToDictionary(day => day.GetProperty("date").GetString()!, day => day.GetProperty("bulletinState").GetString());
        Assert.Equal((ExchangeRateBulletinStatesV1.NotFound, ExchangeRateBulletinStatesV1.NotFound),
            (weekendDays["2026-10-24"], weekendDays["2026-10-25"]));
        bulletins.Override(Day("2026-10-26"), Published("2026-10-26", ("USD", "41.9")));
        await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-monday",
            new ImportExchangeRatesRequestV1(Day("2026-10-24"), Day("2026-10-26")), HttpStatusCode.OK);
        weekendDays = (await run.JsonAsync(editor, $"{Root}?from=2026-10-24&to=2026-10-26")).GetProperty("days").EnumerateArray()
            .ToDictionary(day => day.GetProperty("date").GetString()!, day => day.GetProperty("bulletinState").GetString());
        Assert.Equal((ExchangeRateBulletinStatesV1.NoBulletin, ExchangeRateBulletinStatesV1.NoBulletin, ExchangeRateBulletinStatesV1.Published),
            (weekendDays["2026-10-24"], weekendDays["2026-10-25"], weekendDays["2026-10-26"]));

        // EK31 SAT-D198: Tue 27 has no automatic half-day answer → "kur belirlenemedi"; a reasoned manual mark resolves it.
        Assert.Equal(RateDayCode("exchange_rate_half_day_undetermined", "2026-10-27"),
            Failure(await Resolve(editor, run, "2026-10-27", "USD")));
        var tuesday = $"{Root}/days/2026-10-27/half-day-marks";
        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, tuesday, "k-m-forbidden",
                   new MarkExchangeRateHalfDayRequestV1("0", "FullDay", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var noReason = await editor.SendAsync(Json(HttpMethod.Post, tuesday, "k-m-noreason",
                   new MarkExchangeRateHalfDayRequestV1("0", "FullDay", " "))))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.ReasonRequired);
        }

        using (var undetermined = await editor.SendAsync(Json(HttpMethod.Post, tuesday, "k-m-state",
                   new MarkExchangeRateHalfDayRequestV1("0", "Undetermined", "SENTETIK"))))
        {
            await AssertProblemAsync(undetermined, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.RequestInvalid);
        }

        // Review A P3-4: a day that has not come yet is not marked (422, nothing stored).
        using (var future = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/days/{run.Today.AddDays(1):yyyy-MM-dd}/half-day-marks",
                   "k-m-future", new MarkExchangeRateHalfDayRequestV1("0", "FullDay", "SENTETIK"))))
        {
            await AssertProblemAsync(future, HttpStatusCode.UnprocessableEntity, ExchangeRateProblemCodesV1.MarkFutureDay);
        }

        var mark = new MarkExchangeRateHalfDayRequestV1("0", "FullDay", "SENTETIK takvim teyidi");
        await run.CommandAsync(editor, HttpMethod.Post, tuesday, "k-m-1", mark, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, HttpMethod.Post, tuesday, "k-m-1", mark, HttpStatusCode.NoContent, replayed: true);
        // Review B P3-4: the same key with another body is 409 and changes nothing.
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, tuesday, "k-m-1", mark with { State = "HalfDay" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.IdempotencyConflict);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, tuesday, "k-m-stale", mark with { State = "HalfDay" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.VersionConflict);
        }

        Assert.Equal(("Resolved", "2026-10-26", "41.9", "Official", false), Summary(await Resolve(editor, run, "2026-10-27", "USD")));
        var marked = (await run.JsonAsync(editor, $"{Root}?from=2026-10-27&to=2026-10-27")).GetProperty("days")[0];
        Assert.Equal((ExchangeRateHalfDayStatesV1.FullDay, ExchangeRateProvenancesV1.Manual), (marked.GetProperty("halfDayState").GetString(),
            marked.GetProperty("halfDayProvenance").GetString()));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.exchange-rate.half-day-marked.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK takvim teyidi' "
            + "AND [PermissionCode] = N'settings.exchange-rates.half-day.mark'"));
        // A definite automatic answer is never overridden by a mark (Mon 19 is FullDay from the source).
        using (var determined = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/days/2026-10-19/half-day-marks", "k-m-det",
                   new MarkExchangeRateHalfDayRequestV1("2", "HalfDay", "SENTETIK"))))
        {
            await AssertProblemAsync(determined, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.HalfDayDetermined);
        }

        // ... and an automatic answer arriving later wins over the mark: the source now says Tue 27 is an arife.
        halfDayAnswers[Day("2026-10-27")] = ExchangeRateHalfDayStatesV1.HalfDay;
        bulletins.Override(Day("2026-10-27"), Published("2026-10-27", ("USD", "42")));
        await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-tuesday",
            new ImportExchangeRatesRequestV1(Day("2026-10-27"), Day("2026-10-27")), HttpStatusCode.OK);
        var official = (await run.JsonAsync(editor, $"{Root}?from=2026-10-27&to=2026-10-27")).GetProperty("days")[0];
        Assert.Equal((ExchangeRateHalfDayStatesV1.HalfDay, ExchangeRateProvenancesV1.Official), (official.GetProperty("halfDayState").GetString(),
            official.GetProperty("halfDayProvenance").GetString()));
        // The arife rule now applies: the previous full business day Mon 26 has no half-day answer → "belirlenemedi".
        Assert.Equal(RateDayCode("exchange_rate_half_day_undetermined", "2026-10-26"),
            Failure(await Resolve(editor, run, "2026-10-27", "USD")));

        // Review B P2: re-verification reopens a final TCMB answer (audited, under the import name); fail-closed until re-imported.
        var reverify = $"{Root}/days/2026-10-16/reverification";
        using (var notFinal = await editor.SendAsync(Json(HttpMethod.Post, $"{Root}/days/2026-10-21/reverification", "k-r-open",
                   new ReverifyExchangeRateDayRequestV1("2", "SENTETIK"))))
        {
            await AssertProblemAsync(notFinal, HttpStatusCode.UnprocessableEntity, ExchangeRateProblemCodesV1.DayNotFinal);
        }

        using (var stale = await editor.SendAsync(Json(HttpMethod.Post, reverify, "k-r-stale",
                   new ReverifyExchangeRateDayRequestV1("2", "SENTETIK"))))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.VersionConflict);
        }

        var reopen = new ReverifyExchangeRateDayRequestV1("3", "SENTETIK geçici TCMB cevabı şüphesi");
        await run.CommandAsync(editor, HttpMethod.Post, reverify, "k-r-1", reopen, HttpStatusCode.NoContent);
        await run.CommandAsync(editor, HttpMethod.Post, reverify, "k-r-1", reopen, HttpStatusCode.NoContent, replayed: true);
        using (var reused = await editor.SendAsync(Json(HttpMethod.Post, reverify, "k-r-1", reopen with { Reason = "SENTETIK başka" })))
        {
            await AssertProblemAsync(reused, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.IdempotencyConflict);
        }

        // Fri 16 is no longer an official published day: its manual rate stops the walk (fail-closed), nothing older is used.
        Assert.Equal(RateDayCode("exchange_rate_manual_conflict", "2026-10-16"), Failure(await Resolve(editor, run, "2026-10-19", "USD")));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.exchange-rate.reverification-requested.v1' "
            + "AND JSON_VALUE([BeforeJson], '$.bulletinState') = N'Published' AND [PermissionCode] = N'settings.exchange-rates.import'"));
        // The next import asks TCMB again for that day; its bulletin restores it and the manual correction applies again.
        await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-friday",
            new ImportExchangeRatesRequestV1(Day("2026-10-16"), Day("2026-10-16")), HttpStatusCode.OK);
        Assert.Equal(("Resolved", "2026-10-16", "41.65", "Manual", false), Summary(await Resolve(editor, run, "2026-10-19", "USD")));

        // Review B P3-4: company B naming A's days with A's versions reaches only its own (empty) days: 409, A unchanged.
        var aRecords = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords] WHERE [CompanyId] = @c",
            ("@c", CompanyA));
        using (var crossMark = await editorB.SendAsync(Json(HttpMethod.Post, tuesday, "k-m-b",
                   new MarkExchangeRateHalfDayRequestV1("3", "HalfDay", "SENTETIK"))))
        {
            await AssertProblemAsync(crossMark, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.VersionConflict);
        }

        using (var crossReverify = await editorB.SendAsync(Json(HttpMethod.Post, reverify, "k-r-b",
                   new ReverifyExchangeRateDayRequestV1("5", "SENTETIK"))))
        {
            await AssertProblemAsync(crossReverify, HttpStatusCode.Conflict, ExchangeRateProblemCodesV1.VersionConflict);
        }

        Assert.Equal(aRecords, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ExchangeRateRecords] WHERE [CompanyId] = @c", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[ExchangeRateRecords] WHERE [CompanyId] = @c", ("@c", CompanyB)));

        // Review A P2-2: a day TCMB once published never becomes "no bulletin" automatically. Thu 15 is re-verified and then
        // answers 404 in two runs while later bulletins exist: it stays NotFound, so a Friday rate day stops on it (fail-closed)
        // instead of sliding onto an older rate.
        await run.CommandAsync(editor, HttpMethod.Post, $"{Root}/days/2026-10-15/reverification", "k-r-15",
            new ReverifyExchangeRateDayRequestV1("2", "SENTETIK"), HttpStatusCode.NoContent);
        bulletins.Override(Day("2026-10-15"), NoBulletin("2026-10-15"));
        foreach (var key in new[] { "k-import-15a", "k-import-15b" })
        {
            await run.CommandJsonAsync(editor, $"{Root}/imports", key,
                new ImportExchangeRatesRequestV1(Day("2026-10-15"), Day("2026-10-15")), HttpStatusCode.OK);
        }

        var thursday = (await run.JsonAsync(editor, $"{Root}?from=2026-10-15&to=2026-10-15")).GetProperty("days")[0];
        Assert.Equal(ExchangeRateBulletinStatesV1.NotFound, thursday.GetProperty("bulletinState").GetString());
        Assert.Equal(RateDayCode("exchange_rate_day_unconfirmed", "2026-10-15"), Failure(await Resolve(editor, run, "2026-10-16", "USD")));

        // The rollback is refused while rate records exist (forward fix); nothing is dropped. The state amendment refuses first
        // (51202: NotFound and manual half-day records exist), so every exchange-rate migration stays applied.
        await using var context = run.SettingsContext(CompanyA, out _);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.IndexOf(applied, Migration);
        var amendment = Array.IndexOf(applied, AmendmentMigration);
        Assert.True(index > 0 && amendment > index);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
        Assert.Equal(51202, rollback.Number);
        Assert.Equal(applied.Take(amendment + 1), await context.Database.GetAppliedMigrationsAsync());

        // Without those records the amendment rolls back, and AddExchangeRateRecords refuses while records remain (51201).
        await run.ExecuteAsync("DELETE r FROM [settings].[ExchangeRateRecordRates] r JOIN [settings].[ExchangeRateRecords] x "
            + "ON x.[CompanyId] = r.[CompanyId] AND x.[Id] = r.[RecordId] WHERE x.[State] = 'NotFound' OR (x.[Kind] = 'HalfDay' AND x.[Provenance] = 'Manual');"
            + "DELETE FROM [settings].[ExchangeRateRecords] WHERE [State] = 'NotFound' OR ([Kind] = 'HalfDay' AND [Provenance] = 'Manual');");
        var older = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
        Assert.Equal(51201, older.Number);
        Assert.Equal(applied.Take(index + 1), await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task The_shipped_sources_are_disabled_so_an_import_records_nothing_and_an_empty_store_rolls_back_on_real_sql()
    {
        await using var run = await HostRun.StartAsync(null);
        var (subject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Read, Import);
        using var client = run.Client(subject, CompanyA);

        using (var disabled = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/imports", "k-disabled",
                   new ImportExchangeRatesRequestV1(Day("2026-10-15"), Day("2026-10-16")))))
        {
            // The platform answers every 503 with its own public code; the list read says the source is disabled.
            await AssertProblemAsync(disabled, HttpStatusCode.ServiceUnavailable, "service_unavailable");
        }

        // Review A P2-1: without an enabled source a day is not reopened (503, nothing recorded).
        using (var reverify = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/days/2026-10-15/reverification", "k-r-disabled",
                   new ReverifyExchangeRateDayRequestV1("0", "SENTETIK"))))
        {
            await AssertProblemAsync(reverify, HttpStatusCode.ServiceUnavailable, "service_unavailable");
        }

        var list = await run.JsonAsync(client, $"{Root}?from=2026-10-15&to=2026-10-16");
        Assert.False(list.GetProperty("bulletinSourceEnabled").GetBoolean());
        Assert.False(list.GetProperty("halfDaySourceEnabled").GetBoolean());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateDays]"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] LIKE N'settings.exchange-rate.%'"));
        using (var tooLong = await client.GetAsync($"{Root}?from=2026-01-01&to=2026-12-31"))
        {
            await AssertProblemAsync(tooLong, HttpStatusCode.BadRequest, ExchangeRateProblemCodesV1.RequestInvalid);
        }

        // On an empty store the migration rolls back and re-applies freely.
        await using var context = run.SettingsContext(CompanyA, out _);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.IndexOf(applied, Migration);
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ExchangeRateDays' AND SCHEMA_NAME([schema_id]) = N'settings'"));
        await context.GetService<IMigrator>().MigrateAsync(applied[^1]);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ExchangeRateDays' AND SCHEMA_NAME([schema_id]) = N'settings'"));
    }

    private static (string?, string?, string?) DayState(JsonElement day) => (day.GetProperty("bulletinState").GetString(),
        day.GetProperty("halfDayState").GetString(), day.GetProperty("version").GetString());

    private static (string?, string?, string?, string?, bool) Summary(JsonElement resolution) => (
        resolution.GetProperty("state").GetString(), resolution.GetProperty("bulletinDate").GetString(),
        resolution.GetProperty("sellingRate").GetString(), resolution.GetProperty("provenance").GetString(),
        resolution.GetProperty("halfDayRuleApplied").GetBoolean());

    private static (string?, string?, string?) RateDayCode(string code, string stoppedAt) => ("Unavailable", code, stoppedAt);

    private static (string?, string?, string?) Failure(JsonElement resolution) => (resolution.GetProperty("state").GetString(),
        resolution.GetProperty("code").GetString(), resolution.GetProperty("stoppedAt").GetString());

    private static Task<JsonElement> Resolve(HttpClient client, HostRun run, string rateDay, string currency) =>
        run.JsonAsync(client, $"{Root}/resolution?rateDay={rateDay}&currencyCode={currency}");

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd");

    private static ExchangeRateBulletinFetchV1 Published(string date, params (string Code, string Rate)[] lines) =>
        new(Day(date), ExchangeRateBulletinStatesV1.Published,
            lines.Select(line => new ExchangeRateLineV1(line.Code, line.Rate)).ToArray(), "SENTETIK bülten " + date);

    private static ExchangeRateBulletinFetchV1 NoBulletin(string date) =>
        new(Day(date), ExchangeRateBulletinStatesV1.NoBulletin, [], "SENTETIK 404 " + date);

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"exchange-rate-{key}");
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status == response.StatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static DateOnly IstanbulToday(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A SYNTHETIC bulletin source: the case's answers; a day it does not know fails like the network (unverified).</summary>
    private sealed class SyntheticBulletinSource(IReadOnlyDictionary<DateOnly, ExchangeRateBulletinFetchV1> answers)
        : IExchangeRateBulletinSourceV1
    {
        private readonly Dictionary<DateOnly, ExchangeRateBulletinFetchV1> _answers = new(answers);
        private readonly List<DateOnly> _asked = [];

        public int Calls => _asked.Count;

        public IReadOnlyList<DateOnly> Asked => _asked;

        public bool IsEnabled => true;

        public void Override(DateOnly date, ExchangeRateBulletinFetchV1 answer) => _answers[date] = answer;

        public Task<ExchangeRateBulletinFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            lock (_asked)
            {
                _asked.Add(date);
            }

            return _answers.TryGetValue(date, out var answer)
                ? Task.FromResult(answer)
                : throw new HttpRequestException("SENTETIK ağ hatası");
        }
    }

    private sealed class SyntheticHalfDaySource(IReadOnlyDictionary<DateOnly, string> answers) : IExchangeRateHalfDaySourceV1
    {
        public bool IsEnabled => true;

        public Task<ExchangeRateHalfDayFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExchangeRateHalfDayFetchV1(date,
                answers.TryGetValue(date, out var state) ? state : ExchangeRateHalfDayStatesV1.Undetermined, "SENTETIK takvim"));
    }

    private sealed class FixedActor(string userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    /// <summary>The REAL host over a fresh database, with Identity grants for the HTTP routes.</summary>
    private sealed class HostRun : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory, TimeProvider clock)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
            Today = IstanbulToday(clock);
        }

        public SqlServerRelationalApiFactory Factory { get; }

        /// <summary>
        /// The business date (Europe/Istanbul) of the exchange-rate administration's server clock, which is fixed at the start
        /// of the run: the server's "today" stays this date even when the run crosses Istanbul midnight (21:00 UTC).
        /// </summary>
        public DateOnly Today { get; }

        public static async Task<HostRun> StartAsync(Action<IServiceCollection>? configure)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            // Only the exchange-rate administration (the one server reader of the business date here) takes the fixed
            // clock; Identity and the other modules keep the system clock, so the synthetic grants stay in force.
            TimeProvider clock = new FixedClock(DateTimeOffset.UtcNow);
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
            {
                configure?.Invoke(services);
                services.Replace(ServiceDescriptor.Scoped<ISettingsExchangeRateAdministrationV1>(provider =>
                    ActivatorUtilities.CreateInstance<SettingsExchangeRateAdministration>(provider, clock)));
            });
            var run = new HostRun(baseConnectionString, factory, clock);
            using var client = run.Factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task CommandAsync(HttpClient client, HttpMethod method, string path, string key, object body,
            HttpStatusCode expected, bool replayed = false)
        {
            using var response = await client.SendAsync(Json(method, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {text}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        }

        public async Task<JsonElement> CommandJsonAsync(HttpClient client, string path, string key, object body,
            HttpStatusCode expected, bool replayed = false)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, path, key, body));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {json}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
            return json;
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            return body;
        }

        public SettingsDbContext SettingsContext(Guid company, out CompanyContext companyContext)
        {
            companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
                .Options, companyContext);
        }

        public async Task<(string Subject, string ActorId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK kur kullanıcısı", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"KUR-{roleId:N}", "SENTETIK kur rolü", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor.ToString("D"));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
