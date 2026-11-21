using System.Security.Cryptography;
using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Synthetic boundary evidence. The real parser/render and SQL owner lifecycle have separate facts.</summary>
public sealed class DocumentBrandingPdfRuntimeTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA, Pub = Guid.Parse("58000000-0000-4000-8000-000000000010");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-17T08:00:00Z");
    [Fact]
    public void The_producer_frame_refuses_a_new_actor_or_company_even_if_that_new_context_itself_can_read_a_record()
    {
        var company = new MutableCompanyContext(Company); var user = new User();
        var frame = new DocumentPdfProducerFrame(company, user); frame.Require();
        user.UserId = "SENTETIK-different-authorized-actor";
        Assert.Equal(403, Assert.Throws<DocumentBrandingServiceException>(frame.Require).Status);
        user.UserId = "SENTETIK-maker"; company.Set(TestCompanyIds.CompanyB);
        Assert.True(user.CanAccessCompany(company.CompanyId));
        Assert.Equal(403, Assert.Throws<DocumentBrandingServiceException>(frame.Require).Status);
    }
    [Fact]
    public async Task A_genuine_absent_publication_is_the_only_current_read_that_can_select_the_technical_layout()
    {
        var owner = new Owner(Absent()); var reader = Reader(owner);
        var answer = await reader.ReadAsync(Company, DocumentTemplateModuleV1.Inventory, "inventory.labels.read");
        Assert.Null(answer.Render); Assert.Null(answer.Publication);
        owner.Answer = Absent() with { Outcome = DocumentPublishedTemplateOutcomeV1.Unavailable, UnavailableReason = "SENTETIK" };
        Assert.Equal(503, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => reader.ReadAsync(Company, DocumentTemplateModuleV1.Inventory, "inventory.labels.read"))).Status);
    }
    [Fact]
    public async Task A_frozen_selection_reads_its_original_id_and_version_instead_of_reselecting_the_latest_publication()
    {
        var owner = new Owner(Available()); var reader = Reader(owner);
        var answer = await reader.ReadSelectedAsync(Company, DocumentTemplateModuleV1.Inventory, "inventory.labels.read", Pub, "7");
        Assert.Equal((Pub, "7"), owner.Selection); Assert.Equal(0, owner.LatestCalls);
        Assert.Equal(Pub, answer.Publication!.PublicationId);
        owner.Answer = owner.Answer with { Publication = owner.Answer.Publication! with { Version = "8", TemplateVersion = "pub.8" } };
        Assert.Equal(503, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => reader.ReadSelectedAsync(Company,
            DocumentTemplateModuleV1.Inventory, "inventory.labels.read", Pub, "7"))).Status);
        owner.Answer = Absent();
        Assert.Equal(503, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => reader.ReadSelectedAsync(Company,
            DocumentTemplateModuleV1.Inventory, "inventory.labels.read", Pub, "7"))).Status);
    }
    [Fact]
    public async Task Company_or_permission_loss_during_the_owner_call_never_releases_asset_content()
    {
        var company = new MutableCompanyContext(Company); var authority = new Authority();
        var owner = new Owner(Available()) { DuringRead = () => company.Set(TestCompanyIds.CompanyB) };
        var reader = Reader(owner, company, authority);
        Assert.Equal(403, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => reader.ReadAsync(Company,
            DocumentTemplateModuleV1.Inventory, "inventory.labels.read"))).Status);
        Assert.Equal(TestCompanyIds.CompanyB, company.CompanyId);
        Assert.Equal(1, owner.LatestCalls);
        company.Set(Company); owner.DuringRead = () => authority.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => reader.ReadAsync(Company,
            DocumentTemplateModuleV1.Inventory, "inventory.labels.read"))).Status);
        Assert.Equal(2, owner.LatestCalls);
    }
    [Fact]
    public async Task Foreign_asset_hash_change_self_publication_and_unknown_outcomes_are_not_printable_success()
    {
        var good = Available();
        var corrupt = new[] { good with { CompanyId = TestCompanyIds.CompanyB },
            good with { Logo = good.Logo! with { Content = "SENTETIK-changed"u8.ToArray() } },
            good with { Publication = good.Publication! with { PublishedBy = good.Publication.PreparedBy } },
            good with { Outcome = (DocumentPublishedTemplateOutcomeV1)99 },
            good with { Publication = good.Publication! with { Logo = good.Publication.Logo! with { CompanyId = TestCompanyIds.CompanyB } } } };
        foreach (var value in corrupt)
            Assert.Equal(503, (await Assert.ThrowsAsync<DocumentBrandingServiceException>(() => Reader(new Owner(value))
                .ReadAsync(Company, DocumentTemplateModuleV1.Inventory, "inventory.labels.read"))).Status);
    }
    private static PublishedDocumentBrandingReader Reader(Owner owner, ICompanyContext? company = null, Authority? authority = null)
    { company ??= new CompanyContext(); if (!company.IsResolved) company.Set(Company); return new(owner, company, new User(), authority ?? new()); }
    private static DocumentPublishedTemplateReadV1 Absent() => new(DocumentPublishedTemplateOutcomeV1.NoSource, Company,
        DocumentTemplateModuleV1.Inventory, DocumentBrandingContractV1.InventoryTemplate, null, null, null);
    private static DocumentPublishedTemplateReadV1 Available()
    {
        var bytes = "%PDF-SENTETIK-fixed-source"u8.ToArray();
        var proof = new DocumentBrandingAssetProofV1(Company, Guid.NewGuid(), 2, "application/pdf", bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "SENTETIK-scanner", "SENTETIK-maker", At);
        var publication = new DocumentTemplatePublicationV1(Pub, Company, DocumentTemplateModuleV1.Inventory,
            DocumentBrandingContractV1.InventoryTemplate, "7", "pub.7", "6", "5", proof, null, "SENTETIK-maker", "SENTETIK-publisher", At, "SENTETIK");
        return new(DocumentPublishedTemplateOutcomeV1.Available, Company, DocumentTemplateModuleV1.Inventory, publication.TemplateCode,
            publication, new(proof, bytes), null);
    }
    private sealed class Owner(DocumentPublishedTemplateReadV1 answer) : IDocumentPublishedTemplateReadPortV1
    {
        public DocumentPublishedTemplateReadV1 Answer = answer; public int LatestCalls; public (Guid, string)? Selection; public Action? DuringRead;
        public Task<DocumentPublishedTemplateReadV1> ReadAsync(Guid companyId, DocumentTemplateModuleV1 module, string code, CancellationToken cancellationToken = default)
        { LatestCalls++; DuringRead?.Invoke(); return Task.FromResult(Answer); }
        public Task<DocumentPublishedTemplateReadV1> ReadPublicationAsync(Guid companyId, DocumentTemplateModuleV1 module, string code,
            Guid id, string version, CancellationToken cancellationToken = default) { Selection = (id, version); DuringRead?.Invoke(); return Task.FromResult(Answer); }
    }
    // Adversarial consumer double: production CompanyContext intentionally refuses this drift.
    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class User : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId { get; set; } = "SENTETIK-maker"; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, TestCompanyIds.CompanyB }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService
    { public bool Granted = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Granted ? new(true, false, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied); }
}
