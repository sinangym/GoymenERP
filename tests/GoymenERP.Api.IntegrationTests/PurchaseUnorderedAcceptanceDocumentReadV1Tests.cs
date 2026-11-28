using System.Reflection;
using System.Security.Cryptography;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Controlled public-owner protocol, not HTTP, real Documents storage or SQL acceptance.</summary>
public sealed class PurchaseUnorderedAcceptanceDocumentReadV1Tests
{
    private static readonly Guid Company = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid Document = Guid.Parse("b1000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Real_document_interface_is_called_for_exact_immutable_version_and_only_identity_hash_facts_are_returned()
    {
        var rig = new Rig();
        var result = await rig.Read();
        Assert.Equal(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found, result.Outcome);
        Assert.Equal(new PurchaseUnorderedAcceptanceDocumentFactsV1(Company, Document, 2, rig.Evidence.Sha256), result.Facts);
        Assert.Equal((Document, 2), Assert.Single(rig.Documents.Reads));
        Assert.Equal(2, rig.ConsumerCalls); Assert.Equal(2, rig.Authority.Calls);
        Assert.All(rig.Authority.Permissions, permission => Assert.Equal(PurchasingPermissionManifestV1.GoodsReceiptsRecord, permission));
        // File hash is a witness only. The adapter has no semantic row, invoice date or prior acceptance dependency.
        Assert.Equal(new[] { "CompanyId", "DocumentId", "DocumentVersion", "Sha256" },
            typeof(PurchaseUnorderedAcceptanceDocumentFactsV1).GetProperties().Select(x => x.Name).Order().ToArray());
    }

    [Theory]
    [InlineData("document")] [InlineData("content-hash")] [InlineData("requested-hash")] [InlineData("bytes")]
    [InlineData("empty-bytes")] [InlineData("null-content")] [InlineData("version-overflow")]
    public async Task Malformed_owner_or_selection_never_supplies_occurrence_facts(string defect)
    {
        var rig = new Rig();
        switch (defect)
        {
            case "document": rig.Documents.Content = rig.Documents.Content with { DocumentId = Guid.NewGuid() }; break;
            case "content-hash": rig.Documents.Content = rig.Documents.Content with { Sha256 = new string('f', 64) }; break;
            case "requested-hash": rig.Evidence = rig.Evidence with { Sha256 = new string('f', 64) }; break;
            case "bytes": rig.Documents.Content = rig.Documents.Content with { Content = [99, 100] }; break;
            case "empty-bytes": rig.Documents.Content = rig.Documents.Content with { Content = [] }; break;
            case "null-content": rig.Documents.Read = () => Task.FromResult<DocumentContentV1>(null!); break;
            default: rig.Evidence = rig.Evidence with { DocumentVersion = (long)int.MaxValue + 1 }; break;
        }
        var result = await rig.Read();
        Assert.Equal(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable, result.Outcome); Assert.Null(result.Facts);
        Assert.Equal(defect == "version-overflow" ? 0 : 1, rig.Documents.Reads.Count);
    }

    [Theory]
    [InlineData(401, PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied)]
    [InlineData(403, PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied)]
    [InlineData(404, PurchaseUnorderedAcceptanceEvidenceOutcomeV1.NotFound)]
    [InlineData(422, PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable)]
    [InlineData(503, PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable)]
    public async Task Owner_permission_scan_or_storage_refusal_is_not_replaced_by_request_echo(int status, PurchaseUnorderedAcceptanceEvidenceOutcomeV1 expected)
    {
        var rig = new Rig(); rig.Documents.Read = () => throw new DocumentServiceException(status, "SENTETIK");
        var result = await rig.Read();
        Assert.Equal(expected, result.Outcome); Assert.Null(result.Facts); Assert.Equal(2, rig.ConsumerCalls);
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("permission")]
    public async Task Late_owner_completion_cannot_release_file_facts_under_changed_authority(string change)
    {
        var rig = new Rig(); rig.Documents.AfterRead = () =>
        {
            if (change == "company") rig.Company.Value = Guid.NewGuid();
            else if (change == "actor") rig.User.Actor = "SENTETIK-other";
            else rig.Authority.Granted = false;
        };
        var result = await rig.Read();
        Assert.Equal(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied, result.Outcome); Assert.Null(result.Facts); Assert.Single(rig.Documents.Reads);
    }

    [Fact]
    public async Task Final_completed_consumer_callback_permission_loss_is_remeasured_and_cancellation_is_honored()
    {
        var rig = new Rig();
        rig.Consumer = call => { if (call == 2) rig.Authority.Granted = false; };
        var denied = await rig.Read(); Assert.Equal(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied, denied.Outcome); Assert.Null(denied.Facts);
        var cancelled = new Rig(); using var cancellation = new CancellationTokenSource();
        cancelled.Consumer = call => { if (call == 2) cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Read(cancellation.Token));
        Assert.Single(cancelled.Documents.Reads);
        var initial = new Rig(); using var stopped = new CancellationTokenSource(); stopped.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.Read(stopped.Token));
        Assert.Equal(0, initial.ConsumerCalls); Assert.Empty(initial.Documents.Reads);
    }

    [Fact]
    public async Task Unresolved_company_and_initial_denial_do_not_call_the_document_owner()
    {
        var unresolved = new Rig(); unresolved.Company.Value = Guid.Empty;
        Assert.Equal(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied, (await unresolved.Read()).Outcome);
        Assert.Empty(unresolved.Documents.Reads); Assert.Equal(0, unresolved.Company.UnresolvedReads);
        var denied = new Rig(); denied.Authority.Granted = false;
        Assert.Equal(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied, (await denied.Read()).Outcome); Assert.Empty(denied.Documents.Reads);
    }

    private sealed class Rig
    {
        public readonly MutableCompany Company = new(); public readonly User User = new(); public readonly Authority Authority = new();
        public readonly DocumentProxy Documents; public PurchaseAcceptanceDocumentEvidenceV1 Evidence { get; set; }
        public int ConsumerCalls { get; private set; } public Action<int>? Consumer { get; set; }
        private readonly PurchaseUnorderedAcceptanceDocumentReadV1 reader;
        public Rig()
        {
            var owner = DispatchProxy.Create<IDocumentService, DocumentProxy>(); Documents = (DocumentProxy)owner;
            var bytes = new byte[] { 1, 2, 3, 4 }; var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Documents.Content = new(Document, "SENTETIK.txt", "text/plain", bytes, hash);
            Evidence = new(Document, 2, hash, "declared-physical-line");
            reader = new(owner, Company, User, Authority);
        }
        public Task<PurchaseUnorderedAcceptanceDocumentAnswerV1> Read(CancellationToken token = default) =>
            reader.ReadAsync(PurchaseUnorderedAcceptanceDocumentReadV1Tests.Company, Evidence,
                _ => { ConsumerCalls++; Consumer?.Invoke(ConsumerCalls); return Task.CompletedTask; }, token);
    }
    public class DocumentProxy : DispatchProxy
    {
        public DocumentContentV1 Content { get; set; } = null!;
        public Func<Task<DocumentContentV1>>? Read { get; set; } public Action? AfterRead { get; set; }
        public List<(Guid Id, int Version)> Reads { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IDocumentService.GetVersionContentAsync)) throw new InvalidOperationException("No document mutation, link or general list.");
            Reads.Add(((Guid)args![0]!, (int)args[1]!)); AfterRead?.Invoke(); return Read?.Invoke() ?? Task.FromResult(Content);
        }
    }
    private sealed class MutableCompany : ICompanyContext
    {
        public Guid Value { get; set; } = Company; public int UnresolvedReads { get; private set; }
        public bool IsResolved => Value != Guid.Empty;
        public Guid CompanyId { get { if (!IsResolved) { UnresolvedReads++; throw new InvalidOperationException("unresolved"); } return Value; } }
        public void Set(Guid value) => Value = value;
    }
    private sealed class User : ICurrentUser
    {
        public string Actor { get; set; } = "SENTETIK-user"; public string UserId => Actor; public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true; public int Calls { get; private set; } public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++; Permissions.Add(permission);
            return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
