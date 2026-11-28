using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work host + source + authority decorator; controlled command/receipt protocol only.
// This fixture is not native Identity SQL, Audit integrity or transaction acceptance.
public sealed class IdentityAccessGrantWorkDecisionHostTests
{
    private const string Kind = "identity.access-grant-request.v1", Key = "SENTETIK-access-work-key", Corr = "SENTETIK-access-work-correlation";
    internal static readonly Guid Target = Guid.Parse("ac580000-0000-4000-8000-000000000003"), Role = Guid.Parse("ac580000-0000-4000-8000-000000000004");

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Human_selected_token_and_original_key_drive_exactly_one_command_without_a_POST_human_read(bool approve)
    {
        using var rig = new Rig(); var preview = await rig.Host.PreviewAsync(Kind, Id, default);
        var human = Assert.IsType<WorkIdentityAccessGrantPreviewV1>(preview.IdentityAccessGrant);
        Assert.Equal(rig.Port.Detail.Request, human.Request); Assert.Equal(rig.Port.Detail.CurrentPermissions, human.CurrentPermissions);
        Assert.Equal(rig.Port.Detail.CurrentImpactDigest, human.ExpectedImpactDigest);
        Assert.Equal(2, rig.Port.Reads);
        var request = rig.Request with { Approve = approve, Reason = approve ? null : "not_required" };
        var answer = await rig.Decide(request);
        Assert.Equal(approve ? "Approved" : "Rejected", answer.State); Assert.False(answer.Replayed);
        Assert.Equal(3, rig.Port.Reads); Assert.Equal(1, rig.Port.ReceiptReads); Assert.Equal(1, rig.Port.Effects);
        Assert.Equal(request.IdentityAccessGrant!.ExpectedImpactDigest, rig.Port.LastToken);
        Assert.Equal(Key, rig.Port.LastMetadata!.IdempotencyKey); Assert.Equal(Corr, answer.OriginalCorrelationId);
        Assert.All(rig.Authority.Asked, p => Assert.Equal(IdentityPermissionManifestV1.AccessApprove, p));
        Assert.Equal(5, JsonSerializer.SerializeToElement(request, Json).EnumerateObject().Count());
        rig.Port.Permissions = [IdentityPermissionManifestV1.AccessApprove];
        var old = await rig.Decide(request);
        Assert.True(old.Replayed); Assert.Equal(answer.State, old.State); Assert.Equal(Corr, old.OriginalCorrelationId);
        Assert.Equal(4, rig.Port.Reads); Assert.Equal(1, rig.Port.Effects);
    }

    [Fact]
    public async Task Changed_permissions_after_preview_reach_native_with_the_seen_token_and_cannot_approve_unseen_permissions()
    {
        using var rig = new Rig(); var frozen = rig.Request;
        rig.Port.Permissions = [IdentityPermissionManifestV1.AccessApprove];
        Assert.NotEqual(frozen.IdentityAccessGrant!.ExpectedImpactDigest, rig.Port.Detail.CurrentImpactDigest);
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(frozen))).Status);
        Assert.Equal(frozen.IdentityAccessGrant.ExpectedImpactDigest, rig.Port.LastToken);
        Assert.Equal(0, rig.Port.Effects); Assert.Equal(0, rig.Port.ReceiptReads);
    }

    [Fact]
    public async Task Old_key_with_a_different_client_token_cannot_relabel_an_original_approval_as_approval_of_new_permissions()
    {
        using var rig = new Rig(); var original = rig.Request; await rig.Decide(original);
        rig.Port.Permissions = [IdentityPermissionManifestV1.AccessApprove];
        var different = original with { IdentityAccessGrant = new(rig.Port.Detail.CurrentImpactDigest) };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(different))).Status);
        Assert.Equal(1, rig.Port.Effects);
        var reconciled = await rig.Decide(original); Assert.True(reconciled.Replayed);
        Assert.Equal(1, rig.Port.Effects); Assert.Equal(Corr, reconciled.OriginalCorrelationId);
    }

    [Theory] [InlineData("absent")] [InlineData("count")] [InlineData("digest")]
    public async Task Approval_requires_actual_historical_impact_equal_to_the_frozen_selection_even_on_replay(string fault)
    {
        using var rig = new Rig(); var request = rig.Request;
        rig.Port.ChangeReceipt = r => fault switch
        {
            "absent" => r with { AppliedImpactDigest = null, AppliedPermissionCount = null },
            "count" => r with { AppliedPermissionCount = null }, _ => r with { AppliedImpactDigest = new('A', 64) }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(1, rig.Port.Effects);
        rig.Port.ChangeReceipt = null; Assert.True((await rig.Decide(request)).Replayed);
        Assert.Equal(1, rig.Port.Effects);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("actor")] [InlineData("key")]
    [InlineData("payload")] [InlineData("source")] [InlineData("answer")]
    public async Task Malformed_or_foreign_original_receipt_suppresses_release_without_claiming_rollback(string fault)
    {
        using var rig = new Rig(); rig.Port.ChangeReceipt = r => fault switch
        {
            "missing" => null, "company" => r with { CompanyId = Guid.NewGuid() }, "actor" => r with { ActorId = Maker.ToString("D") },
            "key" => r with { KeyHash = new('A', 64) }, "payload" => r with { PayloadHash = new('A', 64) },
            "source" => r with { SourceFactsDigest = new('A', 64) }, _ => r with { OriginalAnswerDigest = new('A', 64) }
        };
        var request = rig.Request;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(1, rig.Port.Effects); rig.Port.ChangeReceipt = null;
        Assert.True((await rig.Decide(request)).Replayed); Assert.Equal(1, rig.Port.Effects);
    }

    [Theory]
    [InlineData("none")] [InlineData("lf")] [InlineData("crlf")] [InlineData("lower")]
    [InlineData("reason-on-approve")] [InlineData("reject-code")] [InlineData("reject-long")]
    [InlineData("version")] [InlineData("cancellation")] [InlineData("normal")]
    public async Task Invalid_selection_native_reason_or_foreign_selector_is_rejected_before_source_IO(string fault)
    {
        using var rig = new Rig(); var q = fault switch
        {
            "none" => rig.Request with { IdentityAccessGrant = null },
            "lf" => rig.Request with { IdentityAccessGrant = new(new string('A', 63) + "\n") },
            "crlf" => rig.Request with { IdentityAccessGrant = new(new string('A', 62) + "\r\n") },
            "lower" => rig.Request with { IdentityAccessGrant = new(new string('a', 64)) },
            "reason-on-approve" => rig.Request with { Reason = "not_required" },
            "reject-code" => rig.Request with { Approve = false, Reason = "not canonical" },
            "reject-long" => rig.Request with { Approve = false, Reason = new('a', 65) },
            "version" => rig.Request with { ExpectedVersion = "01" },
            "cancellation" => rig.Request with { InventoryCostCancellation = new("1") },
            _ => rig.Request with { NormalJournalDraft = new(Id) }
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(q))).Status);
        Assert.Equal(0, rig.Port.Reads); Assert.Equal(0, rig.Port.Calls);
    }

    [Theory] [InlineData("maker")] [InlineData("target")] [InlineData("company")]
    [InlineData("permission")] [InlineData("scoped")]
    public async Task Current_context_and_native_second_person_exclusions_remain_closed(string fault)
    {
        using var rig = new Rig();
        if (fault == "maker") rig.User.Actor = Maker; if (fault == "target") rig.User.Actor = Target;
        if (fault == "company") rig.Company.Id = Guid.NewGuid();
        if (fault == "permission") rig.Authority.Allowed = false; if (fault == "scoped") rig.Authority.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("native")] [InlineData("receipt")] [InlineData("delegation")]
    public async Task Late_authority_or_delegation_loss_is_rechecked_inside_native_and_before_receipt_release(string fault)
    {
        using var rig = new Rig();
        var request = rig.Request;
        if (fault == "native") rig.Port.BeforeCommand = () => rig.Authority.Allowed = false;
        if (fault == "receipt") rig.Port.BeforeReceipt = () => rig.Authority.Allowed = false;
        if (fault == "delegation") { request = request with { DelegationId = Guid.NewGuid() }; rig.Port.BeforeCommand = () => rig.Delegations.Live = false; }
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(fault == "receipt" ? 1 : 0, rig.Port.Effects);
    }

    [Fact]
    public async Task Missing_owner_is_unavailable_and_plain_native_request_has_no_invented_maker_exclusion()
    {
        using var rig = new Rig();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.BuildHost(null).PreviewAsync(Kind, Id, default))).Status);
        rig.Port.Critical = false; rig.User.Actor = Maker;
        Assert.Equal("Approved", (await rig.Decide()).State); Assert.Equal(1, rig.Port.Effects);
    }

    [Fact]
    public async Task Selection_on_another_kind_is_rejected_before_source_or_native_IO()
    {
        using var rig = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.SalesOrder, Id, rig.Request, Key, default))).Status);
        Assert.Equal(0, rig.Port.Reads); Assert.Equal(0, rig.Port.Calls);
    }

    internal static IdentityAccessGrantDecisionDetailV1 Facts(IReadOnlyList<string>? permissions = null, string status = "Pending", bool critical = true)
    {
        permissions ??= [IdentityPermissionManifestV1.RolesManage];
        var request = new IdentityAccessGrantRequestView(Id, new(Maker, "SENTETIK requester"), new(Target, "SENTETIK target"), Role,
            "SENTETIK_ROLE", "SENTETIK role", status, At, At, At.AddDays(1), "SENTETIK request reason", status == "Pending" ? "1" : "2");
        var source = IdempotencyHash.ComputePayload(new { Id, CompanyId = Company, RequestedByUserId = Maker, TargetUserId = Target,
            RoleId = Role, RequiresSecondApproval = critical, RequestedAtUtc = At, ValidFromUtc = (DateTimeOffset?)At,
            ValidUntilUtc = (DateTimeOffset?)At.AddDays(1), Reason = request.Reason });
        var impact = IdempotencyHash.ComputePayload(new { Source = source, RoleCode = request.RoleCode, Permissions = permissions });
        var detail = new IdentityAccessGrantDecisionDetailV1(request, critical, true, true, permissions, permissions.Any(ErpPermissions.IsCritical), source, impact, "");
        return detail with { PreviewDigest = IdempotencyHash.ComputePayload(detail) };
    }

    private sealed class Rig : IDisposable
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new();
        internal readonly Delegations Delegations = new(); internal readonly Protocol Port; private readonly IdentityDbContext db;
        private readonly CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new();
        private readonly WorkDecisionAuthorityV1 scoped; internal WorkDecisionHostV1 Host { get; }
        internal Rig()
        {
            correlation.Set(Corr, null, "Web", null, null); scoped = new(Authority, execution); Port = new(this, scoped);
            db = new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase("SENTETIK-access-host-" + Guid.NewGuid()).Options);
            Host = BuildHost(Port);
        }
        internal WorkDecisionHostV1 BuildHost(IIdentityAccessGrantDecisionV1? owner) => new(Company, User, correlation, scoped,
            [new IdentityAccessGrantRequestWorkInboxSourceV1(db, Company, User, scoped, Port)], Delegations, execution, null!, null!, identityAccessDecisions: owner);
        internal WorkDecisionRequestV1 Request => new("1", true, null) { IdentityAccessGrant = new(Port.Detail.CurrentImpactDigest) };
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null) => Host.DecideAsync(Kind, Id, request ?? Request, Key, default);
        public void Dispose() => db.Dispose();
    }
    private sealed record Saved(IdentityAccessCommandResultV1 Result, IdentityAccessGrantDecisionReceiptV1 Receipt);
    private sealed class Protocol(Rig rig, IErpAuthorityRevalidationService authority) : IIdentityAccessGrantDecisionV1
    {
        internal IReadOnlyList<string> Permissions = [IdentityPermissionManifestV1.RolesManage]; internal bool Critical = true;
        internal string Status = "Pending"; internal int Reads, Calls, ReceiptReads, Effects; internal string? LastToken;
        internal IdentityAccessCommandMetadataV1? LastMetadata; internal Action? BeforeCommand, BeforeReceipt;
        internal Func<IdentityAccessGrantDecisionReceiptV1, IdentityAccessGrantDecisionReceiptV1?>? ChangeReceipt;
        private readonly Dictionary<string, Saved> commands = [];
        internal IdentityAccessGrantDecisionDetailV1 Detail => Facts(Permissions, Status, Critical);
        public Task<IdentityAccessGrantDecisionDetailV1?> ReadDecisionAsync(Guid id, IdentityAccessCommandMetadataV1 m, Func<CancellationToken, Task> f, CancellationToken t = default) =>
            ReadDecisionAsync(id, new IdentityAccessGrantDecisionReadContextV1(m.CompanyId, m.ActorId, m.CorrelationId, m.CausationId), f, t);
        public async Task<IdentityAccessGrantDecisionDetailV1?> ReadDecisionAsync(Guid id, IdentityAccessGrantDecisionReadContextV1 m, Func<CancellationToken, Task> f, CancellationToken t = default)
        {
            await f(t); Reads++; await f(t);
            if (Critical && (rig.User.Actor == Maker || rig.User.Actor == Target)) throw new UnauthorizedAccessException();
            return id == Id ? Detail : null;
        }
        public async Task<IdentityAccessCommandResultV1> DecidePurposeAsync(Guid id, bool approve, string expectedVersion, string? reason,
            string token, IdentityAccessCommandMetadataV1 m, Func<CancellationToken, Task> f, CancellationToken t = default)
        {
            Calls++; LastToken = token; LastMetadata = m; BeforeCommand?.Invoke(); await Fence(m, f, t);
            object payload = approve ? new ApproveAccessGrantRequestV1(expectedVersion) : new RejectAccessGrantRequestV1(expectedVersion, reason!);
            var hash = IdempotencyHash.ComputePayload(new { Command = approve ? IdentityAccessCommandsV1.Approve : IdentityAccessCommandsV1.Reject, Target = id.ToString("D"), Request = payload });
            if (commands.TryGetValue(m.IdempotencyKey, out var old))
                return old.Receipt.PayloadHash == hash && old.Receipt.ActorId == m.ActorId ? old.Result with { IsReplay = true }
                    : new(new(409, "idempotency_conflict", "SENTETIK", "SENTETIK"), false, m.CorrelationId);
            if (Status != "Pending" || expectedVersion != "1" || approve && token != Detail.CurrentImpactDigest)
                return new(new(409, "impact_changed", "SENTETIK", "SENTETIK"), false, m.CorrelationId);
            var d = Detail; Status = approve ? "Approved" : "Rejected"; Effects++;
            var answer = new IdentityAccessCommandAnswerV1(200, null, "SENTETIK", "SENTETIK", new(id, "2", Status, approve ? Guid.NewGuid() : null));
            var result = new IdentityAccessCommandResultV1(answer, false, m.CorrelationId);
            var receipt = new IdentityAccessGrantDecisionReceiptV1(m.CompanyId, id, Target, Role, Maker, At, expectedVersion, "2",
                approve ? IdentityAccessCommandsV1.Approve : IdentityAccessCommandsV1.Reject, IdempotencyHash.ComputeKey(m.IdempotencyKey), hash,
                m.ActorId, m.CorrelationId, At.AddMinutes(1), d.SourceFactsDigest, answer, IdempotencyHash.ComputePayload(answer))
                { AppliedImpactDigest = approve ? token : null, AppliedPermissionCount = approve ? d.CurrentPermissions.Count : null };
            commands.Add(m.IdempotencyKey, new(result, receipt)); return result;
        }
        public async Task<IdentityAccessGrantDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid id, bool approve, string expectedVersion,
            string? reason, IdentityAccessCommandMetadataV1 m, Func<CancellationToken, Task> f, CancellationToken t = default)
        {
            ReceiptReads++; BeforeReceipt?.Invoke(); await Fence(m, f, t);
            return commands.TryGetValue(m.IdempotencyKey, out var saved) ? ChangeReceipt is null ? saved.Receipt : ChangeReceipt(saved.Receipt) : null;
        }
        private async Task Fence(IdentityAccessCommandMetadataV1 m, Func<CancellationToken, Task> f, CancellationToken t)
        {
            t.ThrowIfCancellationRequested(); await f(t);
            var permission = await authority.RevalidatePermissionScopeAsync(IdentityPermissionManifestV1.AccessApprove, m.CompanyId, t);
            if (!permission.IsGranted || !permission.IsCompanyWide || rig.Company.Id != m.CompanyId || rig.User.UserId != m.ActorId) throw new UnauthorizedAccessException();
            t.ThrowIfCancellationRequested();
        }
    }
    private sealed class MutableCompany : ICompanyContext
    {
        internal Guid Id = InventoryCostWorkDecisionHostTests.Company; public bool IsResolved => true; public Guid CompanyId => Id;
        public void Set(Guid id) => Id = id; public void Clear() => Id = Guid.Empty;
    }
    private sealed class MutableUser : ICurrentUser
    {
        internal Guid Actor = InventoryCostWorkDecisionHostTests.Actor; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => id == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true, CompanyWide = true; internal List<string> Asked = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken t = default)
        { Asked.Add(p); return Task.FromResult(new PermissionScope(Allowed && p == IdentityPermissionManifestV1.AccessApprove, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, IdentityPermissionManifestV1.AccessApprove, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
