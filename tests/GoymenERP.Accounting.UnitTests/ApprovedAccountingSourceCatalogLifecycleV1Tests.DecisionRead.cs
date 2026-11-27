using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class ApprovedAccountingSourceCatalogLifecycleV1Tests
{
    private static readonly JsonSerializerOptions DecisionJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Activation_purpose_read_uses_only_companywide_activate_and_emits_the_complete_flat_package_with_two_frozen_versions()
    {
        var store = new MemoryStore(await Preview()) { DecisionActiveVersion = 7 };
        var authority = new DecisionAuthority(); var service = Service(store, authority); var consumers = 0;
        var result = await service.ReadDecisionAsync(Package, _ => { consumers++; return Task.CompletedTask; });
        Assert.Equal(AccountingApiStatus.Ok, result.Status); var detail = Assert.IsType<ApprovedAccountingSourceCatalogDecisionDetailV1>(result.Resource);
        Assert.Equal("Prepared", detail.Catalog.Status); Assert.Equal(1L, detail.ExpectedPackageVersion); Assert.Equal(7L, detail.ExpectedActiveCatalogVersion);
        Assert.Equal(2, detail.Package.Accounts.Count); var binding = Assert.Single(detail.Package.Bindings); Assert.Equal(2, binding.Mappings.Count);
        Assert.Equal(store.Record!.Package.Mirror.Rows.Select(row => (row.Account.AccountId, row.Code, row.Name)), detail.Package.Accounts.Select(row => (row.AccountId, row.Code, row.Name)));
        Assert.Equal(ApprovedAccountingSourceCatalogServiceV1.CatalogSourceFactsDigest(store.Record), detail.SourceFactsDigest);
        Assert.Equal(ApprovedAccountingSourceCatalogServiceV1.CatalogPreviewDigest(detail.Catalog, detail.Package, 1, 7), detail.PreviewDigest);
        Assert.All(authority.Permissions, permission => Assert.Equal(ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, permission));
        Assert.Equal(5, consumers); Assert.Equal(1, store.DecisionReads); Assert.Equal(0, store.CommandCount); Assert.Equal(0, store.Activations);
        var wire = JsonSerializer.SerializeToElement(detail, DecisionJson);
        Assert.Equal(6, wire.EnumerateObject().Count()); Assert.Equal(15, wire.GetProperty("catalog").EnumerateObject().Count());
        Assert.False(wire.GetRawText().Contains("\"mirror\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("unresolved")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Final_normally_completed_consumer_cannot_release_catalog_facts_after_authority_or_context_loss(string fault)
    {
        var store = new MemoryStore(await Preview()); var company = new MutableCompanyContext(Company); var user = new ActivationUser(); var authority = new DecisionAuthority();
        var service = Service(store, authority, company, user); var callbacks = 0; using var cancellation = new CancellationTokenSource();
        Task Consumer(CancellationToken _)
        {
            if (++callbacks == 5)
            {
                if (fault == "permission") authority.Allowed = false;
                if (fault == "company") company.Set(OtherCompany);
                if (fault == "unresolved") company.Set(Guid.Empty);
                if (fault == "actor") user.ActorId = Guid.NewGuid();
                if (fault == "member") user.Member = false;
                if (fault == "cancel") cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(Package, Consumer, cancellation.Token));
        else { var result = await service.ReadDecisionAsync(Package, Consumer); Assert.Equal(AccountingApiStatus.Denied, result.Status); Assert.Null(result.Resource); }
        Assert.Equal(5, callbacks); Assert.Equal(1, store.DecisionReads); Assert.Equal(0, store.Activations); Assert.Equal(0, store.CommandCount);
    }

    [Theory]
    [InlineData("denied")] [InlineData("scoped")] [InlineData("null-authority")] [InlineData("not-found")]
    [InlineData("foreign-company")] [InlineData("foreign-id")] [InlineData("malformed-version")] [InlineData("malformed-package")]
    [InlineData("provider")] [InlineData("null-record")]
    public async Task Catalog_purpose_read_refuses_unknown_malformed_or_unauthorized_source_without_granting_prepare(string fault)
    {
        var store = new MemoryStore(await Preview())
        {
            DecisionMissing = fault == "not-found", DecisionFailure = fault == "provider", DecisionNullRecord = fault == "null-record",
            DecisionTransform = source => fault switch
            {
                "foreign-company" => source with { CompanyId = OtherCompany }, "foreign-id" => source with { PackageId = Guid.NewGuid() },
                "malformed-version" => source with { Version = 0 }, "malformed-package" => source with { Package = null! }, _ => source,
            },
        };
        var authority = new DecisionAuthority { Allowed = fault != "denied", CompanyWide = fault != "scoped", NullScope = fault == "null-authority" };
        var result = await Service(store, authority).ReadDecisionAsync(Package, _ => Task.CompletedTask);
        Assert.Equal(fault is "denied" or "scoped" or "null-authority" ? AccountingApiStatus.Denied : fault == "not-found" ? AccountingApiStatus.NotFound : AccountingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource); Assert.Equal(0, store.CommandCount); Assert.Equal(0, store.Activations);
        if (fault is "denied" or "scoped" or "null-authority") Assert.Equal(0, store.DecisionReads);
    }

    [Fact]
    public async Task Genuine_original_catalog_receipt_keeps_Active_two_and_original_two_versions_after_current_supersession()
    {
        var store = new MemoryStore(await Preview()); var service = Service(store, new FixedAuthority(Granted()));
        var before = (await service.ReadDecisionAsync(Package, _ => Task.CompletedTask)).Resource!;
        var request = new ActivateApprovedAccountingSourceCatalogRequestV1(1, 0); const string key = "catalog-original-proof";
        var original = await service.ActivateAsync(Package, request, key); var proof = Assert.IsType<ApprovedAccountingSourceCatalogActivationReceiptV1>(original.Resource?.ActivationReceipt);
        Assert.Equal(Actor, proof.ActorId); Assert.Equal(1L, proof.ExpectedPackageVersion); Assert.Equal(0L, proof.ExpectedActiveCatalogVersion);
        Assert.Equal(2L, proof.ResultVersion); Assert.Equal(1L, proof.CatalogVersion); Assert.Equal(before.SourceFactsDigest, proof.SourceFactsDigest);
        var command = (await store.FindCommandAsync(ApprovedAccountingSourceCatalogCommandNamesV1.Activate, proof.IdempotencyKeyHash))!;
        Assert.Equal(LocalHash(key), proof.IdempotencyKeyHash);
        Assert.Equal(LocalHash(ApprovedAccountingSourceCatalogCommandNamesV1.Activate + "|" + JsonSerializer.Serialize(new { packageId = Package, request.ExpectedPackageVersion, request.ExpectedActiveCatalogVersion }, DecisionJson)), proof.PayloadHash);
        Assert.Equal(LocalHash(command.ResultJson), proof.OriginalResultDigest); Assert.Equal(ApprovedAccountingSourceCatalogServiceV1.ActivationOriginalResultDigest(original), proof.OriginalResultDigest);
        store.SupersedeDecisionRecord();
        var current = (await service.ReadDecisionAsync(Package, _ => Task.CompletedTask)).Resource!;
        Assert.Equal("Superseded", current.Catalog.Status); Assert.Equal(3L, current.ExpectedPackageVersion); Assert.Equal(2L, current.ExpectedActiveCatalogVersion);
        Assert.Equal(before.SourceFactsDigest, current.SourceFactsDigest); Assert.NotEqual(before.PreviewDigest, current.PreviewDigest);
        var replay = await service.ActivateAsync(Package, request, key);
        Assert.True(replay.IsReplay); Assert.Equal(command.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version); Assert.Equal(proof, replay.Resource?.ActivationReceipt);
        Assert.Equal(1, store.Activations); Assert.Equal(1, store.CommandCount);
        var wire = JsonSerializer.SerializeToElement(replay, DecisionJson); Assert.Equal(8, wire.EnumerateObject().Count());
        Assert.Equal(15, wire.GetProperty("resource").EnumerateObject().Count()); Assert.False(wire.GetProperty("resource").TryGetProperty("activationReceipt", out _));
        Assert.Equal(JsonSerializer.Serialize(original with { IsReplay = false, OriginalCorrelationId = null }, DecisionJson), command.ResultJson);
    }

    [Theory]
    [InlineData("missing")] [InlineData("null-json")] [InlineData("null-envelope")] [InlineData("malformed-json")] [InlineData("unknown-root")] [InlineData("unknown-resource")] [InlineData("private-proof")]
    [InlineData("missing-resource")] [InlineData("foreign-company")] [InlineData("foreign-id")] [InlineData("wrong-command")] [InlineData("wrong-key")]
    [InlineData("wrong-actor")] [InlineData("wrong-hash")] [InlineData("wrong-correlation")] [InlineData("empty-correlation")]
    [InlineData("wrong-version")] [InlineData("wrong-catalog-version")] [InlineData("wrong-source")] [InlineData("wrong-preparer")]
    [InlineData("wrong-count")] [InlineData("current-answer")] [InlineData("wrong-active-expected")] [InlineData("wrong-package-expected")]
    public async Task Private_catalog_receipt_never_certifies_malformed_legacy_foreign_or_current_replacement_answers(string fault)
    {
        var store = new MemoryStore(await Preview()); var request = new ActivateApprovedAccountingSourceCatalogRequestV1(1, 0); const string key = "catalog-bind";
        var answer = await Service(store, new FixedAuthority(Granted())).ActivateAsync(Package, request, key);
        AccountingCommandRecord? command = await store.FindCommandAsync(ApprovedAccountingSourceCatalogCommandNamesV1.Activate, ApprovedAccountingSourceCatalogServiceV1.ActivationKeyHash(key));
        var source = store.Record!; var actor = Actor;
        if (fault == "missing") command = null;
        if (fault == "wrong-command") command = command! with { CommandName = "another-command" };
        if (fault == "wrong-key") command = command! with { IdempotencyKeyHash = LocalHash("another-key") };
        if (fault == "wrong-actor") actor = Guid.NewGuid();
        if (fault == "wrong-hash") command = command! with { RequestHash = new string('A', 64) };
        if (fault == "empty-correlation") command = command! with { OriginalCorrelationId = "" };
        if (fault == "wrong-correlation") answer = answer with { IsReplay = true, OriginalCorrelationId = "other-correlation" };
        if (fault == "foreign-company") source = source with { CompanyId = OtherCompany };
        if (fault == "foreign-id") source = source with { PackageId = Guid.NewGuid() };
        if (fault == "wrong-source") source = source with { AccountPlanSourceVersion = "other-source" };
        if (fault == "wrong-preparer") source = source with { PreparedByUserId = Guid.NewGuid() };
        if (fault == "wrong-package-expected") request = request with { ExpectedPackageVersion = 2 };
        if (fault == "wrong-active-expected") request = request with { ExpectedActiveCatalogVersion = 1 };
        if (fault == "current-answer") answer = answer with { Resource = answer.Resource! with { Status = "Superseded", Version = 3 } };
        if (fault == "null-json") command = command! with { ResultJson = null! };
        if (fault == "null-envelope") command = command! with { ResultJson = "null" };
        if (fault == "malformed-json") command = command! with { ResultJson = "{" };
        if (fault is "unknown-root" or "unknown-resource" or "private-proof" or "missing-resource" or "wrong-version" or "wrong-catalog-version" or "wrong-count")
        {
            var json = JsonNode.Parse(command!.ResultJson)!;
            if (fault == "unknown-root") json["unexpected"] = true;
            if (fault == "unknown-resource") json["resource"]!["unexpected"] = true;
            if (fault == "private-proof") json["resource"]!["activationReceipt"] = new JsonObject();
            if (fault == "missing-resource") json.AsObject().Remove("resource");
            if (fault == "wrong-version") json["resource"]!["version"] = 3;
            if (fault == "wrong-catalog-version") json["resource"]!["catalogVersion"] = 2;
            if (fault == "wrong-count") json["resource"]!["accountCount"] = 3;
            command = command with { ResultJson = json.ToJsonString() };
        }
        Assert.Null(ApprovedAccountingSourceCatalogServiceV1.BindCatalogActivationReceipt(command, source, answer, request, key, actor));
    }

    [Fact]
    public async Task Legacy_native_port_without_purpose_source_keeps_native_wire_but_never_invents_private_provenance()
    {
        var inner = new MemoryStore(await Preview()); var legacy = new ReaderlessCatalog(inner);
        var service = new ApprovedAccountingSourceCatalogServiceV1(new ApprovedAccountingSourcePackageParserV1(), legacy, inner,
            new FixedAuthority(Granted()), new FixedCompanyContext(), new FixedUser(), new FixedCorrelation(), new FixedTimeProvider(Now));
        var read = await service.ReadDecisionAsync(Package, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, read.Status); Assert.Null(read.Resource);
        var native = await service.ActivateAsync(Package, new(1, 0), "legacy-native-catalog");
        Assert.Equal(AccountingApiStatus.Ok, native.Status); Assert.Null(native.Resource?.ActivationReceipt);
        Assert.Equal(1, inner.Activations); Assert.Equal(1, inner.CommandCount);
    }

    private static string LocalHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class DecisionAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true, NullScope; public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Permissions.Add(permission); return Task.FromResult(NullScope ? null! : new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class ReaderlessCatalog(MemoryStore inner) : IApprovedAccountingSourceCatalogStoreV1
    {
        public Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> RunActivationAsync(Func<bool, Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default) => inner.RunActivationAsync(attempt, fence, token);
        public Task<ApprovedAccountingSourceCatalogRecordV1?> ReadAsync(Guid id, CancellationToken token = default) => inner.ReadAsync(id, token);
        public Task<(ApprovedAccountingSourceCatalogRecordV1 Record, AccountingWriteOutcome Outcome)> PrepareAsync(Guid id, ApprovedAccountingSourcePackagePreviewV1 preview,
            Guid actor, DateTimeOffset at, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => inner.PrepareAsync(id, preview, actor, at, fence, metadata, token);
        public Task<ApprovedAccountingSourceCatalogRecordV1> ActivateAsync(Guid id, long version, long activeVersion, Guid actor, DateTimeOffset at,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => inner.ActivateAsync(id, version, activeVersion, actor, at, fence, metadata, token);
        public Task<NormalJournalPostingRuleV1?> ResolveAsync(Guid company, Guid source, string kind, string version, string profile, string profileVersion, Guid currency,
            CancellationToken token = default) => inner.ResolveAsync(company, source, kind, version, profile, profileVersion, currency, token);
    }
    private sealed partial class MemoryStore : IApprovedAccountingSourceCatalogDecisionStoreV1
    {
        public long? DecisionActiveVersion; public bool DecisionMissing, DecisionFailure, DecisionNullRecord; public int DecisionReads;
        public Func<ApprovedAccountingSourceCatalogRecordV1, ApprovedAccountingSourceCatalogRecordV1>? DecisionTransform;
        public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActivationReceiptSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); token.ThrowIfCancellationRequested(); DecisionReads++;
            if (DecisionFailure) throw new AccountingStoreException(AccountingStoreCodes.ProviderUnavailable, "SENTETIK purpose provider failure");
            var result = DecisionMissing || Record is null ? null : DecisionTransform?.Invoke(Record) ?? Record;
            await fence(token); token.ThrowIfCancellationRequested(); return result;
        }
        public async Task<ApprovedAccountingSourceCatalogDecisionSourceV1?> ReadActivationDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            var source = await ReadActivationReceiptSourceAsync(id, fence, token); if (source is null) return null;
            await fence(token); token.ThrowIfCancellationRequested();
            return new(DecisionNullRecord ? null! : source, DecisionActiveVersion ?? (source.Status == ApprovedAccountingSourceCatalogStatusV1.Active ? source.CatalogVersion ?? 0 : 0));
        }
        public void SupersedeDecisionRecord() { Record = Record! with { Status = ApprovedAccountingSourceCatalogStatusV1.Superseded, Version = 3 }; DecisionActiveVersion = 2; }
    }
}
