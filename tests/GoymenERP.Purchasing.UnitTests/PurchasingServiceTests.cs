using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// PUR-4a service without a database or a host: the fences, the replay, the owners in order (Cari supplier
/// account → SUBMITTED source request → Product purchase eligibility → Settings kg profile), the domain's
/// decision, the terminal refusals (§7, incl. the store's Σ-shares rule) and the mapping. SENTETIK / NOT_ENV.
/// Registered platform names stand in for the Purchasing names until the central catalogue knows them (the
/// Sales / Logistics tests' way).
/// </summary>
public sealed class PurchasingServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid KgUnit = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid KgProfile = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly DateOnly Day = new(2026, 9, 19);
    private const string Actor = "synthetic-actor";
    private const string CreatePermission = ErpPermissions.CounterpartyPartiesCreate;
    private const string ReadPermission = ErpPermissions.CounterpartyPartiesRead;
    private const string ApprovePermission = ErpPermissions.CounterpartyAccountsCreate;

    [Fact]
    public async Task A_request_and_an_order_bound_to_it_are_stored_once_with_the_owners_product_version_and_the_fences_measured()
    {
        var world = new World();

        var request = await world.Service().CreateRequestAsync(RequestOf("100"), Metadata("k-req"));
        Assert.False(request.IsReplay);
        Assert.Equal((Company, "Draft", "2", Day, 1), (request.Detail.Request.CompanyId, request.Detail.Request.Status, request.Detail.Request.Version,
            request.Detail.Request.EffectiveOn, request.Detail.Request.LineCount));
        var requestLine = Assert.Single(request.Detail.Lines);
        Assert.Equal((Product, "7", KgUnit, "100"), (requestLine.ProductId, requestLine.ProductVersion, requestLine.UnitId, requestLine.Quantity));

        var submitted = await world.Service().SubmitRequestAsync(request.Detail.Request.Id, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit"));
        Assert.Equal(("Submitted", "3"), (submitted.Detail.Request.Status, submitted.Detail.Request.Version));

        var order = await world.Service().CreateOrderAsync(OrderOf(request.Detail.Request.Id, requestLine.LineId, "60"), Metadata("k-order"));
        Assert.False(order.IsReplay);
        var header = order.Detail.Order;
        Assert.Equal((Company, "Draft", "2", Party, Account, request.Detail.Request.Id, Day, 1),
            (header.CompanyId, header.Status, header.Version, header.SupplierPartyId, header.CompanyAccountId, header.SourceRequestId, header.EffectiveOn, header.LineCount));
        var line = Assert.Single(order.Detail.Lines);
        Assert.Equal(("General", requestLine.LineId, Product, "7", KgUnit, "60", "0", "60", "0", "60"),
            (line.Kind, line.SourceRequestLineId, line.ProductId, line.ProductVersion, line.UnitId, line.Ordered, line.Technical, line.Committed, line.Accepted, line.Remaining));
        var envelope = world.Store.Envelopes.Last();
        Assert.Equal((Actor, CreatePermission, "synthetic-correlation", "k-order"), (envelope.ActorId, envelope.PermissionCode, envelope.CorrelationId, envelope.IdempotencyKey));
        Assert.True(world.Authority.Calls >= 10, $"measured {world.Authority.Calls} times");
        Assert.Equal(3, world.Store.Envelopes.Count);
    }

    [Fact]
    public async Task The_same_key_content_and_actor_replays_before_any_owner_is_asked()
    {
        var world = new World();
        var request = OrderOf(null, null, "5");
        var first = await world.Service().CreateOrderAsync(request, Metadata("k-1"));
        world.ResetOwnerCalls();

        var replay = await world.Service().CreateOrderAsync(request, Metadata("k-1", correlation: "second"));

        Assert.True(replay.IsReplay);
        Assert.Equal((first.Detail.Order.Id, "synthetic-correlation"), (replay.Detail.Order.Id, replay.OriginalCorrelationId));
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Products.Calls, world.Settings.Calls));
    }

    [Theory]
    [InlineData("passive-account", 409, PurchasingProblemCodesV1.AccountState, "passive_company_account")]
    [InlineData("not-supplier", 409, PurchasingProblemCodesV1.AccountState, "not_supplier")]
    [InlineData("not-found", 409, PurchasingProblemCodesV1.AccountState, "invalid_reference")]
    [InlineData("other-company", 409, PurchasingProblemCodesV1.AccountState, "company_mismatch")]
    [InlineData("denied", 403, "forbidden", null)]
    [InlineData("unavailable", 503, PurchasingProblemCodesV1.CounterpartyProviderUnavailable, null)]
    public async Task The_supplier_is_the_Cari_owners_answer_read_before_any_line(string fault, int status, string code, string? fieldCode)
    {
        var world = new World { Accounts = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-1")));

        Assert.Equal((status, code, 0, 0), (refusal.Status, refusal.Code, world.Products.Calls, world.Settings.Calls));
        if (fieldCode is not null)
        {
            Assert.Contains(refusal.Errors, error => error.Code == fieldCode);
        }

        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task Without_the_Product_owners_purchase_eligibility_every_create_is_503_fail_closed_and_nothing_is_stored()
    {
        var world = new World { ProductsPublished = false };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(RequestOf("1"), Metadata("k-1")));

        Assert.Equal((503, PurchasingProblemCodesV1.ProductEligibilityUnavailable, 0), (refusal.Status, refusal.Code, world.Settings.Calls));
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("not-purchasable", 422, PurchasingProblemCodesV1.UnsupportedProduct, "lines[0].productId", "not_purchasable")]
    [InlineData("denied", 403, "forbidden", null, null)]
    [InlineData("unavailable", 503, PurchasingProblemCodesV1.ProductEligibilityUnavailable, null, null)]
    [InlineData("throws", 503, PurchasingProblemCodesV1.ProductEligibilityUnavailable, null, null)]
    [InlineData("empty-refusal", 503, PurchasingProblemCodesV1.ReferenceProviderInvalid, null, null)]
    [InlineData("blank-version", 503, PurchasingProblemCodesV1.ReferenceProviderInvalid, null, null)]
    public async Task The_product_is_the_Product_owners_answer_with_its_codes_prefixed_by_the_line(string fault, int status, string code, string? field, string? fieldCode)
    {
        var world = new World { Products = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(RequestOf("1"), Metadata("k-1")));

        Assert.Equal((status, code), (refusal.Status, refusal.Code));
        if (field is not null)
        {
            Assert.Equal((field, fieldCode), (refusal.Errors[0].Field, refusal.Errors[0].Code));
        }

        Assert.Equal(0, world.Settings.Calls);
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_stale_product_version_is_a_recorded_terminal_refusal_of_the_create_key_and_replays()
    {
        var world = new World { Products = { Fault = "version-conflict" } };
        var request = RequestOf("1");

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(request, Metadata("k-1")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, false, "lines[0].expectedProductVersion", "version_conflict"),
            (refusal.Status, refusal.Code, refusal.IsReplay, refusal.Errors[0].Field, refusal.Errors[0].Code));
        Assert.Equal((PurchasingCommands.RequestCreateV1, 1), (Assert.Single(world.Store.Refusals).Command, world.Store.Refusals.Count));

        world.Products.Fault = "";
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(request, Metadata("k-1", correlation: "retry")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, true, "synthetic-correlation"), (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("draft", "sourceRequestId", "not_submitted")]
    [InlineData("missing", "sourceRequestId", "invalid_reference")]
    [InlineData("unknown-line", "lines[0].sourceRequestLineId", "invalid_reference")]
    [InlineData("other-product", "lines[0].productId", "source_line_mismatch")]
    [InlineData("other-unit", "lines[0].unitId", "source_line_mismatch")]
    public async Task Only_a_submitted_request_of_this_company_is_sourced_and_a_bound_line_keeps_the_request_lines_product_and_unit(string fault, string field, string fieldCode)
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf("100"), Metadata("k-req"));
        var requestId = created.Detail.Request.Id;
        var requestLineId = created.Detail.Lines[0].LineId;
        if (fault != "draft")
        {
            await world.Service().SubmitRequestAsync(requestId, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit"));
        }

        var order = fault switch
        {
            "missing" => OrderOf(Guid.NewGuid(), requestLineId, "10"),
            "unknown-line" => OrderOf(requestId, Guid.NewGuid(), "10"),
            "other-product" => OrderOf(requestId, requestLineId, "10", productId: Guid.NewGuid()),
            "other-unit" => OrderOf(requestId, requestLineId, "10", unitId: Guid.NewGuid()),
            _ => OrderOf(requestId, requestLineId, "10"),
        };
        world.ResetOwnerCalls();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(order, Metadata("k-order")));

        Assert.Equal((409, PurchasingProblemCodesV1.RequestState, field, fieldCode), (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
        // The request is read after the supplier and before any product or unit: nothing of the line owners was asked.
        Assert.Equal((1, 0, 0), (world.Accounts.Calls, world.Products.Calls, world.Settings.Calls));
        Assert.Equal(fault == "draft" ? 1 : 2, world.Store.Envelopes.Count);
    }

    [Theory]
    [InlineData("unit-not-found", 422, PurchasingProblemCodesV1.UnsupportedUnit)]
    [InlineData("unit-piece", 422, PurchasingProblemCodesV1.UnsupportedUnit)]
    [InlineData("unit-other-company", 422, PurchasingProblemCodesV1.UnsupportedUnit)]
    [InlineData("unit-access-denied", 403, "forbidden")]
    [InlineData("unit-unavailable", 503, "settings_provider_unavailable")]
    public async Task The_unit_is_the_Settings_kilogram_profile_or_nothing(string fault, int status, string code)
    {
        var world = new World { Settings = { Fault = fault } };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-1")));

        Assert.Equal((status, code), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_quantity_beyond_the_profiles_scale_is_refused_never_rounded()
    {
        var world = new World();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(OrderOf(null, null, "1.2345"), Metadata("k-1")));

        Assert.Equal((422, PurchasingProblemCodesV1.UnsupportedUnit, "lines[0].unitId", PurchaseUnitCodes.QuantityScaleExceeded),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
    }

    [Fact]
    public async Task Approval_moves_the_order_one_version_under_the_approve_name_and_a_stale_version_is_a_recorded_terminal_refusal_that_replays()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-create"));
        var id = created.Detail.Order.Id;

        var approved = await world.Service().ApproveOrderAsync(id, new ApprovePurchaseOrderRequest("2"), Metadata("k-approve"));
        Assert.Equal(("Approved", "3", ApprovePermission), (approved.Detail.Order.Status, approved.Detail.Order.Version, world.Authority.LastPermission));
        Assert.Single(world.Store.Transitions);

        var stale = new ApprovePurchaseOrderRequest("2");
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveOrderAsync(id, stale, Metadata("k-stale")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, false, "expectedVersion"), (refusal.Status, refusal.Code, refusal.IsReplay, refusal.Errors[0].Field));
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveOrderAsync(id, stale, Metadata("k-stale", correlation: "retry")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, true, "synthetic-correlation"), (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));

        // Approving an approved order is a state refusal (terminal, §7); the record stays at version 3.
        var state = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveOrderAsync(id, new ApprovePurchaseOrderRequest("3"), Metadata("k-again")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderState, "status"), (state.Status, state.Code, state.Errors[0].Field));
        Assert.Equal("3", (await world.Service().GetOrderDetailAsync(id)).Order.Version);
        Assert.Equal(2, world.Store.Refusals.Count);

        world.Bindings = world.Bindings with { ApprovePermission = null };
        var unbound = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveOrderAsync(id, new ApprovePurchaseOrderRequest("3"), Metadata("k-unbound")));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));
    }

    [Fact]
    public async Task The_stores_sum_of_shares_refusal_at_approval_is_terminal_with_the_stores_field_errors()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-create"));
        var id = created.Detail.Order.Id;
        world.Store.OverSourced = true;

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveOrderAsync(id, new ApprovePurchaseOrderRequest("2"), Metadata("k-approve")));

        Assert.Equal((409, PurchasingProblemCodesV1.RequestLineOverSourced, false, "lines[0].sourceRequestLineId", "purchasing_request_line_over_sourced"),
            (refusal.Status, refusal.Code, refusal.IsReplay, refusal.Errors[0].Field, refusal.Errors[0].Code));
        var recorded = Assert.Single(world.Store.Refusals);
        Assert.Equal((PurchasingCommands.OrderApproveV1, PurchasingProblemCodesV1.RequestLineOverSourced), (recorded.Command, recorded.Refusal.Code));
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveOrderAsync(id, new ApprovePurchaseOrderRequest("2"), Metadata("k-approve", correlation: "retry")));
        Assert.True(again.IsReplay);
        Assert.Equal("2", (await world.Service().GetOrderDetailAsync(id)).Order.Version);
    }

    [Fact]
    public async Task Submitting_a_submitted_request_is_a_terminal_state_refusal()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf("1"), Metadata("k-req"));
        var id = created.Detail.Request.Id;
        await world.Service().SubmitRequestAsync(id, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit"));

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().SubmitRequestAsync(id, new SubmitPurchaseRequestRequest("3"), Metadata("k-submit-2")));

        Assert.Equal((409, PurchasingProblemCodesV1.RequestNotDraft, "status"), (refusal.Status, refusal.Code, refusal.Errors[0].Field));
        Assert.Single(world.Store.Refusals);
    }

    [Fact]
    public async Task Authority_is_measured_before_the_replay_and_a_lost_grant_refuses_before_the_store()
    {
        var world = new World { Authority = { Granted = false } };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-1")));

        Assert.Equal(403, refusal.Status);
        Assert.Equal((0, 0, 0), (world.Store.Replays, world.Accounts.Calls, world.Store.Envelopes.Count));
    }

    [Fact]
    public async Task Another_companys_record_is_404_and_the_lists_filter_on_an_exact_status_name()
    {
        var world = new World();
        await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-1"));
        await world.Service().CreateRequestAsync(RequestOf("1"), Metadata("k-2"));

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderDetailAsync(Guid.NewGuid()));
        Assert.Equal(404, missing.Status);
        Assert.Single((await world.Service().GetOrderListAsync(new PurchaseOrderListQuery(Status: "Draft"))).Items);
        Assert.Empty((await world.Service().GetOrderListAsync(new PurchaseOrderListQuery(Status: "Approved"))).Items);
        Assert.Single((await world.Service().GetRequestListAsync(new PurchaseRequestListQuery(Status: "Draft"))).Items);
        Assert.Empty((await world.Service().GetRequestListAsync(new PurchaseRequestListQuery(Status: "Submitted"))).Items);
        var bad = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderListAsync(new PurchaseOrderListQuery(Status: "draft")));
        Assert.Equal((400, "validation_failed"), (bad.Status, bad.Code));
        var wrongKind = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetRequestListAsync(new PurchaseRequestListQuery(Status: "Approved")));
        Assert.Equal(400, wrongKind.Status);
        Assert.Equal(ReadPermission, world.Authority.LastPermission);
    }

    [Fact]
    public void The_wire_shapes_are_strict()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCreateOrder("{}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadApprove("{\"expectedVersion\":\"0\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadSubmit("{\"expectedVersion\":\"1\",\"extra\":1}"));
        // A bound line needs a source request; the same request line binds once per order.
        var line = $"{{\"lineId\":\"{Guid.NewGuid():D}\",\"productId\":\"{Product:D}\",\"expectedProductVersion\":\"7\",\"unitId\":\"{KgUnit:D}\",\"quantity\":\"6\",\"sourceRequestLineId\":\"{Guid.NewGuid():D}\"}}";
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCreateOrder(
            $"{{\"supplierPartyId\":\"{Party:D}\",\"companyAccountId\":\"{Account:D}\",\"sourceRequestId\":null,\"effectiveOn\":\"2026-09-19\",\"lines\":[{line}]}}"));
        var order = PurchasingJson.ReadCreateOrder(
            $"{{\"supplierPartyId\":\"{Party:D}\",\"companyAccountId\":\"{Account:D}\",\"sourceRequestId\":\"{Guid.NewGuid():D}\",\"effectiveOn\":\"2026-09-19\",\"lines\":[{line}]}}");
        Assert.Equal("6", order.Lines[0].Quantity);
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.OrderCreateV1, OrderOf(null, null, "1.25")),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderCreateV1, OrderOf(null, null, "1.250")));
    }

    // ---- world ----------------------------------------------------------------------------------

    private static CreatePurchaseRequestRequest RequestOf(string quantity) =>
        new(Day, [new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, quantity)]);

    private static CreatePurchaseOrderRequest OrderOf(Guid? sourceRequestId, Guid? sourceRequestLineId, string quantity, Guid? productId = null, Guid? unitId = null) =>
        new(Party, Account, sourceRequestId, Day,
            [new PurchaseOrderLineRequest(Guid.NewGuid(), productId ?? Product, "7", unitId ?? KgUnit, quantity, sourceRequestLineId)]);

    private static PurchasingCommandMetadata Metadata(string key, string correlation = "synthetic-correlation") =>
        new(key, correlation, null, DateTimeOffset.UtcNow);

    private sealed class World
    {
        public FixedUser User { get; } = new();
        public FixedAuthority Authority { get; init; } = new();
        public FakeAccounts Accounts { get; init; } = new();
        public FakeProducts Products { get; init; } = new();
        public bool ProductsPublished { get; init; } = true;
        public FakeSettings Settings { get; init; } = new();
        public FakeStore Store { get; init; } = new();
        public PurchasingServiceBindings Bindings { get; set; } = new(CreatePermission, ReadPermission, ApprovePermission);

        public PurchasingService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new PurchasingService(company, User, Authority, new FixedCorrelation(), Accounts, ProductsPublished ? Products : null,
                new PurchaseUnitVerifier(Settings), Store, Bindings, TimeProvider.System);
        }

        public void ResetOwnerCalls()
        {
            Accounts.Calls = 0;
            Products.Calls = 0;
            Settings.Calls = 0;
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public int Calls { get; private set; }
        public string? LastPermission { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPermission = permission;
            return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(["ROLE-A"], StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class FakeAccounts : IPurchasingPartyAccountPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        public Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Fault switch
            {
                "not-found" => new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.NotFound, null),
                "denied" => new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Denied, null),
                "unavailable" => new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Unavailable, null),
                _ => new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Found, new PurchasingPartyAccountV1(partyId, companyAccountId,
                    Fault == "other-company" ? Guid.NewGuid() : Company, true, Fault != "passive-account", Fault != "not-supplier", "3", "2")),
            });
        }
    }

    private sealed class FakeProducts : IProductPurchaseEligibilityPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        public Task<ProductPurchaseEligibilityAnswerV1> EvaluateAsync(ProductPurchaseSelectionV1 selection, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fault switch
            {
                "throws" => Task.FromException<ProductPurchaseEligibilityAnswerV1>(new InvalidOperationException("synthetic outage")),
                "denied" => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Denied, null, [])),
                "unavailable" => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Unavailable, null, [])),
                "not-purchasable" => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Refused, null,
                    [new PurchasingFieldError("productId", "not_purchasable")])),
                "version-conflict" => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Refused, null,
                    [new PurchasingFieldError("expectedProductVersion", "version_conflict")])),
                "empty-refusal" => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Refused, null, [])),
                "blank-version" => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Eligible, " ", [])),
                _ => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Eligible, selection.ExpectedProductVersion, [])),
            };
        }
    }

    private sealed class FakeSettings : ISettingsPrerequisiteProvider
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fault switch
            {
                "unit-not-found" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound)),
                "unit-access-denied" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException("settings_access_denied")),
                "unit-unavailable" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException("settings_provider_unavailable")),
                _ => Task.FromResult(new UnitProfileV1(Fault == "unit-other-company" ? Guid.NewGuid() : Company, unitId,
                    Fault == "unit-piece" ? "adet" : "kg", Fault == "unit-piece" ? "count" : "weight", Fault != "unit-piece",
                    new ProfileVersionV1(KgProfile, 4), 3, true, new DateOnly(2026, 9, 1), null)),
            };
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>An in-memory store of the contract's replay rules; the Σ-shares rule is switched on by <see cref="OverSourced"/> (the real rule is the SQL store's).</summary>
    private sealed class FakeStore : IPurchasingStore
    {
        private sealed record Attempt(string Actor, string Hash, StoredPurchaseRequest? Request, StoredPurchaseOrder? Order, PurchasingTerminalRefusal? Refusal, string Correlation);

        private readonly Dictionary<(string Command, string Key), Attempt> _attempts = [];
        private readonly List<StoredPurchaseRequest> _requests = [];
        private readonly List<StoredPurchaseOrder> _orders = [];
        public List<PurchasingWriteEnvelope> Envelopes { get; } = [];
        public List<(string Command, PurchasingTerminalRefusal Refusal)> Refusals { get; } = [];
        public List<(string Command, Guid Id, long Before, long After)> Transitions { get; } = [];
        public int Replays { get; private set; }
        public bool OverSourced { get; set; }

        public Task<PurchasingRequestStoreResult> StoreRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
            Func<PurchaseRequest> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default)
        {
            if (_attempts.TryGetValue((PurchasingCommands.RequestCreateV1, envelope.IdempotencyKey), out var attempt))
            {
                Check(attempt, envelope, requestHash);
                return Task.FromResult(new PurchasingRequestStoreResult(attempt.Request!, PurchasingWriteOutcome.Replayed, attempt.Correlation));
            }

            var stored = new StoredPurchaseRequest(build(), effectiveOn, productVersionsByLineId);
            _requests.Add(stored);
            _attempts[(PurchasingCommands.RequestCreateV1, envelope.IdempotencyKey)] = new Attempt(envelope.ActorId, requestHash, stored, null, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            return Task.FromResult(new PurchasingRequestStoreResult(stored, PurchasingWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<PurchasingRequestStoreResult?> MutateRequestAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, Guid requestId,
            Action<PurchaseRequest, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default)
        {
            if (_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                Check(attempt, envelope, requestHash);
                if (attempt.Request is null) throw new PurchasingStoreException(PurchasingStoreCodes.ConcurrentChange, "synthetic refusal under the key");
                return Task.FromResult<PurchasingRequestStoreResult?>(new PurchasingRequestStoreResult(attempt.Request, PurchasingWriteOutcome.Replayed, attempt.Correlation));
            }

            var stored = _requests.SingleOrDefault(candidate => candidate.Request.Id == requestId);
            if (stored is null) return Task.FromResult<PurchasingRequestStoreResult?>(null);
            var before = stored.Request.Version;
            mutate(stored.Request, TimeProvider.System.GetUtcNow());
            _attempts[(commandName, envelope.IdempotencyKey)] = new Attempt(envelope.ActorId, requestHash, stored, null, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            Transitions.Add((commandName, requestId, before, stored.Request.Version));
            return Task.FromResult<PurchasingRequestStoreResult?>(new PurchasingRequestStoreResult(stored, PurchasingWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<StoredPurchaseRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_requests.SingleOrDefault(candidate => candidate.Request.Id == requestId));

        public Task<PurchasingRequestPage> ListRequestsAsync(int pageSize, string? cursor, PurchaseRequestStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchasingRequestPage([.. _requests.Where(stored => status is null || stored.Request.Status == status)], null));

        public Task<PurchasingOrderStoreResult> StoreOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, Guid companyAccountId, DateOnly effectiveOn,
            Func<PurchaseOrder> build, IReadOnlyDictionary<Guid, string> productVersionsByLineId, CancellationToken cancellationToken = default)
        {
            if (_attempts.TryGetValue((PurchasingCommands.OrderCreateV1, envelope.IdempotencyKey), out var attempt))
            {
                Check(attempt, envelope, requestHash);
                return Task.FromResult(new PurchasingOrderStoreResult(attempt.Order!, PurchasingWriteOutcome.Replayed, attempt.Correlation));
            }

            var stored = new StoredPurchaseOrder(build(), companyAccountId, effectiveOn, productVersionsByLineId);
            _orders.Add(stored);
            _attempts[(PurchasingCommands.OrderCreateV1, envelope.IdempotencyKey)] = new Attempt(envelope.ActorId, requestHash, null, stored, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            return Task.FromResult(new PurchasingOrderStoreResult(stored, PurchasingWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<PurchasingOrderStoreResult?> MutateOrderAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, Guid orderId,
            Action<PurchaseOrder, DateTimeOffset> mutate, PurchasingMutationAudit audit, CancellationToken cancellationToken = default)
        {
            if (_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                Check(attempt, envelope, requestHash);
                if (attempt.Order is null) throw new PurchasingStoreException(PurchasingStoreCodes.ConcurrentChange, "synthetic refusal under the key");
                return Task.FromResult<PurchasingOrderStoreResult?>(new PurchasingOrderStoreResult(attempt.Order, PurchasingWriteOutcome.Replayed, attempt.Correlation));
            }

            var stored = _orders.SingleOrDefault(candidate => candidate.Order.Id == orderId);
            if (stored is null) return Task.FromResult<PurchasingOrderStoreResult?>(null);
            var before = stored.Order.Version;
            if (OverSourced && commandName == PurchasingCommands.OrderApproveV1)
            {
                // The SQL store measures the Σ-shares rule inside the save and leaves nothing tracked.
                throw new PurchasingStoreException(PurchasingStoreCodes.RequestLineOverSourced, "synthetic over-sourcing", null,
                    [new PurchasingFieldError("lines[0].sourceRequestLineId", PurchasingStoreCodes.RequestLineOverSourced)]);
            }

            mutate(stored.Order, TimeProvider.System.GetUtcNow());
            _attempts[(commandName, envelope.IdempotencyKey)] = new Attempt(envelope.ActorId, requestHash, null, stored, null, envelope.CorrelationId);
            Envelopes.Add(envelope);
            Transitions.Add((commandName, orderId, before, stored.Order.Version));
            return Task.FromResult<PurchasingOrderStoreResult?>(new PurchasingOrderStoreResult(stored, PurchasingWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<StoredPurchaseOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_orders.SingleOrDefault(candidate => candidate.Order.Id == orderId));

        public Task<PurchasingOrderPage> ListOrdersAsync(int pageSize, string? cursor, PurchaseOrderStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchasingOrderPage([.. _orders.Where(stored => status is null || stored.Order.Status == status)], null));

        public Task<PurchasingReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken = default)
        {
            Replays++;
            if (!_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                return Task.FromResult<PurchasingReplay?>(null);
            }

            Check(attempt, envelope, requestHash);
            return Task.FromResult<PurchasingReplay?>(new PurchasingReplay(attempt.Request, attempt.Order, attempt.Refusal, attempt.Correlation));
        }

        public Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
            CancellationToken cancellationToken = default)
        {
            if (_attempts.ContainsKey((commandName, envelope.IdempotencyKey)))
            {
                throw new PurchasingStoreException(PurchasingStoreCodes.ConcurrentChange, "synthetic");
            }

            _attempts[(commandName, envelope.IdempotencyKey)] = new Attempt(envelope.ActorId, requestHash, null, null, refusal, envelope.CorrelationId);
            Refusals.Add((commandName, refusal));
            return Task.CompletedTask;
        }

        private static void Check(Attempt attempt, PurchasingWriteEnvelope envelope, string requestHash)
        {
            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
            {
                throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "synthetic");
            }
        }
    }
}
