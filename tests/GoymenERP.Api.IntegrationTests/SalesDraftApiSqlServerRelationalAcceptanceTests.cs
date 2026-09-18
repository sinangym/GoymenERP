using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The Sales API slice against a REAL SQL Server through the REAL host: authentication, company
/// context, the three draft routes, the draft service measuring fresh grants through the central
/// authority, and the host's real owners (the Counterparty party read, the approved Settings
/// provider, the Product eligibility runtime as composed). Callers are the factory's development
/// users; their Sales grants are synthetic roles on the registered names, created in this test's own
/// database and never seeded. This is API evidence, not a user outcome: it measures HONESTLY what a
/// create answers today with the real owners — the first owner that cannot answer refuses, and
/// nothing is stored. Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesDraftApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly string[] DraftGrants = [SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead];

    [SqlServerFact]
    public async Task The_draft_routes_answer_only_with_fresh_sales_grants_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();

        // Company A's read-only user holds no Sales permission (none is seeded): every route refuses, code-less.
        using (var viewer = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(SalesEndpoints.DraftsPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{SalesEndpoints.DraftsPath}/{Guid.NewGuid()}")).StatusCode);
            using var save = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() };
            save.Headers.Add("Idempotency-Key", "SENTETIK-viewer-1");
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(save)).StatusCode);
        }

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        {
            var list = await client.GetAsync(SalesEndpoints.DraftsPath);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.True(list.Headers.CacheControl is { Private: true, NoStore: true });
            var page = await Result(list);
            Assert.Empty(page.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);

            // A malformed cursor and an out-of-range page size are bad requests with their codes.
            var cursor = await client.GetAsync($"{SalesEndpoints.DraftsPath}?cursor=not-a-cursor");
            Assert.Equal(HttpStatusCode.BadRequest, cursor.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.InvalidCursor, (await Result(cursor)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesEndpoints.DraftsPath}?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{SalesEndpoints.DraftsPath}/{Guid.NewGuid()}")).StatusCode);
        }

        // The same grants in company B are nothing for company A: B's reader naming A is refused before any read.
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, DraftGrants);
        using (var foreign = run.Factory.CreateClientFor(readerOfB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync(SalesEndpoints.DraftsPath)).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
    }

    [SqlServerFact]
    public async Task A_create_reaches_its_real_owners_in_order_and_answers_the_first_refusal_without_storing_anything()
    {
        await using var run = await Run.StartAsync();
        var saver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using var client = run.Factory.CreateClientFor(saver, CompanyA);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        // Without an Idempotency-Key the save is not attempted; a non-canonical or repeated key neither.
        using (var noKey = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        {
            var response = await client.SendAsync(noKey);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid, (await Result(response)).GetProperty("code").GetString());
        }

        using (var padded = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        {
            padded.Headers.Add("Idempotency-Key", " SENTETIK-1 ");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(padded)).StatusCode);
        }

        // A body outside the strict #257 shape (an unknown member) is refused before the service.
        using (var extra = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath)
        {
            Content = new StringContent("{\"kind\":\"Order\",\"companyId\":\"x\"}", Encoding.UTF8, "application/json"),
        })
        {
            extra.Headers.Add("Idempotency-Key", "SENTETIK-shape");
            var response = await client.SendAsync(extra);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await Result(response)).GetProperty("code").GetString());
        }

        // The card's fail-closed deltas answer before any owner: a commercial field, an owner reference.
        using (var priced = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body(unitPrice: "12.50") })
        {
            priced.Headers.Add("Idempotency-Key", "SENTETIK-priced");
            var response = await client.SendAsync(priced);
            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            var problem = await Result(response);
            Assert.Equal(SalesDraftProblemCodesV1.CommercialNotAvailable, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].unitPrice", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        // With a well-formed request the first REAL owner answers: the Counterparty party read knows no
        // such party in this company, so the customer is refused (409 account_state) — today's honest
        // behaviour with the real providers; the money profile and the Product runtime are never reached.
        using (var keyed = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        {
            keyed.Headers.Add("Idempotency-Key", "SENTETIK-save-1");
            var response = await client.SendAsync(keyed);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await Result(response);
            Assert.Equal(SalesDraftProblemCodesV1.AccountState, problem.GetProperty("code").GetString());
            Assert.Contains(problem.GetProperty("errors").EnumerateArray(), error => error.GetProperty("field").GetString() == "partyId");
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft'"));
    }

    [SqlServerFact]
    public async Task A_stored_draft_is_read_by_its_company_only_and_its_cursor_binds_the_company()
    {
        await using var run = await Run.StartAsync();
        var draftOfA = await run.StoredDraftAsync(CompanyA);
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using (var clientOfA = run.Factory.CreateClientFor(readerOfA, CompanyA))
        {
            var detail = await clientOfA.GetAsync($"{SalesEndpoints.DraftsPath}/{draftOfA}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await Result(detail);
            Assert.Equal(draftOfA, body.GetProperty("draft").GetProperty("id").GetGuid());
            Assert.Equal(CompanyA, body.GetProperty("draft").GetProperty("companyId").GetGuid());
            Assert.Equal("Rev.0", body.GetProperty("draft").GetProperty("revision").GetString());
            Assert.Single(body.GetProperty("lines").EnumerateArray());

            var list = await Result(await clientOfA.GetAsync(SalesEndpoints.DraftsPath));
            Assert.Equal(draftOfA, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, DraftGrants);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draftOfA}")).StatusCode);
            Assert.Empty((await Result(await clientOfB.GetAsync(SalesEndpoints.DraftsPath))).GetProperty("items").EnumerateArray());
            var cursorOfA = SalesStore.ListCursor.Encode(CompanyA, DateTimeOffset.UtcNow, Guid.NewGuid());
            var foreign = await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}?cursor={Uri.EscapeDataString(cursorOfA)}");
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.InvalidCursor, (await Result(foreign)).GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// The real host, the real store and the real SQL Server with SENTETIK owner ports (Cari, Settings,
    /// Product answer fixed identities for the context company; nothing else is replaced): ADR-011
    /// §8 — two callers of the same key and content meet at the Product port and race the store: one
    /// draft, one attempt, one audit row, both answer that draft (201 + Location for the winner, 200 +
    /// replay headers for the loser); §4 — the key replays it again with the first correlation; §7 — a
    /// stale product version is recorded and replayed as the same 409 with the replay headers; a
    /// provider's 503 reaches the wire as the platform's public code only; the raced draft reads back.
    /// </summary>
    [SqlServerFact]
    public async Task A_same_key_race_stores_one_draft_and_every_replay_holds_in_the_real_host()
    {
        await using var run = await Run.StartAsync(services =>
        {
            services.RemoveAll<ISalesPartyAccountPortV1>();
            services.AddScoped<ISalesPartyAccountPortV1, SyntheticAccounts>();
            services.RemoveAll<ISettingsPrerequisiteProvider>();
            services.AddScoped<ISettingsPrerequisiteProvider, SyntheticSettings>();
            services.RemoveAll<IProductSalesEligibilityV1>();
            services.AddSingleton<SyntheticGate>();
            services.AddScoped<IProductSalesEligibilityV1, SyntheticProducts>();
        });
        var saver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using var client = run.Factory.CreateClientFor(saver, CompanyA);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");

        // §8: the same key and content twice at once; the gate holds both at the Product port until both
        // passed the replay lookup, so both reach the store and exactly one insert wins.
        var raced = Json(GateProduct);
        var correlationA = Guid.NewGuid().ToString("N");
        var correlationB = Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(
            client.SendAsync(Post("SENTETIK-race", raced, correlationA)),
            client.SendAsync(Post("SENTETIK-race", raced, correlationB)));
        var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var draftId = (await Result(winner)).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid();
        Assert.Equal(draftId, (await Result(loser)).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
        Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), winner.Headers.Location);
        Assert.False(winner.Headers.Contains("Idempotency-Replayed"));
        var winnerCorrelation = Assert.Single(winner.Headers.GetValues("X-Correlation-Id"));
        Assert.Contains(winnerCorrelation, new[] { correlationA, correlationB });
        Assert.Equal("true", Assert.Single(loser.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(winnerCorrelation, Assert.Single(loser.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE Outcome = 1 AND DraftId = @id AND RefusalJson IS NULL AND CorrelationId = @c",
            ("@id", draftId), ("@c", winnerCorrelation)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft'"));

        // §4: the same key later answers the same draft with the first correlation, without the owners.
        using (var again = await client.SendAsync(Post("SENTETIK-race", raced, Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal(draftId, (await Result(again)).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
            Assert.Equal(winnerCorrelation, Assert.Single(again.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        // §7: the Product owner's stale version is the key's terminal result — recorded, then replayed.
        var stale = Json(StaleProduct);
        var staleCorrelation = Guid.NewGuid().ToString("N");
        using (var first = await client.SendAsync(Post("SENTETIK-stale", stale, staleCorrelation)))
        {
            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await Result(first)).GetProperty("code").GetString());
            Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        }

        using (var replayed = await client.SendAsync(Post("SENTETIK-stale", stale, Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            var problem = await Result(replayed);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].productId", problem.GetProperty("errors")[0].GetProperty("field").GetString());
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(staleCorrelation, Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE Outcome = 2 AND DraftId IS NULL AND ISJSON(RefusalJson) = 1 AND CorrelationId = @c",
            ("@c", staleCorrelation)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));

        // A provider outage is the platform's 503: its own code never reaches the wire, nothing is recorded.
        using (var outage = await client.SendAsync(Post("SENTETIK-outage", Json(GateProduct, UnavailableParty), Guid.NewGuid().ToString("N"))))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, outage.StatusCode);
            var body = await outage.Content.ReadAsStringAsync();
            Assert.Equal("service_unavailable", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("counterparty_provider_unavailable", body, StringComparison.Ordinal);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));

        // The raced draft is a real stored draft of the company.
        using var detail = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{draftId:D}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var stored = await Result(detail);
        Assert.Equal(draftId, stored.GetProperty("draft").GetProperty("id").GetGuid());
        Assert.Equal(1, stored.GetProperty("draft").GetProperty("lineCount").GetInt32());
    }

    private static readonly Guid GateProduct = Guid.Parse("5e11e000-0000-4000-8000-000000000001");
    private static readonly Guid StaleProduct = Guid.Parse("5e11e000-0000-4000-8000-000000000002");
    private static readonly Guid UnavailableParty = Guid.Parse("5e11e000-0000-4000-8000-000000000003");
    private static readonly Guid KgProfile = Guid.Parse("5e11e000-0000-4000-8000-000000000004");
    private static readonly Guid MoneyProfile = Guid.Parse("5e11e000-0000-4000-8000-000000000005");
    private static readonly Guid FixedParty = Guid.Parse("5e11e000-0000-4000-8000-000000000006");
    private static readonly Guid FixedAccount = Guid.Parse("5e11e000-0000-4000-8000-000000000007");
    private static readonly Guid FixedCurrency = Guid.Parse("5e11e000-0000-4000-8000-000000000008");
    private static readonly Guid FixedUnit = Guid.Parse("5e11e000-0000-4000-8000-000000000009");
    private static readonly Guid FixedLine = Guid.Parse("5e11e000-0000-4000-8000-00000000000a");

    /// <summary>A fixed #257 body (the same bytes each time it is asked for the same product and party).</summary>
    private static string Json(Guid productId, Guid? partyId = null)
    {
        var line = new SalesOrderDraftLineRequest(FixedLine, productId, "7", FixedUnit, "1.25");
        var request = new CreateSalesOrderDraftRequest("Order", partyId ?? FixedParty, FixedAccount, new DateOnly(2026, 9, 18), FixedCurrency, [line]);
        return JsonSerializer.Serialize(request, SalesDraftJson.Options);
    }

    private static HttpRequestMessage Post(string idempotencyKey, string json, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId);
        return request;
    }

    /// <summary>Holds the first two callers of the gate product until both arrived; later callers pass; a lone caller fails visibly.</summary>
    private sealed class SyntheticGate
    {
        private readonly TaskCompletionSource<bool> both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref arrivals) >= 2)
            {
                both.TrySetResult(true);
            }

            return both.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private sealed class SyntheticAccounts(ICompanyContext companyContext) : ISalesPartyAccountPortV1
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(partyId == UnavailableParty
                ? new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Unavailable, null)
                : new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Found,
                    new SalesPartyAccountV1(partyId, companyAccountId, companyContext.CompanyId, true, true, "3", "2")));
    }

    private sealed class SyntheticSettings(ICompanyContext companyContext) : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(companyContext.CompanyId, currencyId, "TRY", operation, new ProfileVersionV1(MoneyProfile, 3), 2, true,
                new DateOnly(2026, 9, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UnitProfileV1(companyContext.CompanyId, unitId, "kg", "weight", true, new ProfileVersionV1(KgProfile, 4), 3, true,
                new DateOnly(2026, 9, 1), null));

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SyntheticProducts(ICompanyContext companyContext, SyntheticGate gate) : IProductSalesEligibilityV1
    {
        public async Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        {
            if (selection.ProductId == StaleProduct)
            {
                return new ProductSalesEligibilityV1(null, [new("productId", ProductApiV1.VersionConflict)]);
            }

            if (selection.ProductId == GateProduct)
            {
                await gate.ArriveAsync();
            }

            var unit = new ProductSalesUnitProfileV1(companyContext.CompanyId, selection.OrderUnitId, KgProfile, "4");
            return new ProductSalesEligibilityV1(
                new ProductSalesSnapshotV1(selection.ProductId, "7", "GDC-001", "Sentetik bant", unit, unit, unit, selection.EffectiveOn), []);
        }
    }

    private static StringContent Body(string? unitPrice = null)
    {
        var line = new SalesOrderDraftLineRequest(Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(), "1.25", unitPrice,
            unitPrice is null ? null : Guid.NewGuid());
        var request = new CreateSalesOrderDraftRequest("Order", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 18), Guid.NewGuid(), [line]);
        return new StringContent(JsonSerializer.Serialize(request, SalesDraftJson.Options), Encoding.UTF8, "application/json");
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, configureServices);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // Idempotent if the host already applied Sales's migrations.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A SENTETIK stored draft of the company with one line, written as rows (its owners are synthetic identities).</summary>
        public async Task<Guid> StoredDraftAsync(Guid company)
        {
            var id = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var profile = Guid.NewGuid();
            await ExecuteAsync(
                "INSERT INTO sales.Drafts (Id, CompanyId, DocumentNo, Kind, State, Revision, Version, PartyId, CompanyAccountId, CurrencyId, "
                + "BusinessDate, MoneyOperation, MoneyProfileId, MoneyProfileVersion, MoneyEffectiveOn, MoneyIsActive, MoneyEffectiveFrom, "
                + "MoneyScale, CreatedAtUtc, UpdatedAtUtc, CreatedBy, CorrelationId) VALUES (@id, @c, N'SO-2026-000001', 1, 1, 0, 2, NEWID(), "
                + "NEWID(), NEWID(), '2026-09-18', N'sales.order-draft.v1', NEWID(), N'3', '2026-09-18', 1, '2026-09-01', 2, "
                + "SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'SENTETIK-actor', N'SENTETIK-correlation')",
                ("@id", id), ("@c", company));
            await ExecuteAsync(
                "INSERT INTO sales.DraftLines (CompanyId, DraftId, [LineNo], LineId, ProductId, ProductVersion, ProductCode, ProductName, "
                + "ProductEffectiveOn, StockUnitCompanyId, StockUnitId, StockUnitProfileId, StockUnitProfileVersion, SalesUnitCompanyId, SalesUnitId, "
                + "SalesUnitProfileId, SalesUnitProfileVersion, OrderUnitBindingCompanyId, OrderUnitBindingUnitId, OrderUnitBindingProfileId, "
                + "OrderUnitBindingProfileVersion, OrderUnitCompanyId, OrderUnitId, OrderUnitCode, OrderUnitDimensionCode, OrderUnitProfileId, "
                + "OrderUnitProfileVersion, OrderUnitIsKilogram, OrderUnitProfileScale, OrderUnitScale, OrderUnitIsActive, OrderUnitEffectiveFrom, "
                + "Quantity, QuantityText) VALUES (@c, @id, 1, NEWID(), NEWID(), N'7', N'GDC-001', N'Sentetik bant', '2026-09-18', "
                + "@c, @u, @p, N'4', @c, @u, @p, N'4', @c, @u, @p, N'4', @c, @u, N'kg', N'weight', @p, N'4', 1, 3, 3, 1, '2026-09-01', 1.250, N'1.25')",
                ("@id", id), ("@c", company), ("@u", unit), ("@p", profile));
            return id;
        }

        /// <summary>A synthetic role with exactly these permissions in one company, assigned to a configured development user.</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SALES-API-{roleId:N}", "Synthetic Sales test role", true);
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
            return developmentUser;
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

        private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }
    }
}
