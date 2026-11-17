using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class DocumentVaultSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Branding_actual_clean_upload_maker_checker_fixed_publication_and_generated_label_replay_keep_the_original_SQL_snapshot()
    {
        var label = Guid.Parse("58000000-0000-4000-8000-000000000040"); var inventory = new InventoryOwnerFixture(label);
        await using var run = await Run.StartAsync(inventory);
        const string makerSubject = "SENTETIK-branding-maker", publisherSubject = "SENTETIK-branding-publisher";
        var actor = await run.GrantAsync(makerSubject, CompanyA, DocumentBrandingPermissionsV1.Manage,
            DocumentBrandingPermissionsV1.InventoryPrepare, DocumentBrandingPermissionsV1.InventoryPublish,
            DocumentsPermissionManifestV1.FilesUpload, ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        inventory.ReadableActors.Add(actor);
        await run.GrantAsync(publisherSubject, CompanyA, DocumentBrandingPermissionsV1.InventoryPublish);
        using var maker = run.Client(makerSubject, CompanyA); using var publisher = run.Client(publisherSubject, CompanyA);
        var bytes = new TechnicalPdfGenerator().Generate(new(Guid.NewGuid(),
            new("SENTETIK", "1", "SENTETIK", new(2026, 11, 17), 1, "SENTETIK"), ["SENTETIK antet İĞÜŞÖÇ"])).Content;
        Guid assetId;
        using (var upload = await run.UploadAsync(maker, CompanyA, bytes, "SENTETIK_Antet.pdf", "s29-upload", DocumentBrandingContractV1.CompanyOwnerKind))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var uploaded = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("document");
            Assert.Equal("Clean", uploaded.GetProperty("scanState").GetString()); assetId = uploaded.GetProperty("id").GetGuid();
        }
        var asset = new DocumentBrandingAssetReferenceV1(assetId, 1);
        await BrandingOk(maker, HttpMethod.Put, "/current", "s29-branding-1", new SetCompanyDocumentBrandingV1("0", asset, null, "SENTETIK"));
        var prepare = new PrepareDocumentTemplateV1(DocumentTemplateModuleV1.Inventory, DocumentBrandingContractV1.InventoryTemplate, "0", "1", "SENTETIK");
        await BrandingOk(maker, HttpMethod.Post, "/templates/inventory/drafts", "s29-draft-1", prepare);
        var publish = new PublishDocumentTemplateV1(DocumentTemplateModuleV1.Inventory, DocumentBrandingContractV1.InventoryTemplate, "1", "0", "SENTETIK");
        using (var own = await maker.SendAsync(BrandingRequest(HttpMethod.Post, "/templates/inventory/publications", "s29-self", publish)))
        {
            Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
            Assert.Equal("document_template_maker_cannot_publish", (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var firstPublication = (await BrandingOk(publisher, HttpMethod.Post, "/templates/inventory/publications", "s29-publish-1", publish)).GetProperty("publication");
        var firstId = firstPublication.GetProperty("publicationId").GetGuid();
        using (var selected = await maker.GetAsync($"/api/v1/inventory/labels/{label:D}/pdf-template"))
        {
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
            Assert.Equal(firstId, (await selected.Content.ReadFromJsonAsync<InventoryLabelPdfTemplateV1>())!.PublicationId);
        }
        var request = new GenerateInventoryLabelPdfV1(T0, firstId, "1");
        byte[] originalPdf; string originalDocument;
        using (var original = await LabelPdf(maker, label, "s29-label-fixed", request))
        {
            Assert.Equal(HttpStatusCode.OK, original.StatusCode); originalPdf = await original.Content.ReadAsByteArrayAsync();
            originalDocument = Assert.Single(original.Headers.GetValues("X-Document-Id"));
        }
        await BrandingOk(maker, HttpMethod.Put, "/current", "s29-branding-2", new SetCompanyDocumentBrandingV1("1", null, asset, "SENTETIK second"));
        await BrandingOk(maker, HttpMethod.Post, "/templates/inventory/drafts", "s29-draft-2", prepare with { ExpectedDraftVersion = "1", ExpectedBrandingVersion = "2" });
        var second = (await BrandingOk(publisher, HttpMethod.Post, "/templates/inventory/publications", "s29-publish-2",
            publish with { ExpectedDraftVersion = "2", ExpectedPublicationVersion = "1" })).GetProperty("publication");
        using (var replay = await LabelPdf(maker, label, "s29-label-fixed", request))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(originalPdf, await replay.Content.ReadAsByteArrayAsync());
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(originalDocument, Assert.Single(replay.Headers.GetValues("X-Document-Id")));
        }
        using (var changed = await LabelPdf(maker, label, "s29-label-fixed", request with { PublicationId = second.GetProperty("publicationId").GetGuid(), PublicationVersion = "2" }))
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(firstId, await run.ScalarAsync<Guid>("SELECT TemplatePublicationId FROM documents.DocumentVersions WHERE CompanyId=@company AND DocumentId=@id AND VersionNo=1",
            ("@company", CompanyA), ("@id", Guid.Parse(originalDocument))));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentTemplatePublications WHERE CompanyId=@company", ("@company", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentAttempts WHERE CompanyId=@company AND DocumentId=@id", ("@company", CompanyA), ("@id", Guid.Parse(originalDocument))));
        using (var selfReplay = await maker.SendAsync(BrandingRequest(HttpMethod.Post, "/templates/inventory/publications", "s29-self", publish)))
        { Assert.Equal(HttpStatusCode.Conflict, selfReplay.StatusCode); Assert.Equal("true", Assert.Single(selfReplay.Headers.GetValues("Idempotency-Replayed"))); }
    }

    [SqlServerFact]
    public async Task Branding_Audit_owner_failure_after_append_rolls_back_revision_attempt_and_audit_then_same_key_succeeds_and_replays_once()
    {
        var fault = new BrandingAuditFault();
        await using var run = await Run.StartAsync(configure: services =>
        {
            var registration = services.Single(x => x.ServiceType == typeof(IAuditTransactionWriterV1)); services.Remove(registration);
            services.AddScoped<IAuditTransactionWriterV1>(provider => new BrandingFaultingAudit(
                registration.ImplementationFactory is { } factory ? (IAuditTransactionWriterV1)factory(provider)
                    : (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
        const string subject = "SENTETIK-branding-atomic"; await run.GrantAsync(subject, CompanyA, DocumentBrandingPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA); var command = new SetCompanyDocumentBrandingV1("0", null, null, "SENTETIK atomic");
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'DocumentBranding'");
        fault.Armed = true;
        using (var failed = await editor.SendAsync(BrandingRequest(HttpMethod.Put, "/current", "s29-atomic", command))) Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(1, fault.Calls);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.CompanyBrandingRevisions"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentBrandingAttempts"));
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'DocumentBranding'"));
        fault.Armed = false;
        await BrandingOk(editor, HttpMethod.Put, "/current", "s29-atomic", command);
        var replay = await BrandingOk(editor, HttpMethod.Put, "/current", "s29-atomic", command);
        Assert.True(replay.GetProperty("isReplay").GetBoolean());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.CompanyBrandingRevisions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM documents.DocumentBrandingAttempts"));
        Assert.Equal(auditBefore + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'DocumentBranding'"));
        using var foreign = run.Client(subject, CompanyB);
        using var denied = await foreign.GetAsync(DocumentBrandingContractV1.BasePath + "/current");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
    private static HttpRequestMessage BrandingRequest(HttpMethod method, string suffix, string key, object value)
    { var request = new HttpRequestMessage(method, DocumentBrandingContractV1.BasePath + suffix) { Content = JsonContent.Create(value) }; request.Headers.Add("Idempotency-Key", key); return request; }
    private static async Task<JsonElement> BrandingOk(HttpClient client, HttpMethod method, string suffix, string key, object value)
    { using var request = BrandingRequest(method, suffix, key, value); using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task<HttpResponseMessage> LabelPdf(HttpClient client, Guid label, string key, GenerateInventoryLabelPdfV1 value)
    { using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/inventory/labels/{label:D}/pdf") { Content = JsonContent.Create(value) }; request.Headers.Add("Idempotency-Key", key); return await client.SendAsync(request); }
    private sealed class BrandingAuditFault { public bool Armed; public int Calls; }
    private sealed class BrandingFaultingAudit(IAuditTransactionWriterV1 inner, BrandingAuditFault fault) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { await inner.WritePendingAsync(transaction, cancellationToken); if (fault.Armed) { fault.Calls++; throw new InvalidOperationException("SENTETIK audit failure after append"); } }
        public void DiscardPending() => inner.DiscardPending();
    }
}
