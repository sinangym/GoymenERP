using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Import_mapping_HTTP_uses_source_bound_reads_and_never_accepts_client_company_or_duplicate_source_members()
    {
        var owner = new SyntheticImportMappingHttpOwner();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { AddInvoiceImportHttpAuthority(services); services.RemoveAll<IPurchaseInvoiceImportMappingV1>(); services.AddScoped<IPurchaseInvoiceImportMappingV1>(_ => owner); }));
        using var client = CreateClient(host);
        var source = new PurchaseInvoiceImportPreviewRequestV1("1", Guid.NewGuid(), 2, new string('a', 64), new(PurchaseInvoiceFileFormatV1.Csv));
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{owner.Id:D}/mapping-references", "SENTETIK-read",
            JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
        var body = await response.Content.ReadFromJsonAsync<PurchaseInvoiceImportReferencesV1>();
        Assert.Equal(CompanyA, body!.CompanyId); Assert.Equal(source.DocumentId, body.DocumentId); Assert.Equal(source, owner.Source);
        Assert.Null(body.ServiceTypes); Assert.Empty(body.ExpenseTypes!); // unavailable is not the healthy-empty catalogue
        foreach (var request in new[] { "{\"expectedVersion\":\"1\",\"expectedVersion\":\"2\"}", "{\"companyId\":\"00000000-0000-0000-0000-000000000001\"}", "{\"documentVersion\":2.5}" })
        {
            using var bad = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{owner.Id:D}/mapping-references", "SENTETIK-invalid", request));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); AssertPrivateNoStore(bad);
        }
        Assert.Equal(1, owner.Reads);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task Import_mapping_HTTP_propagates_safe_owner_failure_without_disclosing_supplier_versions(int status)
    {
        var owner = new SyntheticImportMappingHttpOwner { Failure = status };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { AddInvoiceImportHttpAuthority(services); services.RemoveAll<IPurchaseInvoiceImportMappingV1>(); services.AddScoped<IPurchaseInvoiceImportMappingV1>(_ => owner); }));
        using var client = CreateClient(host);
        var source = new PurchaseInvoiceImportPreviewRequestV1("1", Guid.NewGuid(), 1, new string('a', 64), new(PurchaseInvoiceFileFormatV1.Csv));
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{owner.Id:D}/supplier-resolution", "SENTETIK-source",
            JsonSerializer.Serialize(new PurchaseInvoiceImportSupplierRequestV1(source, Guid.NewGuid(), Guid.NewGuid()), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response);
        Assert.Equal(1, owner.Reads);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("partyVersion", body); Assert.DoesNotContain("accountVersion", body);
    }

    [Fact]
    public async Task Import_record_HTTP_preserves_terminal_replay_errors_and_original_correlation()
    {
        var admission = new SyntheticImportRecordRefusal();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { AddInvoiceImportHttpAuthority(services); services.RemoveAll<IPurchaseInvoiceImportAdmissionV1>(); services.AddScoped<IPurchaseInvoiceImportAdmissionV1>(_ => admission); }));
        using var client = CreateClient(host);
        var request = new PurchaseInvoiceImportAdmissionRequestV1(new("1", Guid.NewGuid(), 1, new string('a', 64), new(PurchaseInvoiceFileFormatV1.Csv)),
            "0", Guid.NewGuid(), Guid.NewGuid(), "1", "1", Guid.NewGuid(), [], []);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{Guid.NewGuid():D}/records", "SENTETIK-record-key",
            JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); AssertPrivateNoStore(response);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        Assert.Equal("SENTETIK-record-key", admission.Metadata!.IdempotencyKey);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("lines[0].expectedProductVersion", body.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Invoice_import_permission_denial_never_calls_mapping_or_admission_owners()
    {
        var mapping = new SyntheticImportMappingHttpOwner();
        var admission = new SyntheticImportRecordRefusal();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            AddInvoiceImportHttpAuthority(services, allowed: false);
            services.RemoveAll<IPurchaseInvoiceImportMappingV1>(); services.AddScoped<IPurchaseInvoiceImportMappingV1>(_ => mapping);
            services.RemoveAll<IPurchaseInvoiceImportAdmissionV1>(); services.AddScoped<IPurchaseInvoiceImportAdmissionV1>(_ => admission);
        }));
        using var client = CreateClient(host);
        var source = new PurchaseInvoiceImportPreviewRequestV1("1", Guid.NewGuid(), 1, new string('a', 64), new(PurchaseInvoiceFileFormatV1.Csv));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var requests = new (string Route, object Body)[]
        {
            ("mapping-references", source),
            ("supplier-resolution", new PurchaseInvoiceImportSupplierRequestV1(source, Guid.NewGuid(), Guid.NewGuid())),
            ("records", new PurchaseInvoiceImportAdmissionRequestV1(source, "0", Guid.NewGuid(), Guid.NewGuid(), "1", "1", Guid.NewGuid(), [], [])),
        };
        foreach (var request in requests)
        {
            using var response = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{mapping.Id:D}/{request.Route}",
                "SENTETIK-denied", JsonSerializer.Serialize(request.Body, options)));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(0, mapping.Reads);
        Assert.Null(mapping.Source);
        Assert.Null(admission.Metadata);
    }

    private static void AddInvoiceImportHttpAuthority(IServiceCollection services, bool allowed = true)
    {
        services.RemoveAll<IErpAuthorizationService>();
        services.AddSingleton<IErpAuthorizationService>(new InvoiceImportHttpAuthority(allowed));
    }

    private sealed class InvoiceImportHttpAuthority(bool allowed) : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId).IsGranted);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) => Task.FromResult(new AuthorizationSnapshot("SENTETIK-invoice-import-http",
                new HashSet<Guid> { CompanyA }, permissions.ToDictionary(permission => permission,
                    permission => Scope(permission, companyId), StringComparer.Ordinal)));
        private PermissionScope Scope(string permission, Guid companyId) =>
            allowed && companyId == CompanyA && permission == PurchaseIncomingInvoicePermissionNamesV1.Import
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;
    }

    // Named synthetic wire owners only. Real service + file reader + SQL write sources are in InvoiceImportAdmission SQL acceptance.
    private sealed class SyntheticImportRecordRefusal : IPurchaseInvoiceImportAdmissionV1
    {
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public Task<PurchaseIncomingInvoiceOperationResultV1> RecordAsync(Guid id, PurchaseInvoiceImportAdmissionRequestV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default)
        { Metadata = metadata; throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict,
            [new("lines[0].expectedProductVersion", PurchasingProblemCodesV1.VersionConflict)], true, "SENTETIK-original"); }
    }
    private sealed class SyntheticImportMappingHttpOwner : IPurchaseInvoiceImportMappingV1
    {
        public Guid Id { get; } = Guid.NewGuid(); public int? Failure { get; init; }
        public PurchaseInvoiceImportPreviewRequestV1? Source { get; private set; } public int Reads { get; private set; }
        public Task<PurchaseInvoiceImportReferencesV1> ReferencesAsync(Guid id, PurchaseInvoiceImportPreviewRequestV1 source, CancellationToken token = default)
        { Source = source; Reads++; return Task.FromResult(new PurchaseInvoiceImportReferencesV1(CompanyA, id, source.DocumentId, source.DocumentVersion,
            source.DocumentSha256, new(2026, 11, 17), [], [], "120", null, null, [])); }
        public Task<PurchaseInvoiceImportSupplierV1> SupplierAsync(Guid id, PurchaseInvoiceImportSupplierRequestV1 request, CancellationToken token = default)
        {
            Reads++;
            throw new PurchasingServiceException(Failure ?? 503, Failure == 409 ? PurchasingProblemCodesV1.VersionConflict : "service_unavailable");
        }
        public Task<PurchaseInvoiceImportProductsV1> ProductsAsync(Guid id, PurchaseInvoiceImportProductsRequestV1 request, CancellationToken token = default) => throw new NotSupportedException();
    }
}
