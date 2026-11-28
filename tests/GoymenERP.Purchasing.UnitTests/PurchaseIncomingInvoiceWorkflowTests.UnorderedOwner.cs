using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceWorkflowTests
{
    [Fact]
    public async Task An_unordered_occurrence_without_actual_prerequisite_owner_is_unavailable_and_saves_nothing()
    {
        var world = new World(); var store = new UnorderedMemoryStore();
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => UnorderedService(world, store)
            .RecordAsync(UnorderedRecordRequest(), Metadata("unordered-missing")));
        Assert.Equal(503, failure.Status); Assert.Null(store.Saved); Assert.Empty(world.Finance.Offers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_goods_and_service_occurrence_preserve_typed_identity_unit_and_evidence_without_a_PO(bool service)
    {
        var world = new World(); var store = new UnorderedMemoryStore(); var prerequisite = new UnorderedPrerequisites();
        var request = UnorderedRecordRequest();
        if (service) request = request with { Subject = new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "service-7") };
        var result = await UnorderedService(world, store, prerequisite).RecordAsync(request, Metadata("unordered-record"));
        Assert.False(result.IsReplay); Assert.Equal(1, prerequisite.Calls); Assert.Equal(Company, prerequisite.CompanyId);
        Assert.Equal(request with { ControlEvidence = prerequisite.Request!.ControlEvidence }, prerequisite.Request);
        Assert.Equal(request.ControlEvidence, prerequisite.Request.ControlEvidence); Assert.Equal(request.Subject, result.Record.Source.Subject);
        Assert.Equal(request.UnitId, result.Record.Source.UnitId); Assert.Equal(request.UnitProfile, result.Record.UnitProfile);
        Assert.Equal(("10", "10", 1L), (result.Record.Source.AcceptedQuantity, result.Record.Source.UninvoicedQuantity, result.Record.Source.Version));
        Assert.Equal(request.Evidence, result.Record.Source.Evidence); Assert.Equal(world.User.Actor, result.Record.RecordedBy);
        Assert.Empty(request.ControlEvidence); Assert.Empty(result.Record.Source.ControlEvidence);
        Assert.Equal(Now, result.Record.RecordedAtUtc); Assert.Empty(world.Finance.Offers);
        var json = JsonSerializer.Serialize(result.Record, PurchasingJson.Options);
        Assert.DoesNotContain("purchaseOrderId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unitPrice", json, StringComparison.OrdinalIgnoreCase);
        var roundTrip = JsonSerializer.Deserialize<PurchaseUnorderedAcceptanceRecordV1>(json, PurchasingJson.Options)!;
        Assert.Equal(request.Subject, roundTrip.Source.Subject);
        Assert.True(PurchaseUnorderedAcceptanceOwnerContractV1.Valid(roundTrip));
    }

    [Theory]
    [InlineData(true, 403)]
    [InlineData(false, 401)]
    public async Task Unresolved_company_is_refused_without_reading_its_throwing_identity_or_any_owner(bool authenticated, int expected)
    {
        var world = new World(); var company = new UnresolvedUnorderedCompany(); var store = new UnorderedMemoryStore();
        var prerequisite = new UnorderedPrerequisites(); var authorityReads = 0;
        world.Authority.AfterMeasure = _ => authorityReads++;
        var service = new PurchaseUnorderedAcceptanceServiceV1(company, new UnorderedAuthentication(authenticated),
            world.Authority, new Correlation(), store, new Clock(), prerequisite);
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordAsync(UnorderedRecordRequest(), Metadata("unordered-no-company")));
        Assert.Equal(expected, denied.Status); Assert.Equal(0, company.IdentityReads); Assert.Equal(0, authorityReads);
        Assert.Equal(0, store.Calls); Assert.Equal(0, prerequisite.Calls); Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Unordered_replay_keeps_original_correlation_without_prerequisite_repeat_and_conflicts_on_other_content()
    {
        var world = new World(); var store = new UnorderedMemoryStore(); var prerequisite = new UnorderedPrerequisites();
        var service = UnorderedService(world, store, prerequisite); var request = UnorderedRecordRequest(); var metadata = Metadata("unordered-replay");
        var first = await service.RecordAsync(request, metadata);
        var replay = await service.RecordAsync(request, metadata with { CorrelationId = "SENTETIK-later" });
        Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.Record, replay.Record); Assert.Equal(1, prerequisite.Calls);
        var conflict = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordAsync(request with { AcceptedQuantity = "9" }, metadata));
        Assert.Equal((409, PurchasingProblemCodesV1.SameKeyPayloadMismatch), (conflict.Status, conflict.Code));
        world.Authority.Denied = PurchasingPermissionManifestV1.GoodsReceiptsRecord;
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordAsync(request, metadata))).Status);
        Assert.Equal(1, prerequisite.Calls); Assert.Equal(first.Record, store.Saved!.Record);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("failed-owner")]
    public async Task Unordered_prerequisite_cannot_release_after_authority_or_context_changes(string change)
    {
        var world = new World(); var store = new UnorderedMemoryStore();
        var prerequisite = new UnorderedPrerequisites { AfterRead = () =>
        {
            if (change == "company") world.Company.Set(Guid.NewGuid());
            else if (change == "actor") world.User.Actor = "SENTETIK-other";
            else world.Authority.Denied = PurchasingPermissionManifestV1.GoodsReceiptsRecord;
            if (change == "failed-owner") throw new InvalidOperationException("SENTETIK owner failure");
        } };
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => UnorderedService(world, store, prerequisite)
            .RecordAsync(UnorderedRecordRequest(), Metadata("unordered-late")));
        Assert.Equal(403, denied.Status); Assert.Null(store.Saved);
    }

    [Fact]
    public async Task A_prerequisite_that_completes_after_cancelling_the_request_cannot_record()
    {
        var world = new World(); var store = new UnorderedMemoryStore(); using var cancellation = new CancellationTokenSource();
        var prerequisite = new UnorderedPrerequisites { AfterRead = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UnorderedService(world, store, prerequisite)
            .RecordAsync(UnorderedRecordRequest(), Metadata("unordered-cancel"), cancellation.Token));
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Control_evidence_is_captured_before_awaited_owner_code_can_mutate_its_caller_list()
    {
        var world = new World(); var store = new UnorderedMemoryStore(); var request = UnorderedRecordRequest();
        var evidence = new List<PurchaseAcceptanceDocumentEvidenceV1> { request.Evidence };
        request = request with { ControlEvidence = evidence };
        var prerequisite = new UnorderedPrerequisites { AfterRead = () => { evidence.Add(evidence[0]); evidence.Add(evidence[0] with { DocumentId = Guid.Empty }); } };
        var result = await UnorderedService(world, store, prerequisite).RecordAsync(request, Metadata("unordered-frozen"));
        Assert.Equal(3, evidence.Count); Assert.Equal(request.Evidence, Assert.Single(result.Record.Source.ControlEvidence));
        Assert.NotSame(evidence, result.Record.Source.ControlEvidence);
        Assert.Throws<NotSupportedException>(() => ((IList<PurchaseAcceptanceDocumentEvidenceV1>)result.Record.Source.ControlEvidence).Add(request.Evidence));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("quantity")]
    [InlineData("subject")]
    [InlineData("evidence")]
    [InlineData("duplicates")]
    [InlineData("unit-profile")]
    public async Task Malformed_unordered_record_is_rejected_before_the_prerequisite(string defect)
    {
        var world = new World(); var store = new UnorderedMemoryStore(); var prerequisite = new UnorderedPrerequisites();
        var request = UnorderedRecordRequest();
        request = defect switch
        {
            "version" => request with { ExpectedVersion = "1" },
            "quantity" => request with { AcceptedQuantity = "01" },
            "subject" => request with { Subject = new PurchaseUnorderedGoodsSubjectV1(Guid.Empty, "1") },
            "evidence" => request with { Evidence = request.Evidence with { DocumentVersion = 0 } },
            "duplicates" => request with { ControlEvidence = [request.Evidence, request.Evidence] },
            _ => request with { UnitProfile = new(Guid.Empty, 1) },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<PurchasingServiceException>(() => UnorderedService(world, store, prerequisite)
            .RecordAsync(request, Metadata("unordered-invalid")))).Status);
        Assert.Equal(0, prerequisite.Calls); Assert.Null(store.Saved);
    }

    private static RecordPurchaseUnorderedAcceptanceV1 UnorderedRecordRequest()
    {
        var source = UnorderedSource(Invoice(false, false));
        return new(source.AcceptanceId, source.AcceptanceLineId, "0", source.SupplierPartyId, source.CompanyAccountId,
            "party-1", "account-1", source.Subject, source.UnitId, new(Guid.NewGuid(), 1), source.AcceptedQuantity,
            source.OccurredAtUtc, source.Evidence, source.ControlEvidence);
    }
    private static PurchaseUnorderedAcceptanceServiceV1 UnorderedService(World world, UnorderedMemoryStore store,
        UnorderedPrerequisites? prerequisite = null) => new(world.Company, world.User, world.Authority, new Correlation(), store, new Clock(), prerequisite);
    // Service contract test double only: it makes no relational transaction or Audit guarantee.
    private sealed partial class UnorderedMemoryStore : IPurchaseUnorderedAcceptanceStoreV1
    {
        public PurchaseUnorderedAcceptanceWriteV1? Saved { get; private set; }
        public int Calls { get; private set; }
        private string? hash; private string? key;
        public async Task<PurchaseUnorderedAcceptanceWriteV1> RecordAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
            PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task<PurchaseUnorderedAcceptanceRecordV1>> prepare,
            Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken)
        {
            Calls++;
            await requireStill(cancellationToken);
            if (Saved is not null && key == envelope.IdempotencyKey)
            {
                if (hash != requestHash) throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "SENTETIK conflict");
                return Saved with { IsReplay = true };
            }
            var record = await prepare(cancellationToken); await requireStill(cancellationToken);
            hash = requestHash; key = envelope.IdempotencyKey; return Saved = new(record, false, envelope.CorrelationId);
        }
    }
    private sealed class UnresolvedUnorderedCompany : ICompanyContext
    {
        public bool IsResolved => false;
        public int IdentityReads { get; private set; }
        public Guid CompanyId { get { IdentityReads++; throw new InvalidOperationException("Unresolved company identity cannot be read."); } }
        public void Set(Guid id) => throw new InvalidOperationException("Test context remains unresolved.");
    }
    private sealed class UnorderedAuthentication(bool authenticated) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    // Synthetic external prerequisite; actual supplier/subject/Documents/Settings validation is still a required integration.
    private sealed class UnorderedPrerequisites : IPurchaseUnorderedAcceptancePrerequisitesV1
    {
        public int Calls { get; private set; } public Guid CompanyId { get; private set; }
        public RecordPurchaseUnorderedAcceptanceV1? Request { get; private set; } public Action? AfterRead { get; init; }
        public async Task ValidateAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV1 request,
            Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken)
        { Calls++; CompanyId = companyId; Request = request; AfterRead?.Invoke(); await requireStill(cancellationToken); }
    }
}
