using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingApprovedSourceCatalogSqlServerRelationalAcceptanceTests
{
    // Real migrated SQL and the existing SAME scoped collector/writer fixture. Authority loss is controlled.
    // These sources have not been executed here and are not HTTP/browser or naturally occurring race evidence.
    [SqlServerFact]
    public async Task Catalog_activation_purpose_read_and_original_receipt_bind_full_immutable_package_after_supersession()
    {
        await using var run = await Run.StartAsync(); var first = Package(Guid.NewGuid(), "SENTETIK-original-package");
        await CatalogSafetyPrepare(run, first); await using var scope = new CatalogActivationScope(run);
        var read = await scope.Service.ReadDecisionAsync(first.Id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, read.Status); var pending = Assert.IsType<ApprovedAccountingSourceCatalogDecisionDetailV1>(read.Resource);
        Assert.Equal("Prepared", pending.Catalog.Status); Assert.Equal(1L, pending.ExpectedPackageVersion); Assert.Equal(0L, pending.ExpectedActiveCatalogVersion);
        Assert.Equal(2, pending.Package.Accounts.Count); Assert.Equal(2, Assert.Single(pending.Package.Bindings).Mappings.Count);
        const string key = "original-catalog-receipt";
        var original = await scope.Service.ActivateAsync(first.Id, new(pending.ExpectedPackageVersion, pending.ExpectedActiveCatalogVersion), key);
        Assert.Equal(AccountingApiStatus.Ok, original.Status);
        var proof = Assert.IsType<ApprovedAccountingSourceCatalogActivationReceiptV1>(original.Resource?.ActivationReceipt);
        Assert.Equal(CatalogSafetyActor, proof.ActorId); Assert.Equal(first.Id, proof.PackageId); Assert.Equal(CompanyA, proof.CompanyId);
        Assert.Equal(1L, proof.ExpectedPackageVersion); Assert.Equal(0L, proof.ExpectedActiveCatalogVersion);
        Assert.Equal(2L, proof.ResultVersion); Assert.Equal(1L, proof.CatalogVersion); Assert.Equal(scope.CorrelationId, proof.OriginalCorrelationId);
        Assert.Equal(pending.SourceFactsDigest, proof.SourceFactsDigest);
        Assert.Equal(ApprovedAccountingSourceCatalogServiceV1.ActivationKeyHash(key), proof.IdempotencyKeyHash);
        Assert.Equal(ApprovedAccountingSourceCatalogServiceV1.ActivationPayloadHash(first.Id, 1, 0), proof.PayloadHash);
        var storedJson = await run.ScalarAsync<string>("SELECT ResultJson FROM accounting.Commands WHERE CompanyId=@c AND CommandName=@n AND IdempotencyKeyHash=@k",
            ("@c", CompanyA), ("@n", ApprovedAccountingSourceCatalogCommandNamesV1.Activate), ("@k", proof.IdempotencyKeyHash));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(original with { IsReplay = false, OriginalCorrelationId = null }, json), storedJson);
        Assert.Equal(ApprovedAccountingSourceCatalogServiceV1.ActivationOriginalResultDigest(original), proof.OriginalResultDigest);
        var originalCounts = await run.CountsAsync(first.Id);
        var next = Package(Guid.NewGuid(), "SENTETIK-replacement-package"); await CatalogSafetyPrepare(run, next);
        Assert.Equal(AccountingApiStatus.Ok, (await scope.Service.ActivateAsync(next.Id, new(1, 1), "replacement-catalog-receipt")).Status);
        var current = (await scope.Service.ReadDecisionAsync(first.Id, _ => Task.CompletedTask)).Resource!;
        Assert.Equal("Superseded", current.Catalog.Status); Assert.Equal(3L, current.ExpectedPackageVersion); Assert.Equal(2L, current.ExpectedActiveCatalogVersion);
        Assert.Equal(pending.SourceFactsDigest, current.SourceFactsDigest); Assert.NotEqual(pending.PreviewDigest, current.PreviewDigest);
        Assert.Equal(JsonSerializer.Serialize(pending.Package, json), JsonSerializer.Serialize(current.Package, json));
        var replay = await scope.Activate(first.Id, key);
        Assert.True(replay.IsReplay); Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version); Assert.Equal(proof, replay.Resource?.ActivationReceipt);
        Assert.Equal(JsonSerializer.Serialize(original.Resource, json), JsonSerializer.Serialize(replay.Resource, json));
        Assert.Equal(originalCounts, await run.CountsAsync(first.Id)); scope.AssertClean();
        var nativeWire = JsonSerializer.SerializeToElement(replay, json); Assert.Equal(8, nativeWire.EnumerateObject().Count());
        Assert.Equal(15, nativeWire.GetProperty("resource").EnumerateObject().Count()); Assert.False(nativeWire.GetProperty("resource").TryGetProperty("activationReceipt", out _));
        var mismatched = await scope.Service.ActivateAsync(first.Id, new(1, 1), key);
        Assert.Equal(AccountingApiStatus.Conflict, mismatched.Status); Assert.Equal(AccountingApiCodes.IdempotencyConflict, mismatched.Code); Assert.Null(mismatched.Resource);
        scope.User.Actor = Guid.NewGuid();
        var otherActor = await scope.Activate(first.Id, key);
        Assert.Equal(AccountingApiStatus.Conflict, otherActor.Status); Assert.Equal(AccountingApiCodes.IdempotencyConflict, otherActor.Code); Assert.Null(otherActor.Resource);
        Assert.Equal(originalCounts, await run.CountsAsync(first.Id)); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Catalog_activation_receipt_read_boundaries_roll_back_command_audit_and_activation_before_one_same_key_retry()
    {
        foreach (var phase in new[] { 5, 7, 8 }) // Saved1/catalog2/lifecycle3; receipt entry4/command5/store entry6/package7/release8; owner9/precommit10.
        {
            await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-receipt-fence");
            await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
            var save = new CatalogActivationSave(package.Id); await using var scope = new CatalogActivationScope(run, save);
            var savedFences = 0; var deny = true;
            scope.Authority.Read = () => !save.Saved || ++savedFences != phase || !deny;
            var refused = await scope.Activate(package.Id, "receipt-fence");
            Assert.Equal(AccountingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); Assert.Equal(phase, savedFences);
            scope.AssertClean(); Assert.Equal(before, await run.CountsAsync(package.Id));
            var row = await run.CatalogAsync(package.Id); Assert.Equal(1, row.Status); Assert.Equal(1L, row.Version); Assert.Null(row.ActivatedBy);
            deny = false;
            var approved = await scope.Activate(package.Id, "receipt-fence"); var replay = await scope.Activate(package.Id, "receipt-fence");
            Assert.Equal(AccountingApiStatus.Ok, approved.Status); Assert.NotNull(approved.Resource?.ActivationReceipt); Assert.True(replay.IsReplay);
            Assert.Equal(approved.Resource?.ActivationReceipt, replay.Resource?.ActivationReceipt); Assert.Equal(2, save.Attempts);
            Assert.Equal(new Counts(1, before.Commands + 1, before.Audits + 1), await run.CountsAsync(package.Id)); scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Catalog_purpose_last_consumer_revocation_cancel_and_foreign_company_never_release_or_write_source_facts()
    {
        foreach (var cancel in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var package = Package(Guid.NewGuid(), "SENTETIK-purpose-release");
            await CatalogSafetyPrepare(run, package); var before = await run.CountsAsync(package.Id);
            await using var scope = new CatalogActivationScope(run); var callbacks = 0; var allowed = true;
            scope.Authority.Read = () => allowed; using var cancellation = new CancellationTokenSource();
            Task Consumer(CancellationToken _)
            {
                if (++callbacks == 5) { if (cancel) cancellation.Cancel(); else allowed = false; }
                return Task.CompletedTask;
            }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Service.ReadDecisionAsync(package.Id, Consumer, cancellation.Token));
            else { var denied = await scope.Service.ReadDecisionAsync(package.Id, Consumer); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource); }
            Assert.Equal(5, callbacks); Assert.Equal(before, await run.CountsAsync(package.Id)); scope.AssertClean();
            scope.Company.Set(CompanyB); allowed = true;
            var native = await scope.Service.ReadDecisionAsync(package.Id, _ => Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Denied, native.Status); Assert.Null(native.Resource);
            // Infrastructure tenant predicate only: no extra public read permission or native authority bypass is claimed.
            var reader = new ApprovedAccountingSourceCatalogStoreV1(scope.Db, scope.Company, scope.Collector, TimeProvider.System);
            Assert.Null(await reader.ReadActivationReceiptSourceAsync(package.Id, _ => Task.CompletedTask));
            Assert.Equal(before, await run.CountsAsync(package.Id)); scope.AssertClean();
        }
    }
}
