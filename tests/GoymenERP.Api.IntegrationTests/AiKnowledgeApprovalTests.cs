using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Knowledge;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A4 knowledge sources: the human approval of a packaged knowledge version (<c>ai.knowledge.approve</c>, registered and
/// granted to no role), the packaged decision index and release behaviour knowledge, and the two READ tools that stay closed
/// until the exact packaged bytes are approved (AI-D32). SENTETIK data only; in-memory approval store, test doubles for the
/// user, authority, audit and correlation. No model, network or SQL.
/// </summary>
public sealed class AiKnowledgeApprovalTests
{
    private static readonly Guid Company = Guid.Parse("c0000000-0000-4000-8000-0000000000a4");
    private static readonly Guid OtherCompany = Guid.Parse("c0000000-0000-4000-8000-0000000000a5");
    private static readonly Guid Approver = Guid.Parse("a0000000-0000-4000-8000-0000000000a4");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly string DecisionSha = new('a', 64);
    private static readonly string ReleaseSha = new('b', 64);

    // ---- Permission registration ---------------------------------------------------------------------------------------

    [Fact]
    public void The_approve_permission_is_registered_company_wide_not_critical_and_spelled_as_the_module_manifest()
    {
        Assert.Equal("ai.knowledge.approve", ErpPermissions.AiKnowledgeApprove);
        Assert.Equal(ErpPermissions.AiKnowledgeApprove, AiKnowledgePermissionManifestV1.Approve);
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.AiKnowledgeApprove));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ErpPermissions.AiKnowledgeApprove));
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.AiKnowledgeApprove));
        Assert.All(AiKnowledgePermissionManifestV1.Names, name => Assert.True(ErpPermissions.IsKnown(name)));
    }

    [Fact]
    public void No_seeded_role_carries_the_approve_permission()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src/backend/GoymenERP.Api/Seed/identity-seed.json"));
        Assert.DoesNotContain(ErpPermissions.AiKnowledgeApprove, seed, StringComparison.Ordinal);
    }

    // ---- Domain --------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_approval_starts_at_version_one_and_is_withdrawn_once_by_one_version_step_with_a_reason()
    {
        var approval = AiKnowledgeApprovalV1.Approve(Approver);
        Assert.Equal((AiKnowledgeApprovalStatusV1.Approved, 1L), (approval.Status, approval.Version));

        Assert.Equal(AiKnowledgeProblemCodesV1.VersionConflict,
            Assert.Throws<AiKnowledgeRuleException>(() => approval.Withdraw(2, Approver, "neden", Now)).Code);
        Assert.Throws<ArgumentException>(() => approval.Withdraw(1, Approver, " ", Now));
        approval.Withdraw(1, Approver, "Dizin bayatladı.", Now);
        Assert.Equal((AiKnowledgeApprovalStatusV1.Withdrawn, 2L, Approver), (approval.Status, approval.Version, approval.WithdrawnByUserId));
        Assert.Equal(AiKnowledgeProblemCodesV1.ApprovalNotActive,
            Assert.Throws<AiKnowledgeRuleException>(() => approval.Withdraw(2, Approver, "tekrar", Now)).Code);
        Assert.Throws<ArgumentException>(() => AiKnowledgeApprovalV1.Approve(Guid.Empty));
    }

    [Theory]
    [InlineData("decision-index", AiKnowledgeSourceKindV1.DecisionIndex)]
    [InlineData("release-knowledge", AiKnowledgeSourceKindV1.ReleaseKnowledge)]
    public void Source_kinds_round_trip_and_unknown_kinds_are_refused(string wire, AiKnowledgeSourceKindV1 kind)
    {
        Assert.True(AiKnowledgeSourceKindsV1.TryParse(wire, out var parsed));
        Assert.Equal(kind, parsed);
        Assert.Equal(wire, AiKnowledgeSourceKindsV1.Wire(kind));
        Assert.False(AiKnowledgeSourceKindsV1.TryParse("Decision-Index", out _));
        Assert.False(AiKnowledgeSourceKindsV1.TryParse(null, out _));
    }

    // ---- Service -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_the_packaged_version_is_one_save_with_attempt_and_audit_and_the_same_key_replays_it()
    {
        var harness = new Harness();
        var first = await harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("k1"));

        Assert.False(first.IsReplay);
        Assert.Equal(("decision-index", DecisionSha, "onaylandi", "1", Approver),
            (first.Approval.SourceKind, first.Approval.PackageSha256, first.Approval.Status, first.Approval.Version, first.Approval.ApprovedByUserId));
        var replay = await harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("k1", "corr-other"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("corr-k1", first.Approval.Id), (replay.OriginalCorrelationId, replay.Approval.Id));
        await using var database = harness.Database();
        Assert.Single(await database.Approvals.ToListAsync());
        Assert.Single(await database.Attempts.ToListAsync());
        var audit = Assert.Single(harness.Audit.Events);
        Assert.Equal(("ai-knowledge.package.approved.v1", ErpPermissions.AiKnowledgeApprove, "k1", Company),
            (audit.EventType, audit.PermissionCode, audit.IdempotencyKey, audit.CompanyId));
        Assert.Equal(AiKnowledgeApprovalStateV1.Approved,
            await new AiKnowledgeApprovalReaderV1(harness.Database()).GetStateAsync(Company, AiKnowledgeSourceKindV1.DecisionIndex, DecisionSha));
    }

    [Fact]
    public async Task Approval_needs_the_company_wide_permission_a_user_and_a_reachable_company()
    {
        var harness = new Harness();
        Assert.Equal(403, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(null)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("a")))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove, siteScoped: true)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("b")))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Anonymous(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("c")))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove, allowedCompany: OtherCompany)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("d")))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiDecisionsRead)
            .ListPackagesAsync())).Status);
        await using var database = harness.Database();
        Assert.Empty(await database.Approvals.ToListAsync());
        Assert.Empty(harness.Audit.Events);
    }

    [Fact]
    public async Task A_different_or_missing_package_version_is_never_approved()
    {
        var harness = new Harness();
        var changed = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", new string('c', 64)), Meta("a")));
        Assert.Equal((422, AiKnowledgeProblemCodesV1.PackageChanged), (changed.Status, changed.Code));
        var malformed = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", DecisionSha.ToUpperInvariant()), Meta("b")));
        Assert.Equal((422, AiKnowledgeProblemCodesV1.ValidationFailed), (malformed.Status, malformed.Code));
        var unknownKind = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("formulas", DecisionSha), Meta("c")));
        Assert.Equal(422, unknownKind.Status);
        harness.Catalog.ReleaseSha = null;
        var unavailable = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("release-knowledge", ReleaseSha), Meta("d")));
        Assert.Equal(503, unavailable.Status);
        await using var database = harness.Database();
        Assert.Empty(await database.Approvals.ToListAsync());
        Assert.Empty(await database.Attempts.ToListAsync());
    }

    [Fact]
    public async Task A_second_approval_of_the_same_version_is_a_recorded_refusal_that_replays_with_its_key()
    {
        var harness = new Harness();
        await harness.Service(ErpPermissions.AiKnowledgeApprove).ApproveAsync(new("decision-index", DecisionSha), Meta("k1"));

        var refused = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("k2")));
        Assert.Equal((409, AiKnowledgeProblemCodesV1.AlreadyApproved, false), (refused.Status, refused.Code, refused.IsReplay));
        var again = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("decision-index", DecisionSha), Meta("k2", "corr-x")));
        Assert.Equal((409, AiKnowledgeProblemCodesV1.AlreadyApproved, true, "corr-k2"),
            (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        var mismatch = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .ApproveAsync(new("release-knowledge", ReleaseSha), Meta("k2")));
        Assert.Equal((409, AiKnowledgeProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
        await using var database = harness.Database();
        Assert.Single(await database.Approvals.ToListAsync());
        Assert.Equal(2, (await database.Attempts.ToListAsync()).Count);
    }

    [Fact]
    public async Task Withdrawing_closes_the_source_keeps_the_record_and_a_stale_version_is_a_recorded_conflict()
    {
        var harness = new Harness();
        var approved = await harness.Service(ErpPermissions.AiKnowledgeApprove).ApproveAsync(new("decision-index", DecisionSha), Meta("k1"));

        var stale = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .WithdrawAsync(approved.Approval.Id, new("2", "Bayat."), Meta("w0")));
        Assert.Equal((409, AiKnowledgeProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        var withdrawn = await harness.Service(ErpPermissions.AiKnowledgeApprove)
            .WithdrawAsync(approved.Approval.Id, new("1", "Yeni karar dosyası geldi."), Meta("w1"));
        Assert.Equal(("geri_alindi", "2", Approver, "Yeni karar dosyası geldi."),
            (withdrawn.Approval.Status, withdrawn.Approval.Version, withdrawn.Approval.WithdrawnByUserId, withdrawn.Approval.WithdrawReason));
        Assert.Equal(AiKnowledgeApprovalStateV1.NotApproved,
            await new AiKnowledgeApprovalReaderV1(harness.Database()).GetStateAsync(Company, AiKnowledgeSourceKindV1.DecisionIndex, DecisionSha));
        var notActive = await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .WithdrawAsync(approved.Approval.Id, new("2", "Tekrar."), Meta("w2")));
        Assert.Equal((409, AiKnowledgeProblemCodesV1.ApprovalNotActive), (notActive.Status, notActive.Code));
        Assert.Equal(400, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .WithdrawAsync(approved.Approval.Id, new("01", "x"), Meta("w3")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AiKnowledgeException>(() => harness.Service(ErpPermissions.AiKnowledgeApprove)
            .WithdrawAsync(Guid.NewGuid(), new("1", "x"), Meta("w4")))).Status);

        // A new approval of the same bytes is a new record; the withdrawn one stays as history.
        var renewed = await harness.Service(ErpPermissions.AiKnowledgeApprove).ApproveAsync(new("decision-index", DecisionSha), Meta("k3"));
        Assert.NotEqual(approved.Approval.Id, renewed.Approval.Id);
        await using var database = harness.Database();
        Assert.Equal(2, (await database.Approvals.ToListAsync()).Count);
        Assert.Contains(harness.Audit.Events, item => item.EventType == "ai-knowledge.approval.withdrawn.v1" && item.Reason == "Yeni karar dosyası geldi.");
    }

    [Fact]
    public async Task The_package_list_shows_what_is_packaged_and_only_the_active_approval_of_that_version()
    {
        var harness = new Harness();
        await harness.Service(ErpPermissions.AiKnowledgeApprove).ApproveAsync(new("release-knowledge", ReleaseSha), Meta("k1"));
        harness.Catalog.DecisionSha = null;

        var page = await harness.Service(ErpPermissions.AiKnowledgeApprove).ListPackagesAsync();

        Assert.Equal(Company, page.CompanyId);
        var decision = Assert.Single(page.Packages, item => item.SourceKind == "decision-index");
        Assert.False(decision.Packaged);
        Assert.Null(decision.ActiveApproval);
        var release = Assert.Single(page.Packages, item => item.SourceKind == "release-knowledge");
        Assert.Equal((true, ReleaseSha, 2), (release.Packaged, release.PackageSha256, release.ItemCount));
        Assert.Equal("onaylandi", release.ActiveApproval!.Status);

        // Another release ships other bytes: the old approval no longer counts for the packaged version.
        harness.Catalog.ReleaseSha = new string('d', 64);
        var after = await harness.Service(ErpPermissions.AiKnowledgeApprove).ListPackagesAsync();
        Assert.Null(Assert.Single(after.Packages, item => item.SourceKind == "release-knowledge").ActiveApproval);
    }

    [Fact]
    public async Task Approvals_are_company_scoped_in_the_reader_and_the_store()
    {
        var harness = new Harness();
        await harness.Service(ErpPermissions.AiKnowledgeApprove).ApproveAsync(new("decision-index", DecisionSha), Meta("k1"));

        var other = new CompanyContext();
        other.Set(OtherCompany);
        await using var otherDatabase = new AiKnowledgeDbContext(harness.Options(), other);
        Assert.Empty(await otherDatabase.Approvals.ToListAsync());
        Assert.Equal(AiKnowledgeApprovalStateV1.NotApproved,
            await new AiKnowledgeApprovalReaderV1(otherDatabase).GetStateAsync(OtherCompany, AiKnowledgeSourceKindV1.DecisionIndex, DecisionSha));
        Assert.Equal(AiKnowledgeApprovalStateV1.NotApproved,
            await new AiKnowledgeApprovalReaderV1(harness.Database()).GetStateAsync(Guid.Empty, AiKnowledgeSourceKindV1.DecisionIndex, DecisionSha));
    }

    [Fact]
    public async Task The_store_refuses_deletes_edits_of_a_package_and_rows_of_another_company()
    {
        var harness = new Harness();
        await harness.Service(ErpPermissions.AiKnowledgeApprove).ApproveAsync(new("decision-index", DecisionSha), Meta("k1"));

        await using (var database = harness.Database())
        {
            database.Approvals.Remove(await database.Approvals.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }

        await using (var database = harness.Database())
        {
            var row = await database.Approvals.SingleAsync();
            row.PackageSha256 = new string('e', 64);
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }

        await using (var database = harness.Database())
        {
            database.Attempts.Add(new AiKnowledgeCommandAttemptRow
            {
                CompanyId = OtherCompany, ActorUserId = Approver, IdempotencyKeySha256 = new string('f', 64), Command = "x",
                RequestSha256 = new string('f', 64), TargetId = Guid.NewGuid(), Status = 201, CorrelationId = "c", RecordedAtUtc = Now,
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        }
    }

    [Fact]
    public void The_migration_Down_starts_with_the_52290_forward_fix_guard()
    {
        var migration = new InitialAiKnowledgeApprovals();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 52290", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[aiknowledge].[PackageApprovals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[aiknowledge].[CommandAttempts]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
    }

    [Fact]
    public void The_model_carries_the_checks_the_active_index_and_the_company_filter()
    {
        using var database = new AiKnowledgeDbContext(
            new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseSqlServer("Server=unused;Database=unused").Options, new CompanyContext());
        var model = database.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var approval = model.FindEntityType(typeof(AiKnowledgeApprovalRow))!;
        Assert.Equal(AiKnowledgeDbContext.Schema, approval.GetSchema());
        Assert.Equal(
            new[] { AiKnowledgeDbContext.ApprovalIdentityCheck, AiKnowledgeDbContext.ApprovalStateCheck }.Order(StringComparer.Ordinal),
            approval.GetCheckConstraints().Select(check => check.Sql).Order(StringComparer.Ordinal));
        var active = Assert.Single(approval.GetIndexes(), index => index.GetDatabaseName() == AiKnowledgeDbContext.ActiveApprovalIndexName);
        Assert.True(active.IsUnique);
        Assert.Equal("[Status] = 1", active.GetFilter());
        Assert.NotNull(approval.GetDeclaredQueryFilters().SingleOrDefault());
        var attempt = model.FindEntityType(typeof(AiKnowledgeCommandAttemptRow))!;
        Assert.Equal(AiKnowledgeDbContext.AttemptCheck, Assert.Single(attempt.GetCheckConstraints()).Sql);
        Assert.NotNull(attempt.GetDeclaredQueryFilters().SingleOrDefault());
    }

    // ---- Packaging -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_api_project_packages_both_knowledge_files_into_build_and_publish_output()
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src/backend/GoymenERP.Api/GoymenERP.Api.csproj"));
        var content = project.Descendants().Where(element => element.Name.LocalName == "Content").ToArray();
        foreach (var file in new[] { AiKnowledgePackageFiles.DecisionIndexFileName, AiKnowledgePackageFiles.ReleaseKnowledgeFileName })
        {
            var item = Assert.Single(content, element => element.Attribute("Include")!.Value == $"..\\..\\..\\contracts\\ai\\{file}");
            Assert.Equal("PreserveNewest", item.Attribute("CopyToOutputDirectory")?.Value);
            Assert.Equal("PreserveNewest", item.Attribute("CopyToPublishDirectory")?.Value);
            // The copied bytes are the committed contract bytes.
            Assert.Equal(File.ReadAllBytes(Path.Combine(FindRepositoryRoot(), "contracts/ai", file)),
                File.ReadAllBytes(AiKnowledgePackageFiles.PathOf(file)));
        }

        Assert.DoesNotContain("DecisionIndexPath", File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src/backend/GoymenERP.Api/Ai/Knowledge/AiDecisionIndexSource.cs")).Replace("Ai:Knowledge:DecisionIndexPath</c> is gone", ""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_packaged_release_knowledge_loads_with_its_byte_digest_and_never_carries_an_unimplemented_note()
    {
        var path = AiKnowledgePackageFiles.PathOf(AiKnowledgePackageFiles.ReleaseKnowledgeFileName);
        var package = new PackagedAiReleaseKnowledgeSource().Current;

        Assert.NotNull(package);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), package.PackageSha256);
        Assert.NotEmpty(package.Notes);
        Assert.Null(package.Find("RK-AI-KAYNAK-ONAY-EKRANI"));
        Assert.True(package.ExcludedCount >= 1);
        var catalog = new PackagedAiKnowledgeCatalog(new FixedAiDecisionIndexSource(null), new FixedAiReleaseKnowledgeSource(package));
        Assert.Equal(package.PackageSha256, catalog.Find(AiKnowledgeSourceKindV1.ReleaseKnowledge)!.PackageSha256);
        Assert.Null(catalog.Find(AiKnowledgeSourceKindV1.DecisionIndex));
    }

    [Fact]
    public void A_decision_index_without_the_generation_mask_policy_or_with_a_name_list_is_refused()
    {
        var masked = DecisionIndexJson(maskPolicy: AiDecisionIndex.MaskPolicy, withNameList: false);
        Assert.NotNull(AiDecisionIndex.Parse(Encoding.UTF8.GetBytes(masked)));
        foreach (var json in new[]
                 {
                     DecisionIndexJson(maskPolicy: null, withNameList: false),
                     DecisionIndexJson(maskPolicy: AiDecisionIndex.MaskPolicy, withNameList: true),
                 })
        {
            var path = WriteTemp(json);
            Assert.Null(AiDecisionIndex.TryLoad(path));
        }
    }

    [Fact]
    public void A_missing_or_broken_package_file_gives_no_source()
    {
        Assert.Null(new PackagedAiDecisionIndexSource(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")).Current);
        Assert.Null(new PackagedAiReleaseKnowledgeSource(WriteTemp("{\"format\":\"goymen.ai.release-knowledge.v1\"}")).Current);
        Assert.Null(AiReleaseKnowledge.TryLoad(WriteTemp("not json")));
    }

    // ---- Tools ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_decision_tool_is_closed_until_the_exact_packaged_index_is_approved_for_the_company()
    {
        var index = AiDecisionIndex.Parse(Encoding.UTF8.GetBytes(DecisionIndexJson(AiDecisionIndex.MaskPolicy, false)));
        var approvals = new FixedApprovals();
        var tool = new DecisionLookupAiTool(new FixedAiDecisionIndexSource(index), approvals, new FixedTime(Now));
        var input = new AiToolInputV1("{\"decisionId\":\"TST-D1\"}");

        Assert.Equal(AiToolOutcomeV1.Unavailable, (await tool.ReadAsync(input, Context(Company))).Outcome);
        approvals.Approve(Company, AiKnowledgeSourceKindV1.DecisionIndex, new string('0', 64));
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await tool.ReadAsync(input, Context(Company))).Outcome);
        approvals.Approve(Company, AiKnowledgeSourceKindV1.DecisionIndex, index.PackageSha256);
        var approved = await tool.ReadAsync(input, Context(Company));
        Assert.Equal(AiToolOutcomeV1.Succeeded, approved.Outcome);
        Assert.Equal(index.PackageSha256, approved.Source!.SourceVersion);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await tool.ReadAsync(input, Context(OtherCompany))).Outcome);
        approvals.Unavailable = true;
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await tool.ReadAsync(input, Context(Company))).Outcome);
    }

    [Fact]
    public async Task The_decision_tool_withholds_local_only_records_and_returns_masked_text()
    {
        var index = AiDecisionIndex.Parse(Encoding.UTF8.GetBytes(DecisionIndexJson(AiDecisionIndex.MaskPolicy, false)));
        var approvals = new FixedApprovals();
        approvals.Approve(Company, AiKnowledgeSourceKindV1.DecisionIndex, index.PackageSha256);
        var tool = new DecisionLookupAiTool(new FixedAiDecisionIndexSource(index), approvals, new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{\"keyword\":\"kural\"}"), Context(Company));

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.Equal(1, content.RootElement.GetProperty("withheldLocalOnlyCount").GetInt32());
        var records = content.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.DoesNotContain(records, record => record.GetProperty("id").GetString() == "TST-D3");
        var masked = Assert.Single(records, record => record.GetProperty("id").GetString() == "TST-D2");
        Assert.Equal("[KİŞİ] kuralı onaylar; iletişim [GİZLİ].", masked.GetProperty("text").GetString());
        Assert.True(masked.GetProperty("masked").GetBoolean());
        Assert.Equal("maskeli_bulut_uygun_aday", masked.GetProperty("cloudEligibility").GetString());
        Assert.Equal(AiDataClassV1.Internal, result.Source!.DataClass);
    }

    [Fact]
    public async Task The_release_tool_releases_a_note_only_to_holders_of_its_permissions_and_only_when_approved()
    {
        var package = AiReleaseKnowledge.Parse(Encoding.UTF8.GetBytes(ReleaseKnowledgeJson()));
        var approvals = new FixedApprovals();
        var authority = new GrantingAuthority();
        var tool = new ReleaseKnowledgeLookupAiTool(new FixedAiReleaseKnowledgeSource(package), approvals, authority, new FixedTime(Now));
        var byId = new AiToolInputV1("{\"noteId\":\"RK-TST-NOT\"}");

        Assert.Equal(AiToolOutcomeV1.Unavailable, (await tool.ReadAsync(byId, Context(Company))).Outcome);
        approvals.Approve(Company, AiKnowledgeSourceKindV1.ReleaseKnowledge, package.PackageSha256);
        // Without work.notes.write the note is not there for this actor (no existence leak).
        Assert.Equal(AiToolOutcomeV1.NotFound, (await tool.ReadAsync(byId, Context(Company))).Outcome);
        var keyword = await tool.ReadAsync(new AiToolInputV1("{\"keyword\":\"özellik\"}"), Context(Company));
        using (var content = JsonDocument.Parse(keyword.ContentJson!))
        {
            Assert.Equal(new[] { "RK-TST-ACIK" },
                content.RootElement.GetProperty("notes").EnumerateArray().Select(note => note.GetProperty("id").GetString()).ToArray());
            Assert.Equal(1, content.RootElement.GetProperty("matchCount").GetInt32());
            Assert.Equal(1, content.RootElement.GetProperty("withheldLocalOnlyCount").GetInt32());
        }

        authority.Grant(ErpPermissions.WorkNotesWrite);
        var granted = await tool.ReadAsync(byId, Context(Company));
        Assert.Equal(AiToolOutcomeV1.Succeeded, granted.Outcome);
        Assert.Equal((ReleaseKnowledgeLookupAiTool.ToolId, package.PackageSha256, ReleaseKnowledgeLookupAiTool.TargetKind),
            (granted.Source!.ToolId, granted.Source.SourceVersion, granted.Source.TargetKind));
        using var noteContent = JsonDocument.Parse(granted.ContentJson!);
        var note = Assert.Single(noteContent.RootElement.GetProperty("notes").EnumerateArray());
        Assert.Equal("docs/release-knowledge/RK-TST-NOT.md", note.GetProperty("source").GetProperty("path").GetString());
        Assert.Contains(ErpPermissions.WorkNotesWrite, authority.Asked);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"noteId\":\"RK-TST-NOT\",\"keyword\":\"not\"}")]
    [InlineData("{\"noteId\":\"rk-tst-not\"}")]
    [InlineData("{\"keyword\":\"x\"}")]
    [InlineData("{\"keyword\":\"not\",\"limit\":0}")]
    [InlineData("{\"keyword\":\"not\",\"companyId\":\"c0000000-0000-4000-8000-0000000000a4\"}")]
    [InlineData("[]")]
    public async Task Malformed_or_unscoped_release_lookup_arguments_are_invalid_and_release_nothing(string arguments)
    {
        var package = AiReleaseKnowledge.Parse(Encoding.UTF8.GetBytes(ReleaseKnowledgeJson()));
        var approvals = new FixedApprovals();
        approvals.Approve(Company, AiKnowledgeSourceKindV1.ReleaseKnowledge, package.PackageSha256);
        var tool = new ReleaseKnowledgeLookupAiTool(new FixedAiReleaseKnowledgeSource(package), approvals, new GrantingAuthority(), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1(arguments), Context(Company));

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Null(result.Source);
    }

    [Fact]
    public void The_tool_catalog_contract_lists_the_release_knowledge_tool_as_its_descriptor_says()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "contracts/ai/tool-catalog-v1.json")));
        var descriptor = new ReleaseKnowledgeLookupAiTool(new FixedAiReleaseKnowledgeSource(null), new FixedApprovals(),
            new GrantingAuthority(), new FixedTime(Now)).Descriptor;
        var entry = Assert.Single(catalog.RootElement.GetProperty("tools").EnumerateArray(),
            e => e.GetProperty("toolId").GetString() == descriptor.ToolId);
        Assert.Equal(descriptor.Version, entry.GetProperty("version").GetString());
        Assert.Equal(descriptor.OwnerModule, entry.GetProperty("ownerModule").GetString());
        Assert.Equal(descriptor.Risk.ToString(), entry.GetProperty("risk").GetString());
        Assert.Equal(descriptor.DataClass.ToString(), entry.GetProperty("dataClass").GetString());
        Assert.Equal(descriptor.RequiredPermissions,
            entry.GetProperty("requiredPermissions").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(descriptor.FieldAllowlist,
            entry.GetProperty("fieldAllowlist").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_runtime_registers_both_knowledge_tools_closed_by_default_without_the_approval_module()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Enabled"] = "false" }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Development"));
        services.AddScoped<IErpAuthorityRevalidationService, GrantingAuthority>();
        services.AddAiRuntime(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var tools = scope.ServiceProvider.GetServices<IAiOwnerToolV1>().Select(tool => tool.Descriptor.ToolId).ToArray();
        Assert.Contains(DecisionLookupAiTool.ToolId, tools);
        Assert.Contains(ReleaseKnowledgeLookupAiTool.ToolId, tools);
        Assert.IsType<UnconfiguredAiKnowledgeApprovalReader>(scope.ServiceProvider.GetRequiredService<IAiKnowledgeApprovalReaderV1>());
        Assert.IsType<PackagedAiKnowledgeCatalog>(scope.ServiceProvider.GetRequiredService<IAiKnowledgePackageCatalogV1>());
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AiToolCatalog>().Descriptors);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------------

    private static AiKnowledgeCommandMetadataV1 Meta(string key, string? correlationId = null) =>
        new(key, correlationId ?? "corr-" + key, null);

    private static AiServerContextV1 Context(Guid company) => new(company, Approver.ToString("D"), "v1.session-sentetik",
        $"company:{company:D}", "roles:sentetik", "corr-ai-wpa4-sentetik", null, null, null);

    private static string WriteTemp(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ai-knowledge-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        return path;
    }

    private static string DecisionIndexJson(string? maskPolicy, bool withNameList)
    {
        var records = new[]
        {
            Record("TST-D1", "Bu kural yayındadır.", false, "bulut_uygun_aday", "internal"),
            Record("TST-D2", "[KİŞİ] kuralı onaylar; iletişim [GİZLİ].", true, "maskeli_bulut_uygun_aday", "internal"),
            Record("TST-D3", "Kredi kuralı 25.000 TL üzerindedir.", false, "yerel_kalir", "confidential"),
        };
        var root = new Dictionary<string, object?>
        {
            ["format"] = AiDecisionIndex.Format,
            ["source_set_sha256"] = new string('9', 64),
            ["records"] = records,
            ["conflicts"] = Array.Empty<object>(),
        };
        if (maskPolicy is not null) root["mask_policy"] = maskPolicy;
        if (withNameList) root["personal_name_terms"] = new[] { "SENTETIK Kisi" };
        return JsonSerializer.Serialize(root);
    }

    private static Dictionary<string, object?> Record(string id, string text, bool masked, string cloud, string dataClass) => new()
    {
        ["id"] = id, ["kind"] = "sinan_karari", ["title"] = id, ["text"] = text, ["status"] = "kilitli",
        ["effective_date"] = "2026-11-22", ["source"] = new Dictionary<string, object?> { ["path"] = "docs/decisions/SINAN_KARARLARI_TEST.md", ["line"] = 3 },
        ["scope"] = new Dictionary<string, object?> { ["area"] = null, ["affected"] = null }, ["superseded_by"] = Array.Empty<object>(),
        ["data_class_candidate"] = masked ? "kisisel_veri_adayi" : "kisisel_veri_isareti_yok", ["masked"] = masked,
        ["cloud_eligibility"] = cloud, ["cloud_data_class"] = dataClass,
    };

    private static string ReleaseKnowledgeJson() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["format"] = AiReleaseKnowledge.Format,
        ["mask_policy"] = AiDecisionIndex.MaskPolicy,
        ["source_set_sha256"] = new string('8', 64),
        ["notes"] = new[]
        {
            Note("RK-TST-NOT", "Not yazma özelliği.", [ErpPermissions.WorkNotesWrite], "bulut_uygun_aday", "internal"),
            Note("RK-TST-ACIK", "Herkese açık özellik.", [], "bulut_uygun_aday", "internal"),
            Note("RK-TST-TUTAR", "Tutar sınırlı özellik 25.000 TL.", [], "yerel_kalir", "confidential"),
        },
        ["excluded"] = new[] { new Dictionary<string, object?> { ["id"] = "RK-TST-PLAN", ["reason"] = "uygulanmadi" } },
    });

    private static Dictionary<string, object?> Note(string id, string text, string[] permissions, string cloud, string dataClass) => new()
    {
        ["id"] = id, ["title"] = id, ["text"] = text, ["required_permissions"] = permissions,
        ["source"] = new Dictionary<string, object?> { ["path"] = $"docs/release-knowledge/{id}.md", ["line"] = 8 },
        ["masked"] = false, ["cloud_eligibility"] = cloud, ["cloud_data_class"] = dataClass,
    };

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class Harness
    {
        private readonly string name = Guid.NewGuid().ToString("N");
        public CollectingAudit Audit { get; } = new();
        public FakeCatalog Catalog { get; } = new();

        public DbContextOptions<AiKnowledgeDbContext> Options() =>
            new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseInMemoryDatabase(name).Options;

        public AiKnowledgeDbContext Database()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new AiKnowledgeDbContext(Options(), company);
        }

        public AiKnowledgeApprovalServiceV1 Service(string? permission, bool siteScoped = false, Guid? allowedCompany = null) =>
            Build(Approver, permission, siteScoped, allowedCompany ?? Company);

        public AiKnowledgeApprovalServiceV1 Anonymous(string? permission) => Build(null, permission, false, Company);

        private AiKnowledgeApprovalServiceV1 Build(Guid? actor, string? permission, bool siteScoped, Guid allowedCompany)
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new AiKnowledgeApprovalServiceV1(new AiKnowledgeDbContext(Options(), company), company,
                new FakeUser(actor, allowedCompany), new FakeAuthority(permission, siteScoped), Catalog, Audit,
                new FakeCorrelation(), new FixedTime(Now));
        }
    }

    private sealed class FakeCatalog : IAiKnowledgePackageCatalogV1
    {
        public string? DecisionSha { get; set; } = AiKnowledgeApprovalTests.DecisionSha;
        public string? ReleaseSha { get; set; } = AiKnowledgeApprovalTests.ReleaseSha;

        public AiKnowledgePackageInfoV1? Find(AiKnowledgeSourceKindV1 kind) => kind switch
        {
            AiKnowledgeSourceKindV1.DecisionIndex when DecisionSha is { } sha => new(kind, sha, new string('1', 64), 3, 0, 1),
            AiKnowledgeSourceKindV1.ReleaseKnowledge when ReleaseSha is { } sha => new(kind, sha, new string('2', 64), 2, 1, 0),
            _ => null,
        };
    }

    private sealed class FixedApprovals : IAiKnowledgeApprovalReaderV1
    {
        private readonly HashSet<(Guid, AiKnowledgeSourceKindV1, string)> approved = [];
        public bool Unavailable { get; set; }

        public void Approve(Guid company, AiKnowledgeSourceKindV1 kind, string sha) => approved.Add((company, kind, sha));

        public Task<AiKnowledgeApprovalStateV1> GetStateAsync(Guid companyId, AiKnowledgeSourceKindV1 kind, string packageSha256,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable ? AiKnowledgeApprovalStateV1.Unavailable
                : approved.Contains((companyId, kind, packageSha256)) ? AiKnowledgeApprovalStateV1.Approved : AiKnowledgeApprovalStateV1.NotApproved);
    }

    private sealed class GrantingAuthority : IErpAuthorityRevalidationService
    {
        private readonly HashSet<string> granted = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];

        public void Grant(string permission) => granted.Add(permission);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted.Contains(permission) && companyId == Company
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class FakeAuthority(string? granted, bool siteScoped) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == granted && companyId == Company
                ? new PermissionScope(true, !siteScoped, new HashSet<string>(["SENTETIK_ROLE"]),
                    siteScoped ? new HashSet<Guid>([Guid.NewGuid()]) : new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FakeUser(Guid? actor, Guid allowed) : ICurrentUser
    {
        public bool IsAuthenticated => actor is not null;
        public string? UserId => actor?.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([allowed]);
        public bool CanAccessCompany(Guid companyId) => companyId == allowed;
    }

    private sealed class FakeCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-request";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }

    private sealed class CollectingAudit : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft auditEvent) => Events.Add(auditEvent);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
