using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Imports;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceImportTests
{
    private static readonly Guid CompanyId = Guid.Parse("798ef301-8409-4468-9284-22f36435fc0c");

    [Fact]
    public async Task Reservation_uses_current_import_authority_and_precommit_fence_without_reading_a_document()
    {
        var world = new World();
        var answer = await world.Service.ReserveAsync(new("synthetic-key", "synthetic-correlation", "synthetic-cause", DateTimeOffset.UtcNow));
        Assert.Equal(CompanyId, answer.Reservation.CompanyId);
        Assert.Equal(PurchaseIncomingInvoicePermissionNamesV1.Import, world.Store.Envelope!.PermissionCode);
        Assert.Equal("synthetic-cause", world.Store.Envelope.CausationId);
        Assert.Equal(0, world.Documents.Calls);
        var revoked = new World(); revoked.Store.BeforeFence = () => revoked.Authority.Denied = true;
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => revoked.Service.ReserveAsync(new("key", "correlation", null, DateTimeOffset.UtcNow)));
        Assert.Equal(403, error.Status); Assert.False(revoked.Store.Saved);
    }

    [Fact]
    public async Task Preview_reads_exact_immutable_source_and_returns_parse_issues_without_creating_an_invoice()
    {
        var world = new World();
        var answer = await world.Service.PreviewAsync(world.Store.Id, world.Request());
        Assert.Equal(world.Store.Id, answer.ImportId);
        Assert.Equal(CompanyId, answer.CompanyId);
        Assert.Equal(world.Documents.Content.Sha256, answer.DocumentSha256);
        Assert.Null(answer.Invoice); Assert.NotEmpty(answer.Issues);
        Assert.Equal(1, world.Documents.Calls); Assert.False(world.Store.Saved);
    }

    [Theory]
    [InlineData("version", 409)]
    [InlineData("hash", 409)]
    [InlineData("format", 400)]
    [InlineData("provider-company", 503)]
    [InlineData("provider-version", 503)]
    [InlineData("corrupt-bytes", 503)]
    public async Task Preview_refuses_stale_selection_and_untrusted_owner_answers(string defect, int status)
    {
        var world = new World(); var request = world.Request();
        if (defect == "version") request = request with { ExpectedVersion = "2" };
        if (defect == "hash") request = request with { DocumentSha256 = new string('a', 64) };
        if (defect == "format") request = request with { Options = new(PurchaseInvoiceFileFormatV1.Csv) };
        if (defect == "provider-company") world.Documents.Content = world.Documents.Content with { CompanyId = Guid.NewGuid() };
        if (defect == "provider-version") world.Documents.Content = world.Documents.Content with { Version = 2 };
        if (defect == "corrupt-bytes") world.Documents.Content = world.Documents.Content with { Content = Encoding.UTF8.GetBytes("changed") };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.PreviewAsync(world.Store.Id, request));
        Assert.Equal(status, error.Status); Assert.False(world.Store.Saved);
        if (defect == "version") Assert.Equal(0, world.Documents.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_read_success_or_failure_cannot_disclose_after_permission_revocation(bool providerFails)
    {
        var world = new World();
        world.Documents.BeforeRead = () => { world.Authority.Denied = true; if (providerFails) throw new IOException("synthetic-unavailable"); };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.PreviewAsync(world.Store.Id, world.Request()));
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Import_and_price_authority_are_both_required_before_reservation_or_file_reads()
    {
        foreach (var denied in new[] { PurchaseIncomingInvoicePermissionNamesV1.Import, PurchasingPermissionManifestV1.OrdersPriceRead })
        {
            var world = new World(); world.Authority.DeniedPermission = denied;
            var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service.PreviewAsync(world.Store.Id, world.Request()));
            Assert.Equal(403, error.Status); Assert.Equal(0, world.Documents.Calls); Assert.Equal(0, world.Store.Reads);
        }
    }

    private sealed class World
    {
        public Company Company { get; } = new();
        public Authority Authority { get; } = new();
        public Store Store { get; } = new();
        public Documents Documents { get; } = new();
        public PurchaseInvoiceImport Service => new(Company, new User(), Authority, new Correlation(), Store, Documents, new PurchaseInvoiceFileReader());
        public PurchaseInvoiceImportPreviewRequestV1 Request() => new("1", Documents.Content.DocumentId, 1, Documents.Content.Sha256, new(PurchaseInvoiceFileFormatV1.UblXml));
    }
    private sealed class Company : ICompanyContext { public Guid CompanyId => PurchaseInvoiceImportTests.CompanyId; public bool IsResolved => true; public void Set(Guid value) { } }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "synthetic-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyId };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Denied { get; set; } public string? DeniedPermission { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(!Denied && DeniedPermission != permission, true, new HashSet<string> { "synthetic-role" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "synthetic-correlation"; public string? CausationId => null;
        public string SourceChannel => "Test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
    private sealed class Store : IPurchaseInvoiceImportReservationStoreV1
    {
        public Guid Id { get; } = Guid.NewGuid(); public int Reads { get; private set; } public bool Saved { get; private set; }
        public PurchasingWriteEnvelope? Envelope { get; private set; } public Action? BeforeFence { get; set; }
        private PurchaseInvoiceImportReservationV1 Reservation => new(Id, CompanyId, "1", "synthetic-actor", DateTimeOffset.UtcNow);
        public Task<PurchaseInvoiceImportReservationV1?> FindAsync(Guid id, CancellationToken token = default)
        { Reads++; return Task.FromResult<PurchaseInvoiceImportReservationV1?>(id == Id ? Reservation : null); }
        public async Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Envelope = envelope; BeforeFence?.Invoke(); await fence(token); Saved = true; return new(Reservation, false, envelope.CorrelationId); }
    }
    private sealed class Documents : IPurchaseInvoiceImportDocumentReadV1
    {
        public Documents()
        {
            var bytes = Encoding.UTF8.GetBytes("<invalid/>");
            Content = new(CompanyId, Guid.NewGuid(), 1, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), PurchaseInvoiceFileFormatV1.UblXml, bytes);
        }
        public PurchaseInvoiceImportDocumentV1 Content { get; set; } public int Calls { get; private set; } public Action? BeforeRead { get; set; }
        public Task<PurchaseInvoiceImportDocumentV1> ReadAsync(Guid importId, Guid documentId, int version, CancellationToken token = default)
        { Calls++; BeforeRead?.Invoke(); return Task.FromResult(Content); }
    }
}
