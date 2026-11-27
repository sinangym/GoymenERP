using System.Globalization;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingApprovedSourceCatalogDecisionWorkSourceTests
{
    private static readonly Guid PackageId = Guid.Parse("51c00000-0000-4000-8000-000000000001");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData("Prepared", 1L, 0L)] [InlineData("Active", 2L, 1L)] [InlineData("Superseded", 3L, 2L)]
    [InlineData("Prepared", long.MaxValue, long.MaxValue)]
    public async Task Purpose_current_states_use_native_package_and_two_lossless_versions_without_maker_exclusion(string status, long version, long activeVersion)
    {
        await using var kit = new Kit(); var authority = new Kit.FixedAuthority(true, true); var fence = kit.Fence(authority: authority);
        var owner = new Owner { Detail = Detail(status, version, activeVersion, Kit.Caller) };
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), PackageId, default))!;
        Assert.Equal(status == "Prepared", snapshot.Pending); Assert.Equal(version.ToString(CultureInfo.InvariantCulture), snapshot.SourceVersion);
        var proof = Assert.IsType<WorkApprovedSourceCatalogProofV1>(snapshot.ApprovedSourceCatalogProof);
        Assert.Equal(version, proof.PackageVersion); Assert.Equal(activeVersion, proof.ActiveCatalogVersion);
        Assert.Equal(Kit.CallerText, snapshot.Item.PreparedBy); Assert.False(snapshot.Item.Excludes(Kit.CallerText)); Assert.Null(snapshot.Item.ExcludedDeciders);
        Assert.Equal("/onaylarim/accounting.approved-source-catalog.v1/" + PackageId.ToString("D"), snapshot.Item.Link);
        var reference = Assert.Single(snapshot.Item.References); Assert.Equal(PackageId, reference.RecordId); Assert.Null(reference.Link);
        Assert.Equal("approved-source-catalog-package", reference.Role); Assert.Null(snapshot.Item.Scope);
        Assert.True(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Detail));
        Assert.True(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof));
        Assert.All(authority.Asked, asked => Assert.Equal(ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, asked.Permission));
        var wire = JsonSerializer.Serialize(snapshot, Json); Assert.DoesNotContain("approvedSourceCatalogProof", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK account", wire, StringComparison.Ordinal); Assert.DoesNotContain(proof.SourceFactsDigest, wire, StringComparison.Ordinal);
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Pending_projection_is_company_bound_oldest_first_max_plus_one_and_the_same_owner_item()
    {
        await using var kit = new Kit(); var first = Row(PackageId); var next = Row(Guid.NewGuid()); next.PreparedAtUtc = first.PreparedAtUtc.AddMinutes(1);
        var foreign = Row(Guid.NewGuid()); foreign.CompanyId = Kit.OtherCompany;
        var terminal = Row(Guid.NewGuid()); terminal.Status = 2; terminal.Version = 2; terminal.CatalogVersion = 1; terminal.ActivatedByUserId = Kit.Caller; terminal.ActivatedAtUtc = first.PreparedAtUtc;
        await kit.SeedAsync(Kit.Company, db => db.ApprovedSourceCatalogs.AddRange(first, next, terminal));
        await kit.SeedAsync(Kit.OtherCompany, db => db.ApprovedSourceCatalogs.Add(foreign));
        var fence = kit.Fence(); var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = Detail("Prepared", 1, 0) });
        var page = await source.ListPendingAsync(Kit.Context(), 1, default); Kit.AssertContractShape(page, source.Kind);
        var listed = Assert.Single(page.Items!); Assert.True(page.HasMore); Assert.Equal(PackageId, listed.ItemId);
        Assert.Equal(JsonSerializer.Serialize(listed, Json), JsonSerializer.Serialize((await source.ReadDecisionAsync(Kit.Context(), PackageId, default))!.Item, Json));
        var all = await source.ListPendingAsync(Kit.Context(), 10, default); Assert.False(all.HasMore);
        Assert.Equal(new[] { first.Id, next.Id }, all.Items!.Select(item => item.ItemId)); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("permission")] [InlineData("scope")] [InlineData("actor")] [InlineData("scoped-context")]
    public async Task Pending_source_retains_companywide_authority_and_does_not_call_general_prepare(string fault)
    {
        await using var kit = new Kit(); var authority = new Kit.FixedAuthority(fault != "permission", fault != "scope"); var fence = kit.Fence(authority: authority);
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence);
        var context = fault == "actor" ? Kit.Context(user: "other-user") : Kit.Context();
        if (fault == "scoped-context") context = context with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        var answer = await source.ListPendingAsync(context, 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, answer.Outcome); Assert.Null(answer.Items);
        Assert.All(authority.Asked, asked => Assert.Equal(ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, asked.Permission)); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("source-hash")] [InlineData("preview-hash")] [InlineData("version")]
    [InlineData("active-version")] [InlineData("account")] [InlineData("mapping")] [InlineData("status")] [InlineData("null-record")]
    public async Task Malformed_owner_detail_never_falls_back_to_local_pending_rows(string fault)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var detail = Detail("Prepared", 1, 0);
        detail = fault switch
        {
            "company" => detail with { Catalog = detail.Catalog with { CompanyId = Kit.OtherCompany } },
            "id" => detail with { Catalog = detail.Catalog with { PackageId = Guid.NewGuid() } },
            "source-hash" => detail with { SourceFactsDigest = new string('A', 64) }, "preview-hash" => detail with { PreviewDigest = new string('A', 64) },
            "version" => detail with { ExpectedPackageVersion = 2 }, "active-version" => detail with { ExpectedActiveCatalogVersion = -1 },
            "account" => detail with { Package = detail.Package with { Accounts = [detail.Package.Accounts[0] with { Name = "changed account" }, detail.Package.Accounts[1]] } },
            "mapping" => detail with { Package = detail.Package with { Bindings = [detail.Package.Bindings[0] with { Mappings = [new("debit", "0", detail.Package.Accounts[0].AccountId)] }] } },
            "status" => detail with { Catalog = detail.Catalog with { Status = "PreparedAgain" } }, _ => detail with { Catalog = null! },
        };
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), PackageId, default)); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(AccountingApiStatus.Denied)] [InlineData(AccountingApiStatus.NotFound)] [InlineData(AccountingApiStatus.Unavailable)]
    public async Task Native_refusals_remain_denied_notfound_or_unavailable(AccountingApiStatus status)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var owner = new Owner { Status = status };
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence, owner);
        if (status == AccountingApiStatus.Denied) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), PackageId, default));
        else if (status == AccountingApiStatus.NotFound) Assert.Null(await source.ReadDecisionAsync(Kit.Context(), PackageId, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), PackageId, default));
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task After_owner_await_captured_source_context_and_authority_are_fresh(string fault)
    {
        await using var kit = new Kit(); kit.Fence(); var company = new MutableCompany(); var user = new MutableUser(); var authority = new MutableAuthority();
        using var cancellation = new CancellationTokenSource(); var owner = new Owner { Detail = Detail("Prepared", 1, 0), AfterRead = () =>
        {
            if (fault == "permission") authority.Allowed = false; if (fault == "company") company.Set(Kit.OtherCompany);
            if (fault == "actor") user.Actor = Guid.NewGuid(); if (fault == "member") user.Member = false; if (fault == "cancel") cancellation.Cancel();
        } };
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, new(company, user, authority), owner);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(Kit.Context(), PackageId, cancellation.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), PackageId, default));
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("module")] [InlineData("kind")] [InlineData("id")] [InlineData("caption")] [InlineData("maker")] [InlineData("time")] [InlineData("link")] [InlineData("actual")] [InlineData("threshold")]
    [InlineData("deviation")] [InlineData("unit")] [InlineData("reference-id")] [InlineData("reference-label")] [InlineData("reference-role")]
    [InlineData("reference-link")] [InlineData("scope")] [InlineData("excluded")]
    public async Task Pure_owner_binding_matches_every_formatter_field_in_GET_and_precommand_proof(string fault)
    {
        var detail = Detail("Prepared", 1, 0); var item = AccountingApprovedSourceCatalogDecisionBindingV1.Item(PackageId, Kit.Maker, detail.Catalog.PreparedAtUtc)!;
        var proof = Proof(detail); var changed = fault switch
        {
            "module" => item with { Module = "other" }, "kind" => item with { Kind = "other.v1" }, "id" => item with { ItemId = Guid.NewGuid() },
            "caption" => item with { DocumentLabel = "other" }, "maker" => item with { PreparedBy = Kit.CallerText },
            "time" => item with { WaitingSinceUtc = item.WaitingSinceUtc.AddTicks(1) }, "link" => item with { Link = "/other" },
            "actual" => item with { Rules = [item.Rules[0] with { Actual = "1" }] }, "threshold" => item with { Rules = [item.Rules[0] with { Threshold = "1" }] },
            "deviation" => item with { Rules = [item.Rules[0] with { Deviation = "1" }] }, "unit" => item with { Rules = [item.Rules[0] with { Unit = "kg" }] },
            "reference-id" => item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }] },
            "reference-label" => item with { References = [item.References[0] with { Label = "other" }] },
            "reference-role" => item with { References = [item.References[0] with { Role = "other" }] },
            "reference-link" => item with { References = [item.References[0] with { Link = "/other" }] },
            "scope" => item with { Scope = new(Guid.NewGuid(), null, null) }, _ => item with { ExcludedDeciders = [Kit.CallerText] },
        };
        Assert.False(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(changed, detail));
        Assert.False(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionProof(changed, proof));
    }

    [Theory] [InlineData("valid")] [InlineData("actor")] [InlineData("expected-version")] [InlineData("active-version")] [InlineData("result-digest")] [InlineData("payload-hash")]
    public void POST_binding_uses_original_Active_two_receipt_while_current_source_is_Superseded_three(string fault)
    {
        var source = Record("Superseded", 3, Kit.Maker); var detail = Detail("Active", 2, 1); var original = detail.Catalog;
        var item = AccountingApprovedSourceCatalogDecisionBindingV1.Item(PackageId, Kit.Maker, original.PreparedAtUtc)!;
        var answer = new AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>(AccountingApiStatus.Ok, original, false, null);
        var command = new AccountingCommandRecord(ApprovedAccountingSourceCatalogCommandNamesV1.Activate,
            ApprovedAccountingSourceCatalogServiceV1.ActivationKeyHash("original"), Kit.CallerText,
            ApprovedAccountingSourceCatalogServiceV1.ActivationPayloadHash(PackageId, 1, 0), JsonSerializer.Serialize(answer, Json), "original-correlation");
        var receipt = ApprovedAccountingSourceCatalogServiceV1.BindCatalogActivationReceipt(command, source, answer, new(1, 0), "original", Kit.Caller)!;
        Assert.NotNull(receipt); receipt = fault switch
        {
            "actor" => receipt with { ActorId = Kit.Maker }, "expected-version" => receipt with { ExpectedPackageVersion = 2 },
            "active-version" => receipt with { ExpectedActiveCatalogVersion = 1 }, "result-digest" => receipt with { OriginalResultDigest = new string('B', 64) },
            "payload-hash" => receipt with { PayloadHash = new string('B', 64) }, _ => receipt,
        };
        Assert.Equal(fault == "valid", AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(item, original, receipt));
    }

    [Fact]
    public void Scalar_formatter_proof_does_not_claim_to_certify_missing_full_package_content()
    {
        var detail = Detail("Prepared", 1, 0); var item = AccountingApprovedSourceCatalogDecisionBindingV1.Item(PackageId, Kit.Maker, detail.Catalog.PreparedAtUtc)!;
        Assert.True(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionProof(item, Proof(detail) with { SourceFactsDigest = new string('B', 64) }));
        Assert.False(AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(item, detail with { SourceFactsDigest = new string('B', 64) }));
        // Full facts are certified only by the fresh typed owner read or genuine immutable native command receipt.
    }

    [Fact]
    public async Task Missing_owner_or_scoped_purpose_never_falls_back_to_pending_table_data()
    {
        await using var kit = new Kit(); var fence = kit.Fence();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence).ReadDecisionAsync(Kit.Context(), PackageId, default));
        var owner = new Owner { Detail = Detail("Prepared", 1, 0) };
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, fence, owner);
        var scoped = Kit.Context() with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(scoped, PackageId, default)); Assert.Equal(0, owner.Reads);
    }

    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task Pending_list_honors_normally_completed_provider_cancellation_before_read_or_release(int cancelAt)
    {
        await using var kit = new Kit(); await kit.SeedAsync(Kit.Company, db => db.ApprovedSourceCatalogs.Add(Row(PackageId))); kit.Fence();
        using var cancellation = new CancellationTokenSource(); var authority = new MutableAuthority();
        authority.BeforeRead = () => { if (authority.Reads == cancelAt) cancellation.Cancel(); };
        var source = new AccountingApprovedSourceCatalogWorkInboxSourceV1(kit.Db, new(new MutableCompany(), new MutableUser(), authority));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListPendingAsync(Kit.Context(), 1, cancellation.Token));
        Assert.Equal(cancelAt, authority.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    private static WorkApprovedSourceCatalogProofV1 Proof(ApprovedAccountingSourceCatalogDecisionDetailV1 detail) => new(Kit.Company, PackageId,
        detail.Catalog.PreparedByUserId, detail.Catalog.PreparedAtUtc, detail.Catalog.Status, detail.ExpectedPackageVersion, detail.ExpectedActiveCatalogVersion, detail.SourceFactsDigest, detail.PreviewDigest);
    private static ApprovedAccountingSourceCatalogDecisionDetailV1 Detail(string status, long version, long activeVersion, Guid? maker = null)
    {
        var record = Record(status, version, maker ?? Kit.Maker); var p = record.Package;
        var view = new ApprovedAccountingSourceCatalogViewV1(PackageId, Kit.Company, status, version, record.CatalogVersion, record.TemplateVersion,
            record.AccountPlanSourceVersion, record.AccountsSourceSha256, record.PostingMappingsSourceSha256, record.PreparedByUserId, record.PreparedAtUtc,
            record.ActivatedByUserId, record.ActivatedAtUtc, p.Mirror.Rows.Count, p.Bindings.Count);
        var package = ApprovedAccountingSourceCatalogServiceV1.CatalogDecisionPackage(p);
        return new(view, package, version, activeVersion, ApprovedAccountingSourceCatalogServiceV1.CatalogSourceFactsDigest(record),
            ApprovedAccountingSourceCatalogServiceV1.CatalogPreviewDigest(view, package, version, activeVersion));
    }
    private static ApprovedAccountingSourceCatalogRecordV1 Record(string status, long version, Guid maker)
    {
        var accounts = Encoding.UTF8.GetBytes(ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n50000000-0000-4000-8000-000000000001,100.01,SENTETIK account debit\n50000000-0000-4000-8000-000000000002,320.01,SENTETIK account credit\n");
        var mappings = Encoding.UTF8.GetBytes(ApprovedAccountingSourcePackageTemplateV1.MappingsHeader
            + "\n60000000-0000-4000-8000-000000000001,finance.manual-receipt.recorded.v1,v1,profile,v1,70000000-0000-4000-8000-000000000001,receipt,v1,debit,Debit,50000000-0000-4000-8000-000000000001"
            + "\n60000000-0000-4000-8000-000000000001,finance.manual-receipt.recorded.v1,v1,profile,v1,70000000-0000-4000-8000-000000000001,receipt,v1,credit,Credit,50000000-0000-4000-8000-000000000002\n");
        var preview = new ApprovedAccountingSourcePackageParserV1().Preview(new(Kit.Company, maker, Kit.Start.ToUniversalTime()), new("SENTETIK-catalog-v1", accounts, mappings));
        Assert.True(preview.IsValid);
        return new(PackageId, Kit.Company, Enum.Parse<ApprovedAccountingSourceCatalogStatusV1>(status), version, status == "Prepared" ? null : 1,
            preview.TemplateVersion, "SENTETIK-catalog-v1", preview.AccountsSourceSha256, preview.PostingMappingsSourceSha256, maker, Kit.Start.ToUniversalTime(),
            status == "Prepared" ? null : Kit.Caller, status == "Prepared" ? null : Kit.Start.ToUniversalTime().AddMinutes(1), preview.Package!);
    }
    private static ApprovedAccountingSourceCatalogRow Row(Guid id) => new()
    {
        Id = id, CompanyId = Kit.Company, Status = 1, Version = 1, TemplateVersion = ApprovedAccountingSourcePackageTemplateV1.Version,
        AccountPlanSourceVersion = "SENTETIK-catalog-v1", AccountsSourceSha256 = new string('A', 64), PostingMappingsSourceSha256 = new string('B', 64),
        PackageJson = "{}", PreparedByUserId = Kit.Maker, PreparedAtUtc = Kit.Start.ToUniversalTime(), CreatedAtUtc = Kit.Start.ToUniversalTime(), UpdatedAtUtc = Kit.Start.ToUniversalTime(),
    };
    private sealed class Owner : IApprovedAccountingSourceCatalogDecisionReadV1
    {
        public ApprovedAccountingSourceCatalogDecisionDetailV1 Detail = null!; public AccountingApiStatus Status = AccountingApiStatus.Ok; public int Reads; public Action? AfterRead;
        public async Task<AccountingApiResult<ApprovedAccountingSourceCatalogDecisionDetailV1>> ReadDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; await fence(token); AfterRead?.Invoke(); return new(Status, Status == AccountingApiStatus.Ok ? Detail : null, false, null); }
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => true; public Guid CompanyId { get; private set; } = Kit.Company; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    { public Guid Actor = Kit.Caller; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Kit.Company }; public bool CanAccessCompany(Guid id) => Member && id == Kit.Company; }
    private sealed class MutableAuthority : IErpAuthorityRevalidationService
    { public bool Allowed = true; public int Reads; public Action? BeforeRead; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Reads++; BeforeRead?.Invoke(); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
}
