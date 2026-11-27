using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceWorkflowTests
{
    [Fact]
    public async Task Composite_without_actual_document_line_owner_still_refuses_occurrence_without_master_reads_or_record()
    {
        var world = new World(); var request = UnorderedRecordRequest(); var masters = new UnorderedMasterPorts(request); var store = new UnorderedMemoryStore();
        var prerequisite = Composite(world, masters, evidence: false);
        var service = new PurchaseUnorderedAcceptanceServiceV1(world.Company, world.User, world.Authority, new Correlation(), store, new Clock(), prerequisite);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordAsync(request, Metadata("nopo-no-evidence-owner")))).Status);
        Assert.Empty(masters.Calls); Assert.Null(store.Saved); Assert.Empty(world.Finance.Offers);
    }

    [Theory]
    [InlineData("goods-kg")]
    [InlineData("goods-piece")]
    [InlineData("service-hour")]
    public async Task Composite_reuses_real_master_contracts_and_only_the_evidenced_business_date_without_order_price_or_conversion(string kind)
    {
        var world = new World(); var request = UnorderedRecordRequest();
        if (kind == "goods-piece") request = request with { UnitId = SettingsUnitIdentitiesV1.Piece };
        if (kind == "service-hour") request = request with { Subject = new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "7"), AcceptedQuantity = "1.25" };
        var masters = new UnorderedMasterPorts(request) { IsKg = kind == "goods-kg", UnitCode = kind == "service-hour" ? "saat" : kind == "goods-piece" ? "adet" : "kg",
            Dimension = kind == "service-hour" ? "time" : kind == "goods-piece" ? "count" : "weight", Scale = kind == "goods-piece" ? 0 : 2 };
        var store = new UnorderedMemoryStore(); var prerequisite = Composite(world, masters);
        var service = new PurchaseUnorderedAcceptanceServiceV1(world.Company, world.User, world.Authority, new Correlation(), store, new Clock(), prerequisite);
        var result = await service.RecordAsync(request, Metadata("nopo-composite"));
        Assert.False(result.IsReplay); Assert.Empty(result.Record.Source.ControlEvidence); Assert.Equal(request.Subject, result.Record.Source.Subject);
        Assert.Equal(request.UnitId, result.Record.Source.UnitId); Assert.Equal(request.AcceptedQuantity, result.Record.Source.AcceptedQuantity);
        Assert.Equal(new[] { "evidence", "supplier", kind == "service-hour" ? "service" : "product", "unit" }, masters.Calls);
        Assert.All(masters.EffectiveDates, day => Assert.Equal(masters.BusinessDate, day));
        Assert.NotEqual(DateOnly.FromDateTime(request.OccurredAtUtc.UtcDateTime), masters.BusinessDate);
        Assert.Empty(world.Finance.Offers);
    }

    [Theory]
    [InlineData("company")][InlineData("acceptance")][InlineData("line")][InlineData("supplier")][InlineData("account")]
    [InlineData("subject")][InlineData("unit")][InlineData("quantity")][InlineData("occurrence")][InlineData("document")]
    [InlineData("document-version")][InlineData("hash")][InlineData("document-line")][InlineData("business-date")][InlineData("control")]
    public async Task Composite_requires_actual_document_line_date_and_every_exact_selection_binding_before_reading_masters(string defect)
    {
        var world = new World(); var request = UnorderedRecordRequest(); var masters = new UnorderedMasterPorts(request);
        var p = masters.Evidence.Facts!;
        p = defect switch
        {
            "company" => p with { CompanyId = Guid.NewGuid() }, "acceptance" => p with { AcceptanceId = Guid.NewGuid() },
            "line" => p with { AcceptanceLineId = Guid.NewGuid() }, "supplier" => p with { SupplierPartyId = Guid.NewGuid() },
            "account" => p with { CompanyAccountId = Guid.NewGuid() }, "subject" => p with { Subject = new PurchaseUnorderedGoodsSubjectV1(Guid.NewGuid(), "1") },
            "unit" => p with { UnitId = Guid.NewGuid() }, "quantity" => p with { AcceptedQuantity = "9" },
            "occurrence" => p with { OccurredAtUtc = p.OccurredAtUtc.AddMinutes(1) }, "document" => p with { Evidence = p.Evidence with { DocumentId = Guid.NewGuid() } },
            "document-version" => p with { Evidence = p.Evidence with { DocumentVersion = 2 } }, "hash" => p with { Evidence = p.Evidence with { Sha256 = new string('b', 64) } },
            "document-line" => p with { Evidence = p.Evidence with { SourceLineId = "SENTETIK-other-line" } },
            "business-date" => p with { BusinessDate = null }, _ => p with { ControlEvidence = [p.Evidence] },
        };
        masters.Evidence = masters.Evidence with { Facts = p };
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => Composite(world, masters)
            .ValidateAsync(Company, request, _ => Task.CompletedTask, default))).Status);
        Assert.Equal(new[] { "evidence" }, masters.Calls);
    }

    [Theory]
    [InlineData(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable, 503)]
    [InlineData(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.NotFound, 404)]
    [InlineData(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied, 403)]
    [InlineData(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found, 503)]
    public async Task Evidence_owner_refusals_or_missing_found_facts_are_not_success(PurchaseUnorderedAcceptanceEvidenceOutcomeV1 outcome, int expected)
    {
        var world = new World(); var request = UnorderedRecordRequest(); var masters = new UnorderedMasterPorts(request) { Evidence = new(outcome, null) };
        Assert.Equal(expected, (await Assert.ThrowsAsync<PurchasingServiceException>(() => Composite(world, masters)
            .ValidateAsync(Company, request, _ => Task.CompletedTask, default))).Status);
        Assert.Equal(new[] { "evidence" }, masters.Calls);
    }

    [Theory]
    [InlineData("supplier-denied", 403)][InlineData("supplier-missing", 404)][InlineData("supplier-inactive", 422)]
    [InlineData("supplier-role", 422)][InlineData("supplier-version", 409)][InlineData("supplier-company", 503)]
    [InlineData("product-denied", 403)][InlineData("product-refused", 422)][InlineData("product-version", 409)]
    [InlineData("unit-company", 422)][InlineData("unit-profile", 409)][InlineData("unit-denied", 403)]
    [InlineData("unit-scale", 422)][InlineData("unit-unsupported", 422)]
    [InlineData("service-inactive", 422)][InlineData("service-version", 409)][InlineData("service-unit-company", 503)]
    public async Task Existing_master_refusals_versions_roles_and_goods_service_unit_rules_remain_fail_closed(string fault, int expected)
    {
        var world = new World(); var request = UnorderedRecordRequest();
        if (fault.StartsWith("service-", StringComparison.Ordinal)) request = request with { Subject = new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "7") };
        if (fault == "unit-scale") request = request with { AcceptedQuantity = "1.2345" };
        var masters = new UnorderedMasterPorts(request) { Fault = fault };
        Assert.Equal(expected, (await Assert.ThrowsAsync<PurchasingServiceException>(() => Composite(world, masters)
            .ValidateAsync(Company, request, _ => Task.CompletedTask, default))).Status);
    }

    [Theory]
    [InlineData("evidence")][InlineData("supplier")][InlineData("product")][InlineData("service")][InlineData("unit")]
    public async Task Every_owner_await_is_followed_by_the_existing_record_authority_fence_before_any_record(string phase)
    {
        var world = new World(); var request = UnorderedRecordRequest();
        if (phase == "service") request = request with { Subject = new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "7") };
        var masters = new UnorderedMasterPorts(request) { AfterRead = current => { if (current == phase) world.Authority.Denied = PurchasingPermissionManifestV1.GoodsReceiptsRecord; } };
        var store = new UnorderedMemoryStore(); var service = new PurchaseUnorderedAcceptanceServiceV1(world.Company, world.User, world.Authority,
            new Correlation(), store, new Clock(), Composite(world, masters));
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordAsync(request, Metadata("nopo-owner-revoke")))).Status);
        Assert.Equal(phase, masters.Calls[^1]); Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Completed_consumer_callback_cancellation_cannot_release_valid_master_evidence()
    {
        var world = new World(); var request = UnorderedRecordRequest(); var masters = new UnorderedMasterPorts(request);
        using var cancellation = new CancellationTokenSource(); var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Composite(world, masters).ValidateAsync(Company, request,
            _ => { calls++; if (calls == 3) cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(new[] { "evidence" }, masters.Calls);
    }

    [Fact]
    public async Task Owner_control_list_is_captured_before_the_post_read_fence_can_mutate_it()
    {
        var world = new World(); var original = UnorderedRecordRequest(); var request = original with { ControlEvidence = [original.Evidence] };
        var masters = new UnorderedMasterPorts(request); var controls = new List<PurchaseAcceptanceDocumentEvidenceV1> { request.Evidence };
        masters.Evidence = masters.Evidence with { Facts = masters.Evidence.Facts! with { ControlEvidence = controls } };
        var changed = false;
        await Composite(world, masters).ValidateAsync(Company, request, _ =>
        {
            if (!changed && masters.Calls.LastOrDefault() == "evidence") { changed = true; controls.Add(controls[0]); controls.Add(controls[0] with { DocumentId = Guid.Empty }); }
            return Task.CompletedTask;
        }, default);
        Assert.True(changed); Assert.Equal(3, controls.Count); Assert.Equal(new[] { "evidence", "supplier", "product", "unit" }, masters.Calls);
        Assert.Equal(request.Evidence, Assert.Single(request.ControlEvidence));
    }

    private static PurchaseUnorderedAcceptancePrerequisitesV1 Composite(World world, UnorderedMasterPorts masters, bool evidence = true) =>
        new(world.Company, masters, masters, masters, new(masters), masters, evidence ? masters : null);

    // Existing owner port shapes, synthetic data only. This fixture is NOT a real Documents owner or operational NOPO acceptance.
    private sealed class UnorderedMasterPorts : IPurchasingPartyAccountPortV1, IProductPurchaseEligibilityPortV1,
        ISettingsPrerequisiteProvider, ISettingsReferenceListReaderV1, IPurchaseUnorderedAcceptanceEvidenceReadV1
    {
        private readonly RecordPurchaseUnorderedAcceptanceV1 request;
        public readonly DateOnly BusinessDate = new(2026, 10, 8);
        public PurchaseUnorderedAcceptanceEvidenceAnswerV1 Evidence { get; set; }
        public List<string> Calls { get; } = []; public List<DateOnly> EffectiveDates { get; } = [];
        public string Fault { get; init; } = ""; public bool IsKg { get; init; } = true;
        public string UnitCode { get; init; } = "kg"; public string Dimension { get; init; } = "weight"; public int Scale { get; init; } = 3;
        public Action<string>? AfterRead { get; init; }
        public UnorderedMasterPorts(RecordPurchaseUnorderedAcceptanceV1 request)
        {
            this.request = request;
            Evidence = new(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found, new(Company, request.AcceptanceId, request.AcceptanceLineId,
                request.SupplierPartyId, request.CompanyAccountId, request.Subject, request.UnitId, request.AcceptedQuantity,
                request.OccurredAtUtc, BusinessDate, request.Evidence, request.ControlEvidence));
        }
        private void Read(string phase) { Calls.Add(phase); AfterRead?.Invoke(phase); }
        public Task<PurchaseUnorderedAcceptanceEvidenceAnswerV1> ReadAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 value,
            Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken)
        { Read("evidence"); Assert.Equal(Company, companyId); Assert.Equal(request.AcceptanceId, value.AcceptanceId); return Task.FromResult(Evidence); }
        public Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(Guid party, Guid account, CancellationToken token = default)
        {
            Read("supplier");
            return Task.FromResult(Fault switch
            {
                "supplier-denied" => new(PurchasingPartyAccountOutcomeV1.Denied, null), "supplier-missing" => new(PurchasingPartyAccountOutcomeV1.NotFound, null),
                _ => new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Found, new(party, account,
                    Fault == "supplier-company" ? Guid.NewGuid() : Company, true, Fault != "supplier-inactive", Fault != "supplier-role",
                    Fault == "supplier-version" ? "different" : request.ExpectedPartyVersion, request.ExpectedAccountVersion)),
            });
        }
        public Task<ProductPurchaseEligibilityAnswerV1> EvaluateAsync(ProductPurchaseSelectionV1 selection, CancellationToken token = default)
        {
            Read("product"); EffectiveDates.Add(selection.EffectiveOn);
            return Task.FromResult(Fault switch
            {
                "product-denied" => new(ProductPurchaseEligibilityOutcomeV1.Denied, null, []),
                "product-refused" => new(ProductPurchaseEligibilityOutcomeV1.Refused, null, [new("productId", "not_purchasable")]),
                "product-version" => new(ProductPurchaseEligibilityOutcomeV1.Refused, null, [new("expectedProductVersion", "version_conflict")]),
                _ => new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Eligible, selection.ExpectedProductVersion, []),
            });
        }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unit, DateOnly day, CancellationToken token = default)
        {
            Read("unit"); EffectiveDates.Add(day);
            if (Fault == "unit-denied") throw new SettingsPrerequisiteException("settings_access_denied");
            return Task.FromResult(new UnitProfileV1(Fault is "unit-company" or "service-unit-company" ? Guid.NewGuid() : Company, unit,
                UnitCode, Dimension, IsKg && Fault != "unit-unsupported", Fault == "unit-profile" ? new(Guid.NewGuid(), 1) : request.UnitProfile,
                Scale, true, BusinessDate.AddDays(-1), null));
        }
        public Task<SettingsReferenceListLookupV1> GetAsync(Guid company, SettingsReferenceListKindV1 kind, Guid id, CancellationToken token = default)
        {
            Read("service"); Assert.Equal(Company, company); Assert.Equal(SettingsReferenceListKindV1.ServiceType, kind);
            return Task.FromResult(new SettingsReferenceListLookupV1(Fault == "service-inactive" ? SettingsReferenceListLookupOutcomeV1.Inactive
                : SettingsReferenceListLookupOutcomeV1.Found, new(id, "SENTETIK-service", "SENTETIK hizmet", Fault != "service-inactive", Fault == "service-version" ? 8 : 7)));
        }
        public Task<SettingsReferenceListV1> ListAsync(Guid company, SettingsReferenceListKindV1 kind, CancellationToken token = default) => throw new InvalidOperationException("No general master browse.");
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currency, string operation, DateOnly day, CancellationToken token = default) => throw new InvalidOperationException("No price or money profile.");
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly day, DateOnly? value, CancellationToken token = default) => throw new InvalidOperationException("No financial period admission.");
    }
}
