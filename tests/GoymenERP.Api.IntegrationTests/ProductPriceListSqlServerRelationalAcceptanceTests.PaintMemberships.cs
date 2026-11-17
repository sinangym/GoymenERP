using System.Data;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>BS-10 real SQL sources. Synthetic authority/audit failure injection is labelled; no service, database or test is started by this file.</summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Paint_membership_requires_a_real_Excel_source_then_preserves_history_and_replays_its_original_answer()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        var productVersion = await run.ScalarAsync<long>("SELECT Version FROM product.Products WHERE Id = @id", ProductId);
        ApplyProductPaintMembershipImportV1 import;
        ProductPaintMembershipMutationV1 original;
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            Assert.Equal(ProductPaintMembershipCatalogOutcomeV1.NoSource, (await service.GetCurrentAsync()).Outcome);
            var refused = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => service.SetAsync(ProductId,
                new("0", productVersion.ToString(), "0", true, "SENTETIK"), PaintMetadata(run, "paint-before-source")));
            Assert.Equal((409, ProductPaintMembershipCodesV1.NoSource), (refused.StatusCode, refused.Code));
            var preview = await service.ValidateImportAsync(PaintFile(code));
            Assert.True(preview.Ready);
            Assert.Equal(ProductId, Assert.Single(preview.Rows).ProductId);
            import = PaintApply(preview, code);
            original = await service.ApplyImportAsync(import, PaintMetadata(run, "paint-import"));
            Assert.Equal("1", original.Version);
            Assert.Equal("1", Assert.Single(original.ChangedEntries).MembershipVersion);
        }
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            var removed = await service.SetAsync(ProductId, new("1", productVersion.ToString(), "1", false, "SENTETIK kaldırma"), PaintMetadata(run, "paint-remove"));
            Assert.Equal(ProductId, Assert.Single(removed.RemovedProductIds!));
            Assert.Empty((await service.GetCurrentAsync()).Entries);
            var replay = await service.ApplyImportAsync(import, PaintMetadata(run, "paint-import") with { CorrelationId = "later" });
            Assert.True(replay.IsReplay);
            Assert.Equal(JsonSerializer.Serialize(original with { IsReplay = true }), JsonSerializer.Serialize(replay));
            Assert.Equal("1", replay.Version); // today's removal cannot rewrite an old successful response
            var conflict = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => service.ApplyImportAsync(import with { Reason = "SENTETIK changed" }, PaintMetadata(run, "paint-import")));
            Assert.Equal(ProductPaintMembershipCodesV1.IdempotencyConflict, conflict.Code);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipLines WHERE Version = 1"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipLines WHERE Version = 2"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PaintMemberships' AND Outcome = N'Succeeded'"));
    }

    [SqlServerFact]
    public async Task Paint_import_never_removes_absent_products_and_the_consumer_revalidates_real_product_lifecycle()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        // Actual second Product card, copied only inside this synthetic SQL fixture. No application master is inferred.
        var secondId = Guid.NewGuid();
        await run.ExecuteAsync("INSERT INTO product.Products (Id,ClassId,Code,CreatedAtUtc,FamilyId,GroupId,Lifecycle,Name,ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version) "
            + "SELECT @id,ClassId,'PAINT-SENTETIK-002',CreatedAtUtc,FamilyId,GroupId,Lifecycle,Name,ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version FROM product.Products WHERE Id = '"
            + ProductId.ToString("D") + "'", secondId);
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            var preview = await service.ValidateImportAsync(PaintFile(code, "PAINT-SENTETIK-002"));
            await service.ApplyImportAsync(PaintApply(preview, code, "PAINT-SENTETIK-002"), PaintMetadata(run, "paint-two"));
        }
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            var preview = await service.ValidateImportAsync(PaintFile(code));
            var result = await service.ApplyImportAsync(PaintApply(preview, code), PaintMetadata(run, "paint-absent"));
            Assert.Equal("1", result.Version); // already present is a no-op; the omitted second card stays a member
            Assert.Equal(2, (await service.GetCurrentAsync()).Entries.Count);
        }
        await run.ExecuteAsync("UPDATE product.Products SET Lifecycle=2, Version=Version+1 WHERE Id=@id", secondId);
        await using (var scope = run.Request())
        {
            var source = new ProductPaintMembershipReader(scope.Context, scope.CompanyContext, new User(run.CreatorActorId));
            Assert.Equal(ProductPaintMembershipReadOutcomeV1.Inactive, (await source.GetAsync(secondId)).Outcome);
            Assert.Single((await source.ListAsync()).Entries);
            Assert.Equal(2, (await PaintService(scope, run.CreatorActorId).GetCurrentAsync()).Entries.Count); // admin can still remove the inactive membership
        }
    }

    [SqlServerFact]
    public async Task Paint_preview_is_bound_to_actual_product_versions_actor_and_company_and_refuses_the_entire_unknown_file()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        ProductPaintMembershipImportPreviewV1 preview;
        await using (var scope = run.Request()) preview = await PaintService(scope, run.CreatorActorId).ValidateImportAsync(PaintFile(code));
        await run.ExecuteAsync("UPDATE product.Products SET Version=Version+1 WHERE Id=@id", ProductId);
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            var refusal = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => service.ApplyImportAsync(PaintApply(preview, code), PaintMetadata(run, "paint-stale-product")));
            Assert.Equal(ProductPaintMembershipCodesV1.PreviewMismatch, refusal.Code);
            var replay = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => service.ApplyImportAsync(PaintApply(preview, code), PaintMetadata(run, "paint-stale-product")));
            Assert.True(replay.IsReplay);
            var bad = await service.ValidateImportAsync(PaintFile(code, "SENTETIK-UNKNOWN"));
            Assert.False(bad.Ready);
            Assert.Contains(bad.Errors, x => x.Code == ProductPaintMembershipCodesV1.NotFound);
        }
        await using (var scope = run.Request(TestCompanyIds.CompanyB))
        {
            var result = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => PaintService(scope, run.CreatorActorId).ApplyImportAsync(
                PaintApply(preview, code), PaintMetadata(run, "paint-other-company") with { CompanyId = TestCompanyIds.CompanyB }));
            Assert.Equal(ProductPaintMembershipCodesV1.PreviewMismatch, result.Code);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
    }

    [SqlServerFact]
    public async Task Paint_membership_and_attempt_and_real_Audit_owner_append_rollback_together_on_an_injected_audit_failure()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        ApplyProductPaintMembershipImportV1 import;
        await using (var scope = run.Request())
        {
            var realWriter = scope.Services.GetRequiredService<IAuditTransactionWriterV1>();
            var service = PaintService(scope, run.CreatorActorId, writer: new PaintFailAfterAudit(realWriter));
            import = PaintApply(await service.ValidateImportAsync(PaintFile(code)), code);
            await Assert.ThrowsAsync<PaintInjectedAuditFailure>(() => service.ApplyImportAsync(import, PaintMetadata(run, "paint-audit-failure")));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Product.PaintMemberships'"));
        await using (var scope = run.Request())
        {
            var result = await PaintService(scope, run.CreatorActorId).ApplyImportAsync(import, PaintMetadata(run, "paint-audit-failure"));
            Assert.False(result.IsReplay); // a rolled-back key remains available
        }
    }

    [SqlServerFact]
    public async Task Paint_final_permission_loss_after_real_Audit_append_rolls_back_all_rows_and_the_key_can_retry()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        var granted = true;
        var authority = new PaintDelegateAuthority(() => granted ? GrantedScope() : PermissionScope.Denied);
        ApplyProductPaintMembershipImportV1 import;
        await using (var scope = run.Request())
        {
            var writer = new PaintAfterAudit(scope.Services.GetRequiredService<IAuditTransactionWriterV1>(), () => granted = false);
            var service = PaintService(scope, run.CreatorActorId, authority, writer);
            import = PaintApply(await service.ValidateImportAsync(PaintFile(code)), code);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyImportAsync(import, PaintMetadata(run, "paint-revoked")));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Product.PaintMemberships'"));
        await using (var scope = run.Request()) await PaintService(scope, run.CreatorActorId).ApplyImportAsync(import, PaintMetadata(run, "paint-revoked"));
    }

    [SqlServerFact]
    public async Task A_real_paint_same_key_winner_is_replayed_after_a_paused_loser_without_rereading_changed_product_state()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        ApplyProductPaintMembershipImportV1 import;
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            import = PaintApply(await service.ValidateImportAsync(PaintFile(code)), code);
        }
        var pause = new PauseAuthority(2); // before lookup, after entering the real serializable owner transaction
        await using var loserScope = run.Request();
        var loser = PaintService(loserScope, run.CreatorActorId, pause).ApplyImportAsync(import, PaintMetadata(run, "paint-race"));
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await using var winnerScope = run.Request();
            var winner = await PaintService(winnerScope, run.CreatorActorId).ApplyImportAsync(import, PaintMetadata(run, "paint-race"));
            Assert.False(winner.IsReplay);
            await run.ExecuteAsync("UPDATE product.Products SET Lifecycle=2, Version=Version+1 WHERE Id=@id", ProductId);
        }
        finally { pause.Release(); }
        var replay = await loser.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(replay.IsReplay);
        Assert.Equal("1", replay.Version);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Product.PaintMemberships'"));
    }

    [SqlServerFact]
    public async Task Paint_Down_guard_passes_empty_and_refuses_while_even_a_terminal_attempt_would_be_lost()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductPaintMemberships().DownOperations[0]).Sql;
        await using var scope = run.Request();
        await scope.Context.Database.ExecuteSqlRawAsync(guard);
        var service = PaintService(scope, run.CreatorActorId);
        await Assert.ThrowsAsync<ProductPaintMembershipException>(() => service.SetAsync(ProductId, new("0", "1", "0", true, "SENTETIK"), PaintMetadata(run, "paint-refusal-only")));
        var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
        Assert.Equal(51975, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipAttempts"));
    }

    [SqlServerFact]
    public async Task Paint_candidates_require_the_real_initial_source_then_allow_remove_and_readd_without_another_workbook()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id=@id", ProductId);
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId); // synthetic authority, real Product and company membership rows
            var noSource = await service.SearchCandidatesAsync(new(code));
            Assert.Equal(ProductPaintMembershipCatalogOutcomeV1.NoSource, noSource.Outcome);
            Assert.Empty(noSource.Items);
            Assert.Equal("0", noSource.Version);
            var preview = await service.ValidateImportAsync(PaintFile(code));
            await service.ApplyImportAsync(PaintApply(preview, code), PaintMetadata(run, "candidate-initial-source"));
            var original = await service.SearchCandidatesAsync(new(code));
            var member = Assert.Single(original.Items);
            Assert.Equal(ProductId, member.ProductId);
            Assert.True(member.AlreadyMember);
            Assert.Equal("1", member.MembershipVersion);
            await service.SetAsync(ProductId, new(original.Version, member.ProductVersion, member.MembershipVersion, false, "SENTETIK çıkarma"), PaintMetadata(run, "candidate-remove"));
            var removed = await service.SearchCandidatesAsync(new(code));
            var candidate = Assert.Single(removed.Items);
            Assert.Equal("2", removed.Version);
            Assert.False(candidate.AlreadyMember);
            Assert.Equal("0", candidate.MembershipVersion);
            var request = new SetProductPaintMembershipV1(removed.Version, candidate.ProductVersion, candidate.MembershipVersion, true, "SENTETIK ERP ekleme");
            var restored = await service.SetAsync(candidate.ProductId, request, PaintMetadata(run, "candidate-readd"));
            Assert.Equal("3", restored.Version);
            Assert.Equal(ProductId, Assert.Single(restored.ChangedEntries).ProductId);
            var replay = await service.SetAsync(candidate.ProductId, request, PaintMetadata(run, "candidate-readd"));
            Assert.True(replay.IsReplay);
            Assert.Equal(restored.Version, replay.Version);
        }
        await using (var foreign = run.Request(TestCompanyIds.CompanyB))
        {
            var result = await PaintService(foreign, run.CreatorActorId).SearchCandidatesAsync(new(code));
            Assert.Equal(TestCompanyIds.CompanyB, result.CompanyId);
            Assert.Equal(ProductPaintMembershipCatalogOutcomeV1.NoSource, result.Outcome);
            Assert.Empty(result.Items); // A's current membership is not a B source
        }
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Product.PaintMemberships' AND Outcome=N'Succeeded'"));
    }

    [SqlServerFact]
    public async Task Paint_candidates_use_actual_active_cards_and_a_bounded_literal_code_or_name_search()
    {
        await using var run = await Run.StartAsync();
        var originalCode = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id=@id", ProductId);
        foreach (var item in new[] { (Code: "CAND-SENTETIK-001", State: 1), (Code: "CAND-SENTETIK-002", State: 1), (Code: "CAND-SENTETIK-003", State: 2), (Code: "CAND-SENTETIK-004", State: 0) })
        {
            // A real fixture Product row, with no fabricated eligibility or inferred paint classification.
            await run.ExecuteAsync("INSERT INTO product.Products (Id,ClassId,Code,CreatedAtUtc,FamilyId,GroupId,Lifecycle,Name,ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version) "
                + "SELECT @id,ClassId,'" + item.Code + "',CreatedAtUtc,FamilyId,GroupId," + item.State + ",N'SENTETIK aday',ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version FROM product.Products WHERE Id='"
                + ProductId.ToString("D") + "'", Guid.NewGuid());
        }
        await using var scope = run.Request();
        var service = PaintService(scope, run.CreatorActorId);
        await service.ApplyImportAsync(PaintApply(await service.ValidateImportAsync(PaintFile(originalCode)), originalCode), PaintMetadata(run, "candidate-window-source"));
        var result = await service.SearchCandidatesAsync(new("CAND-SENTETIK", 1));
        Assert.True(result.SearchWindowFull);
        Assert.Equal("CAND-SENTETIK-001", Assert.Single(result.Items).ProductCode);
        Assert.False(result.Items[0].AlreadyMember);
        var complete = await service.SearchCandidatesAsync(new("CAND-SENTETIK", 50));
        Assert.False(complete.SearchWindowFull);
        Assert.Equal(new[] { "CAND-SENTETIK-001", "CAND-SENTETIK-002" }, complete.Items.Select(x => x.ProductCode));
        Assert.Empty((await service.SearchCandidatesAsync(new("%"))).Items); // Contains treats wildcard text literally
        var empty = await service.SearchCandidatesAsync(new("SENTETIK-no-match"));
        Assert.Equal(ProductPaintMembershipCatalogOutcomeV1.Available, empty.Outcome);
        Assert.Equal("1", empty.Version);
        Assert.Empty(empty.Items);
    }

    [SqlServerFact]
    public async Task Paint_candidate_product_change_or_late_permission_loss_cannot_release_a_stale_result()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id=@id", ProductId);
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            await service.ApplyImportAsync(PaintApply(await service.ValidateImportAsync(PaintFile(code)), code), PaintMetadata(run, "candidate-fresh-source"));
        }
        var authority = new PaintCandidateAuthority(async call =>
        {
            if (call == 6) await run.ExecuteAsync("UPDATE product.Products SET Version=Version+1 WHERE Id=@id", ProductId);
            return GrantedScope();
        });
        await using (var scope = run.Request())
        {
            var conflict = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => PaintService(scope, run.CreatorActorId, authority).SearchCandidatesAsync(new(code)));
            Assert.Equal((409, ProductPaintMembershipCodesV1.VersionConflict), (conflict.StatusCode, conflict.Code));
        }
        await using (var scope = run.Request())
        {
            var revoked = new PaintCandidateAuthority(call => Task.FromResult(call < 5 ? GrantedScope() : PermissionScope.Denied));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PaintService(scope, run.CreatorActorId, revoked).SearchCandidatesAsync(new(code)));
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipAttempts"));
    }
    [SqlServerFact]
    public async Task Paint_manual_candidate_add_preserves_the_existing_five_thousand_membership_bound()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id=@id", ProductId);
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            await service.ApplyImportAsync(PaintApply(await service.ValidateImportAsync(PaintFile(code)), code), PaintMetadata(run, "candidate-limit-source"));
        }
        // Fixture-only actual Product and membership rows fill the already-existing technical bound.
        await run.ExecuteAsync("DECLARE @rows TABLE (Id uniqueidentifier NOT NULL, Number int NOT NULL); "
            + "INSERT @rows SELECT TOP (4999) NEWID(),CONVERT(int,ROW_NUMBER() OVER (ORDER BY a.object_id,b.object_id)) FROM sys.all_objects a CROSS JOIN sys.all_objects b; "
            + "INSERT INTO product.Products (Id,ClassId,Code,CreatedAtUtc,FamilyId,GroupId,Lifecycle,Name,ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version) "
            + "SELECT r.Id,p.ClassId,'PAINT-LIMIT-'+CONVERT(varchar(10),r.Number),p.CreatedAtUtc,p.FamilyId,p.GroupId,p.Lifecycle,p.Name,p.ProductionInputProvenance,p.ProductionInputValue,p.ProductionOutputProvenance,p.ProductionOutputValue,p.PurchasableProvenance,p.PurchasableValue,p.ReferencesJson,p.SellableProvenance,p.SellableValue,p.UpdatedAtUtc,p.Version FROM @rows r CROSS JOIN product.Products p WHERE p.Id=@id; "
            + "INSERT product.PaintMembershipLines (CompanyId,Version,ProductId,MembershipVersion,RecordedProductVersion) SELECT '" + TestCompanyIds.CompanyA.ToString("D")
            + "',1,r.Id,1,p.Version FROM @rows r JOIN product.Products p ON p.Id=r.Id;", ProductId);
        var extra = Guid.NewGuid();
        await run.ExecuteAsync("INSERT INTO product.Products (Id,ClassId,Code,CreatedAtUtc,FamilyId,GroupId,Lifecycle,Name,ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version) "
            + "SELECT @id,ClassId,'PAINT-LIMIT-EXTRA',CreatedAtUtc,FamilyId,GroupId,Lifecycle,Name,ProductionInputProvenance,ProductionInputValue,ProductionOutputProvenance,ProductionOutputValue,PurchasableProvenance,PurchasableValue,ReferencesJson,SellableProvenance,SellableValue,UpdatedAtUtc,Version FROM product.Products WHERE Id='" + ProductId.ToString("D") + "'", extra);
        await using (var scope = run.Request())
        {
            var service = PaintService(scope, run.CreatorActorId);
            var result = await service.SearchCandidatesAsync(new("PAINT-LIMIT-EXTRA"));
            var candidate = Assert.Single(result.Items);
            var refusal = await Assert.ThrowsAsync<ProductPaintMembershipException>(() => service.SetAsync(candidate.ProductId,
                new(result.Version, candidate.ProductVersion, candidate.MembershipVersion, true, "SENTETIK sınır"), PaintMetadata(run, "candidate-limit-refusal")));
            Assert.Equal((422, ProductPaintMembershipCodesV1.InvalidFile), (refusal.StatusCode, refusal.Code));
        }
        Assert.Equal(ProductPaintMembershipLimitsV1.MaximumRows, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipLines WHERE Version=1"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.PaintMembershipVersions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Product.PaintMemberships' AND Outcome=N'Succeeded'"));
    }
    private sealed class PaintCandidateAuthority(Func<int, Task<PermissionScope>> answer) : IErpAuthorityRevalidationService
    {
        private int calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken ct = default)
        {
            Assert.Equal(ProductPaintMembershipPermissionsV1.Manage, permission);
            ct.ThrowIfCancellationRequested();
            return answer(++calls);
        }
    }

    private static ProductPaintMembershipService PaintService(RequestScope scope, Guid actor,
        IErpAuthorityRevalidationService? authority = null, IAuditTransactionWriterV1? writer = null)
    {
        var user = new User(actor);
        return new(scope.Context, scope.CompanyContext, user, authority ?? new Granted(), ScopeAudit(scope),
            writer ?? scope.Services.GetRequiredService<IAuditTransactionWriterV1>(), scope.Services.GetRequiredService<IRefusedAttemptRecorderV1>(), TimeProvider.System,
            new ProductPaintMembershipReader(scope.Context, scope.CompanyContext, user));
    }
    private static ProductPaintMembershipCommandMetadataV1 PaintMetadata(Run run, string key) => new(TestCompanyIds.CompanyA, run.CreatorActorId.ToString("D"), "corr-" + key, null, key);
    private static ValidateProductPaintMembershipImportV1 PaintFile(params string[] codes) => new("boya.xlsx", Convert.ToBase64String(PaintWorkbook(codes)), null);
    private static ApplyProductPaintMembershipImportV1 PaintApply(ProductPaintMembershipImportPreviewV1 preview, params string[] codes) =>
        new(preview.Version, "boya.xlsx", Convert.ToBase64String(PaintWorkbook(codes)), preview.ProductCodeColumn, preview.FileSha256, preview.PreviewToken, "SENTETIK Excel", "SENTETIK gerekçe");
    private static byte[] PaintWorkbook(params string[] codes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            PaintEntry(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            PaintEntry(archive, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Boya\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            PaintEntry(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            PaintEntry(archive, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
                + "<row r=\"1\">" + PackageText("A1", ProductPaintMembershipLimitsV1.DefaultProductCodeColumn) + "</row>"
                + string.Concat(codes.Select((code, i) => "<row r=\"" + (i + 2) + "\">" + PackageText("A" + (i + 2), code) + "</row>")) + "</sheetData></worksheet>");
        }
        return stream.ToArray();
    }
    private static void PaintEntry(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }
    private sealed class PaintInjectedAuditFailure : Exception { }
    private sealed class PaintFailAfterAudit(IAuditTransactionWriterV1 inner) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken ct = default) { await inner.WritePendingAsync(transaction, ct); throw new PaintInjectedAuditFailure(); }
        public void DiscardPending() => inner.DiscardPending();
    }
    private sealed class PaintAfterAudit(IAuditTransactionWriterV1 inner, Action after) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken ct = default) { await inner.WritePendingAsync(transaction, ct); after(); }
        public void DiscardPending() => inner.DiscardPending();
    }
    private sealed class PaintDelegateAuthority(Func<PermissionScope> answer) : IErpAuthorityRevalidationService
    { public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken ct = default) => Task.FromResult(answer()); }
}
