using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceImportAdmissionTests
{
    [Fact]
    public async Task Foreign_import_retains_file_mapping_and_actual_profile_proofs_without_finalizing_or_admitting_Finance()
    {
        var world = new World();
        var result = await world.Service.RecordAsync(world.Id, world.Request, World.Metadata);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, result.Invoice.Status);
        Assert.Null(result.Invoice.AdmissionId);
        Assert.Equal(2m, result.Invoice.OfficialRate);
        Assert.Equal(10m, Assert.Single(result.Invoice.Lines).Amounts.Net);
        Assert.Equal(20.01m, Assert.Single(result.Invoice.Lines).FunctionalAmounts!.Net);
        Assert.Equal(new PurchaseInvoiceAmounts(20.01m, 4m, 0m), result.Invoice.FunctionalAmounts);
        Assert.Equal(world.Id, result.Invoice.Source.ImportProof!.ImportId);
        Assert.Contains("source-01", result.Invoice.Source.ImportProof.MappingJson, StringComparison.Ordinal);
        Assert.Equal("OfficialInvoiceRate", result.Invoice.Source.ImportProof.OfficialRatePurpose);
        Assert.Equal(PurchaseIncomingInvoicePermissionNamesV1.Import, world.SavedEnvelope!.PermissionCode);
        Assert.Equal("1", result.Invoice.Source.DocumentVersion.ToString());
    }

    [Theory]
    [InlineData("import", 403)]
    [InlineData("price", 403)]
    [InlineData("supplier-version", 409)]
    [InlineData("product-version", 409)]
    [InlineData("unit-company", 503)]
    [InlineData("source-line", 400)]
    [InlineData("source-currency", 400)]
    [InlineData("official-rate", 400)]
    public async Task Invalid_or_stale_owner_bindings_never_record_an_invoice(string defect, int status)
    {
        var world = new World(); var request = world.Request;
        if (defect == "import") world.Denied = PurchaseIncomingInvoicePermissionNamesV1.Import;
        if (defect == "price") world.Denied = PurchasingPermissionManifestV1.OrdersPriceRead;
        if (defect == "supplier-version") request = request with { ExpectedAccountVersion = "2" };
        if (defect == "product-version") world.ProductVersion = "2";
        if (defect == "unit-company") world.UnitCompany = Guid.NewGuid();
        if (defect == "source-line") request = request with { Lines = [request.Lines[0] with { SourceLineId = "unknown" }] };
        if (defect == "source-currency") world.File = world.File with { Currency = "EUR" };
        if (defect == "official-rate") world.File = world.File with { Rates = [] };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.RecordAsync(world.Id, request, World.Metadata));
        Assert.Equal(status, error.Status); Assert.Null(world.Saved);
        if (status == 403) Assert.Equal(0, world.PreviewReads);
    }

    [Fact]
    public async Task Stored_replay_uses_current_authority_and_does_not_reinterpret_or_reread_the_file()
    {
        var world = new World();
        var original = await world.Service.RecordAsync(world.Id, world.Request, World.Metadata);
        world.File = world.File with { Currency = "BROKEN" };
        var replay = await world.Service.RecordAsync(world.Id, world.Request, World.Metadata);
        Assert.True(replay.IsReplay); Assert.Equal(original.Invoice, replay.Invoice); Assert.Equal(1, world.PreviewReads);
        world.Denied = PurchaseIncomingInvoicePermissionNamesV1.Import;
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.RecordAsync(world.Id, world.Request, World.Metadata))).Status);
    }

    [Fact]
    public async Task Terminal_owner_version_refusal_replays_without_reinterpreting_new_master_versions()
    {
        var world = new World { ProductVersion = "2" };
        var first = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.RecordAsync(world.Id, world.Request, World.Metadata));
        Assert.Equal(409, first.Status);
        Assert.False(first.IsReplay);
        world.ProductVersion = "1";
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.RecordAsync(world.Id, world.Request, World.Metadata));
        Assert.Equal(first.Code, replay.Code);
        Assert.True(replay.IsReplay); Assert.Equal(World.Metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Null(world.Saved);
        Assert.Equal(1, world.PreviewReads);
        Assert.Equal(1, world.RefusalWrites);
    }

    [Fact]
    public async Task Source_supplied_functional_lines_are_not_recomputed_and_revocation_before_save_refuses_the_command()
    {
        var world = new World();
        world.File = world.File with { Lines = [world.File.Lines[0] with { FunctionalAmounts = new("20.01", "4", "0") }] };
        var result = await world.Service.RecordAsync(world.Id, world.Request, World.Metadata);
        Assert.Equal(20.01m, result.Invoice.Lines[0].FunctionalAmounts!.Net);
        var revoked = new World { BeforeSave = true };
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => revoked.Service.RecordAsync(revoked.Id, revoked.Request, World.Metadata))).Status);
        Assert.Null(revoked.Saved);
    }

    [Fact]
    public async Task Source_due_date_and_header_tax_facts_cannot_be_silently_replaced_by_line_mapping()
    {
        var due = new World();
        due.File = due.File with { Payments = [new(new(2026, 12, 18), null, Source: "InvoiceDueDate")] };
        Assert.Equal(400, (await Assert.ThrowsAsync<PurchasingServiceException>(() => due.Service.RecordAsync(due.Id, due.Request, World.Metadata))).Status);
        Assert.Null(due.Saved);
        var tax = new World();
        tax.File = tax.File with { Taxes = [new("0015", "20", new("USD", "3"), null, false)] };
        Assert.Equal(400, (await Assert.ThrowsAsync<PurchasingServiceException>(() => tax.Service.RecordAsync(tax.Id, tax.Request, World.Metadata))).Status);
        Assert.Null(tax.Saved);
    }

    [Fact]
    public async Task Single_foreign_installment_uses_the_authoritative_total_but_multiple_installments_require_explicit_TL_shares()
    {
        var one = new World(); var request = one.Request with { Installments = [one.Request.Installments[0] with { FunctionalAmount = null }] };
        var recorded = await one.Service.RecordAsync(one.Id, request, World.Metadata);
        Assert.Equal(24.01m, Assert.Single(recorded.Invoice.Installments).FunctionalAmount);
        var many = new World(); var first = many.Request.Installments[0];
        var invalid = many.Request with { Installments = [first with { Amount = "6", FunctionalAmount = null },
            first with { Id = Guid.NewGuid(), OpenItemId = Guid.NewGuid(), Sequence = 2, Amount = "6", FunctionalAmount = null }] };
        Assert.Equal(400, (await Assert.ThrowsAsync<PurchasingServiceException>(() => many.Service.RecordAsync(many.Id, invalid, World.Metadata))).Status);
        Assert.Null(many.Saved);
    }

    [Fact]
    public async Task Source_payment_amount_without_a_due_date_accepts_an_explicit_date_without_replacing_the_source_amount()
    {
        var world = new World(); world.File = world.File with { Payments = [new(null, new("USD", "12"), Source: "PaymentMeans")] };
        Assert.Equal(new DateOnly(2026, 12, 17), Assert.Single((await world.Service.RecordAsync(world.Id, world.Request, World.Metadata)).Invoice.Installments).DueOn);
    }

    [Fact]
    public async Task Mapping_reads_actual_selected_versions_profiles_and_server_TL_total_without_recording_or_granting()
    {
        var world = new World(); var request = world.Request;
        var refs = await world.Service.ReferencesAsync(world.Id, request.Source);
        Assert.Equal("KGM", Assert.Single(refs.Units).Code); Assert.Equal("24.01", refs.FunctionalPayable);
        Assert.Equal("USD", Assert.Single(refs.Currencies).Code); Assert.NotNull(refs.ServiceTypes); Assert.Empty(refs.ServiceTypes);
        var supplier = await world.Service.SupplierAsync(world.Id, new(request.Source, request.SupplierPartyId, request.CompanyAccountId));
        Assert.Equal(("1", "1"), (supplier.PartyVersion, supplier.AccountVersion));
        var product = await world.Service.ProductsAsync(world.Id, new(request.Source, "SENTETIK"));
        Assert.Equal(request.Lines[0].ProductId, Assert.Single(product.Items).ProductId); Assert.Null(world.Saved);
        world.Denied = PurchasingPermissionManifestV1.OrdersPriceRead;
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.SupplierAsync(world.Id,
            new(request.Source, request.SupplierPartyId, request.CompanyAccountId)))).Status);
    }

    [Fact]
    public async Task Technical_concurrent_write_is_not_stored_as_a_terminal_import_refusal()
    {
        var world = new World { ConcurrentWrite = true };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.RecordAsync(world.Id, world.Request, World.Metadata));
        Assert.Equal(PurchasingProblemCodesV1.ConcurrentChange, error.Code); Assert.Equal(0, world.RefusalWrites); Assert.Null(world.Saved);
        world.ConcurrentWrite = false;
        Assert.False((await world.Service.RecordAsync(world.Id, world.Request, World.Metadata)).IsReplay);
    }

    [Fact]
    public async Task Import_installment_never_rounds_excess_significant_input_digits_into_the_official_amount()
    {
        var world = new World(); var request = world.Request with { Installments = [world.Request.Installments[0] with { Amount = "12.00000000000000000000000000001" }] };
        Assert.Equal(400, (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.RecordAsync(world.Id, request, World.Metadata))).Status);
        Assert.Null(world.Saved); Assert.Equal(0, world.RefusalWrites);
    }

    private sealed class World : ICompanyContext, ICurrentUser, IErpAuthorityRevalidationService, ICorrelationContext,
        IPurchaseInvoiceImportV1, IPurchasingIncomingInvoiceStoreV1, IPurchasingPartyAccountPortV2,
        IProductPurchaseEligibilityPortV1, ISettingsPrerequisiteProvider, ISettingsReferenceListReaderV1,
        ISettingsUnitIdentityCatalogV1, ISettingsMoneyCandidateReaderV2, IPurchaseInvoiceImportProductSearchV1
    {
        public static PurchasingCommandMetadata Metadata => new("synthetic-import-key", "synthetic-import-correlation", null, DateTimeOffset.UtcNow);
        public Guid Id { get; } = Guid.NewGuid();
        public Guid CompanyId { get; } = Guid.NewGuid();
        private readonly Guid supplier = Guid.NewGuid(), account = Guid.NewGuid(), product = Guid.NewGuid(), unit = Guid.NewGuid();
        public Guid? UnitCompany { get; set; }
        public string? Denied { get; set; }
        public string ProductVersion { get; set; } = "1";
        public bool BeforeSave { get; set; }
        public bool ConcurrentWrite { get; set; }
        public PurchaseIncomingInvoiceSnapshot? Saved { get; private set; }
        public PurchasingWriteEnvelope? SavedEnvelope { get; private set; }
        public int PreviewReads { get; private set; }
        public int RefusalWrites { get; private set; }
        private PurchasingTerminalRefusal? terminalRefusal;
        public PurchaseInvoiceFileFactsV1 File { get; set; }
        public World()
        {
            File = new("SYNTHETIC20260001", Guid.NewGuid(), new(2026, 11, 17), "SATIS", "USD", [], [],
                new Dictionary<string, PurchaseInvoiceFileAmountV1>
                { ["LineExtensionAmount"] = new("USD", "10"), ["TaxExclusiveAmount"] = new("USD", "10"),
                  ["TaxInclusiveAmount"] = new("USD", "12"), ["PayableAmount"] = new("USD", "12") }, [], [],
                [new("OfficialInvoiceRate", "USD", "TRY", "2", null, null, null, new(2026, 11, 17))], [],
                [new("source-01", "synthetic-product", null, "Sentetik ürün", "KGM", "5", new("USD", "10"),
                    [new("0015", "20", new("USD", "2"), new("USD", "10"), false)], [])], new("20.01", "4", "0"));
        }
        public PurchaseInvoiceImportAdmission Service => new(this, this, this, this, this, this, this, this, this, this, TimeProvider.System, this, this, this);
        public PurchaseInvoiceImportAdmissionRequestV1 Request => new(new("1", Id, 1, new string('a', 64), new(PurchaseInvoiceFileFormatV1.Csv)),
            "0", supplier, account, "1", "1", SettingsCurrencyIdentitiesV1.UnitedStatesDollar,
            [new("source-01", Guid.Parse("86f4a3cf-261b-4372-ae49-bbe8606a8a33"), product, "1", unit, "General", "resale", "0015")],
            [new(Guid.Parse("196081c5-19ef-45b3-a333-c0d68d576749"), Guid.Parse("f029bf01-af08-4994-9e7b-3d9da6f5d305"), 1, new(2026, 12, 17), "12", "24.01")]);
        public bool IsResolved => true; public void Set(Guid companyId) { }
        public bool IsAuthenticated => true; public string UserId => "synthetic-import-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyId }; public bool CanAccessCompany(Guid value) => value == CompanyId;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permissionCode, Guid companyId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PermissionScope(Denied != permissionCode, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        public string CorrelationId => "synthetic-import-correlation"; public string? CausationId => null;
        public string SourceChannel => "Test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
        public Task<PurchaseInvoiceImportPreviewV1> PreviewAsync(Guid importId, PurchaseInvoiceImportPreviewRequestV1 request, CancellationToken token = default)
        { PreviewReads++; return Task.FromResult(new PurchaseInvoiceImportPreviewV1(importId, CompanyId, "1", Id, 1, new string('a', 64), File, [])); }
        public Task<PurchaseInvoiceImportReservationPageV1> ListAsync(int pageIndex, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PurchaseInvoiceImportReservationV1> ReadAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<PurchasingPartyAccountAnswerV2> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PurchasingPartyAccountAnswerV2(PurchasingPartyAccountOutcomeV1.Found, new(partyId, companyAccountId, CompanyId, true, true, true, false, "1", "1")));
        public Task<ProductPurchaseEligibilityAnswerV1> EvaluateAsync(ProductPurchaseSelectionV1 selection, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Eligible, ProductVersion, []));
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid id, DateOnly date, CancellationToken cancellationToken = default)
            => Task.FromResult(new UnitProfileV1(UnitCompany ?? CompanyId, id, "KGM", "mass", true, new(id, 1), 3, true, date, null));
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid id, string operation, DateOnly date, CancellationToken cancellationToken = default)
            => Task.FromResult(new MoneyProfileV1(CompanyId, id, id == SettingsCurrencyIdentitiesV1.TurkishLira ? "TRY" : "USD", operation, new(id, 1), 2, true, date, null));
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SettingsReferenceListLookupV1> GetAsync(Guid companyId, SettingsReferenceListKindV1 kind, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SettingsReferenceListV1> ListAsync(Guid companyId, SettingsReferenceListKindV1 kind, CancellationToken cancellationToken = default) => Task.FromResult(new SettingsReferenceListV1(companyId, kind, true, []));
        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<UnitIdentityV1>>([new(unit, "KGM", "mass", true)]);
        Task<UnitIdentityV1?> ISettingsUnitIdentityCatalogV1.ReadAsync(Guid id, CancellationToken token) => Task.FromResult<UnitIdentityV1?>(new(id, "KGM", "mass", true));
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(string operation, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(SettingsCurrencyIdentitiesV1.UnitedStatesDollar, "USD", "SENTETIK USD")]);
        public Task<(IReadOnlyList<PurchaseInvoiceImportProductV1> Items, bool SearchWindowFull)> SearchAsync(string? q, DateOnly on, CancellationToken token) =>
            Task.FromResult<(IReadOnlyList<PurchaseInvoiceImportProductV1>, bool)>(([new(product, ProductVersion, "SYN", "SENTETIK", unit, "KGM", unit, "1")], false));
        public Task<PurchaseIncomingInvoiceSnapshot?> FindIncomingInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public Task<PurchasingIncomingInvoiceWriteResult?> FindIncomingInvoiceAttemptAsync(string command, PurchasingWriteEnvelope envelope, string hash, CancellationToken cancellationToken = default)
            => Task.FromResult(Saved is null && terminalRefusal is null ? null : new PurchasingIncomingInvoiceWriteResult(Saved, terminalRefusal, true, Metadata.CorrelationId));
        public async Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceAsync(PurchaseIncomingInvoice invoice, PurchasingWriteEnvelope envelope,
            string hash, Func<CancellationToken, Task> fence, CancellationToken cancellationToken = default)
        { if (ConcurrentWrite) throw new PurchasingStoreException(PurchasingStoreCodes.ConcurrentChange, "SENTETIK concurrent write");
            if (BeforeSave) Denied = PurchaseIncomingInvoicePermissionNamesV1.Import; await fence(cancellationToken); SavedEnvelope = envelope;
            Saved = PurchaseIncomingInvoiceSnapshot.Capture(invoice); return new(Saved, null, false, envelope.CorrelationId); }
        public Task<PurchasingIncomingInvoiceWriteResult?> MutateIncomingInvoiceAsync(Guid invoiceId, string command, PurchasingWriteEnvelope envelope, string hash,
            IReadOnlyList<Guid> ids, Func<PurchaseIncomingInvoice, CancellationToken, Task> mutate, Func<CancellationToken, Task> fence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceRefusalAsync(string command, PurchasingWriteEnvelope envelope, string hash,
            PurchasingTerminalRefusal refusal, Func<CancellationToken, Task> fence, CancellationToken cancellationToken = default)
        {
            await fence(cancellationToken); RefusalWrites++; terminalRefusal = refusal;
            return new(null, refusal, false, envelope.CorrelationId);
        }
    }
}
