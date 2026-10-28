using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The draft routes' HTTP boundary over a SENTETIK <see cref="ISalesDraftService"/> (no database,
/// no owner): what the wire carries for each service answer. The service's own rules are unit-tested
/// (<c>SalesDraftServiceTests</c>); the real host with the real store is the SQL Server class.
/// </summary>
public sealed class SalesDraftHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Theory]
    [InlineData("permission_unregistered")]
    [InlineData("counterparty_provider_unavailable")]
    [InlineData("settings_provider_unavailable")]
    [InlineData("reference_provider_invalid")]
    [InlineData("product_eligibility_unavailable")]
    [InlineData("service_unavailable")]
    public async Task A_503_of_the_draft_service_reaches_the_wire_only_as_the_platforms_public_code(string sourceCode)
    {
        var service = new StubDraftService { Create = _ => throw new SalesDraftServiceException(503, sourceCode) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var request = Post("SENTETIK-503");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("code").GetString());
        if (!string.Equals(sourceCode, "service_unavailable", StringComparison.Ordinal))
        {
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
        }

        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task A_created_draft_answers_201_with_its_absolute_detail_location_and_no_replay_header()
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService { Create = _ => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var request = Post("SENTETIK-created");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), response.Headers.Location);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(draftId, body.GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
        Assert.False(body.GetProperty("detail").GetProperty("draft").TryGetProperty("productSnapshots", out _));
        Assert.Equal("SENTETIK-created", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task List_and_detail_keep_the_legacy_default_and_publish_party_display_only_for_the_exact_sales_contract()
    {
        var draftId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var snapshot = new SalesOrderDraftProductSnapshotViewV1(
            productId, "7", "GDC-001", "Sentetik bant", unitId, "kg");
        var summary = new SalesOrderDraftSummaryView(
            draftId, CompanyA, "SO-2026-000001", "Order", "Draft", "3", "Rev.0",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 18),
            new DateTimeOffset(2026, 9, 18, 14, 0, 0, TimeSpan.Zero), 1, "Preparing",
            SalesCommercialStageV1.Order, [snapshot]);
        var line = new SalesOrderDraftLineDetail(
            Guid.NewGuid(), 1, productId, "7", "GDC-001", "Sentetik bant", unitId, "1.25",
            null, null, null, null, null, null, SalesPriceDisclosure.Withheld, "kg");
        var detail = new SalesOrderDraftDetailView(
            summary, null, null, [line], null, null, null, null, null, null,
            new SalesDraftApprovalV1(null, null, null, null, null),
            new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);
        var service = new StubDraftService
        {
            List = _ => Task.FromResult(new SalesOrderDraftListResponse([summary], null)),
            DetailRead = _ => Task.FromResult(detail),
        };
        var display = new SalesPartyDisplayV1(summary.PartyId, "CAR-000001", "Sentetik Musteri A.S.", "Sentetik Musteri");
        var submitter = Guid.NewGuid();
        var readService = new StubDraftReadService
        {
            List = _ => Task.FromResult(new SalesOrderDraftReadListResponseV1(
                [ReadSummary(summary, display)], null)),
            Detail = _ => Task.FromResult(ReadDetail(detail, display,
                [new SalesUserDisplayV1(submitter, "SENTETIK Gonderen", false)])),
        };
        using var host = CreateHost(service, readService);
        using var client = CreateClient(host);

        using (var legacyListResponse = await client.GetAsync(SalesEndpoints.DraftsPath))
        {
            Assert.Equal(HttpStatusCode.OK, legacyListResponse.StatusCode);
            Assert.False(legacyListResponse.Headers.Contains(SalesEndpoints.DraftReadContractHeader));
            var legacyBody = await legacyListResponse.Content.ReadAsStringAsync();
            Assert.Equal(
                JsonSerializer.Serialize(new SalesOrderDraftListResponse([summary], null), SalesDraftJson.Options),
                legacyBody);
            using var legacyDocument = JsonDocument.Parse(legacyBody);
            var legacy = legacyDocument.RootElement;
            var legacyItem = legacy.GetProperty("items")[0];
            Assert.False(legacyItem.TryGetProperty("partyDisplay", out _));
            Assert.Equal(summary.PartyId, legacyItem.GetProperty("partyId").GetGuid());
        }

        using (var legacyDetailResponse = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{draftId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, legacyDetailResponse.StatusCode);
            Assert.False(legacyDetailResponse.Headers.Contains(SalesEndpoints.DraftReadContractHeader));
            var legacyBody = await legacyDetailResponse.Content.ReadAsStringAsync();
            Assert.Equal(JsonSerializer.Serialize(detail, SalesDraftJson.Options), legacyBody);
            using var legacyDocument = JsonDocument.Parse(legacyBody);
            var legacy = legacyDocument.RootElement;
            Assert.False(legacy.TryGetProperty("partyDisplay", out _));
            Assert.False(legacy.TryGetProperty("userDisplays", out _));
            Assert.Equal(summary.PartyId, legacy.GetProperty("draft").GetProperty("partyId").GetGuid());
        }

        client.DefaultRequestHeaders.Add(
            SalesEndpoints.DraftReadContractHeader,
            SalesEndpoints.DraftReadContractVersion);

        using (var listResponse = await client.GetAsync(SalesEndpoints.DraftsPath))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            Assert.Equal(SalesEndpoints.DraftReadContractVersion,
                Assert.Single(listResponse.Headers.GetValues(SalesEndpoints.DraftReadContractHeader)));
            var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
            var item = list.GetProperty("items")[0];
            Assert.Equal(CompanyA, item.GetProperty("companyId").GetGuid());
            Assert.Equal((summary.PartyId, "CAR-000001", "Sentetik Musteri A.S.", "Sentetik Musteri"),
                (item.GetProperty("partyDisplay").GetProperty("partyId").GetGuid(),
                    item.GetProperty("partyDisplay").GetProperty("code").GetString(),
                    item.GetProperty("partyDisplay").GetProperty("legalName").GetString(),
                    item.GetProperty("partyDisplay").GetProperty("tradeName").GetString()));
            var product = item.GetProperty("productSnapshots")[0];
            Assert.Equal((productId, "7", "GDC-001", "Sentetik bant", unitId, "kg"),
                (product.GetProperty("productId").GetGuid(), product.GetProperty("productVersion").GetString(),
                    product.GetProperty("productCode").GetString(), product.GetProperty("productName").GetString(),
                    product.GetProperty("orderUnitId").GetGuid(), product.GetProperty("orderUnitCode").GetString()));
        }

        using (var detailResponse = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{draftId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            Assert.Equal(SalesEndpoints.DraftReadContractVersion,
                Assert.Single(detailResponse.Headers.GetValues(SalesEndpoints.DraftReadContractHeader)));
            var read = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(display.PartyId, read.GetProperty("partyDisplay").GetProperty("partyId").GetGuid());
            Assert.Equal(display.Code, read.GetProperty("partyDisplay").GetProperty("code").GetString());
            // MOD-08: the enriched detail names the people it identifies, as the Identity directory shows them.
            var person = Assert.Single(read.GetProperty("userDisplays").EnumerateArray().ToArray());
            Assert.Equal(["displayName", "isActiveInCompany", "userId"],
                person.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal((submitter, "SENTETIK Gonderen", false),
                (person.GetProperty("userId").GetGuid(), person.GetProperty("displayName").GetString(),
                    person.GetProperty("isActiveInCompany").GetBoolean()));
            var publishedLine = read.GetProperty("lines")[0];
            Assert.Equal("kg", publishedLine.GetProperty("orderUnitCode").GetString());
            Assert.Equal("Withheld", publishedLine.GetProperty("priceDisclosure").GetString());
            Assert.Equal(JsonValueKind.Null, publishedLine.GetProperty("unitPrice").ValueKind);
            Assert.Equal(JsonValueKind.Null, publishedLine.GetProperty("priceUnitId").ValueKind);
            Assert.Equal(JsonValueKind.Null, publishedLine.GetProperty("discountPercent").ValueKind);
        }
    }

    [Theory]
    [InlineData("unknown-v1")]
    [InlineData("record-party-display-v1,record-party-display-v1")]
    public async Task List_and_detail_reject_an_unknown_or_ambiguous_sales_read_contract_without_calling_either_reader(
        string declaration)
    {
        var service = new StubDraftService
        {
            List = _ => throw new InvalidOperationException("Legacy reader must not run."),
            DetailRead = _ => throw new InvalidOperationException("Legacy reader must not run."),
        };
        var readService = new StubDraftReadService
        {
            List = _ => throw new InvalidOperationException("Enriched reader must not run."),
            Detail = _ => throw new InvalidOperationException("Enriched reader must not run."),
        };
        using var host = CreateHost(service, readService);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            SalesEndpoints.DraftReadContractHeader,
            declaration);

        using var list = await client.GetAsync(SalesEndpoints.DraftsPath);
        using var detail = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, detail.StatusCode);
        Assert.False(list.Headers.Contains(SalesEndpoints.DraftReadContractHeader));
        Assert.False(detail.Headers.Contains(SalesEndpoints.DraftReadContractHeader));
    }

    [Theory]
    [InlineData(CounterpartySalesRecordPartyDisplayOutcomeV1.Denied, SalesPartyDisplayBatchOutcomeV1.Denied)]
    [InlineData(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable, SalesPartyDisplayBatchOutcomeV1.Unavailable)]
    public async Task Central_display_host_forwards_the_server_context_and_maps_closed_owner_outcomes(
        CounterpartySalesRecordPartyDisplayOutcomeV1 ownerOutcome,
        SalesPartyDisplayBatchOutcomeV1 expected)
    {
        var partyId = Guid.NewGuid();
        var owner = new StubSalesRecordPartyDisplaySource
        {
            Answer = new CounterpartySalesRecordPartyDisplayAnswerV1(ownerOutcome, []),
        };
        var host = new SalesPartyDisplayHost(owner);

        var answer = await host.GetAuthorizedBatchAsync(
            new SalesPartyDisplayBatchRequestV1(CompanyA, "SENTETIK-actor", [partyId]),
            CancellationToken.None);

        Assert.Equal(expected, answer.Outcome);
        Assert.Empty(answer.Items);
        Assert.Equal((CompanyA, "SENTETIK-actor", partyId),
            (owner.Request!.CompanyId, owner.Request.ActorId, Assert.Single(owner.Request.PartyIds)));
    }

    [Fact]
    public async Task Central_display_host_maps_the_exact_owner_rows_and_preserves_cancellation()
    {
        var partyId = Guid.NewGuid();
        var owner = new StubSalesRecordPartyDisplaySource
        {
            Answer = new CounterpartySalesRecordPartyDisplayAnswerV1(
                CounterpartySalesRecordPartyDisplayOutcomeV1.Found,
                [new(partyId, "CAR-000002", "Bagli Musteri A.S.", null)]),
        };
        var host = new SalesPartyDisplayHost(owner);

        var answer = await host.GetAuthorizedBatchAsync(
            new SalesPartyDisplayBatchRequestV1(CompanyA, "SENTETIK-actor", [partyId]),
            CancellationToken.None);
        Assert.Equal(SalesPartyDisplayBatchOutcomeV1.Success, answer.Outcome);
        Assert.Equal(new SalesPartyDisplayV1(partyId, "CAR-000002", "Bagli Musteri A.S.", null),
            Assert.Single(answer.Items));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.GetAuthorizedBatchAsync(
            new SalesPartyDisplayBatchRequestV1(CompanyA, "SENTETIK-actor", [partyId]),
            cancelled.Token));
    }

    [Fact]
    public async Task A_replayed_draft_and_a_replayed_refusal_carry_the_replay_headers_of_the_first_attempt()
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService { Create = _ => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-first")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var request = Post("SENTETIK-replay"))
        using (var response = await client.SendAsync(request))
        {
            // ADR-011 §4: the persisted 201 is replayed, told apart by the headers only.
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), response.Headers.Location);
        }

        service.Create = _ => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict,
            [new("lines[0].productId", "product_version_conflict")], isReplay: true, originalCorrelationId: "SENTETIK-first");
        using (var request = Post("SENTETIK-replay"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].productId", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task A_missing_repeated_or_over_long_key_is_refused_before_the_service()
    {
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var none = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        using (var response = await client.SendAsync(none))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var twice = Post("SENTETIK-a"))
        {
            twice.Headers.Add("Idempotency-Key", "SENTETIK-b");
            using var response = await client.SendAsync(twice);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var over = Post(new string('k', 129)))
        using (var response = await client.SendAsync(over))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var exact = Post(new string('k', 128)))
        using (var response = await client.SendAsync(exact))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(new[] { new string('k', 128) }, service.Keys);
    }

    private static HttpRequestMessage Post(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static StringContent Body()
    {
        var line = new SalesOrderDraftLineRequest(Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(), "1.25", null, null);
        var request = new CreateSalesOrderDraftRequest("Order", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 18), Guid.NewGuid(), [line]);
        return new StringContent(JsonSerializer.Serialize(request, SalesDraftJson.Options), Encoding.UTF8, "application/json");
    }

    private static SalesOrderDraftDetailView Detail(Guid draftId, string approvalState = "Preparing") => new(
        new SalesOrderDraftSummaryView(draftId, CompanyA, "SO-2026-000001", "Order", "Draft", "1", "Rev.0", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 9, 18), new DateTimeOffset(2026, 9, 18, 14, 0, 0, TimeSpan.Zero), 0, approvalState),
        null, null, [], null, null, null, null, null, null, new SalesDraftApprovalV1(null, null, null, null, null),
        new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);

    private static SalesOrderDraftReadSummaryViewV1 ReadSummary(
        SalesOrderDraftSummaryView draft,
        SalesPartyDisplayV1 display) => new(
        draft.Id, draft.CompanyId, draft.DocumentNo, draft.Kind, draft.State, draft.Version, draft.Revision,
        draft.PartyId, draft.CompanyAccountId, draft.CurrencyId, draft.BusinessDate, draft.CreatedAtUtc,
        draft.LineCount, draft.ApprovalState, draft.CommercialStage, draft.ProductSnapshots, display);

    private static SalesOrderDraftReadDetailViewV1 ReadDetail(
        SalesOrderDraftDetailView detail,
        SalesPartyDisplayV1 display,
        IReadOnlyList<SalesUserDisplayV1>? users = null) => new(
        detail.Draft, detail.RequestedDeliveryDate, detail.Note, detail.Lines, detail.DeliveryAddress,
        detail.Contact, detail.Responsible, detail.PaymentTerms, detail.SourceStrategy, detail.Package,
        detail.Approval, detail.CustomerAcceptance, detail.IsOwnDraft, detail.IsOwnDecisionRestricted, display, users ?? []);

    private WebApplicationFactory<Program> CreateHost(
        ISalesDraftService service,
        ISalesDraftReadServiceV1? readService = null) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesDraftService>();
            services.AddSingleton(service);
            services.RemoveAll<ISalesDraftReadServiceV1>();
            services.AddSingleton(readService ?? new StubDraftReadService());
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private sealed class StubDraftService : ISalesDraftService
    {
        public List<string> Keys { get; } = [];

        public Func<CreateSalesOrderDraftRequest, Task<SalesOrderDraftResponse>> Create { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftResponse(Detail(Guid.NewGuid()), false, "SENTETIK-corr"));

        public Task<SalesOrderDraftResponse> CreateDraftAsync(CreateSalesOrderDraftRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Func<Guid, Task<SalesOrderDraftDetailView>> DetailRead { get; set; } =
            _ => Task.FromException<SalesOrderDraftDetailView>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesOrderDraftDetailView> GetDraftDetailAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            DetailRead(draftId);

        public Func<SalesOrderDraftListQuery, Task<SalesOrderDraftListResponse>> List { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftListResponse([], null));

        public Task<SalesOrderDraftListResponse> GetDraftListAsync(SalesOrderDraftListQuery query, CancellationToken cancellationToken = default) =>
            List(query);

        /// <summary>SALES-A1: what the stub answers a state command with; the command name and body reach <see cref="StateCalls"/>.</summary>
        public Func<string, Guid, SalesDraftStateCommandRequest, Task<SalesOrderDraftResponse>> State { get; set; } =
            (_, id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "PendingApproval"), false, "SENTETIK-corr"));

        public List<(string Command, Guid DraftId, string ExpectedVersion, string? Reason, string Key)> StateCalls { get; } = [];

        public Func<Guid, SalesDraftCommercialRevisionRequest, Task<SalesOrderDraftResponse>> Commercial { get; set; } =
            (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id), false, "SENTETIK-corr"));

        public List<(Guid DraftId, SalesDraftCommercialRevisionRequest Request, string Key)> CommercialCalls { get; } = [];

        public Task<SalesOrderDraftResponse> ReviseCommercialAsync(Guid draftId, SalesDraftCommercialRevisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            CommercialCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Commercial(draftId, request);
        }

        public Task<SalesOrderDraftResponse> ApprovePriceFloorExceptionsAsync(Guid draftId,
            SalesDraftPriceFloorExceptionApprovalRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), false, "SENTETIK-corr"));

        /// <summary>SAT-D2 / API-12: what the stub answers a discount-exception approval with; the body and key reach <see cref="DiscountApprovalCalls"/>.</summary>
        public Func<Guid, SalesDraftDiscountExceptionApprovalRequest, Task<SalesOrderDraftResponse>> DiscountApproval { get; set; } =
            (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id), false, "SENTETIK-corr"));

        public List<(Guid DraftId, SalesDraftDiscountExceptionApprovalRequest Request, string Key)> DiscountApprovalCalls { get; } = [];

        public Task<SalesOrderDraftResponse> ApproveDiscountExceptionsAsync(Guid draftId,
            SalesDraftDiscountExceptionApprovalRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            DiscountApprovalCalls.Add((draftId, request, metadata.IdempotencyKey));
            return DiscountApproval(draftId, request);
        }

        public Func<Guid, Task<SalesOrderDraftRevisionsResponse>> Revisions { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftRevisionsResponse([]));

        public List<Guid> RevisionReads { get; } = [];

        /// <summary>AT-018: the HTTP surface of the line-pricing reads is proven with the real service; the stub answers 404.</summary>
        public Task<SalesOrderDraftLinePricingResponse> GetDraftLinePricingAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Task.FromException<SalesOrderDraftLinePricingResponse>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesOrderDraftPriceReferencesResponse> GetLinePriceReferencesAsync(Guid draftId, Guid lineId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<SalesOrderDraftPriceReferencesResponse>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            RevisionReads.Add(draftId);
            return Revisions(draftId);
        }

        /// <summary>DEC-SAL-002: what the stub answers a quantity revision with; the calls reach <see cref="QuantityCalls"/>.</summary>
        public Func<Guid, SalesDraftQuantityRevisionRequest, Task<SalesOrderDraftResponse>> Quantity { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public List<(Guid DraftId, SalesDraftQuantityRevisionRequest Request, string Key)> QuantityCalls { get; } = [];

        public Task<SalesOrderDraftResponse> ReviseQuantityAsync(Guid draftId, SalesDraftQuantityRevisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            QuantityCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Quantity(draftId, request);
        }

        /// <summary>DEC-SAL-003: the delivery-schedule update and its history; this stub answers nothing it was not given.</summary>
        public Func<Guid, SalesDraftDeliveryScheduleRequest, Task<SalesOrderDraftResponse>> Schedule { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public List<(Guid DraftId, SalesDraftDeliveryScheduleRequest Request, string Key)> ScheduleCalls { get; } = [];

        public Task<SalesOrderDraftResponse> RescheduleDeliveryAsync(Guid draftId, SalesDraftDeliveryScheduleRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ScheduleCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Schedule(draftId, request);
        }

        public Task<SalesOrderDraftDeliverySchedulesResponse> GetDraftDeliverySchedulesAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            throw new SalesDraftServiceException(404, "not_found");

        /// <summary>AT-024: what the stub answers the delivery-progress read with; the draft ids asked reach <see cref="ProgressReads"/>.</summary>
        public Func<Guid, Task<SalesOrderDraftDeliveryProgressResponse>> Progress { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> ProgressReads { get; } = [];

        public Task<SalesOrderDraftDeliveryProgressResponse> GetDraftDeliveryProgressAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            ProgressReads.Add(draftId);
            return Progress(draftId);
        }

        /// <summary>DEC-SAL-005: what the stub answers the loading-progress read with.</summary>
        public Func<Guid, Task<SalesOrderDraftLoadingProgressResponse>> LoadingProgress { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> LoadingProgressReads { get; } = [];

        public Task<SalesOrderDraftLoadingProgressResponse> GetDraftLoadingProgressAsync(Guid draftId,
            CancellationToken cancellationToken = default)
        {
            LoadingProgressReads.Add(draftId);
            return LoadingProgress(draftId);
        }

        /// <summary>SAT-D8 / API-10: what the stub answers the customer-returns read with.</summary>
        public Func<Guid, Task<SalesOrderDraftCustomerReturnsResponse>> CustomerReturns { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> CustomerReturnReads { get; } = [];

        public Task<SalesOrderDraftCustomerReturnsResponse> GetDraftCustomerReturnsAsync(Guid draftId,
            CancellationToken cancellationToken = default)
        {
            CustomerReturnReads.Add(draftId);
            return CustomerReturns(draftId);
        }

        /// <summary>DEC-SAL-001 / API-08: what the stub answers the status-axes read with.</summary>
        public Func<Guid, Task<SalesOrderDraftStatusResponse>> Status { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> StatusReads { get; } = [];

        /// <summary>API-03: what the stub answers the totals read with.</summary>
        public Func<Guid, Task<SalesOrderDraftTotalsResponse>> Totals { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> TotalsReads { get; } = [];

        public Task<SalesOrderDraftTotalsResponse> GetDraftTotalsAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            TotalsReads.Add(draftId);
            return Totals(draftId);
        }

        public Task<SalesOrderDraftStatusResponse> GetDraftStatusAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            StatusReads.Add(draftId);
            return Status(draftId);
        }

        /// <summary>MOD-01 / API-06: what the stub answers the risk-check read with.</summary>
        public Func<Guid, Task<SalesOrderDraftRiskCheckResponse>> RiskCheck { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> RiskCheckReads { get; } = [];

        public Task<SalesOrderDraftRiskCheckResponse> GetDraftRiskCheckAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            RiskCheckReads.Add(draftId);
            return RiskCheck(draftId);
        }

        public Task<SalesOrderDraftResponse> SubmitDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("submit", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> ApproveDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("approve", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> RecordCustomerAcceptanceAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            StateAsync("customer-acceptance", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> WithdrawDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("withdraw", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> RejectDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("reject", draftId, request, metadata);

        /// <summary>DEC-SAL-005: the preview, the command and the record; what the stub answers and what reached it.</summary>
        public Func<Guid, Task<SalesOrderDraftCancellationImpactResponse>> Impact { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, SalesDraftCancelRequest, Task<SalesOrderDraftResponse>> Cancel { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, Task<SalesOrderDraftCancellationResponse>> Cancellation { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftCancellationResponse(null));

        public List<(Guid DraftId, SalesDraftCancelRequest Request, string Key)> CancelCalls { get; } = [];

        public Task<SalesOrderDraftCancellationImpactResponse> GetCancellationImpactAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Impact(draftId);

        public Task<SalesOrderDraftResponse> CancelDraftAsync(Guid draftId, SalesDraftCancelRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            CancelCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Cancel(draftId, request);
        }

        public Task<SalesOrderDraftCancellationResponse> GetDraftCancellationAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Cancellation(draftId);

        // No 30: the force-close preview, command and read, recorded like the cancellation's.
        public Func<Guid, Task<SalesOrderDraftForceCloseImpactResponse>> ForceCloseImpact { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, SalesDraftForceCloseRequest, Task<SalesOrderDraftResponse>> ForceClose { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, Task<SalesOrderDraftForceClosureResponse>> ForceClosure { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftForceClosureResponse(null));

        public List<(Guid DraftId, SalesDraftForceCloseRequest Request, string Key)> ForceCloseCalls { get; } = [];

        public Task<SalesOrderDraftForceCloseImpactResponse> GetForceCloseImpactAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            ForceCloseImpact(draftId);

        public Task<SalesOrderDraftResponse> ForceCloseDraftAsync(Guid draftId, SalesDraftForceCloseRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ForceCloseCalls.Add((draftId, request, metadata.IdempotencyKey));
            return ForceClose(draftId, request);
        }

        public Task<SalesOrderDraftForceClosureResponse> GetDraftForceClosureAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            ForceClosure(draftId);

        public Func<Guid, SalesDraftRemainderChangeRequest, Task<SalesOrderDraftResponse>> RemainderRequest { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<string, Guid, int, SalesDraftRemainderDecisionRequest, Task<SalesOrderDraftResponse>> RemainderDecision { get; set; } =
            (_, _, _, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, Task<SalesOrderDraftRemainderChangesResponse>> RemainderRead { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<(Guid DraftId, SalesDraftRemainderChangeRequest Request, string Key)> RemainderRequests { get; } = [];
        public List<(string Command, Guid DraftId, int ChangeNo, SalesDraftRemainderDecisionRequest Request, string Key)> RemainderDecisions { get; } = [];
        public List<Guid> RemainderReads { get; } = [];

        public Task<SalesOrderDraftResponse> RequestRemainderChangeAsync(Guid draftId, SalesDraftRemainderChangeRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            RemainderRequests.Add((draftId, request, metadata.IdempotencyKey));
            return RemainderRequest(draftId, request);
        }

        public Task<SalesOrderDraftResponse> ApproveRemainderChangeAsync(Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            RemainderDecisionAsync("approve", draftId, changeNo, request, metadata);

        public Task<SalesOrderDraftResponse> RejectRemainderChangeAsync(Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            RemainderDecisionAsync("reject", draftId, changeNo, request, metadata);

        public Task<SalesOrderDraftRemainderChangesResponse> GetRemainderChangesAsync(Guid draftId,
            CancellationToken cancellationToken = default)
        {
            RemainderReads.Add(draftId);
            return RemainderRead(draftId);
        }

        private Task<SalesOrderDraftResponse> RemainderDecisionAsync(string command, Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata)
        {
            RemainderDecisions.Add((command, draftId, changeNo, request, metadata.IdempotencyKey));
            return RemainderDecision(command, draftId, changeNo, request);
        }

        private Task<SalesOrderDraftResponse> StateAsync(string command, Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata)
        {
            StateCalls.Add((command, draftId, request.ExpectedVersion, request.Reason, metadata.IdempotencyKey));
            return State(command, draftId, request);
        }
    }

    private sealed class StubDraftReadService : ISalesDraftReadServiceV1
    {
        public Func<Guid, Task<SalesOrderDraftReadDetailViewV1>> Detail { get; set; } =
            _ => Task.FromException<SalesOrderDraftReadDetailViewV1>(new SalesDraftServiceException(404, "not_found"));
        public Func<SalesOrderDraftListQuery, Task<SalesOrderDraftReadListResponseV1>> List { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftReadListResponseV1([], null));

        public Task<SalesOrderDraftReadDetailViewV1> GetDraftDetailAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) => Detail(draftId);

        public Task<SalesOrderDraftReadListResponseV1> GetDraftListAsync(
            SalesOrderDraftListQuery query,
            CancellationToken cancellationToken = default) => List(query);
    }

    private sealed class StubSalesRecordPartyDisplaySource : ICounterpartySalesRecordPartyDisplaySourceV1
    {
        public CounterpartySalesRecordPartyDisplayAnswerV1 Answer { get; init; } =
            new(CounterpartySalesRecordPartyDisplayOutcomeV1.Unavailable, []);
        public CounterpartySalesRecordPartyDisplayRequestV1? Request { get; private set; }

        public Task<CounterpartySalesRecordPartyDisplayAnswerV1> ReadForAuthorizedSalesRecordsAsync(
            CounterpartySalesRecordPartyDisplayRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(Answer);
        }
    }

    // ---- SALES-A1: the state command routes' wire ------------------------------------------------

    [Theory]
    [InlineData("submission", "submit")]
    [InlineData("approval", "approve")]
    [InlineData("customer-acceptance", "customer-acceptance")]
    [InlineData("rejection", "reject")]
    [InlineData("withdrawal", "withdraw")]
    public async Task A_state_command_answers_200_with_the_draft_and_the_replay_headers_only_on_a_replay(string segment, string command)
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService
        {
            State = (_, id, request) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "PendingApproval"),
                request.ExpectedVersion == "2", "SENTETIK-first-corr")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var first = await client.SendAsync(StateCommand(draftId, segment, "SENTETIK-s-1", "{\"expectedVersion\":\"1\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.True(first.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.False(first.Headers.Contains("Idempotency-Replayed"));
            var body = await first.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(draftId, body.GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
            Assert.Equal("PendingApproval", body.GetProperty("detail").GetProperty("draft").GetProperty("approvalState").GetString());
            Assert.False(body.GetProperty("isReplay").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("detail").GetProperty("approval").GetProperty("submittedBy").ValueKind);
        }

        using (var replay = await client.SendAsync(StateCommand(draftId, segment, "SENTETIK-s-2", "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first-corr", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal([(command, draftId, "1", null, "SENTETIK-s-1"), (command, draftId, "2", null, "SENTETIK-s-2")], service.StateCalls);
    }

    [Fact]
    public async Task A_state_command_without_a_canonical_key_a_json_body_or_the_strict_shape_never_reaches_the_service()
    {
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var draftId = Guid.NewGuid();

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/submission")
        {
            Content = new StringContent("{\"expectedVersion\":\"1\"}", Encoding.UTF8, "application/json"),
        })
        {
            using var response = await client.SendAsync(noKey);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid,
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var text = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/approval")
        {
            Content = new StringContent("expectedVersion=1", Encoding.UTF8, "text/plain"),
        })
        {
            text.Headers.Add("Idempotency-Key", "SENTETIK-text");
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.SendAsync(text)).StatusCode);
        }

        // A missing version, a non-positive or non-integer one, an unknown member, and a reason outside rejection/withdrawal: 400.
        foreach (var (segment, json) in new[]
                 {
                     ("submission", "{}"), ("submission", "{\"expectedVersion\":\"0\"}"), ("approval", "{\"expectedVersion\":1}"),
                     ("approval", "{\"expectedVersion\":\"1\",\"lifecycle\":\"Active\"}"), ("submission", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}"),
                     ("rejection", "{\"expectedVersion\":\"01\"}"),
                 })
        {
            using var response = await client.SendAsync(StateCommand(draftId, segment, "SENTETIK-shape", json));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // The rejection carries its reason; null is "no reason", never a default text.
        using (var rejected = await client.SendAsync(StateCommand(draftId, "rejection", "SENTETIK-r-1", "{\"expectedVersion\":\"3\",\"reason\":\"SENTETIK neden\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }

        Assert.Equal([("reject", draftId, "3", "SENTETIK neden", "SENTETIK-r-1")], service.StateCalls);
    }

    [Theory]
    [InlineData(409, SalesDraftProblemCodesV1.StateConflict, true)]
    [InlineData(409, SalesDraftProblemCodesV1.VersionConflict, true)]
    [InlineData(422, SalesDraftProblemCodesV1.ApprovalReasonTooLong, false)]
    [InlineData(404, "not_found", false)]
    public async Task A_state_commands_refusal_carries_its_code_and_a_replayed_terminal_refusal_its_headers(int status, string code, bool replayed)
    {
        var service = new StubDraftService
        {
            State = (_, _, _) => throw new SalesDraftServiceException(status, code, [new("approvalState", code)], isReplay: replayed,
                originalCorrelationId: replayed ? "SENTETIK-first-corr" : null),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StateCommand(Guid.NewGuid(), "rejection", "SENTETIK-refused", "{\"expectedVersion\":\"1\"}"));

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (status == 404)
        {
            // Platform 404 stays code-less on this wire: the service's code never reaches it.
            Assert.False(body.TryGetProperty("code", out var codeless) && codeless.GetString() == code);
        }
        else
        {
            Assert.Equal(code, body.GetProperty("code").GetString());
            Assert.Equal("approvalState", body.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        if (replayed)
        {
            Assert.Equal("SENTETIK-first-corr", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }
    }

    [Fact]
    public async Task Remainder_routes_preserve_the_strict_commands_replay_headers_and_private_read_shape()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var service = new StubDraftService
        {
            RemainderRequest = (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), false, "SENTETIK-request")),
            RemainderDecision = (_, id, _, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), true, "SENTETIK-decision-first")),
            RemainderRead = _ => Task.FromResult(new SalesOrderDraftRemainderChangesResponse("7", SalesRemainderChangeNamesV1.Readable,
                [new(lineId, 1, unitId, "5", SalesRemainderChangeNamesV1.Open, "3", "2", "1", "3")], [])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/remainder-changes";

        using (var invalid = StateCommand(draftId, "remainder-changes", "SENTETIK-invalid",
                   $"{{\"expectedVersion\":\"7\",\"lineId\":\"{lineId:D}\",\"kind\":\"Cancel\",\"quantity\":\"1\",\"reason\":\"x\"}}"))
        using (var response = await client.SendAsync(invalid))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(service.RemainderRequests);

        var requestBody = JsonSerializer.Serialize(
            new SalesDraftRemainderChangeRequest("7", lineId, SalesRemainderChangeNamesV1.Revise, "4", "SENTETIK kalan"),
            SalesDraftJson.Options);
        using (var response = await client.SendAsync(StateCommand(draftId, "remainder-changes", "SENTETIK-request", requestBody)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }
        var requested = Assert.Single(service.RemainderRequests);
        Assert.Equal((draftId, "7", lineId, "Revise", "4", "SENTETIK-request"),
            (requested.DraftId, requested.Request.ExpectedVersion, requested.Request.LineId, requested.Request.Kind,
                requested.Request.Quantity, requested.Key));

        var decisionBody = JsonSerializer.Serialize(new SalesDraftRemainderDecisionRequest("8"), SalesDraftJson.Options);
        using (var response = await client.SendAsync(StateCommand(draftId, "remainder-changes/1/approval", "SENTETIK-approve", decisionBody)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-decision-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var rejectionBody = JsonSerializer.Serialize(new SalesDraftRemainderDecisionRequest("9", "SENTETIK ret"), SalesDraftJson.Options);
        using (var response = await client.SendAsync(StateCommand(draftId, "remainder-changes/2/rejection", "SENTETIK-reject", rejectionBody)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal([("approve", draftId, 1, "8", (string?)null, "SENTETIK-approve"),
            ("reject", draftId, 2, "9", "SENTETIK ret", "SENTETIK-reject")],
            service.RemainderDecisions.Select(call =>
                (call.Command, call.DraftId, call.ChangeNo, call.Request.ExpectedVersion, call.Request.Reason, call.Key)));

        using var read = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl is { Private: true, NoStore: true });
        var body = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["draftVersion", "floorStatus", "lines", "items"], body.EnumerateObject().Select(member => member.Name));
        Assert.Equal("Readable", body.GetProperty("floorStatus").GetString());
        Assert.Equal([draftId], service.RemainderReads);
    }

    [Fact]
    public async Task Commercial_revision_and_history_use_the_strict_owner_contract_and_replay_headers()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var priceUnitId = Guid.NewGuid();
        var moneyProfileId = Guid.NewGuid();
        var archivedAt = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        var service = new StubDraftService
        {
            Commercial = (_, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-commercial-first")),
            Revisions = _ => Task.FromResult(new SalesOrderDraftRevisionsResponse([
                new SalesDraftRevisionV1("Rev.0", "SENTETIK-submitter", archivedAt.AddMinutes(-2), "SENTETIK-approver",
                    archivedAt.AddMinutes(-1), archivedAt, "SENTETIK-pricer", moneyProfileId, "7", 2,
                    [new SalesDraftRevisionLineV1(lineId, 1, Guid.NewGuid(), "3", Guid.NewGuid(), "2.5", "13.00", priceUnitId, "5", SalesPriceDisclosure.Visible)],
                    null)
            ])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        var command = new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(lineId, "13.00", priceUnitId, "5")]);
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/commercial-revision")
               {
                   Content = new StringContent(JsonSerializer.Serialize(command, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        {
            request.Headers.Add("Idempotency-Key", "SENTETIK-commercial-1");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-commercial-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var call = Assert.Single(service.CommercialCalls);
        Assert.Equal((draftId, "4", "SENTETIK-commercial-1"), (call.DraftId, call.Request.ExpectedVersion, call.Key));
        var sentLine = Assert.Single(call.Request.Lines);
        Assert.Equal((lineId, "13.00", priceUnitId, "5"),
            (sentLine.LineId, sentLine.UnitPrice, sentLine.PriceUnitId, sentLine.DiscountPercent));

        using var history = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{draftId:D}/revisions");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var body = await history.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Rev.0", item.GetProperty("revision").GetString());
        Assert.Equal("Visible", item.GetProperty("lines")[0].GetProperty("priceDisclosure").GetString());
        Assert.Equal("13.00", item.GetProperty("lines")[0].GetProperty("unitPrice").GetString());
        // DEC-SAL-002: the member is present (a price revision archives with no reason).
        Assert.Equal(JsonValueKind.Null, item.GetProperty("reason").ValueKind);
        Assert.Equal([draftId], service.RevisionReads);
    }

    /// <summary>
    /// DEC-SAL-002 / AT-023: the quantity-revision route's wire — one canonical key and the strict body {expectedVersion, reason,
    /// lines[{lineId, quantity}]} before the service is reached (no unit, price or slice member, no numeric quantity); 200
    /// with the detail after the change and the replay headers on a replay; a blocked revision is a 409 that names each
    /// blocking source and carries the replay headers when it is replayed; a 503 stays code-less.
    /// </summary>
    [Fact]
    public async Task Quantity_revision_uses_the_strict_owner_contract_replay_headers_and_a_named_blocked_conflict()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var service = new StubDraftService
        {
            Quantity = (_, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-quantity-first")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/quantity-revision";

        foreach (var json in new[]
                 {
                     "{}",
                     $"{{\"expectedVersion\":\"4\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":\"2\"}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"r\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":2}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"r\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":\"2\",\"orderUnitId\":\"{lineId:D}\"}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"r\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":\"0\"}}]}}",
                 })
        {
            using var invalid = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            invalid.Headers.Add("Idempotency-Key", "SENTETIK-invalid");
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var body = new SalesDraftQuantityRevisionRequest("4", "SENTETIK neden", [new SalesDraftLineQuantityV1(lineId, "2.5")]);
        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(service.QuantityCalls);

        using (var request = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        {
            request.Headers.Add("Idempotency-Key", "SENTETIK-quantity-1");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-quantity-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var call = Assert.Single(service.QuantityCalls);
        Assert.Equal((draftId, "4", "SENTETIK neden", "SENTETIK-quantity-1"), (call.DraftId, call.Request.ExpectedVersion, call.Request.Reason, call.Key));
        Assert.Equal((lineId, "2.5"), (Assert.Single(call.Request.Lines).LineId, call.Request.Lines[0].Quantity));

        service.Quantity = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.RevisionBlocked,
            [new("inventory.active-allocations", "Present"), new("logistics.loading-without-dispatch-note", "Present")], true, "SENTETIK-quantity-first");
        using (var blocked = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        {
            blocked.Headers.Add("Idempotency-Key", "SENTETIK-quantity-2");
            using var refused = await client.SendAsync(blocked);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("true", Assert.Single(refused.Headers.GetValues("Idempotency-Replayed")));
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.RevisionBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(["inventory.active-allocations", "logistics.loading-without-dispatch-note"],
                problem.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()));
        }

        service.Quantity = (_, _) => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        using var unavailableRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        unavailableRequest.Headers.Add("Idempotency-Key", "SENTETIK-quantity-3");
        using var unavailable = await client.SendAsync(unavailableRequest);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain(SalesDraftProblemCodesV1.RevisionImpactUnavailable, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delivery_progress_is_a_private_read_of_the_service_answer_and_its_404_and_503_stay_code_less()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var service = new StubDraftService
        {
            Progress = id => Task.FromResult(new SalesOrderDraftDeliveryProgressResponse("5",
                [new SalesDraftLineDeliveryProgressV1(lineId, 1, unitId, "6", "4", "2.5", "3.5", "0")])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/delivery-progress";

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal("5", body.GetProperty("draftVersion").GetString());
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "shipped", "delivered", "remaining", "excess"],
                line.EnumerateObject().Select(member => member.Name));
            Assert.Equal((lineId.ToString("D"), 1, "6", "4", "2.5", "3.5", "0"),
                (line.GetProperty("lineId").GetString(), line.GetProperty("lineNo").GetInt32(), line.GetProperty("ordered").GetString(),
                    line.GetProperty("shipped").GetString(), line.GetProperty("delivered").GetString(), line.GetProperty("remaining").GetString(),
                    line.GetProperty("excess").GetString()));
        }

        Assert.Equal([draftId], service.ProgressReads);

        service.Progress = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        foreach (var code in new[] { SalesDraftProblemCodesV1.DeliveryProviderUnavailable, SalesDraftProblemCodesV1.ReferenceProviderInvalid })
        {
            service.Progress = _ => throw new SalesDraftServiceException(503, code);
            using var unavailable = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.DoesNotContain(code, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Loading_progress_is_a_private_read_of_the_service_answer_and_its_404_and_503_stay_code_less()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var service = new StubDraftService
        {
            LoadingProgress = id => Task.FromResult(new SalesOrderDraftLoadingProgressResponse("5",
                [new SalesDraftLineLoadingProgressV1(lineId, 1, unitId, "6", 2, "6", "5", "4", "2.5")])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/loading-progress";

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal("5", body.GetProperty("draftVersion").GetString());
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "loadingInstructionLines", "instructed", "loaded", "shipped", "delivered"],
                line.EnumerateObject().Select(member => member.Name));
            Assert.Equal((lineId.ToString("D"), 1, "6", 2, "6", "5", "4", "2.5"),
                (line.GetProperty("lineId").GetString(), line.GetProperty("lineNo").GetInt32(), line.GetProperty("ordered").GetString(),
                    line.GetProperty("loadingInstructionLines").GetInt32(), line.GetProperty("instructed").GetString(),
                    line.GetProperty("loaded").GetString(), line.GetProperty("shipped").GetString(), line.GetProperty("delivered").GetString()));
        }

        Assert.Equal([draftId], service.LoadingProgressReads);

        service.LoadingProgress = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        service.LoadingProgress = _ => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.LoadingProviderUnavailable);
        using var unavailable = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain(SalesDraftProblemCodesV1.LoadingProviderUnavailable,
            await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_returns_are_a_private_read_of_the_service_answer_and_their_404_and_503_stay_code_less()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var service = new StubDraftService
        {
            CustomerReturns = id => Task.FromResult(new SalesOrderDraftCustomerReturnsResponse("5",
                [new SalesDraftLineCustomerReturnV1(lineId, 1, unitId, "6", "1.5")])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/customer-returns";

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal("5", body.GetProperty("draftVersion").GetString());
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "approvedReturned"],
                line.EnumerateObject().Select(member => member.Name));
            Assert.Equal((lineId.ToString("D"), 1, unitId.ToString("D"), "6", "1.5"),
                (line.GetProperty("lineId").GetString(), line.GetProperty("lineNo").GetInt32(),
                    line.GetProperty("orderUnitId").GetString(), line.GetProperty("ordered").GetString(),
                    line.GetProperty("approvedReturned").GetString()));
        }

        Assert.Equal([draftId], service.CustomerReturnReads);

        service.CustomerReturns = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.True(missing.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        foreach (var code in new[] { SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable, SalesDraftProblemCodesV1.ReferenceProviderInvalid })
        {
            service.CustomerReturns = _ => throw new SalesDraftServiceException(503, code);
            using var unavailable = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.True(unavailable.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.DoesNotContain(code, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Status_axes_are_a_private_read_of_the_service_answer_with_every_member_and_its_404_stays_code_less()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var service = new StubDraftService
        {
            Status = id => Task.FromResult(new SalesOrderDraftStatusResponse("7", "Rev.0",
                new SalesDraftStatusAxisV1("Closed"), new SalesDraftStatusAxisV1("Approved"),
                new SalesDraftCustomerAcceptanceAxisV1("Accepted", "Rev.0"), new SalesDraftStatusAxisV1("NoSource"),
                new SalesDraftRealizationAxisV1("ClosedWithRemainder", false, "Rev.0",
                    new SalesDraftDeliveryToleranceV1(profileId, 1, "1", "2", "3", new DateOnly(2026, 10, 25), null, new DateOnly(2026, 10, 26)),
                    [new SalesDraftLineRealizationV1(lineId, 1, unitId, "100", "97", "90", "ClosedWithRemainder", "Under", "3", "Block", false, false, "3", "1.5")]),
                new SalesDraftStatusAxisV1("NoSource"), new SalesDraftStatusAxisV1("NoSource"), false)),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/status";

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "revision", "lifecycle", "internalApproval", "customerAcceptance", "source", "realization",
                "invoicing", "financialClosure", "readyToClose"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal(JsonValueKind.False, body.GetProperty("readyToClose").ValueKind);
            Assert.Equal(("7", "Rev.0", "Closed", "Approved"), (body.GetProperty("draftVersion").GetString(),
                body.GetProperty("revision").GetString(), body.GetProperty("lifecycle").GetProperty("state").GetString(),
                body.GetProperty("internalApproval").GetProperty("state").GetString()));
            Assert.Equal(["state", "acceptedRevision"], body.GetProperty("customerAcceptance").EnumerateObject().Select(member => member.Name));
            foreach (var axis in new[] { "source", "invoicing", "financialClosure" })
            {
                Assert.Equal("NoSource", Assert.Single(body.GetProperty(axis).EnumerateObject()).Value.GetString());
            }

            var realization = body.GetProperty("realization");
            Assert.Equal(["state", "warning", "basisRevision", "tolerance", "lines"], realization.EnumerateObject().Select(member => member.Name));
            Assert.Equal("Rev.0", realization.GetProperty("basisRevision").GetString());
            Assert.Equal(JsonValueKind.False, realization.GetProperty("warning").ValueKind);
            var tolerance = realization.GetProperty("tolerance");
            Assert.Equal(["profileId", "profileVersion", "warnPercent", "approvalPercent", "blockPercent", "effectiveFrom",
                "effectiveThrough", "evaluatedOn"], tolerance.EnumerateObject().Select(member => member.Name));
            Assert.Equal((profileId.ToString("D"), "1", "2", "3", "2026-10-25", JsonValueKind.Null, "2026-10-26"),
                (tolerance.GetProperty("profileId").GetString(), tolerance.GetProperty("warnPercent").GetString(),
                    tolerance.GetProperty("approvalPercent").GetString(), tolerance.GetProperty("blockPercent").GetString(),
                    tolerance.GetProperty("effectiveFrom").GetString(), tolerance.GetProperty("effectiveThrough").ValueKind,
                    tolerance.GetProperty("evaluatedOn").GetString()));
            var line = Assert.Single(realization.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "shipped", "delivered", "state", "direction", "deviation", "band",
                "warning", "approvedOverDelivery", "closedRemainder", "approvedReturned"], line.EnumerateObject().Select(member => member.Name));
            Assert.Equal((JsonValueKind.False, JsonValueKind.False, "1.5"), (line.GetProperty("warning").ValueKind,
                line.GetProperty("approvedOverDelivery").ValueKind, line.GetProperty("approvedReturned").GetString()));
            Assert.Equal((lineId.ToString("D"), "97", "ClosedWithRemainder", "Under", "3", "Block", "3"),
                (line.GetProperty("lineId").GetString(), line.GetProperty("shipped").GetString(), line.GetProperty("state").GetString(),
                    line.GetProperty("direction").GetString(), line.GetProperty("deviation").GetString(), line.GetProperty("band").GetString(),
                    line.GetProperty("closedRemainder").GetString()));
        }

        Assert.Equal([draftId], service.StatusReads);

        service.Status = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.True(missing.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // A 503 of this read is the service's own (the Sales store, an unregistered permission name); an owner failure is an
        // axis state, never a 503.
        service.Status = _ => throw new SalesDraftServiceException(503, "service_unavailable");
        using (var unavailable = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.True(unavailable.Headers.CacheControl is { Private: true, NoStore: true });
        }

        service.Status = _ => throw new SalesDraftServiceException(403, "forbidden");
        using (var forbidden = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.True(forbidden.Headers.CacheControl is { Private: true, NoStore: true });
        }
    }

    [Fact]
    public async Task Risk_check_is_a_private_read_of_the_service_answer_with_every_member_and_its_404_stays_code_less()
    {
        var draftId = Guid.NewGuid();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        var currency = Guid.NewGuid();
        var service = new StubDraftService
        {
            RiskCheck = id => Task.FromResult(new SalesOrderDraftRiskCheckResponse("4", party, account, "Unavailable",
                [new SalesDraftRiskReasonV1("ShippedNotInvoiced", SalesDraftRiskReasonCodesV1.InvoicingOwnerMissing)],
                SalesPriceDisclosure.Visible, new SalesDraftRiskTotalsV1(currency, null, null), null,
                [
                    new SalesDraftRiskFigureV1("OpenReceivables", "finance.open-items", "Available", "IncludingTax",
                        [new SalesDraftRiskAmountV1(currency, "100.00")], null),
                    new SalesDraftRiskFigureV1("ShippedNotInvoiced", SalesDraftRiskSourcesV1.Invoicing, "NoSource", null, null,
                        SalesDraftRiskReasonCodesV1.InvoicingOwnerMissing),
                    new SalesDraftRiskFigureV1("ApprovedUnshippedRemainder", SalesDraftRiskSourcesV1.ApprovedOrderRemainders, "Available",
                        "IncludingTax", [], null),
                ],
                new SalesDraftRiskFigureV1("ThisOrder", SalesDraftRiskSourcesV1.ThisOrder, "Unavailable", null, null,
                    SalesDraftRiskReasonCodesV1.VatInclusiveAmountUnavailable),
                new SalesDraftRiskFigureV1("CreditLimit", "finance.party-credit-limit", "NoSource", null, null, "finance_credit_limit_owner_missing"),
                new SalesDraftRiskFigureV1("Collateral", "finance.party-collateral", "NoSource", null, null, "finance_collateral_owner_missing"))),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/risk-check";
        Assert.Equal(SalesDraftApiV1.RiskCheckPath.Replace("{id}", draftId.ToString("D"), StringComparison.Ordinal), path);

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "partyId", "companyAccountId", "result", "reasons", "amountDisclosure", "inLocalCurrency", "inLimitCurrency",
                "components", "thisOrder", "creditLimit", "collateral"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal(("4", party.ToString("D"), account.ToString("D"), "Unavailable", "Visible"),
                (body.GetProperty("draftVersion").GetString(), body.GetProperty("partyId").GetString(),
                    body.GetProperty("companyAccountId").GetString(), body.GetProperty("result").GetString(),
                    body.GetProperty("amountDisclosure").GetString()));
            var local = body.GetProperty("inLocalCurrency");
            Assert.Equal(["currencyId", "grossRisk", "grossRiskWithThisOrder"], local.EnumerateObject().Select(member => member.Name));
            Assert.Equal((currency.ToString("D"), JsonValueKind.Null, JsonValueKind.Null), (local.GetProperty("currencyId").GetString(),
                local.GetProperty("grossRisk").ValueKind, local.GetProperty("grossRiskWithThisOrder").ValueKind));
            Assert.Equal(JsonValueKind.Null, body.GetProperty("inLimitCurrency").ValueKind);
            Assert.Equal(("ThisOrder", "Unavailable", "vat_inclusive_amount_unavailable"), (body.GetProperty("thisOrder").GetProperty("kind").GetString(),
                body.GetProperty("thisOrder").GetProperty("state").GetString(), body.GetProperty("thisOrder").GetProperty("reasonCode").GetString()));
            var reason = Assert.Single(body.GetProperty("reasons").EnumerateArray());
            Assert.Equal(["component", "code"], reason.EnumerateObject().Select(member => member.Name));
            Assert.Equal(("ShippedNotInvoiced", "invoicing_owner_missing"),
                (reason.GetProperty("component").GetString(), reason.GetProperty("code").GetString()));
            var components = body.GetProperty("components").EnumerateArray().ToArray();
            Assert.Equal(["OpenReceivables", "ShippedNotInvoiced", "ApprovedUnshippedRemainder"],
                components.Select(component => component.GetProperty("kind").GetString()));
            Assert.All(components.Append(body.GetProperty("thisOrder")).Append(body.GetProperty("creditLimit")).Append(body.GetProperty("collateral")), figure =>
                Assert.Equal(["kind", "source", "state", "taxBasis", "amounts", "reasonCode"], figure.EnumerateObject().Select(member => member.Name)));
            var amount = Assert.Single(components[0].GetProperty("amounts").EnumerateArray());
            Assert.Equal((currency.ToString("D"), "100.00"), (amount.GetProperty("currencyId").GetString(), amount.GetProperty("amount").GetString()));
            Assert.Equal((JsonValueKind.Null, "invoicing_owner_missing"), (components[1].GetProperty("amounts").ValueKind,
                components[1].GetProperty("reasonCode").GetString()));
            Assert.Equal((JsonValueKind.Array, 0), (components[2].GetProperty("amounts").ValueKind, components[2].GetProperty("amounts").GetArrayLength()));
            Assert.Equal(("NoSource", "finance_credit_limit_owner_missing"), (body.GetProperty("creditLimit").GetProperty("state").GetString(),
                body.GetProperty("creditLimit").GetProperty("reasonCode").GetString()));
        }

        Assert.Equal([draftId], service.RiskCheckReads);

        service.RiskCheck = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.True(missing.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // A 503 of this read is the service's own (the Sales store, an unregistered permission name); an owner failure is a
        // figure state, never a 503.
        service.RiskCheck = _ => throw new SalesDraftServiceException(503, "service_unavailable");
        using (var unavailable = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.True(unavailable.Headers.CacheControl is { Private: true, NoStore: true });
        }

        service.RiskCheck = _ => throw new SalesDraftServiceException(403, "forbidden");
        using (var forbidden = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.True(forbidden.Headers.CacheControl is { Private: true, NoStore: true });
        }
    }

    [Fact]
    public async Task Totals_are_a_private_read_of_the_service_answer_with_every_member_and_its_refusals_carry_no_amount()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unpricedLineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var currencyId = Guid.NewGuid();
        var service = new StubDraftService
        {
            Totals = _ => Task.FromResult(new SalesOrderDraftTotalsResponse("4", "Rev.1", currencyId, 2, "NoSource",
                new SalesDraftDocumentTotalsV1("Incomplete", 1, 0, null, "NotComputed", null, null, null, null),
                [
                    new SalesDraftLineTotalV1(lineId, 1, unitId, "12.5", "Computed", "11.475", "143.44", "NoSource",
                        null, null, null, null, null, null),
                    new SalesDraftLineTotalV1(unpricedLineId, 2, unitId, "3", "Unpriced", null, null, "NotComputed",
                        null, null, null, null, null, null),
                ])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/totals";

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "revision", "currencyId", "amountScale", "taxState", "totals", "lines"],
                body.EnumerateObject().Select(member => member.Name));
            Assert.Equal(("4", "Rev.1", currencyId.ToString("D"), 2, "NoSource"), (body.GetProperty("draftVersion").GetString(),
                body.GetProperty("revision").GetString(), body.GetProperty("currencyId").GetString(),
                body.GetProperty("amountScale").GetInt32(), body.GetProperty("taxState").GetString()));
            var totals = body.GetProperty("totals");
            Assert.Equal(["state", "unpricedLineCount", "priceUnitNotOrderUnitLineCount", "netAmount", "taxState", "vatAmount",
                "withheldVatAmount", "grossAmount", "payableAmount"], totals.EnumerateObject().Select(member => member.Name));
            Assert.Equal(("Incomplete", 1, JsonValueKind.Null, "NotComputed"), (totals.GetProperty("state").GetString(),
                totals.GetProperty("unpricedLineCount").GetInt32(), totals.GetProperty("netAmount").ValueKind,
                totals.GetProperty("taxState").GetString()));
            var lines = body.GetProperty("lines").EnumerateArray().ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "quantity", "state", "netUnitPrice", "netAmount", "taxState", "vatPercent",
                "vatAmount", "withholdingShare", "withheldVatAmount", "grossAmount", "payableAmount"],
                lines[0].EnumerateObject().Select(member => member.Name));
            Assert.Equal((lineId.ToString("D"), "12.5", "Computed", "11.475", "143.44", "NoSource", JsonValueKind.Null),
                (lines[0].GetProperty("lineId").GetString(), lines[0].GetProperty("quantity").GetString(),
                    lines[0].GetProperty("state").GetString(), lines[0].GetProperty("netUnitPrice").GetString(),
                    lines[0].GetProperty("netAmount").GetString(), lines[0].GetProperty("taxState").GetString(),
                    lines[0].GetProperty("vatAmount").ValueKind));
            Assert.Equal(("Unpriced", JsonValueKind.Null), (lines[1].GetProperty("state").GetString(),
                lines[1].GetProperty("netAmount").ValueKind));
        }

        Assert.Equal([draftId], service.TotalsReads);

        // The price read is the service's decision: its 403 (no sales.orders.price.read) is the platform's forbidden problem;
        // the 404 (another company's draft) and the 503 (an unregistered permission, the Sales store) reach the wire code-less.
        // None of them carries an amount.
        foreach (var (status, code) in new[] { (403, "forbidden"), (404, "not_found"), (503, "permission_unregistered") })
        {
            service.Totals = _ => throw new SalesDraftServiceException(status, code);
            using var refused = await client.GetAsync(path);
            Assert.Equal((HttpStatusCode)status, refused.StatusCode);
            Assert.True(refused.Headers.CacheControl is { Private: true, NoStore: true });
            var text = await refused.Content.ReadAsStringAsync();
            if (status != 403)
            {
                Assert.DoesNotContain(code, text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("netAmount", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Commercial_revision_refuses_invalid_envelopes_before_the_service_and_replays_a_terminal_conflict()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var priceUnitId = Guid.NewGuid();
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/commercial-revision";

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent("{\"expectedVersion\":\"1\",\"lines\":[]}", Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        foreach (var json in new[]
                 {
                     "{}",
                     "{\"expectedVersion\":1,\"lines\":[]}",
                     $"{{\"expectedVersion\":\"1\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"unitPrice\":\"13.00\"}}]}}",
                     $"{{\"expectedVersion\":\"1\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"unitPrice\":\"13.00\",\"priceUnitId\":\"{priceUnitId:D}\",\"discountPercent\":null,\"extra\":true}}]}}",
                 })
        {
            using var invalid = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            invalid.Headers.Add("Idempotency-Key", "SENTETIK-invalid");
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Empty(service.CommercialCalls);

        service.Commercial = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict,
            [new("expectedVersion", SalesDraftProblemCodesV1.VersionConflict)], true, "SENTETIK-commercial-first");
        var valid = new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(lineId, "13.00", priceUnitId, null)]);
        using var conflict = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(valid, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        conflict.Headers.Add("Idempotency-Key", "SENTETIK-terminal");
        using var refused = await client.SendAsync(conflict);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("true", Assert.Single(refused.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-commercial-first", Assert.Single(refused.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
        Assert.Equal("expectedVersion", problem.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    /// <summary>
    /// SAT-D2 / API-12: the discount-exception approval route's wire — one canonical key, a JSON body and the strict shape
    /// {expectedVersion, lines[{lineId, reason}]} before the service is reached; 200 with the detail and the replay headers only
    /// on a replay; a replayed terminal refusal carries its code, field and the first correlation; a line refusal is a coded
    /// 422; a 503 (an unreadable discount limit included) stays code-less.
    /// </summary>
    [Fact]
    public async Task Discount_exception_approval_uses_the_strict_owner_contract_replay_headers_and_code_less_503()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/discount-exception-approval";
        var valid = JsonSerializer.Serialize(new SalesDraftDiscountExceptionApprovalRequest("4",
            [new SalesDraftDiscountExceptionApprovalLineV1(lineId, "SENTETIK müşteri anlaşması")]), SalesDraftJson.Options);

        HttpRequestMessage Request(string json, string? key, string mediaType = "application/json")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
            if (key is not null)
            {
                request.Headers.Add("Idempotency-Key", key);
            }

            return request;
        }

        using (var noKey = Request(valid, null))
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid,
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var notJson = Request(valid, "SENTETIK-discount-text", "text/plain"))
        using (var response = await client.SendAsync(notJson))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        foreach (var json in new[]
                 {
                     "{}",
                     "{\"expectedVersion\":4,\"lines\":[]}",
                     $"{{\"expectedVersion\":\"4\",\"lines\":[{{\"lineId\":\"{lineId:D}\"}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"reason\":\"x\",\"limitPercent\":\"100\"}}]}}",
                 })
        {
            using var invalid = Request(json, "SENTETIK-discount-invalid");
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Empty(service.DiscountApprovalCalls);

        service.DiscountApproval = (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id), true, "SENTETIK-discount-first"));
        using (var replay = Request(valid, "SENTETIK-discount-1"))
        using (var response = await client.SendAsync(replay))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-discount-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Equal(draftId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
        }

        var call = Assert.Single(service.DiscountApprovalCalls);
        Assert.Equal((draftId, "4", "SENTETIK-discount-1"), (call.DraftId, call.Request.ExpectedVersion, call.Key));
        Assert.Equal((lineId, "SENTETIK müşteri anlaşması"), (Assert.Single(call.Request.Lines).LineId, call.Request.Lines[0].Reason));

        service.DiscountApproval = (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id), false, "SENTETIK-discount-new"));
        using (var first = Request(valid, "SENTETIK-discount-2"))
        using (var response = await client.SendAsync(first))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }

        service.DiscountApproval = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict,
            [new("expectedVersion", SalesDraftProblemCodesV1.VersionConflict)], true, "SENTETIK-discount-first");
        using (var conflict = Request(valid, "SENTETIK-discount-3"))
        using (var response = await client.SendAsync(conflict))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-discount-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
            Assert.Equal("expectedVersion", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        service.DiscountApproval = (_, _) => throw new SalesDraftServiceException(422, "validation_failed",
            [new("lines[0].reason", "sales_discount_exception_same_actor")]);
        using (var sameActor = Request(valid, "SENTETIK-discount-4"))
        using (var response = await client.SendAsync(sameActor))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
            Assert.Equal("sales_discount_exception_same_actor", problem.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }

        service.DiscountApproval = (_, _) => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DiscountLimitUnavailable);
        using (var unavailable = Request(valid, "SENTETIK-discount-5"))
        using (var response = await client.SendAsync(unavailable))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.DiscountLimitUnavailable, await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// DEC-SAL-005 / AT-007: the three cancellation routes' wire — the preview and the record are private reads whose 404/503
    /// stay code-less; the command needs one canonical key and the strict body {expectedVersion, reason, impactToken} before
    /// the service is reached, answers 200 with the detail after the change, and carries the replay headers on a replayed
    /// result and on a replayed terminal refusal (the blocked cancellation names its blocking source).
    /// </summary>
    [Fact]
    public async Task Cancellation_preview_command_and_record_use_the_strict_owner_contract_and_replay_headers()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var token = new string('d', 64);
        var service = new StubDraftService
        {
            Impact = id => Task.FromResult(new SalesOrderDraftCancellationImpactResponse("4", "Rev.0", false, token,
                [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present),
                 new(SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactStatusesV1.NoSource)],
                [new(lineId, 1, unitId, "6", 1, "2.5", null, null, null, null, null)])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draftId:D}/cancellation-impact";
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/cancellation";

        using (var preview = await client.GetAsync(impactPath))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.True(preview.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "revision", "cancellable", "impactToken", "sources", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal(["source", "status"], body.GetProperty("sources")[0].EnumerateObject().Select(member => member.Name));
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "activeAllocations", "allocatedKg", "loadingInstructionLines",
                "instructed", "loaded", "shipped", "delivered"], line.EnumerateObject().Select(member => member.Name));
            // An owner that could not be read is null on the wire, never 0.
            Assert.Equal((1, "2.5", JsonValueKind.Null), (line.GetProperty("activeAllocations").GetInt32(), line.GetProperty("allocatedKg").GetString(),
                line.GetProperty("instructed").ValueKind));
        }

        foreach (var status in new[] { 404, 503 })
        {
            service.Impact = _ => throw new SalesDraftServiceException(status, status == 404 ? "not_found" : SalesDraftProblemCodesV1.CancellationImpactUnavailable);
            using var refused = await client.GetAsync(impactPath);
            Assert.Equal(status, (int)refused.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.CancellationImpactUnavailable, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent($"{{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\"{token}\"}}", Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        foreach (var json in new[]
                 {
                     "{}",
                     $"{{\"expectedVersion\":4,\"reason\":\"x\",\"impactToken\":\"{token}\"}}",
                     $"{{\"expectedVersion\":\"4\",\"impactToken\":\"{token}\"}}",
                     "{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\" \"}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\"{token}\",\"extra\":true}}",
                     $"{{\"expectedVersion\":\"04\",\"reason\":\"x\",\"impactToken\":\"{token}\"}}",
                 })
        {
            using var invalid = StateCommand(draftId, "cancellation", "SENTETIK-invalid", json);
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Empty(service.CancelCalls);

        var valid = JsonSerializer.Serialize(new SalesDraftCancelRequest("4", "SENTETIK neden", token), SalesDraftJson.Options);
        service.Cancel = (id, request) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), request.Reason == "replayed", "SENTETIK-cancel-first"));
        using (var cancelled = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-cancel", valid)))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.False(cancelled.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal((draftId, "4", "SENTETIK neden", token, "SENTETIK-cancel"),
            (service.CancelCalls[0].DraftId, service.CancelCalls[0].Request.ExpectedVersion, service.CancelCalls[0].Request.Reason,
                service.CancelCalls[0].Request.ImpactToken, service.CancelCalls[0].Key));

        using (var replayed = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-cancel-2",
                   JsonSerializer.Serialize(new SalesDraftCancelRequest("4", "replayed", token), SalesDraftJson.Options))))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-cancel-first", Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.Cancel = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.CancellationBlocked,
            [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present)], true, "SENTETIK-blocked-first");
        using (var blocked = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-blocked", valid)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("true", Assert.Single(blocked.Headers.GetValues("Idempotency-Replayed")));
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.CancellationBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(SalesCancellationImpactSourcesV1.ActiveAllocations, problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        service.Cancel = (_, _) => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CancellationImpactUnavailable);
        using (var unavailable = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-503", valid)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.CancellationImpactUnavailable, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        service.Cancellation = id => Task.FromResult(new SalesOrderDraftCancellationResponse(new SalesDraftCancellationV1("Rev.0", "SENTETIK-canceller",
            new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), "SENTETIK neden", token,
            [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None)])));
        using (var record = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, record.StatusCode);
            var body = await record.Content.ReadFromJsonAsync<JsonElement>();
            var cancellation = body.GetProperty("cancellation");
            Assert.Equal(["revision", "cancelledBy", "cancelledAtUtc", "reason", "impactToken", "sources"], cancellation.EnumerateObject().Select(member => member.Name));
            Assert.Equal("SENTETIK neden", cancellation.GetProperty("reason").GetString());
        }
    }

    /// <summary>
    /// No 30 / SAT-D12: the three force-close routes' wire — the preview (each source with its status AND whether it blocks,
    /// every line with the closed remainder a closure would record) and the record are private reads whose 404/503 stay
    /// code-less; the command needs one canonical key and the strict body {expectedVersion, reason, impactToken} before the
    /// service is reached, answers 200 with the detail after the change, and carries the replay headers on a replayed result
    /// and on a replayed terminal refusal.
    /// </summary>
    [Fact]
    public async Task Force_close_preview_command_and_record_use_the_strict_owner_contract_and_replay_headers()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var token = new string('e', 64);
        var service = new StubDraftService
        {
            ForceCloseImpact = id => Task.FromResult(new SalesOrderDraftForceCloseImpactResponse("4", "Rev.0", true, token,
                [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None, false),
                 new(SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes, SalesCancellationImpactStatusesV1.Present, false)],
                [new(lineId, 1, unitId, "6", 0, "0", 1, "4", "4", "4", "4", "2")])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draftId:D}/force-close-impact";
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/force-close";

        using (var preview = await client.GetAsync(impactPath))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.True(preview.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "revision", "closable", "impactToken", "sources", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal(["source", "status", "blocks"], body.GetProperty("sources")[0].EnumerateObject().Select(member => member.Name));
            Assert.Equal(JsonValueKind.False, body.GetProperty("sources")[1].GetProperty("blocks").ValueKind);
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "activeAllocations", "allocatedKg", "loadingInstructionLines",
                "instructed", "loaded", "shipped", "delivered", "closedRemainder"], line.EnumerateObject().Select(member => member.Name));
            Assert.Equal(("4", "2"), (line.GetProperty("shipped").GetString(), line.GetProperty("closedRemainder").GetString()));
        }

        foreach (var status in new[] { 404, 503 })
        {
            service.ForceCloseImpact = _ => throw new SalesDraftServiceException(status, status == 404 ? "not_found" : SalesDraftProblemCodesV1.ForceCloseImpactUnavailable);
            using var refused = await client.GetAsync(impactPath);
            Assert.Equal(status, (int)refused.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.ForceCloseImpactUnavailable, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent($"{{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\"{token}\"}}", Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var notJson = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent("expectedVersion=4", Encoding.UTF8, "text/plain"),
               })
        {
            notJson.Headers.Add("Idempotency-Key", "SENTETIK-not-json");
            using var response = await client.SendAsync(notJson);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        foreach (var json in new[]
                 {
                     "{}",
                     $"{{\"expectedVersion\":4,\"reason\":\"x\",\"impactToken\":\"{token}\"}}",
                     $"{{\"expectedVersion\":\"4\",\"impactToken\":\"{token}\"}}",
                     "{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\" \"}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\"{token}\",\"lines\":[]}}",
                     $"{{\"expectedVersion\":\"04\",\"reason\":\"x\",\"impactToken\":\"{token}\"}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"{new string('x', 4001)}\",\"impactToken\":\"{token}\"}}",
                 })
        {
            using var invalid = StateCommand(draftId, "force-close", "SENTETIK-invalid", json);
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Empty(service.ForceCloseCalls);

        var valid = JsonSerializer.Serialize(new SalesDraftForceCloseRequest("4", "SENTETIK neden", token), SalesDraftJson.Options);
        service.ForceClose = (id, request) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), request.Reason == "replayed", "SENTETIK-close-first"));
        using (var closed = await client.SendAsync(StateCommand(draftId, "force-close", "SENTETIK-close", valid)))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.False(closed.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal((draftId, "4", "SENTETIK neden", token, "SENTETIK-close"),
            (service.ForceCloseCalls[0].DraftId, service.ForceCloseCalls[0].Request.ExpectedVersion, service.ForceCloseCalls[0].Request.Reason,
                service.ForceCloseCalls[0].Request.ImpactToken, service.ForceCloseCalls[0].Key));
        Assert.Empty(service.CancelCalls);

        using (var replayed = await client.SendAsync(StateCommand(draftId, "force-close", "SENTETIK-close-2",
                   JsonSerializer.Serialize(new SalesDraftForceCloseRequest("4", "replayed", token), SalesDraftJson.Options))))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-close-first", Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.ForceClose = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ForceCloseBlocked,
            [new(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.Present)], true, "SENTETIK-blocked-first");
        using (var blocked = await client.SendAsync(StateCommand(draftId, "force-close", "SENTETIK-blocked", valid)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("true", Assert.Single(blocked.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-blocked-first", Assert.Single(blocked.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.ForceCloseBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        service.ForceClose = (_, _) => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ForceCloseImpactUnavailable);
        using (var unavailable = await client.SendAsync(StateCommand(draftId, "force-close", "SENTETIK-503", valid)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.ForceCloseImpactUnavailable, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var none = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, none.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await none.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("forceClosure").ValueKind);
        }

        service.ForceClosure = id => Task.FromResult(new SalesOrderDraftForceClosureResponse(new SalesDraftForceClosureV1("Rev.0", "SENTETIK-closer",
            new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), "SENTETIK neden", token,
            [new(SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes, SalesCancellationImpactStatusesV1.Present, false)],
            [new(lineId, 1, "6", "4", "2")])));
        using (var record = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, record.StatusCode);
            Assert.True(record.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await record.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["forceClosure"], body.EnumerateObject().Select(member => member.Name));
            var closure = body.GetProperty("forceClosure");
            Assert.Equal(["revision", "closedBy", "closedAtUtc", "reason", "impactToken", "sources", "lines"], closure.EnumerateObject().Select(member => member.Name));
            Assert.Equal(["source", "status", "blocks"], closure.GetProperty("sources")[0].EnumerateObject().Select(member => member.Name));
            var line = Assert.Single(closure.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "ordered", "shipped", "closedRemainder"], line.EnumerateObject().Select(member => member.Name));
            Assert.Equal("2", line.GetProperty("closedRemainder").GetString());
        }

        service.ForceClosure = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static HttpRequestMessage StateCommand(Guid draftId, string segment, string idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
