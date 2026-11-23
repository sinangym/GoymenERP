using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Imports;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Actual_file_reader_and_import_admission_store_one_SQL_invoice_and_replay_without_rereading_or_Finance_effects()
    {
        await using var run = await Run.StartAsync();
        var inputs = new SyntheticImportAdmissionInputs(run.Actor, CompanyA);
        Guid id; PurchaseInvoiceImportAdmissionRequestV1 request;
        var metadata = new PurchasingCommandMetadata("SENTETIK-import-admission", run.Correlation, null, DateTimeOffset.UtcNow);
        await using (var scope = run.Store(CompanyA))
        {
            var imports = inputs.Imports(scope.Store);
            id = (await imports.ReserveAsync(metadata with { IdempotencyKey = "SENTETIK-reserve-admission" })).Reservation.Id;
            request = inputs.Request();
            var admission = inputs.Admission(imports, scope.Store);
            var preview = await imports.PreviewAsync(id, request.Source);
            Assert.Empty(preview.Issues); Assert.Equal("001", Assert.Single(preview.Invoice!.Lines).Id);
            var references = await admission.ReferencesAsync(id, request.Source);
            Assert.Equal(inputs.UnitId, Assert.Single(references.Units).UnitId);
            var first = await admission.RecordAsync(id, request, metadata);
            Assert.Equal(GoymenERP.Modules.Purchasing.Domain.Invoices.PurchaseIncomingInvoiceStatus.Recorded, first.Invoice.Status);
            Assert.Contains("001", first.Invoice.Source.ImportProof!.MappingJson, StringComparison.Ordinal);
            using var typeProof = System.Text.Json.JsonDocument.Parse(first.Invoice.Source.ImportProof.ProductItemTypesJson!);
            Assert.Equal("Steel", Assert.Single(typeProof.RootElement.EnumerateArray()).GetProperty("itemType").GetString());
            Assert.Equal(7, typeProof.RootElement[0].GetProperty("itemTypeVersion").GetInt64());
            var reads = inputs.DocumentReads;
            var replay = await admission.RecordAsync(id, request, metadata with { CorrelationId = "SENTETIK-retry" });
            Assert.True(replay.IsReplay); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
            Assert.Equal(reads, inputs.DocumentReads);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first.Invoice), System.Text.Json.JsonSerializer.Serialize(replay.Invoice));
        }
        await using (var stored = run.Store(CompanyA))
        {
            var invoice = await ((IPurchasingIncomingInvoiceStoreV1)stored.Store).FindIncomingInvoiceAsync(id);
            Assert.Contains("Steel", invoice!.Source.ImportProof!.ProductItemTypesJson!, StringComparison.Ordinal);
        }
        await using (var other = run.Store(CompanyB)) Assert.Null(await ((IPurchasingIncomingInvoiceStoreV1)other.Store).FindIncomingInvoiceAsync(id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices WHERE InvoiceId=@id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE CorrelationId=@correlation", ("@correlation", metadata.CorrelationId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.IncomingInvoice' AND EntityId=@id", ("@id", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE AggregateId=@id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Actual_import_record_save_failure_rolls_back_invoice_attempt_and_audit_and_the_same_key_can_retry()
    {
        await using var run = await Run.StartAsync();
        var inputs = new SyntheticImportAdmissionInputs(run.Actor, CompanyA);
        var metadata = new PurchasingCommandMetadata("SENTETIK-import-rollback", run.Correlation, null, DateTimeOffset.UtcNow);
        Guid id;
        await using (var reservation = run.Store(CompanyA))
            id = (await inputs.Imports(reservation.Store).ReserveAsync(metadata with { IdempotencyKey = "SENTETIK-reserve-rollback" })).Reservation.Id;
        await using (var broken = run.Store(CompanyA, new ImportRecordSavedFailure()))
        {
            var admission = inputs.Admission(inputs.Imports(broken.Store), broken.Store);
            var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => admission.RecordAsync(id, inputs.Request(), metadata));
            Assert.Equal(503, error.Status);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices WHERE InvoiceId=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts WHERE CorrelationId=@correlation", ("@correlation", metadata.CorrelationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.IncomingInvoice' AND EntityId=@id", ("@id", id.ToString("D"))));
        await using var retry = run.Store(CompanyA);
        Assert.False((await inputs.Admission(inputs.Imports(retry.Store), retry.Store).RecordAsync(id, inputs.Request(), metadata)).IsReplay);
    }

    private sealed class ImportRecordSavedFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
            => throw new InvalidOperationException("SENTETIK failure after the actual SQL invoice write, before commit");
    }

    // Real Purchasing reservation/store/reader/application service; only Documents bytes and prerequisite masters are named synthetic inputs.
    // This does not claim real Documents, Cari, Product or Settings owner acceptance.
    private sealed class SyntheticImportAdmissionInputs(string actor, Guid companyId) : ICompanyContext, ICurrentUser,
        IErpAuthorityRevalidationService, ICorrelationContext, IPurchaseInvoiceImportDocumentReadV1,
        IPurchasingPartyAccountPortV2, IProductPurchaseEligibilityPortV1, ISettingsPrerequisiteProvider,
        ISettingsReferenceListReaderV1, ISettingsUnitIdentityCatalogV1, ISettingsMoneyCandidateReaderV2, IPurchasingProductItemTypePortV1
    {
        public Guid UnitId { get; } = Guid.NewGuid();
        private readonly Guid productId = Guid.NewGuid(), partyId = Guid.NewGuid(), accountId = Guid.NewGuid(), documentId = Guid.NewGuid();
        private readonly Guid lineId = Guid.NewGuid(), installmentId = Guid.NewGuid(), openItemId = Guid.NewGuid();
        private static readonly string[] Columns = ["Number", "Ettn", "InvoiceDate", "InvoiceType", "Currency", "SupplierTaxId", "SupplierTaxScheme",
            "BuyerTaxId", "BuyerTaxScheme", "LineId", "Description", "UnitCode", "Quantity", "LineNet", "LineExtensionAmount", "TaxExclusiveAmount",
            "TaxInclusiveAmount", "PayableAmount", "LineTaxAmount", "LineTaxCode", "TaxAmount", "TaxCode", "LineTaxPercent", "TaxPercent"];
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(string.Join(';', Columns) + "\n" + string.Join(';',
            new[] { "SENTETIK20260001", Guid.NewGuid().ToString("D"), "2026-11-17", "SATIS", "TRY", "0123456789", "VKN", "9876543210", "VKN",
                "001", "SENTETIK mal", "KGM", "2.5", "100", "100", "100", "120", "120", "20", "0015", "20", "0015", "20", "20" }));
        public int DocumentReads { get; private set; }
        public PurchaseInvoiceImport Imports(object store) => new(this, this, this, this, (IPurchaseInvoiceImportReservationStoreV1)store, this, new PurchaseInvoiceFileReader());
        public PurchaseInvoiceImportAdmission Admission(IPurchaseInvoiceImportV1 imports, object store) => new(this, this, this, this, imports,
            (IPurchasingIncomingInvoiceStoreV1)store, this, this, this, this, TimeProvider.System, this, this, itemTypes: this);
        public PurchaseInvoiceImportAdmissionRequestV1 Request() => new(new("1", documentId, 1, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            new(PurchaseInvoiceFileFormatV1.Csv, Columns: Columns.ToDictionary(x => x, x => x), Delimiter: ';')), "0", partyId, accountId, "1", "1",
            SettingsCurrencyIdentitiesV1.TurkishLira, [new("001", lineId, productId, "1", UnitId, "Steel", "resale", "0015", ExpectedItemTypeVersion: "7")],
            [new(installmentId, openItemId, 1, new(2026, 12, 17), "120", null)]);
        public Task<PurchaseInvoiceImportDocumentV1> ReadAsync(Guid importId, Guid id, int version, CancellationToken token = default)
        { DocumentReads++; return Task.FromResult(new PurchaseInvoiceImportDocumentV1(companyId, documentId, 1, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), PurchaseInvoiceFileFormatV1.Csv, bytes)); }
        public bool IsResolved => true; public Guid CompanyId => companyId; public void Set(Guid id) => throw new NotSupportedException();
        public bool IsAuthenticated => true; public string UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { companyId }; public bool CanAccessCompany(Guid id) => id == companyId;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permissionCode, Guid id, CancellationToken token = default)
            => Task.FromResult(new PermissionScope(id == companyId, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        public string CorrelationId => "SENTETIK-import-source"; public string? CausationId => null; public string SourceChannel => SourceChannels.Web;
        public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string id, string? cause, string channel, string? ip, string? agent) => throw new NotSupportedException();
        public Task<PurchasingPartyAccountAnswerV2> GetAuthorizedAsync(Guid party, Guid account, CancellationToken token = default)
            => Task.FromResult(new PurchasingPartyAccountAnswerV2(PurchasingPartyAccountOutcomeV1.Found, new(partyId, accountId, companyId, true, true, true, false, "1", "1")));
        public Task<ProductPurchaseEligibilityAnswerV1> EvaluateAsync(ProductPurchaseSelectionV1 selection, CancellationToken token = default)
            => Task.FromResult(new ProductPurchaseEligibilityAnswerV1(ProductPurchaseEligibilityOutcomeV1.Eligible, "1", []));
        public Task<PurchasingProductItemTypeSourceV1> GetAsync(Guid id, CancellationToken token = default)
            => Task.FromResult(new PurchasingProductItemTypeSourceV1(PurchasingProductItemTypeOutcomeV1.Found, companyId, id, PurchasingProductItemTypeKindV1.Steel, 7));
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid id, DateOnly date, CancellationToken token = default)
            => Task.FromResult(new UnitProfileV1(companyId, id, "KGM", "mass", true, new(id, 1), 3, true, date, null));
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid id, string operation, DateOnly date, CancellationToken token = default)
            => Task.FromResult(new MoneyProfileV1(companyId, id, "TRY", operation, new(id, 1), 2, true, date, null));
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly date, DateOnly? valueOn, CancellationToken token = default) => throw new NotSupportedException();
        public Task<SettingsReferenceListLookupV1> GetAsync(Guid company, SettingsReferenceListKindV1 kind, Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<SettingsReferenceListV1> ListAsync(Guid company, SettingsReferenceListKindV1 kind, CancellationToken token = default) => Task.FromResult(new SettingsReferenceListV1(company, kind, true, []));
        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<UnitIdentityV1>>([new(UnitId, "KGM", "mass", true)]);
        Task<UnitIdentityV1?> ISettingsUnitIdentityCatalogV1.ReadAsync(Guid id, CancellationToken token) => Task.FromResult<UnitIdentityV1?>(new(id, "KGM", "mass", true));
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(string operation, CancellationToken token = default)
            => Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "SENTETIK TL")]);
    }
}
