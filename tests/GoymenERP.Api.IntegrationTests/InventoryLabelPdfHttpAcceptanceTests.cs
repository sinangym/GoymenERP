using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryLabelPdfHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Label = Guid.Parse("8a170000-0000-4000-8000-000000000001");
    private static readonly Guid Document = Guid.Parse("8a170000-0000-4000-8000-000000000002");

    [Fact]
    public async Task The_attachment_is_returned_only_after_the_vault_accepts_the_same_company_label_and_producer_permission()
    {
        var vault = new PdfVault(GeneratedDocumentStoreOutcomeV1.Stored);
        var labels = new LabelDetails("SENTETIK-001");
        using var host = Host(labels, vault);
        using var client = Client(host);
        using var request = Request();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Document.ToString("D"), Assert.Single(response.Headers.GetValues("X-Document-Id")));
        Assert.Equal([Label, Label], labels.Requests);
        var sent = Assert.Single(vault.Requests);
        Assert.Equal((TestCompanyIds.CompanyA, Label, "inventory.labels.read", InventoryLabelPdfApiV1.Command),
            (sent.CompanyId, sent.OwnerId, sent.ProducerPermission, sent.ProducerCommand));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString((await response.Content.ReadAsByteArrayAsync()).AsSpan(0, 5)));
    }

    [Theory]
    [InlineData(GeneratedDocumentStoreOutcomeV1.Forbidden, 403)]
    [InlineData(GeneratedDocumentStoreOutcomeV1.OwnerNotFound, 404)]
    [InlineData(GeneratedDocumentStoreOutcomeV1.Unavailable, 503)]
    public async Task A_vault_refusal_returns_no_pdf_and_preserves_the_permission_or_unavailable_boundary(
        GeneratedDocumentStoreOutcomeV1 outcome, int status)
    {
        var labels = new LabelDetails("SENTETIK-001");
        var vault = new PdfVault(outcome);
        using var host = Host(labels, vault);
        using var client = Client(host);
        using var request = Request();
        using var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([Label, Label], labels.Requests);
        var sent = Assert.Single(vault.Requests);
        Assert.Equal((TestCompanyIds.CompanyA, Label, ErpPermissions.InventoryLabelsRead),
            (sent.CompanyId, sent.OwnerId, sent.ProducerPermission));
    }

    [Fact]
    public async Task An_unsupported_barcode_never_reaches_the_vault_and_is_a_visible_422()
    {
        var vault = new PdfVault(GeneratedDocumentStoreOutcomeV1.Stored);
        var labels = new LabelDetails("SENTETIK-İ");
        using var host = Host(labels, vault);
        using var client = Client(host);
        using var request = Request();
        using var response = await client.SendAsync(request);
        Assert.Equal(422, (int)response.StatusCode);
        Assert.Equal(Label, Assert.Single(labels.Requests));
        Assert.Empty(vault.Requests);
    }

    [Theory]
    [InlineData(ErpPermissions.InventoryLabelsRead)]
    [InlineData(ErpPermissions.InventoryBalancesRead)]
    public async Task A_missing_endpoint_read_permission_reaches_neither_the_label_nor_the_vault(string deniedPermission)
    {
        var labels = new LabelDetails("SENTETIK-001");
        var vault = new PdfVault(GeneratedDocumentStoreOutcomeV1.Stored);
        using var host = Host(labels, vault, new HttpAuthority(deniedPermission));
        using var client = Client(host);
        using var request = Request();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(labels.Requests);
        Assert.Empty(vault.Requests);
    }

    private WebApplicationFactory<Program> Host(IInventoryLabelDetailsV1 labels, IGeneratedDocumentStorePortV1 vault,
        IErpAuthorizationService? authority = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Synthetic endpoint authority only; these registered permissions are not seeded grants.
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority ?? new HttpAuthority());
            services.RemoveAll<IDocumentPublishedTemplateReadPortV1>();
            services.AddSingleton<IDocumentPublishedTemplateReadPortV1, TechnicalDocumentTemplateSourceForTests>();
            services.RemoveAll<IInventoryLabelDetailsV1>(); services.AddSingleton(labels);
            services.RemoveAll<IGeneratedDocumentStorePortV1>(); services.AddSingleton(vault);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static HttpRequestMessage Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/labels/{Label:D}/pdf")
        { Content = JsonContent.Create(new GenerateInventoryLabelPdfV1(DateTimeOffset.Parse("2026-11-16T09:00:00Z"))) };
        request.Headers.Add("Idempotency-Key", "SENTETIK-label-pdf");
        return request;
    }

    private sealed class LabelDetails(string code) : IInventoryLabelDetailsV1
    {
        public List<Guid> Requests { get; } = [];
        public Task<InventoryLabelDetailV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default)
        {
            Requests.Add(labelId);
            var lot = Guid.Parse("8a170000-0000-4000-8000-000000000003");
            return Task.FromResult(new InventoryLabelDetailV1("1", new(Label, TestCompanyIds.CompanyA, code,
                Guid.Parse("8a170000-0000-4000-8000-000000000004"), lot, "SENTETIK-İĞÜŞÖÇ", null, null,
                "Complete", [], [], [], new(lot, null, null)), new(Label, "0", "0", "0", []),
                new(Label, code, [], false), new(Label, [], false)));
        }
    }

    private sealed class PdfVault(GeneratedDocumentStoreOutcomeV1 outcome) : IGeneratedDocumentStorePortV1
    {
        public List<GeneratedDocumentStoreRequestV1> Requests { get; } = [];
        public Task<GeneratedDocumentStoreResultV1> StoreAsync(GeneratedDocumentStoreRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(outcome == GeneratedDocumentStoreOutcomeV1.Stored
                ? new(outcome, Document, 1, "SENTETIK-hash", "SystemGenerated", true, null, null)
                : GeneratedDocumentStoreResultV1.Refused(outcome, null));
        }
    }

    private sealed class HttpAuthority(string? deniedPermission = null) : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("SENTETIK-label-pdf", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId), StringComparer.Ordinal)));

        private PermissionScope Scope(string permission, Guid companyId) =>
            companyId == TestCompanyIds.CompanyA && permission != deniedPermission
                && permission is ErpPermissions.InventoryLabelsRead or ErpPermissions.InventoryBalancesRead
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;
    }
}
