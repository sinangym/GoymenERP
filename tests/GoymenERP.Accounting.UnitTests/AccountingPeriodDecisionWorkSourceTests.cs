using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingPeriodDecisionWorkSourceTests
{
    [Theory] [InlineData("Proposed", true)] [InlineData("Open", false)] [InlineData("Closed", false)]
    public async Task Opening_pending_and_later_terminal_owner_views_preserve_the_existing_native_item(string status, bool pending)
    {
        await using var kit = new Kit(); var owner = new Owner { Opening = Opening(status) }; var fence = kit.Fence();
        var source = new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Kit.Context(), owner.Opening.Period.PeriodId, default));
        Assert.Equal(pending, snapshot.Pending); Assert.Equal(owner.Opening.Period.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.SourceVersion);
        Assert.Equal(owner.Opening.Period.Version, snapshot.AccountingPeriodOpeningProof!.Version);
        Assert.Equal(owner.Opening.SourceFactsDigest, snapshot.AccountingPeriodOpeningProof.SourceFactsDigest);
        Assert.Equal(AccountingWorkInboxFence.Link(AccountingPeriodOpeningWorkInboxSourceV1.ScreenPath, snapshot.Item.ItemId), snapshot.Item.Link);
        Assert.Equal(owner.Opening.Period.PeriodId, Assert.Single(snapshot.Item.References).RecordId);
        Assert.Equal(AccountingPeriodOpeningWorkInboxSourceV1.OpeningApprovalRequiredRule, Assert.Single(snapshot.Item.Rules).RuleCode);
        Assert.True(AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Opening));
        Assert.DoesNotContain("accountingPeriodOpeningProof", JsonSerializer.Serialize(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Reopening_owner_read_keeps_later_closed_rule_and_original_proposal_version_separate_from_current_period(bool approved, bool later)
    {
        await using var kit = new Kit(); var owner = new Owner { Reopening = Reopening(approved, later) }; var fence = kit.Fence();
        var source = new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Kit.Context(), owner.Reopening.Reopening.Id, default));
        var proof = Assert.IsType<WorkAccountingPeriodReopeningProofV1>(snapshot.AccountingPeriodReopeningProof);
        Assert.Equal(!approved, snapshot.Pending); Assert.Equal(approved ? "2" : "1", snapshot.SourceVersion);
        Assert.Equal(approved ? 30 : 3, proof.PeriodVersion); Assert.Equal(3, proof.ClosedPeriodVersion);
        Assert.Equal(later, proof.LaterPeriodClosed); Assert.Equal(approved, proof.Approved);
        Assert.Equal(AccountingWorkInboxFence.Link(AccountingPeriodReopeningWorkInboxSourceV1.ScreenPath, snapshot.Item.ItemId), snapshot.Item.Link);
        Assert.Equal(new[] { "period", "period-closure" }, snapshot.Item.References.Select(x => x.Role));
        Assert.Equal(later ? 2 : 1, snapshot.Item.Rules.Count);
        Assert.Equal(later, snapshot.Item.Rules.Any(x => x.RuleCode == AccountingPeriodReopeningWorkInboxSourceV1.LaterPeriodClosedRule));
        Assert.True(AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Reopening));
        Assert.DoesNotContain("accountingPeriodReopeningProof", JsonSerializer.Serialize(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Reopening_keeps_its_actual_maker_rule_without_excluding_the_original_opening_maker()
    {
        await using var kit = new Kit(); var detail = Reopening(false, false);
        detail = Rehash(detail with { Period = detail.Period with { PreparedByUserId = Kit.Caller, ApprovedByUserId = Kit.Maker, IsOwnProposal = false } });
        var owner = new Owner { Reopening = detail }; var fence = kit.Fence();
        var source = new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(Kit.Context(), detail.Reopening.Id, default));
        Assert.True(snapshot.Pending); Assert.Equal(Kit.Maker.ToString("D"), snapshot.Item.PreparedBy);
        Assert.False(snapshot.Item.Excludes(Kit.CallerText));
    }

    [Theory] [InlineData("valid")] [InlineData("caption")] [InlineData("reference")] [InlineData("maker")]
    [InlineData("dates")] [InlineData("later")]
    public async Task Precommand_scalar_matchers_certify_only_the_existing_formatter_facts_the_carrier_actually_contains(string fault)
    {
        await using var kit = new Kit(); var owner = new Owner { Opening = Opening(), Reopening = Reopening(false, true) }; var fence = kit.Fence();
        var opening = (await new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner)
            .ReadDecisionAsync(Kit.Context(), owner.Opening.Period.PeriodId, default))!;
        var reopening = (await new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner)
            .ReadDecisionAsync(Kit.Context(), owner.Reopening.Reopening.Id, default))!;
        var op = opening.AccountingPeriodOpeningProof!; var rp = reopening.AccountingPeriodReopeningProof!;
        WorkInboxItemV1 Change(WorkInboxItemV1 item) => fault switch
        {
            "caption" => item with { DocumentLabel = "wrong" },
            "reference" => item with { References = item.References.Select(x => x with { RecordId = Guid.NewGuid() }).ToArray() },
            "maker" => item with { PreparedBy = Kit.CallerText }, _ => item,
        };
        if (fault == "dates") { op = op with { StartsOn = op.StartsOn.AddDays(1) }; rp = rp with { StartsOn = rp.StartsOn.AddDays(1) }; }
        if (fault == "later") rp = rp with { LaterPeriodClosed = false };
        Assert.Equal(fault is "valid" or "later", AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionProof(Change(opening.Item), op));
        Assert.Equal(fault == "valid", AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionProof(Change(reopening.Item), rp));
        Assert.False(AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionProof(opening.Item, opening.AccountingPeriodOpeningProof! with { SourceFactsDigest = new string('B', 64) }));
        // Reopening's reason/closure facts are absent from the scalar carrier. This helper does NOT certify its full digest.
        // Genuine native receipt validation after the command remains the release boundary for that mismatch.
        Assert.True(AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionProof(reopening.Item,
            reopening.AccountingPeriodReopeningProof! with { SourceFactsDigest = new string('B', 64) }));
    }

    [Fact]
    public void Opening_six_scalar_hash_is_exactly_the_existing_owner_canonical_hash_without_surrogate_native_fields()
    {
        var view = Opening().Period;
        var canonical = JsonSerializer.Serialize(new { view.PeriodId, view.CompanyId, view.StartsOn, view.EndsOn,
            view.PreparedByUserId, view.PreparedAtUtc }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var original = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(original, AccountingApiService.PeriodOpeningSourceFactsDigest(view));
        Assert.Equal(original, AccountingApiService.PeriodOpeningSourceFactsDigest(view.PeriodId, view.CompanyId,
            view.StartsOn, view.EndsOn, view.PreparedByUserId, view.PreparedAtUtc));
    }

    [Theory] [InlineData("caption")] [InlineData("maker")] [InlineData("link")] [InlineData("actual")]
    [InlineData("threshold")] [InlineData("deviation")] [InlineData("unit")] [InlineData("ref-id")] [InlineData("ref-label")]
    [InlineData("ref-role")] [InlineData("ref-link")] [InlineData("excluded")]
    public async Task Both_owner_matchers_bind_the_whole_existing_item_instead_of_echoing_the_supplied_snapshot(string fault)
    {
        await using var kit = new Kit(); var owner = new Owner { Opening = Opening(), Reopening = Reopening(false, true) }; var fence = kit.Fence();
        var opening = new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner);
        var reopening = new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner);
        var first = (await opening.ReadDecisionAsync(Kit.Context(), owner.Opening.Period.PeriodId, default))!;
        var second = (await reopening.ReadDecisionAsync(Kit.Context(), owner.Reopening.Reopening.Id, default))!;
        Assert.False(AccountingPeriodOpeningDecisionBindingV1.MatchesDecisionItem(Change(first.Item), owner.Opening));
        Assert.False(AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(Change(second.Item), owner.Reopening));
        WorkInboxItemV1 Change(WorkInboxItemV1 item) => fault switch
        {
            "caption" => item with { DocumentLabel = "wrong" }, "maker" => item with { PreparedBy = Kit.CallerText },
            "link" => item with { Link = "/wrong" }, "excluded" => item with { ExcludedDeciders = [Kit.CallerText] },
            "actual" => item with { Rules = item.Rules.Select((r, i) => i == 0 ? r with { Actual = "1" } : r).ToArray() },
            "threshold" => item with { Rules = item.Rules.Select((r, i) => i == 0 ? r with { Threshold = "1" } : r).ToArray() },
            "deviation" => item with { Rules = item.Rules.Select((r, i) => i == 0 ? r with { Deviation = "1" } : r).ToArray() },
            "unit" => item with { Rules = item.Rules.Select((r, i) => i == 0 ? r with { Unit = "kg" } : r).ToArray() },
            _ => item with { References = item.References.Select((r, i) => i != 0 ? r : fault switch
            { "ref-id" => r with { RecordId = Guid.NewGuid() }, "ref-label" => r with { Label = "wrong" },
                "ref-role" => r with { Role = "other" }, _ => r with { Link = "/wrong" } }).ToArray() },
        };
    }

    [Fact]
    public async Task Removing_the_real_later_rule_or_its_owner_fact_never_certifies_a_reopening_item()
    {
        await using var kit = new Kit(); var owner = new Owner { Reopening = Reopening(false, true) }; var fence = kit.Fence();
        var source = new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), owner.Reopening.Reopening.Id, default))!;
        Assert.False(AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item with { Rules = [snapshot.Item.Rules[0]] }, owner.Reopening));
        Assert.False(AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Reopening with { LaterPeriodClosed = false }));
        Assert.False(AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, owner.Reopening with { LaterPeriodClosed = null }));
        owner.Reopening = owner.Reopening with { LaterPeriodClosed = null };
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), owner.Reopening.Reopening.Id, default));
    }

    [Theory] [InlineData("valid")] [InlineData("missing-start")] [InlineData("missing-end")] [InlineData("wrong-start")]
    [InlineData("wrong-end")] [InlineData("wrong-period")] [InlineData("missing-later")]
    public async Task Reopening_POST_formatter_uses_genuine_owner_receipt_dates_not_the_snapshot_caption(string fault)
    {
        await using var kit = new Kit(); var detail = Reopening(true, true); var owner = new Owner { Reopening = detail }; var fence = kit.Fence();
        var source = new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner);
        var snapshot = (await source.ReadDecisionAsync(Kit.Context(), detail.Reopening.Id, default))!;
        var answer = new AccountingApiResult<AccountingPeriodReopeningViewV1>(AccountingApiStatus.Ok, detail.Reopening, false, null);
        var receipt = new AccountingPeriodApprovalReceiptV1(Kit.Company, detail.Period.PeriodId, detail.Reopening.Id,
            "accounting.period.reopening.approve.v1", new string('A', 64), new string('B', 64), Kit.Caller, 1, 3, 2,
            "SENTETIK original", detail.Reopening.ApprovedAtUtc!.Value, detail.SourceFactsDigest,
            AccountingApiService.PeriodApprovalOriginalResultDigest(answer))
        { PeriodStartsOn = detail.Period.StartsOn, PeriodEndsOn = detail.Period.EndsOn, LaterPeriodClosed = true };
        receipt = fault switch
        {
            "missing-start" => receipt with { PeriodStartsOn = null }, "missing-end" => receipt with { PeriodEndsOn = null },
            "wrong-start" => receipt with { PeriodStartsOn = detail.Period.StartsOn.AddDays(1) },
            "wrong-end" => receipt with { PeriodEndsOn = detail.Period.EndsOn.AddDays(1) },
            "wrong-period" => receipt with { PeriodId = Guid.NewGuid() }, "missing-later" => receipt with { LaterPeriodClosed = null }, _ => receipt,
        };
        Assert.Equal(fault == "valid", AccountingPeriodReopeningDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail.Reopening, receipt));
        Assert.DoesNotContain("periodStartsOn", JsonSerializer.Serialize(receipt), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("periodEndsOn", JsonSerializer.Serialize(receipt), StringComparison.OrdinalIgnoreCase);
    }

    [Theory] [InlineData(AccountingApiStatus.Denied)] [InlineData(AccountingApiStatus.NotFound)] [InlineData(AccountingApiStatus.Unavailable)]
    public async Task Neither_source_falls_back_to_a_shadow_table_query_for_owner_refusal(AccountingApiStatus status)
    {
        foreach (var reopen in new[] { false, true })
        {
            await using var kit = new Kit(); var owner = new Owner { Status = status }; var fence = kit.Fence();
            IWorkApprovalDecisionSourceV1 source = reopen ? new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner)
                : new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner);
            if (status == AccountingApiStatus.Denied) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
            else if (status == AccountingApiStatus.NotFound) Assert.Null(await source.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
            Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
        }
    }

    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("source")] [InlineData("preview")] [InlineData("maker")]
    public async Task Wrong_identity_or_digest_and_the_actual_maker_never_release_a_snapshot(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            await using var kit = new Kit(); var owner = new Owner { Opening = Opening(), Reopening = Reopening(false, false) }; var fence = kit.Fence();
            var id = reopen ? owner.Reopening.Reopening.Id : owner.Opening.Period.PeriodId;
            if (reopen)
            {
                var detail = owner.Reopening; var proposal = detail.Reopening;
                if (fault == "company") proposal = proposal with { CompanyId = Kit.OtherCompany };
                if (fault == "id") proposal = proposal with { Id = Guid.NewGuid() };
                if (fault == "maker") proposal = proposal with { ProposedBy = Kit.Caller };
                owner.Reopening = Rehash(detail with { Reopening = proposal });
                if (fault == "source") owner.Reopening = owner.Reopening with { SourceFactsDigest = new string('B', 64) };
                if (fault == "preview") owner.Reopening = owner.Reopening with { PreviewDigest = new string('B', 64) };
            }
            else
            {
                var view = owner.Opening.Period;
                if (fault == "company") view = view with { CompanyId = Kit.OtherCompany };
                if (fault == "id") view = view with { PeriodId = Guid.NewGuid() };
                if (fault == "maker") view = view with { PreparedByUserId = Kit.Caller, IsOwnProposal = true };
                owner.Opening = OpenDetail(view);
                if (fault == "source") owner.Opening = owner.Opening with { SourceFactsDigest = new string('B', 64) };
                if (fault == "preview") owner.Opening = owner.Opening with { PreviewDigest = new string('B', 64) };
            }
            IWorkApprovalDecisionSourceV1 source = reopen ? new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner)
                : new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner);
            if (fault == "maker") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), id, default));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(Kit.Context(), id, default));
        }
    }

    [Theory] [InlineData("permission")] [InlineData("scoped")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Final_owner_await_cannot_release_after_company_actor_authority_or_cancellation_changes(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            await using var kit = new Kit(); kit.Fence(); var company = new MutableCompanyContextForFenceTests(Kit.Company);
            var user = new User(); var authority = new Authority(); var fence = new AccountingWorkInboxFence(company, user, authority);
            using var cancellation = new CancellationTokenSource();
            var owner = new Owner { Opening = Opening(), Reopening = Reopening(false, false), AfterRead = () =>
            {
                if (fault == "permission") authority.Allowed = false;
                if (fault == "scoped") authority.CompanyWide = false;
                if (fault == "company") company.Set(Kit.OtherCompany);
                if (fault == "actor") user.Actor = Kit.Maker.ToString("D");
                if (fault == "member") user.Member = false;
                if (fault == "cancel") cancellation.Cancel();
            } };
            IWorkApprovalDecisionSourceV1 source = reopen ? new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner)
                : new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner);
            var id = reopen ? owner.Reopening.Reopening.Id : owner.Opening.Period.PeriodId;
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadDecisionAsync(Kit.Context(), id, cancellation.Token));
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(), id, default));
            Assert.Equal(1, owner.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
        }
    }

    [Fact]
    public async Task Missing_owner_scoped_and_wrong_actor_contexts_do_not_start_a_native_read()
    {
        foreach (var reopen in new[] { false, true })
        {
            await using var kit = new Kit(); var owner = new Owner { Opening = Opening(), Reopening = Reopening(false, false) }; var fence = kit.Fence();
            IWorkApprovalDecisionSourceV1 source = reopen ? new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence, owner)
                : new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence, owner);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context(user: "foreign"), Guid.NewGuid(), default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Kit.Context() with { GrantScope = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) }, Guid.NewGuid(), default));
            IWorkApprovalDecisionSourceV1 unsupported = reopen ? new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence)
                : new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unsupported.ReadDecisionAsync(Kit.Context(), Guid.NewGuid(), default));
            Assert.Equal(0, owner.Reads);
        }
    }

    private static AccountingPeriodOpeningDecisionDetailV1 Opening(string status = "Proposed") => OpenDetail(Period(status));
    private static AccountingPeriodOpeningDecisionDetailV1 OpenDetail(PeriodViewV1 view) => new(view,
        AccountingApiService.PeriodOpeningSourceFactsDigest(view), AccountingApiService.PeriodOpeningPreviewDigest(view));
    private static PeriodViewV1 Period(string status)
    {
        var at = Kit.Start.ToUniversalTime();
        return new(Guid.NewGuid(), Kit.Company, new(2026, 10, 1), new(2026, 10, 31), status, status == "Proposed" ? 1 : status == "Open" ? 2 : 3,
            Kit.Maker, at, status == "Proposed" ? null : Kit.Caller, status == "Proposed" ? null : at,
            status == "Closed" ? Kit.Caller : null, status == "Closed" ? at : null, false);
    }
    private static AccountingPeriodReopeningDecisionDetailV1 Reopening(bool approved, bool later)
    {
        var period = Period("Closed"); if (approved) period = period with { Version = 30 };
        var at = Kit.Start.ToUniversalTime(); var id = Guid.NewGuid(); var closure = Guid.NewGuid();
        var proposal = new AccountingPeriodReopeningViewV1(id, Kit.Company, period.PeriodId, closure, 3, Kit.Maker, at, "SENTETIK reason",
            approved ? Kit.Caller : null, approved ? at : null, approved ? 2 : 1);
        var closing = new AccountingPeriodClosureViewV1(closure, Kit.Company, period.PeriodId, 3, Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), 0,
            Kit.Caller, at, approved ? id : null, approved ? Kit.Caller : null, approved ? at : null);
        return Rehash(new(proposal, period, closing, "", "") { LaterPeriodClosed = later });
    }
    private static AccountingPeriodReopeningDecisionDetailV1 Rehash(AccountingPeriodReopeningDecisionDetailV1 detail)
    {
        var source = new AccountingPeriodReopeningDecisionSourceV1(AccountingPeriodOpeningWorkInboxSourceV1.RestoreView(detail.Period), detail.Reopening, detail.Closure)
            { LaterPeriodClosed = detail.LaterPeriodClosed };
        return detail with { SourceFactsDigest = AccountingApiService.PeriodReopeningSourceFactsDigest(source),
            PreviewDigest = AccountingApiService.PeriodReopeningPreviewDigest(detail.Reopening, detail.Period, detail.Closure) };
    }
    private sealed class Owner : IAccountingPeriodDecisionReadV1
    {
        public AccountingPeriodOpeningDecisionDetailV1 Opening = null!; public AccountingPeriodReopeningDecisionDetailV1 Reopening = null!;
        public AccountingApiStatus Status = AccountingApiStatus.Ok; public int Reads; public Action? AfterRead;
        public async Task<AccountingApiResult<AccountingPeriodOpeningDecisionDetailV1>> ReadOpeningDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token)
        { await fence(token); Reads++; await fence(token); AfterRead?.Invoke(); return new(Status, Status == AccountingApiStatus.Ok ? Opening : null, false, null); }
        public async Task<AccountingApiResult<AccountingPeriodReopeningDecisionDetailV1>> ReadReopeningDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token)
        { await fence(token); Reads++; await fence(token); AfterRead?.Invoke(); return new(Status, Status == AccountingApiStatus.Ok ? Reopening : null, false, null); }
    }
    private sealed class User : ICurrentUser
    {
        public string Actor = Kit.CallerText; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Kit.Company };
        public bool CanAccessCompany(Guid company) => Member && company == Kit.Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) =>
            Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
