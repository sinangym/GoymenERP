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
    private const string Read = SettingsExchangeRatePermissionManifestV1.Read;
    private const string Correct = SettingsExchangeRatePermissionManifestV1.Correct;
    private const string Import = SettingsExchangeRatePermissionManifestV1.Import;

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
        var halfDays = new SyntheticHalfDaySource(new Dictionary<DateOnly, string>
        {
            [Day("2026-10-15")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-16")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-17")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-18")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-19")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-20")] = ExchangeRateHalfDayStatesV1.HalfDay,
            [Day("2026-10-21")] = ExchangeRateHalfDayStatesV1.FullDay,
            [Day("2026-10-22")] = ExchangeRateHalfDayStatesV1.FullDay,
        });
        await using var run = await HostRun.StartAsync(services =>
        {
            services.RemoveAll<IExchangeRateBulletinSourceV1>();
            services.AddSingleton<IExchangeRateBulletinSourceV1>(bulletins);
            services.RemoveAll<IExchangeRateHalfDaySourceV1>();
            services.AddSingleton<IExchangeRateHalfDaySourceV1>(halfDays);
        });
        var (editorSubject, editorActor) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Read, Correct, Import);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, Read);
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Read, Correct, Import);
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
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.exchange-rate.imported.v1' AND [CompanyId] = @c "
            + "AND [PermissionCode] = N'settings.exchange-rates.import'", ("@c", CompanyA)));

        // The list: per day state, half-day state and the per-one-unit rates (JPY normalised by the source to one unit).
        var list = await run.JsonAsync(editor, $"{Root}?from=2026-10-14&to=2026-10-22");
        var days = list.GetProperty("days").EnumerateArray().ToDictionary(day => day.GetProperty("date").GetString()!);
        Assert.Equal(9, days.Count);
        Assert.True(list.GetProperty("bulletinSourceEnabled").GetBoolean());
        Assert.Equal((ExchangeRateBulletinStatesV1.NotRecorded, ExchangeRateHalfDayStatesV1.Undetermined, "0"),
            DayState(days["2026-10-14"]));
        Assert.Equal((ExchangeRateBulletinStatesV1.NoBulletin, ExchangeRateHalfDayStatesV1.FullDay, "2"), DayState(days["2026-10-17"]));
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

        // A re-import never overwrites: same answers are unchanged; a different definite answer is a conflict, nothing written.
        bulletins.Override(Day("2026-10-15"), Published("2026-10-15", ("USD", "99"), ("EUR", "48.25")));
        var again = await run.CommandJsonAsync(editor, $"{Root}/imports", "k-import-2", importBody, HttpStatusCode.OK);
        var againOutcomes = again.GetProperty("days").EnumerateArray()
            .ToDictionary(day => day.GetProperty("date").GetString()!, day => day.GetProperty("bulletinOutcome").GetString());
        Assert.Equal(ExchangeRateImportOutcomesV1.Conflict, againOutcomes["2026-10-15"]);
        Assert.All(againOutcomes.Where(pair => pair.Key != "2026-10-15"),
            pair => Assert.Equal(ExchangeRateImportOutcomesV1.Unchanged, pair.Value));
        Assert.Equal(16, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords]"));
        Assert.Equal(1, await run.ScalarAsync<int>(
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
                   new CorrectExchangeRateRequestV1("2", "USD", "41.65", "SENTETIK"))))
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

        // The rollback is refused while rate records exist (forward fix, 51201); nothing is dropped.
        await using var context = run.SettingsContext(CompanyA, out _);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.IndexOf(applied, Migration);
        Assert.True(index > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
        Assert.Equal(51201, rollback.Number);
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

    private static DateOnly IstanbulToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    /// <summary>A SYNTHETIC bulletin source: the case's answers; a day it does not know fails like the network (unverified).</summary>
    private sealed class SyntheticBulletinSource(IReadOnlyDictionary<DateOnly, ExchangeRateBulletinFetchV1> answers)
        : IExchangeRateBulletinSourceV1
    {
        private readonly Dictionary<DateOnly, ExchangeRateBulletinFetchV1> _answers = new(answers);
        private int _calls;

        public int Calls => _calls;

        public bool IsEnabled => true;

        public void Override(DateOnly date, ExchangeRateBulletinFetchV1 answer) => _answers[date] = answer;

        public Task<ExchangeRateBulletinFetchV1> FetchAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
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

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public DateOnly Today { get; } = IstanbulToday();

        public static async Task<HostRun> StartAsync(Action<IServiceCollection>? configure)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new HostRun(baseConnectionString, new SqlServerRelationalApiFactory(baseConnectionString, configure));
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
