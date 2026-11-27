using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingPostingAccountMappingDecisionWorkSourceTests
{
    private static readonly Guid WorkId = AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(1);
    private static readonly Guid AccountId = Guid.Parse("52c00000-0000-4000-8000-000000000001");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(1L)] [InlineData(2L)] [InlineData(9007199254740991L)] [InlineData(9007199254740992L)]
    [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public void Custom_uuid8_carrier_roundtrips_every_native_long_bit_without_culture_or_loss(long value)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var invariant = AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(value);
            foreach (var culture in new[] { "tr-TR", "en-US", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var id = AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(value);
                Assert.Equal(invariant, id); Assert.Equal('8', id.ToString("N")[12]); Assert.Equal('8', id.ToString("N")[16]);
                Assert.True(AccountingPostingAccountMappingDecisionBindingV1.TryMappingVersion(id, out var restored)); Assert.Equal(value, restored);
                Assert.Equal(value.ToString(CultureInfo.InvariantCulture), AccountingPostingAccountMappingDecisionBindingV1.Item(value, Kit.Maker, Kit.Start.ToUniversalTime())!.DocumentLabel);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ac530000-0000-8000-8000-010000000000")]
    [InlineData("ac520000-0000-4000-8000-010000000000")]
    [InlineData("ac520000-0000-8000-9000-010000000000")]
    [InlineData("ac520000-0000-8000-8000-010000000001")]
    [InlineData("ac520000-0000-8000-8000-000000000000")]
    [InlineData("ac528000-0000-8000-8000-000000000000")]
    [InlineData("ac52ffff-ffff-8fff-8fff-ff0000000000")]
    public async Task Wrong_namespace_reserved_bits_zero_tail_zero_negative_and_overflow_never_reach_native_owner(string text)
    {
        var id = Guid.ParseExact(text, "D"); Assert.False(AccountingPostingAccountMappingDecisionBindingV1.TryMappingVersion(id, out var value)); Assert.Equal(0L, value);
        await using var kit = new Kit(); var owner = new Owner { Detail = Detail("Prepared", 1, 0) };
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, kit.Fence(), owner);
        Assert.Null(await source.ReadDecisionAsync(Kit.Context(), id, default)); Assert.Equal(0, owner.Reads);
    }

    [Fact]
    public void Custom_carrier_is_unique_and_rejects_native_zero_or_negative_before_formatting()
    {
        var values = new[] { 1L, 2L, 9007199254740991L, 9007199254740992L, 9007199254740993L, long.MaxValue };
        Assert.Equal(values.Length, values.Select(AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId).Distinct().Count());
        Assert.Equal(Guid.Parse("ac520000-0000-8000-8000-010000000000"), AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(-1));
    }

    [Theory] [InlineData("Prepared", 1L, 0L)] [InlineData("Active", 2L, 1L)] [InlineData("Superseded", 3L, 2L)]
    public async Task Purpose_current_states_use_native_mapping_and_two_versions_with_native_maker_exclusion(string status, long version, long activeVersion)
    {
        await using var kit = new Kit(); var authority = new Kit.FixedAuthority(true, true); var fence = kit.Fence(authority: authority);
        var owner = new Owner { Detail = Detail(status, version, activeVersion) };
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), WorkId, default))!;
        Assert.Equal(status == "Prepared", snapshot.Pending); Assert.Equal(version.ToString(CultureInfo.InvariantCulture), snapshot.SourceVersion);
        var proof = Assert.IsType<WorkPostingAccountMappingProofV1>(snapshot.PostingAccountMappingProof);
        Assert.Equal(version, proof.Version); Assert.Equal(activeVersion, proof.ActiveMappingVersion);
        Assert.Equal(Kit.Maker.ToString("D"), snapshot.Item.PreparedBy); Assert.True(snapshot.Item.Excludes(Kit.Maker.ToString("D"))); Assert.False(snapshot.Item.Excludes(Kit.CallerText)); Assert.Null(snapshot.Item.ExcludedDeciders);
        Assert.Equal("/onaylarim/accounting.posting-mapping.v1/" + WorkId.ToString("D"), snapshot.Item.Link);
        var reference = Assert.Single(snapshot.Item.References); Assert.Equal(WorkId, reference.RecordId); Assert.Equal(AccountingPostingAccountMappingDecisionBindingV1.NativeScreenPath, reference.Link);
        Assert.Equal("posting-account-mapping-version", reference.Role); Assert.Null(snapshot.Item.Scope);
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Detail));
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionProof(snapshot.Item, proof));
        Assert.All(authority.Asked, asked => Assert.Equal(AccountingPostingAccountMappingPermissionNamesV1.Activate, asked.Permission));
        var wire = JsonSerializer.Serialize(snapshot, Json); Assert.DoesNotContain("postingAccountMappingProof", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK account", wire, StringComparison.Ordinal); Assert.DoesNotContain(proof.SourceFactsDigest, wire, StringComparison.Ordinal);
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Pending_projection_is_company_bound_oldest_first_max_plus_one_and_the_same_owner_item()
    {
        await using var kit = new Kit(); var first = Row(1); var next = Row(2); next.PreparedAtUtc = first.PreparedAtUtc.AddMinutes(1);
        var foreign = Row(3); foreign.CompanyId = Kit.OtherCompany;
        var terminal = Row(4); terminal.Status = 2; terminal.Version = 2; terminal.ActivatedByUserId = Kit.Caller; terminal.ActivatedAtUtc = first.PreparedAtUtc;
        await kit.SeedAsync(Kit.Company, db => db.PostingAccountMappingVersions.AddRange(first, next, terminal));
        await kit.SeedAsync(Kit.OtherCompany, db => db.PostingAccountMappingVersions.Add(foreign));
        var fence = kit.Fence(); var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = Detail("Prepared", 1, 0) });
        var page = await source.ListPendingAsync(Kit.Context(), 1, default); Kit.AssertContractShape(page, source.Kind);
        var listed = Assert.Single(page.Items!); Assert.True(page.HasMore); Assert.Equal(WorkId, listed.ItemId);
        Assert.Equal(JsonSerializer.Serialize(listed, Json), JsonSerializer.Serialize((await source.ReadDecisionAsync(Kit.Context(), WorkId, default))!.Item, Json));
        var all = await source.ListPendingAsync(Kit.Context(), 10, default); Assert.False(all.HasMore);
        Assert.Equal(new[] { AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(first.MappingVersion), AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(next.MappingVersion) }, all.Items!.Select(item => item.ItemId)); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("permission")] [InlineData("scope")] [InlineData("actor")] [InlineData("scoped-context")]
    public async Task Pending_source_retains_companywide_authority_and_does_not_call_general_prepare(string fault)
    {
        await using var kit = new Kit(); var authority = new Kit.FixedAuthority(fault != "permission", fault != "scope"); var fence = kit.Fence(authority: authority);
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence);
        var context = fault == "actor" ? Kit.Context(user: "other-user") : Kit.Context();
        if (fault == "scoped-context") context = context with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        var answer = await source.ListPendingAsync(context, 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, answer.Outcome); Assert.Null(answer.Items);
        Assert.All(authority.Asked, asked => Assert.Equal(AccountingPostingAccountMappingPermissionNamesV1.Activate, asked.Permission)); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("source-hash")] [InlineData("preview-hash")]
    [InlineData("version")] [InlineData("active-version")] [InlineData("prepared-plan")] [InlineData("current-plan")]
    [InlineData("account")] [InlineData("mapping")] [InlineData("status")] [InlineData("null-record")]
    public async Task Malformed_owner_detail_never_falls_back_to_local_pending_rows(string fault)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var detail = Detail("Prepared", 1, 0);
        detail = fault switch
        {
            "company" => detail with { Mapping = detail.Mapping with { CompanyId = Kit.OtherCompany } },
            "id" => detail with { Mapping = detail.Mapping with { MappingVersion = 2 } },
            "source-hash" => detail with { SourceFactsDigest = new string('B', 64) }, "preview-hash" => detail with { PreviewDigest = new string('B', 64) },
            "version" => detail with { ExpectedVersion = 2 }, "active-version" => detail with { ExpectedActiveMappingVersion = -1 },
            "prepared-plan" => detail with { PreparedAccountPlan = new("other", new string('A', 64)) },
            "current-plan" => detail with { CurrentActiveAccountPlan = detail.CurrentActiveAccountPlan with { Reference = new("other", new string('B', 64)) } },
            "account" => detail with { CurrentActiveAccountPlan = detail.CurrentActiveAccountPlan with { Accounts = [detail.CurrentActiveAccountPlan.Accounts[0] with { Name = "changed account" }] } },
            "mapping" => detail with { Mapping = detail.Mapping with { Rows = [detail.Mapping.Rows[0] with { AccountCode = "other" }] } },
            "status" => detail with { Mapping = detail.Mapping with { Status = "PreparedAgain" } }, _ => detail with { Mapping = null! },
        };
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, default)); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(AccountingApiStatus.Denied)] [InlineData(AccountingApiStatus.NotFound)] [InlineData(AccountingApiStatus.Unavailable)]
    public async Task Native_refusals_remain_denied_notfound_or_unavailable(AccountingApiStatus status)
    {
        await using var kit = new Kit(); var fence = kit.Fence(); var owner = new Owner { Status = status };
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence, owner);
        if (status == AccountingApiStatus.Denied) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, default));
        else if (status == AccountingApiStatus.NotFound) Assert.Null(await source.ReadDecisionAsync(Kit.Context(), WorkId, default));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, default));
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
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, new(company, user, authority), owner);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, cancellation.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, default));
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("module")] [InlineData("kind")] [InlineData("id")] [InlineData("caption")] [InlineData("maker")] [InlineData("time")] [InlineData("link")] [InlineData("actual")] [InlineData("threshold")]
    [InlineData("deviation")] [InlineData("unit")] [InlineData("reference-id")] [InlineData("reference-label")] [InlineData("reference-role")]
    [InlineData("reference-link")] [InlineData("scope")] [InlineData("excluded")]
    public void Pure_owner_binding_matches_every_formatter_field_in_GET_and_precommand_proof(string fault)
    {
        var detail = Detail("Prepared", 1, 0); var item = AccountingPostingAccountMappingDecisionBindingV1.Item(1, Kit.Maker, detail.Mapping.PreparedAtUtc)!;
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
        Assert.False(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(changed, detail));
        Assert.False(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionProof(changed, proof));
    }

    [Theory] [InlineData("valid")] [InlineData("actor")] [InlineData("expected-version")] [InlineData("active-version")] [InlineData("result-digest")] [InlineData("payload-hash")]
    public void POST_binding_uses_original_Active_two_receipt_while_current_source_is_Superseded_three(string fault)
    {
        var source = Record("Superseded", 3, Kit.Maker); var detail = Detail("Active", 2, 1); var original = detail.Mapping;
        var item = AccountingPostingAccountMappingDecisionBindingV1.Item(1, Kit.Maker, original.PreparedAtUtc)!;
        var answer = new AccountingApiResult<AccountingPostingAccountMappingViewV1>(AccountingApiStatus.Ok, original, false, null);
        var command = new AccountingCommandRecord(AccountingPostingAccountMappingCommandNamesV1.Activate,
            AccountingPostingAccountMappingServiceV1.ActivationKeyHash("original"), Kit.CallerText,
            AccountingPostingAccountMappingServiceV1.ActivationPayloadHash(1, 1, 0), JsonSerializer.Serialize(answer, Json), "original-correlation");
        var receipt = AccountingPostingAccountMappingServiceV1.BindMappingActivationReceipt(command, source, answer, new(1, 0), "original", Kit.Caller)!;
        Assert.NotNull(receipt); receipt = fault switch
        {
            "actor" => receipt with { ActorId = Kit.Maker }, "expected-version" => receipt with { ExpectedVersion = 2 },
            "active-version" => receipt with { ExpectedActiveMappingVersion = 1 }, "result-digest" => receipt with { OriginalResultDigest = new string('B', 64) },
            "payload-hash" => receipt with { PayloadHash = new string('B', 64) }, _ => receipt,
        };
        Assert.Equal(fault == "valid", AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(item, original, receipt));
    }

    [Fact]
    public void Scalar_formatter_proof_does_not_claim_to_certify_missing_full_mapping_content()
    {
        var detail = Detail("Prepared", 1, 0); var item = AccountingPostingAccountMappingDecisionBindingV1.Item(1, Kit.Maker, detail.Mapping.PreparedAtUtc)!;
        Assert.True(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionProof(item, Proof(detail) with { SourceFactsDigest = new string('B', 64) }));
        Assert.False(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(item, detail with { SourceFactsDigest = new string('B', 64) }));
        // Full facts are certified only by the fresh typed owner read or genuine immutable native command receipt.
    }

    [Fact]
    public async Task Missing_owner_or_scoped_purpose_never_falls_back_to_pending_table_data()
    {
        await using var kit = new Kit(); var fence = kit.Fence();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence).ReadDecisionAsync(Kit.Context(), WorkId, default));
        var owner = new Owner { Detail = Detail("Prepared", 1, 0) };
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence, owner);
        var scoped = Kit.Context() with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(scoped, WorkId, default)); Assert.Equal(0, owner.Reads);
    }

    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task Pending_list_honors_normally_completed_provider_cancellation_before_read_or_release(int cancelAt)
    {
        await using var kit = new Kit(); await kit.SeedAsync(Kit.Company, db => db.PostingAccountMappingVersions.Add(Row(1))); kit.Fence();
        using var cancellation = new CancellationTokenSource(); var authority = new MutableAuthority();
        authority.BeforeRead = () => { if (authority.Reads == cancelAt) cancellation.Cancel(); };
        var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, new(new MutableCompany(), new MutableUser(), authority));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListPendingAsync(Kit.Context(), 1, cancellation.Token));
        Assert.Equal(cancelAt, authority.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Malformed_pending_scalar_or_null_owner_answer_never_releases_a_partial_or_fallback_item()
    {
        await using var kit = new Kit(); var bad = Row(1); bad.Version = 2;
        await kit.SeedAsync(Kit.Company, db => db.PostingAccountMappingVersions.Add(bad)); var fence = kit.Fence();
        var owner = new Owner { NullAnswer = true }; var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, fence, owner);
        var list = await source.ListPendingAsync(Kit.Context(), 10, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, list.Outcome); Assert.Null(list.Items);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, default)); Assert.Equal(1, owner.Reads);
    }

    [Fact]
    public async Task Even_a_faulty_owner_cannot_bypass_the_native_mapping_maker_exclusion()
    {
        await using var kit = new Kit(); var source = new AccountingPostingAccountMappingWorkInboxSourceV1(kit.Db, kit.Fence(), new Owner { Detail = Detail("Prepared", 1, 0, Kit.Caller) });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), WorkId, default));
        var item = AccountingPostingAccountMappingDecisionBindingV1.Item(1, Kit.Maker, Kit.Start.ToUniversalTime())!;
        Assert.True(item.Excludes(Kit.Maker.ToString("D"))); Assert.False(item.Excludes(Kit.CallerText));
    }

    [Theory] [InlineData("source")] [InlineData("preview")]
    [InlineData("source-lf")] [InlineData("source-cr")] [InlineData("source-crlf")]
    [InlineData("preview-lf")] [InlineData("preview-cr")] [InlineData("preview-crlf")]
    public void Scalar_proof_digests_are_exact_uppercase_64_without_accepting_terminal_line_endings(string fault)
    {
        var detail = Detail("Prepared", 1, 0); var proof = Proof(detail); var item = AccountingPostingAccountMappingDecisionBindingV1.Item(1, Kit.Maker, detail.Mapping.PreparedAtUtc)!;
        var malformed = fault.EndsWith("-crlf", StringComparison.Ordinal) ? new string('A', 62) + "\r\n"
            : fault.EndsWith("-lf", StringComparison.Ordinal) ? new string('A', 63) + "\n"
            : fault.EndsWith("-cr", StringComparison.Ordinal) ? new string('A', 63) + "\r" : new string('a', 64);
        proof = fault.StartsWith("source", StringComparison.Ordinal) ? proof with { SourceFactsDigest = malformed } : proof with { PreviewDigest = malformed };
        Assert.False(AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionProof(item, proof));
    }

    [Theory]
    [InlineData("effective-date")] [InlineData("prepared-plan-version")] [InlineData("prepared-plan-sha")]
    [InlineData("prepared-by")] [InlineData("prepared-time")] [InlineData("activated-by")] [InlineData("activated-time")]
    [InlineData("purpose")] [InlineData("key")] [InlineData("item-type")] [InlineData("line-purpose")]
    [InlineData("export")] [InlineData("account-id")] [InlineData("account-code")] [InlineData("vat")]
    public void Full_native_header_and_every_mapping_row_field_are_bound_without_truncation_or_recalculation(string fault)
    {
        var detail = Detail("Active", 2, 1); var view = detail.Mapping; var row = view.Rows[0];
        view = fault switch
        {
            "effective-date" => view with { EffectiveFrom = view.EffectiveFrom.AddDays(1) },
            "prepared-plan-version" => view with { AccountPlanSourceVersion = "other" }, "prepared-plan-sha" => view with { AccountPlanSourceSha256 = new string('C', 64) },
            "prepared-by" => view with { PreparedByUserId = Guid.NewGuid() }, "prepared-time" => view with { PreparedAtUtc = view.PreparedAtUtc.AddTicks(1) },
            "activated-by" => view with { ActivatedByUserId = Guid.NewGuid() }, "activated-time" => view with { ActivatedAtUtc = view.ActivatedAtUtc!.Value.AddTicks(1) }, _ => view
        };
        row = fault switch
        {
            "purpose" => row with { Purpose = "other" }, "key" => row with { MappingKey = "other" }, "item-type" => row with { ItemType = "Other" },
            "line-purpose" => row with { LinePurpose = "Other" }, "export" => row with { IsExport = true }, "account-id" => row with { AccountId = Guid.NewGuid() },
            "account-code" => row with { AccountCode = "600.99" }, "vat" => row with { VatRateCode = "Other" }, _ => row
        };
        Assert.False(AccountingPostingAccountMappingServiceV1.ValidMappingDecisionDetail(detail with { Mapping = view with { Rows = [row] } }));
    }

    private static WorkPostingAccountMappingProofV1 Proof(AccountingPostingAccountMappingDecisionDetailV1 detail) => new(Kit.Company, 1,
        detail.Mapping.PreparedByUserId, detail.Mapping.PreparedAtUtc, detail.Mapping.Status, detail.ExpectedVersion, detail.ExpectedActiveMappingVersion, detail.SourceFactsDigest, detail.PreviewDigest);
    private static AccountingPostingAccountMappingDecisionDetailV1 Detail(string status, long version, long activeVersion, Guid? maker = null)
    {
        var record = Record(status, version, maker ?? Kit.Maker); var view = AccountingPostingAccountMappingServiceV1.View(record);
        var plan = new AccountingPostingMappingDecisionPlanV1(Kit.Company, new("SENTETIK-current-plan", new string('B', 64)), [new(AccountId, "600.02", "SENTETIK current account")]);
        return new(view, new(view.AccountPlanSourceVersion, view.AccountPlanSourceSha256), plan, version, activeVersion,
            AccountingPostingAccountMappingServiceV1.MappingSourceFactsDigest(record), AccountingPostingAccountMappingServiceV1.MappingPreviewDigest(view, plan, version, activeVersion));
    }
    private static AccountingPostingAccountMappingRecordV1 Record(string status, long version, Guid maker) => new(Kit.Company, 1, new DateOnly(2026, 11, 20),
        Enum.Parse<AccountingPostingAccountMappingStatusV1>(status), version, "SENTETIK-prepared-plan", new string('A', 64), maker, Kit.Start.ToUniversalTime(),
        status == "Prepared" ? null : Kit.Caller, status == "Prepared" ? null : Kit.Start.ToUniversalTime().AddMinutes(1),
        [new(1, AccountingPostingAccountMappingPurposesV1.SalesInvoice, AccountingPostingAccountMappingKeysV1.Revenue, null, null, null, AccountId, "600.01")]);
    private static AccountingPostingAccountMappingVersionRow Row(long mappingVersion) => new()
    {
        CompanyId = Kit.Company, MappingVersion = mappingVersion, Status = 1, Version = 1, EffectiveFrom = new DateOnly(2026, 11, 20),
        AccountPlanSourceVersion = "SENTETIK-prepared-plan", AccountPlanSourceSha256 = new string('A', 64),
        PreparedByUserId = Kit.Maker, PreparedAtUtc = Kit.Start.ToUniversalTime(), CreatedAtUtc = Kit.Start.ToUniversalTime(), UpdatedAtUtc = Kit.Start.ToUniversalTime(),
    };
    private sealed class Owner : IAccountingPostingAccountMappingDecisionReadV1
    {
        public AccountingPostingAccountMappingDecisionDetailV1 Detail = null!; public AccountingApiStatus Status = AccountingApiStatus.Ok; public int Reads; public bool NullAnswer; public Action? AfterRead;
        public async Task<AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1>> ReadDecisionAsync(long id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; await fence(token); AfterRead?.Invoke(); if (NullAnswer) return null!; return new(Status, Status == AccountingApiStatus.Ok ? Detail : null, false, null); }
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => true; public Guid CompanyId { get; private set; } = Kit.Company; public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    { public Guid Actor = Kit.Caller; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Kit.Company }; public bool CanAccessCompany(Guid id) => Member && id == Kit.Company; }
    private sealed class MutableAuthority : IErpAuthorityRevalidationService
    { public bool Allowed = true; public int Reads; public Action? BeforeRead; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Reads++; BeforeRead?.Invoke(); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
}
