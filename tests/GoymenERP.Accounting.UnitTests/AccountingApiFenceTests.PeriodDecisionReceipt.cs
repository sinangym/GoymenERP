using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingApiFenceTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Period_purpose_reads_use_only_the_existing_exact_approval_permission_and_do_not_write(bool reopen)
    {
        var opening = PeriodSource(); var source = ReopeningSource(opening);
        var stores = new Stores { OpeningDecision = opening, ReopeningDecision = source };
        var authority = new PeriodDecisionAuthority(reopen ? AccountingPermissionManifestV1.PeriodsReopen : AccountingPermissionManifestV1.PeriodsApprove);
        var service = Service(authority, stores);
        if (reopen)
        {
            var result = await service.ReadReopeningDecisionAsync(source.Reopening.Id, _ => Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Ok, result.Status); var detail = Assert.IsType<AccountingPeriodReopeningDecisionDetailV1>(result.Resource);
            Assert.Equal(source.Closure, detail.Closure); Assert.Equal(source.Reopening, detail.Reopening);
            Assert.False(detail.LaterPeriodClosed); Assert.DoesNotContain("LaterPeriodClosed", JsonSerializer.Serialize(detail), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(AccountingApiService.PeriodReopeningSourceFactsDigest(source), detail.SourceFactsDigest);
            Assert.Equal(11, JsonSerializer.SerializeToElement(detail.Reopening).EnumerateObject().Count());
        }
        else
        {
            var result = await service.ReadOpeningDecisionAsync(opening.Period.Id, _ => Task.CompletedTask);
            Assert.Equal(AccountingApiStatus.Ok, result.Status); var detail = Assert.IsType<AccountingPeriodOpeningDecisionDetailV1>(result.Resource);
            Assert.Equal(opening.Period.Id, detail.Period.PeriodId); Assert.Equal(1, detail.Period.Version);
            Assert.Equal(13, JsonSerializer.SerializeToElement(detail.Period).EnumerateObject().Count());
            Assert.Equal(AccountingApiService.PeriodOpeningSourceFactsDigest(detail.Period), detail.SourceFactsDigest);
        }
        Assert.DoesNotContain(nameof(IAccountingCommandLog.RecordCommandAsync), stores.Calls);
        Assert.DoesNotContain(nameof(IAccountingPeriodStore.ApprovePeriodAsync), stores.Calls);
        Assert.DoesNotContain(AccountingPermissionManifestV1.PeriodsRead, authority.Requested);
        Assert.Single(stores.Calls);
    }

    [Theory]
    [InlineData("permission")] [InlineData("scoped")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Last_completed_period_consumer_callback_cannot_release_stale_authority_or_cancelled_data(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            var source = PeriodSource(); var reopening = ReopeningSource(source);
            var stores = new Stores { OpeningDecision = source, ReopeningDecision = reopening };
            var company = new MutableCompany(); var user = new MutableUser(company);
            var authority = new PeriodDecisionAuthority(reopen ? AccountingPermissionManifestV1.PeriodsReopen : AccountingPermissionManifestV1.PeriodsApprove);
            var service = Service(authority, stores, company, user); using var cancellation = new CancellationTokenSource(); var callbacks = 0;
            Task Consumer(CancellationToken _)
            {
                if (++callbacks == 4) // service entry, store entry/read, service release: last awaited external fence.
                {
                    if (fault == "permission") authority.Allowed = false;
                    if (fault == "scoped") authority.CompanyWide = false;
                    if (fault == "company") company.Set(OtherCompany);
                    if (fault == "actor") user.ActorId = OtherActor.ToString("D");
                    if (fault == "member") user.Member = false;
                    if (fault == "cancel") cancellation.Cancel();
                }
                return Task.CompletedTask;
            }
            if (reopen)
            {
                if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadReopeningDecisionAsync(reopening.Reopening.Id, Consumer, cancellation.Token));
                else Assert.Equal(AccountingApiStatus.Denied, (await service.ReadReopeningDecisionAsync(reopening.Reopening.Id, Consumer)).Status);
            }
            else
            {
                if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadOpeningDecisionAsync(source.Period.Id, Consumer, cancellation.Token));
                else Assert.Equal(AccountingApiStatus.Denied, (await service.ReadOpeningDecisionAsync(source.Period.Id, Consumer)).Status);
            }
            Assert.Equal(4, callbacks); Assert.Single(stores.Calls);
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Period_purpose_rejects_only_the_actual_proposal_maker(bool reopen)
    {
        var opening = PeriodSource(preparer: Actor); var reopening = ReopeningSource(opening);
        var authority = new PeriodDecisionAuthority(reopen ? AccountingPermissionManifestV1.PeriodsReopen : AccountingPermissionManifestV1.PeriodsApprove);
        var stores = new Stores { OpeningDecision = opening, ReopeningDecision = reopening }; var service = Service(authority, stores);
        if (!reopen) Assert.Equal(AccountingApiStatus.Denied, (await service.ReadOpeningDecisionAsync(opening.Period.Id, _ => Task.CompletedTask)).Status);
        else
        {
            // The original opening maker may approve someone ELSE's reopening; no invented extra maker exclusion.
            Assert.Equal(AccountingApiStatus.Ok, (await service.ReadReopeningDecisionAsync(reopening.Reopening.Id, _ => Task.CompletedTask)).Status);
            stores.ReopeningDecision = reopening with { Reopening = reopening.Reopening with { ProposedBy = Actor } };
            Assert.Equal(AccountingApiStatus.Denied, (await service.ReadReopeningDecisionAsync(reopening.Reopening.Id, _ => Task.CompletedTask)).Status);
        }
    }

    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("closure")] [InlineData("missing-later-proof")]
    public async Task Purpose_reads_never_release_missing_foreign_or_misbound_owner_source(string fault)
    {
        var opening = PeriodSource(); var reopening = ReopeningSource(opening);
        var stores = new Stores { OpeningDecision = opening, ReopeningDecision = reopening };
        if (fault == "missing") { stores.OpeningDecision = null; stores.ReopeningDecision = null; }
        if (fault == "company")
        {
            stores.OpeningDecision = new(AccountingPeriod.Propose(opening.Period.Id, OtherCompany, opening.Period.StartsOn, opening.Period.EndsOn, OtherActor, PeriodDecisionAt), 1);
            stores.ReopeningDecision = reopening with { Reopening = reopening.Reopening with { CompanyId = OtherCompany } };
        }
        if (fault == "id")
        {
            stores.OpeningDecision = new(AccountingPeriod.Propose(Guid.NewGuid(), Company, opening.Period.StartsOn, opening.Period.EndsOn, OtherActor, PeriodDecisionAt), 1);
            stores.ReopeningDecision = reopening with { Reopening = reopening.Reopening with { Id = Guid.NewGuid() } };
        }
        if (fault == "missing-later-proof") stores.ReopeningDecision = reopening with { LaterPeriodClosed = null };
        if (fault == "closure") stores.ReopeningDecision = reopening with { Closure = reopening.Closure with { CompanyId = OtherCompany } };
        var expected = fault == "missing" ? AccountingApiStatus.NotFound : AccountingApiStatus.Unavailable;
        if (fault is not "closure" and not "missing-later-proof") Assert.Equal(expected, (await Service(new PeriodDecisionAuthority(AccountingPermissionManifestV1.PeriodsApprove), stores)
            .ReadOpeningDecisionAsync(opening.Period.Id, _ => Task.CompletedTask)).Status);
        Assert.Equal(expected, (await Service(new PeriodDecisionAuthority(AccountingPermissionManifestV1.PeriodsReopen), stores)
            .ReadReopeningDecisionAsync(reopening.Reopening.Id, _ => Task.CompletedTask)).Status);
        Assert.DoesNotContain(nameof(IAccountingCommandLog.RecordCommandAsync), stores.Calls);
    }

    [Theory]
    [InlineData("unknown-root")] [InlineData("unknown-resource")] [InlineData("private-proof")] [InlineData("key")]
    [InlineData("actor")] [InlineData("payload")] [InlineData("correlation")] [InlineData("company")] [InlineData("version")] [InlineData("source")]
    public void Original_opening_receipt_rejects_malformed_or_foreign_persisted_provenance(string fault)
    {
        var source = PeriodSource(approved: true); var answer = OpeningAnswer(source); var command = OpeningCommand(answer);
        if (fault.StartsWith("unknown", StringComparison.Ordinal) || fault == "private-proof")
        {
            var node = JsonNode.Parse(command.ResultJson)!;
            if (fault == "unknown-root") node["unexpected"] = true;
            else node["resource"]![fault == "private-proof" ? "approvalReceipt" : "unexpected"] = true;
            command = command with { ResultJson = node.ToJsonString() };
        }
        if (fault == "key") command = command with { IdempotencyKeyHash = "other" };
        if (fault == "actor") command = command with { ActorId = OtherActor.ToString("D") };
        if (fault == "payload") command = command with { RequestHash = "other" };
        if (fault == "correlation") command = command with { OriginalCorrelationId = "" };
        if (fault == "company") answer = answer with { Resource = answer.Resource! with { CompanyId = OtherCompany } };
        if (fault == "version") answer = answer with { Resource = answer.Resource! with { Version = 9 } };
        if (fault == "source") source = PeriodSource(approved: true, starts: new DateOnly(2026, 9, 1));
        Assert.Null(AccountingApiService.BindPeriodOpeningApprovalReceipt(command, source, answer, 1, "period-original", Actor));
    }

    [Fact]
    public void Opening_original_receipt_survives_later_period_version_and_public_thirteen_fields_stay_unchanged()
    {
        var source = PeriodSource(approved: true); var answer = OpeningAnswer(source); var command = OpeningCommand(answer);
        var receipt = AccountingApiService.BindPeriodOpeningApprovalReceipt(command, source with { Version = 20 }, answer, 1, "period-original", Actor);
        Assert.NotNull(receipt); Assert.Equal(answer.Resource!.StartsOn, receipt.PeriodStartsOn); Assert.Equal(answer.Resource.EndsOn, receipt.PeriodEndsOn);
        Assert.Equal(AccountingApiService.PeriodApprovalOriginalResultDigest(answer), receipt.OriginalResultDigest);
        Assert.Equal((1L, 2L), (receipt.ExpectedVersion, receipt.ResultVersion));
        var replay = answer with { IsReplay = true, OriginalCorrelationId = command.OriginalCorrelationId };
        Assert.Equal(receipt, AccountingApiService.BindPeriodOpeningApprovalReceipt(command, source with { Version = 20 }, replay, 1, "period-original", Actor));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(answer, json), JsonSerializer.Serialize(answer with { Resource = answer.Resource! with { ApprovalReceipt = receipt } }, json));
        Assert.Equal(13, JsonSerializer.SerializeToElement(answer.Resource).EnumerateObject().Count());
        Assert.Equal(8, JsonSerializer.SerializeToElement(answer).EnumerateObject().Count());
    }

    [Fact]
    public void Reopening_original_receipt_is_bound_to_its_exact_closure_proposal_and_both_expected_versions()
    {
        var source = ReopeningSource(PeriodSource(approved: true)) with { Period = PeriodSource(approved: true) with { Version = 4 } };
        var at = source.Reopening.ProposedAtUtc.AddHours(1);
        source = source with { Reopening = source.Reopening with { ApprovedBy = Actor, ApprovedAtUtc = at, Version = 2 },
            Closure = source.Closure with { ReopeningId = source.Reopening.Id, ReopenedBy = Actor, ReopenedAtUtc = at } };
        var answer = new AccountingApiResult<AccountingPeriodReopeningViewV1>(AccountingApiStatus.Ok, source.Reopening, false, null);
        var request = new ApproveAccountingPeriodReopeningV1(1, 3);
        var command = new AccountingCommandRecord("accounting.period.reopening.approve.v1", AccountingApiService.PeriodApprovalKeyHash("period-original"),
            Actor.ToString("D"), AccountingApiService.PeriodReopeningApprovalPayloadHash(source.Reopening.PeriodId, source.Reopening.Id, request),
            JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "original-correlation");
        var proof = AccountingApiService.BindPeriodReopeningApprovalReceipt(command, source, answer, request, "period-original", Actor);
        Assert.Null(AccountingApiService.BindPeriodReopeningApprovalReceipt(command, source with { LaterPeriodClosed = null }, answer, request, "period-original", Actor));
        var changedFlag = source with { LaterPeriodClosed = true };
        Assert.Equal(AccountingApiService.PeriodReopeningSourceFactsDigest(source), AccountingApiService.PeriodReopeningSourceFactsDigest(changedFlag));
        Assert.True(AccountingApiService.BindPeriodReopeningApprovalReceipt(command, changedFlag, answer, request, "period-original", Actor)!.LaterPeriodClosed);
        Assert.NotNull(proof); Assert.Equal(source.Period.Period.StartsOn, proof.PeriodStartsOn); Assert.Equal(source.Period.Period.EndsOn, proof.PeriodEndsOn);
        Assert.Equal(AccountingApiService.PeriodApprovalOriginalResultDigest(answer), proof.OriginalResultDigest);
        Assert.Equal(AccountingApiService.PeriodApprovalOriginalResultDigest(answer), AccountingApiService.PeriodApprovalOriginalResultDigest(answer with { IsReplay = true, OriginalCorrelationId = "original-correlation" }));
        Assert.False(proof.LaterPeriodClosed); Assert.Equal((1L, 3L, 2L), (proof.ExpectedVersion, proof.ExpectedPeriodVersion!.Value, proof.ResultVersion));
        Assert.Equal(proof, AccountingApiService.BindPeriodReopeningApprovalReceipt(command,
            source with { Period = source.Period with { Version = 30 } }, answer with { IsReplay = true, OriginalCorrelationId = command.OriginalCorrelationId }, request, "period-original", Actor));
        Assert.Null(AccountingApiService.BindPeriodReopeningApprovalReceipt(command, source, answer, request with { ExpectedPeriodVersion = 4 }, "period-original", Actor));
        Assert.Null(AccountingApiService.BindPeriodReopeningApprovalReceipt(command, source with { Closure = source.Closure with { CarryVoucherId = Guid.Empty } }, answer, request, "period-original", Actor));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(answer, json), JsonSerializer.Serialize(answer with { Resource = source.Reopening with { ApprovalReceipt = proof } }, json));
        Assert.Equal(11, JsonSerializer.SerializeToElement(source.Reopening).EnumerateObject().Count());
    }

    private static readonly DateTimeOffset PeriodDecisionAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static AccountingPeriodRecord PeriodSource(bool approved = false, Guid? preparer = null, DateOnly? starts = null)
    {
        var period = AccountingPeriod.Propose(View().PeriodId, Company, starts ?? new(2026, 10, 1), new(2026, 10, 31), preparer ?? OtherActor, PeriodDecisionAt);
        if (approved) period.Approve(Actor, PeriodDecisionAt.AddHours(1));
        return new(period, approved ? 2 : 1);
    }
    private static AccountingApiResult<PeriodViewV1> OpeningAnswer(AccountingPeriodRecord source) => new(AccountingApiStatus.Ok,
        new(source.Period.Id, Company, source.Period.StartsOn, source.Period.EndsOn, "Open", 2, source.Period.PreparedByUserId,
            PeriodDecisionAt, Actor, PeriodDecisionAt.AddHours(1), null, null, false), false, null);
    private static AccountingCommandRecord OpeningCommand(AccountingApiResult<PeriodViewV1> answer) => new(AccountingCommandNames.ApprovePeriod,
        AccountingApiService.PeriodApprovalKeyHash("period-original"), Actor.ToString("D"), AccountingApiService.PeriodOpeningApprovalPayloadHash(answer.Resource!.PeriodId, 1),
        JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "original-correlation");
    private static AccountingPeriodReopeningDecisionSourceV1 ReopeningSource(AccountingPeriodRecord period)
    {
        var id = Guid.Parse("a0000000-0000-4000-8000-0000000000d1"); var closure = Guid.Parse("a0000000-0000-4000-8000-0000000000d2");
        return new(period, new(id, Company, period.Period.Id, closure, 3, OtherActor, PeriodDecisionAt.AddDays(2), "SENTETIK reason", null, null, 1),
            new(closure, Company, period.Period.Id, 3, Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), 0, Actor, PeriodDecisionAt.AddDays(1), null, null, null)) { LaterPeriodClosed = false };
    }
    private sealed class PeriodDecisionAuthority(string held) : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true; public List<string> Requested { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Requested.Add(permission); return Task.FromResult(Allowed && permission == held ? Granted() with { IsCompanyWide = CompanyWide } : PermissionScope.Denied); }
    }
    private sealed partial class Stores : IAccountingPeriodDecisionStoreV1
    {
        public AccountingPeriodRecord? OpeningDecision; public AccountingPeriodReopeningDecisionSourceV1? ReopeningDecision;
        public async Task<AccountingPeriodRecord?> ReadOpeningDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); Calls.Add("ReadOpeningDecisionAsync"); var source = OpeningDecision; await fence(token); return source; }
        public async Task<AccountingPeriodReopeningDecisionSourceV1?> ReadReopeningDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); Calls.Add("ReadReopeningDecisionAsync"); var source = ReopeningDecision; await fence(token); return source; }
    }
}
