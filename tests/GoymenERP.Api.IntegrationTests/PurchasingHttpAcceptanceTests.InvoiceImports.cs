using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Fact]
    public async Task Invoice_import_list_reopens_company_source_reservations_with_private_no_store_and_strict_query()
    {
        var owner = new InvoiceImportHttpOwner();
        using var host = InvoiceImportHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync("/api/v1/purchasing/invoice-imports?pageIndex=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertPrivateNoStore(response);
        var page = await response.Content.ReadFromJsonAsync<PurchaseInvoiceImportReservationPageV1>();
        Assert.NotNull(page); Assert.Equal(CompanyA, page.CompanyId); Assert.Equal(owner.Id, Assert.Single(page.Items).Id);
        foreach (var query in new[] { "?extra=1", "?pageIndex=0&pageIndex=1", "?pageIndex=invalid" })
        {
            using var invalid = await client.GetAsync("/api/v1/purchasing/invoice-imports" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task Invoice_import_reservation_preserves_idempotency_metadata_and_preview_is_a_source_read_only()
    {
        var owner = new InvoiceImportHttpOwner();
        using var host = InvoiceImportHost(owner); using var client = CreateClient(host);
        using var reserved = await client.SendAsync(Post("/api/v1/purchasing/invoice-imports/reservations", "SENTETIK-import-key", "{}"));
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        Assert.Equal("SENTETIK-import-key", owner.Metadata!.IdempotencyKey);
        Assert.Equal("true", Assert.Single(reserved.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(reserved.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        AssertPrivateNoStore(reserved);
        var document = Guid.NewGuid();
        var request = JsonSerializer.Serialize(new { expectedVersion = "1", documentId = document, documentVersion = 3,
            documentSha256 = new string('a', 64), options = new { format = 2, columns = new { Number = "FaturaNo" }, delimiter = ";" } });
        using var preview = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{owner.Id:D}/preview", "SENTETIK-read", request));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode); AssertPrivateNoStore(preview);
        Assert.Equal(document, owner.Preview!.DocumentId); Assert.Equal(3, owner.Preview.DocumentVersion);
        Assert.Equal(PurchaseInvoiceFileFormatV1.Csv, owner.Preview.Options.Format);
        Assert.Equal("FaturaNo", owner.Preview.Options.Columns!["Number"]);
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(owner.Id, body.GetProperty("importId").GetGuid());
        Assert.Equal("column_missing", body.GetProperty("issues")[0].GetProperty("code").GetString());
        Assert.Equal(1, owner.Reservations);
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"1\",\"expectedVersion\":\"2\"}")]
    [InlineData("{\"expectedVersion\":\"1\",\"companyId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"expectedVersion\":1}")]
    [InlineData("{\"options\":{\"format\":1,\"isOfficial\":true}}")]
    public async Task Invoice_import_preview_rejects_duplicate_or_client_authoritative_members_before_owner(string request)
    {
        var owner = new InvoiceImportHttpOwner();
        using var host = InvoiceImportHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"/api/v1/purchasing/invoice-imports/{owner.Id:D}/preview", "SENTETIK-key", request));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(owner.Preview); Assert.Equal(0, owner.Reservations);
        AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task Invoice_import_read_failure_does_not_disclose_source_metadata(int status)
    {
        var owner = new InvoiceImportHttpOwner { FailureStatus = status };
        using var host = InvoiceImportHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync($"/api/v1/purchasing/invoice-imports/{owner.Id:D}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode); AssertPrivateNoStore(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("reservedBy", out _)); Assert.False(body.TryGetProperty("detail", out _));
    }

    private WebApplicationFactory<Program> InvoiceImportHost(InvoiceImportHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        { services.RemoveAll<IPurchaseInvoiceImportV1>(); services.AddScoped<IPurchaseInvoiceImportV1>(_ => owner); }));

    private sealed class InvoiceImportHttpOwner : IPurchaseInvoiceImportV1
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int? FailureStatus { get; init; }
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public PurchaseInvoiceImportPreviewRequestV1? Preview { get; private set; }
        public int Reservations { get; private set; }
        private PurchaseInvoiceImportReservationV1 Reservation => new(Id, CompanyA, "1", "SENTETIK", DateTimeOffset.Parse("2026-11-17T10:00:00Z"));
        public Task<PurchaseInvoiceImportReservationPageV1> ListAsync(int pageIndex, CancellationToken token = default) => FailureStatus is { } status
            ? throw new PurchasingServiceException(status, "SENTETIK-internal")
            : Task.FromResult(new PurchaseInvoiceImportReservationPageV1(CompanyA, pageIndex, false, [Reservation]));
        public Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingCommandMetadata metadata, CancellationToken token = default)
        { Metadata = metadata; Reservations++; return Task.FromResult(new PurchaseInvoiceImportReservationResultV1(Reservation, true, "SENTETIK-original")); }
        public Task<PurchaseInvoiceImportReservationV1> ReadAsync(Guid importId, CancellationToken token = default) => FailureStatus is { } status
            ? throw new PurchasingServiceException(status, "SENTETIK-internal") : Task.FromResult(Reservation);
        public Task<PurchaseInvoiceImportPreviewV1> PreviewAsync(Guid importId, PurchaseInvoiceImportPreviewRequestV1 request, CancellationToken token = default)
        { Preview = request; return Task.FromResult(new PurchaseInvoiceImportPreviewV1(Id, CompanyA, "1", request.DocumentId,
            request.DocumentVersion, request.DocumentSha256, null, [new("Number", "column_missing")])); }
    }
}
