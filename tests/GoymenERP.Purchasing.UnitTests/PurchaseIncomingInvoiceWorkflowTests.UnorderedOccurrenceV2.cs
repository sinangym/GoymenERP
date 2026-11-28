using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceWorkflowTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  Fiilî kabul\nsonra kontrol  ")]
    public async Task Explicit_occurrence_day_and_optional_raw_reason_are_recorded_without_a_preexisting_acceptance(string? reason)
    {
        var world = new World(); var legacy = UnorderedRecordRequest() with { OccurredAtUtc = Now.AddDays(-10) };
        var request = new RecordPurchaseUnorderedAcceptanceV2(legacy, new(2026, 10, 9), reason);
        var masters = new UnorderedMasterPorts(legacy); var documents = new OccurrenceDocuments(); var store = new UnorderedMemoryStore();
        var composite = OccurrenceComposite(world, masters, documents);
        var service = new PurchaseUnorderedAcceptanceServiceV1(world.Company, world.User, world.Authority, new Correlation(), store, new Clock(), composite);
        var first = await service.RecordOccurrenceAsync(request, Metadata("nopo-explicit-day"));
        Assert.Equal(new PurchaseUnorderedAcceptanceDeclarationV2(request.BusinessDate, reason), first.Record.Declaration);
        Assert.Equal(world.User.UserId, first.Record.RecordedBy); Assert.Equal(Now, first.Record.RecordedAtUtc);
        Assert.Equal(legacy.OccurredAtUtc, first.Record.Source.OccurredAtUtc);
        Assert.NotEqual(DateOnly.FromDateTime(legacy.OccurredAtUtc.UtcDateTime), request.BusinessDate);
        Assert.Equal(new[] { "supplier", "product", "unit" }, masters.Calls); // No semantic evidence owner or preexisting acceptance read.
        Assert.All(masters.EffectiveDates, day => Assert.Equal(request.BusinessDate, day)); Assert.Single(documents.Calls);
        Assert.Empty(first.Record.Source.ControlEvidence); Assert.Empty(world.Finance.Offers);
        var original = first.Record;
        var wire = JsonSerializer.Serialize(original, PurchasingJson.Options);
        using (var receipt = JsonDocument.Parse(wire))
        {
            Assert.False(receipt.RootElement.TryGetProperty("declaration", out _));
            Assert.True(receipt.RootElement.TryGetProperty("source", out var source));
            Assert.True(source.TryGetProperty("declaration", out var declaration));
            Assert.True(declaration.TryGetProperty("businessDate", out var day)); Assert.Equal("2026-10-09", day.GetString());
        }
        documents.AfterRead = _ => throw new InvalidOperationException("Replay must not manufacture new document facts.");
        var replay = await service.RecordOccurrenceAsync(request, Metadata("nopo-explicit-day") with { CorrelationId = "SENTETIK-later" });
        Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(original, replay.Record);
        Assert.Single(documents.Calls);
    }

    [Fact]
    public async Task Service_unit_profile_uses_the_explicit_day_and_controls_are_file_witnesses_not_required_approvals()
    {
        var world = new World(); var legacy = UnorderedRecordRequest();
        legacy = legacy with { Subject = new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "7"), AcceptedQuantity = "1.25",
            ControlEvidence = [legacy.Evidence with { DocumentId = Guid.NewGuid(), SourceLineId = "SENTETIK-control-note" }] };
        var request = new RecordPurchaseUnorderedAcceptanceV2(legacy, new(2026, 10, 9));
        var masters = new UnorderedMasterPorts(legacy) { IsKg = false, UnitCode = "saat", Dimension = "time", Scale = 2 };
        var documents = new OccurrenceDocuments();
        await OccurrenceComposite(world, masters, documents).ValidateOccurrenceAsync(Company, request, _ => Task.CompletedTask, default);
        Assert.Equal(new[] { "supplier", "service", "unit" }, masters.Calls);
        Assert.Equal(new[] { legacy.Evidence, legacy.ControlEvidence[0] }, documents.Calls);
        Assert.All(masters.EffectiveDates, day => Assert.Equal(request.BusinessDate, day));
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("document")] [InlineData("version")] [InlineData("hash")]
    [InlineData("denied")] [InlineData("not-found")] [InlineData("unavailable")] [InlineData("malformed-outcome")]
    public async Task Actual_document_binding_fails_closed_before_master_or_occurrence_writes(string fault)
    {
        var world = new World(); var legacy = UnorderedRecordRequest(); var masters = new UnorderedMasterPorts(legacy);
        var documents = new OccurrenceDocuments { Defect = fault }; var store = new UnorderedMemoryStore();
        var service = new PurchaseUnorderedAcceptanceServiceV1(world.Company, world.User, world.Authority, new Correlation(), store, new Clock(),
            OccurrenceComposite(world, masters, fault == "missing" ? null : documents));
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordOccurrenceAsync(new(legacy, new(2026, 10, 9)), Metadata("nopo-doc-refused")));
        Assert.Equal(fault == "denied" ? 403 : fault == "not-found" ? 404 : 503, refusal.Status);
        Assert.Empty(masters.Calls); Assert.Null(store.Saved); Assert.Empty(world.Finance.Offers);
    }

    [Fact]
    public async Task Current_record_permission_is_rechecked_after_document_read_and_completed_consumer_cancellation_stops_the_record()
    {
        var world = new World(); var legacy = UnorderedRecordRequest(); var masters = new UnorderedMasterPorts(legacy);
        var documents = new OccurrenceDocuments { AfterRead = _ => world.Authority.Denied = PurchasingPermissionManifestV1.GoodsReceiptsRecord };
        var store = new UnorderedMemoryStore();
        var service = new PurchaseUnorderedAcceptanceServiceV1(world.Company, world.User, world.Authority, new Correlation(), store, new Clock(), OccurrenceComposite(world, masters, documents));
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordOccurrenceAsync(new(legacy, new(2026, 10, 9)), Metadata("nopo-doc-revoke")))).Status);
        Assert.Null(store.Saved); Assert.Empty(masters.Calls);
        world.Authority.Denied = null; documents.AfterRead = null; using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OccurrenceComposite(world, masters, documents).ValidateOccurrenceAsync(Company,
            new(legacy, new(2026, 10, 9)), _ => { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.Empty(masters.Calls);
    }

    [Fact]
    public void Versioned_wire_requires_explicit_day_preserves_raw_optional_reason_and_does_not_alias_legacy_hash()
    {
        var legacy = UnorderedRecordRequest(); var request = new RecordPurchaseUnorderedAcceptanceV2(legacy, new(2026, 10, 9), " raw ");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(request, options);
        var restored = JsonSerializer.Deserialize<RecordPurchaseUnorderedAcceptanceV2>(json, options)!;
        Assert.Equal(request.BusinessDate, restored.BusinessDate); Assert.Equal(request.Reason, restored.Reason);
        Assert.Equal(json, JsonSerializer.Serialize(restored, options));
        Assert.Equal(PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request), PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(restored));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "businessDate", "occurrence", "reason" }, doc.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
        var missing = JsonSerializer.Serialize(new { request.Occurrence, request.Reason }, options);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecordPurchaseUnorderedAcceptanceV2>(missing, options));
        var optional = JsonSerializer.Serialize(new { request.Occurrence, request.BusinessDate }, options);
        Assert.Null(JsonSerializer.Deserialize<RecordPurchaseUnorderedAcceptanceV2>(optional, PurchasingJson.Options)!.Reason);
        Assert.True(PurchaseUnorderedAcceptanceOwnerContractV1.Valid(request with { BusinessDate = DateOnly.MinValue }));
        Assert.True(PurchaseUnorderedAcceptanceOwnerContractV1.Valid(request with { BusinessDate = DateOnly.MaxValue }));
        var hash = PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request);
        Assert.NotEqual(PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(legacy), hash);
        Assert.NotEqual(hash, PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request with { BusinessDate = request.BusinessDate.AddDays(1) }));
        Assert.NotEqual(hash, PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request with { Reason = request.Reason!.Trim() }));
        Assert.NotEqual(PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request with { Reason = null }),
            PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(request with { Reason = "" }));
        Assert.True(PurchaseUnorderedAcceptanceOwnerContractV1.Valid(request with { Reason = new string('x', PurchaseOrder.MaximumReasonLength) }));
        Assert.False(PurchaseUnorderedAcceptanceOwnerContractV1.Valid(request with { Reason = new string('x', PurchaseOrder.MaximumReasonLength + 1) }));
    }

    [Fact]
    public void Legacy_record_roundtrip_keeps_declaration_absent_and_original_request_hash_without_date_backfill()
    {
        var request = UnorderedRecordRequest();
        var source = new PurchaseUnorderedAcceptanceSourceV1(Company, request.AcceptanceId, request.AcceptanceLineId, 1,
            request.SupplierPartyId, request.CompanyAccountId, request.Subject, request.UnitId, request.AcceptedQuantity,
            request.AcceptedQuantity, request.OccurredAtUtc, request.Evidence, request.ControlEvidence);
        var original = new PurchaseUnorderedAcceptanceRecordV1(source, request.ExpectedPartyVersion, request.ExpectedAccountVersion,
            request.UnitProfile, "SENTETIK-actor", Now);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); var json = JsonSerializer.Serialize(original, options);
        Assert.DoesNotContain("declaration", json, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<PurchaseUnorderedAcceptanceRecordV1>(json, options)!;
        Assert.Null(restored.Declaration);
        Assert.Equal(PurchasingRequestHash.Compute(PurchaseUnorderedAcceptanceOwnerContractV1.RecordCommand, request.AcceptanceId, request),
            PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(restored));
        Assert.Equal(json, JsonSerializer.Serialize(restored, options));
    }

    private static PurchaseUnorderedAcceptancePrerequisitesV1 OccurrenceComposite(World world, UnorderedMasterPorts masters,
        IPurchaseUnorderedAcceptanceDocumentReadV1? documents) => new(world.Company, masters, masters, masters, new(masters), masters, masters, documents);

    // Controlled owner protocol only; the API leaf tests separately bind real IDocumentService calls and content hashes.
    private sealed class OccurrenceDocuments : IPurchaseUnorderedAcceptanceDocumentReadV1
    {
        public List<PurchaseAcceptanceDocumentEvidenceV1> Calls { get; } = [];
        public string? Defect { get; init; } public Action<PurchaseAcceptanceDocumentEvidenceV1>? AfterRead { get; set; }
        public Task<PurchaseUnorderedAcceptanceDocumentAnswerV1> ReadAsync(Guid companyId, PurchaseAcceptanceDocumentEvidenceV1 value,
            Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken)
        {
            Calls.Add(value); AfterRead?.Invoke(value);
            if (Defect is "denied" or "not-found" or "unavailable") return Task.FromResult(new PurchaseUnorderedAcceptanceDocumentAnswerV1(
                Defect == "denied" ? PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied : Defect == "not-found"
                    ? PurchaseUnorderedAcceptanceEvidenceOutcomeV1.NotFound : PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable, null));
            return Task.FromResult(new PurchaseUnorderedAcceptanceDocumentAnswerV1(Defect == "malformed-outcome" ? (PurchaseUnorderedAcceptanceEvidenceOutcomeV1)99
                : PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found, new(Defect == "company" ? Guid.NewGuid() : companyId,
                    Defect == "document" ? Guid.NewGuid() : value.DocumentId, Defect == "version" ? value.DocumentVersion + 1 : value.DocumentVersion,
                    Defect == "hash" ? new string('f', 64) : value.Sha256)));
        }
    }
    private sealed partial class UnorderedMemoryStore
    {
        public Task<PurchaseUnorderedAcceptanceWriteV1> RecordOccurrenceAsync(Guid companyId, RecordPurchaseUnorderedAcceptanceV2 request,
            PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task<PurchaseUnorderedAcceptanceRecordV1>> prepare,
            Func<CancellationToken, Task> requireStill, CancellationToken cancellationToken) =>
            RecordAsync(companyId, request.Occurrence, envelope, requestHash, prepare, requireStill, cancellationToken);
    }
}
