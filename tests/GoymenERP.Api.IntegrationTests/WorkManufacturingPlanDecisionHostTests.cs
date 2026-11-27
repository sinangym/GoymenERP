using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real native command replay/validation composed with synthetic stores/authority. No SQL/browser acceptance.</summary>
public sealed partial class WorkManufacturingPlanDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("e8230000-0000-4000-8000-000000000001");
    private static readonly Guid PlanId = Guid.Parse("e8230000-0000-4000-8000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("e8230000-0000-4000-8000-000000000003");
    private const string Actor = "e8230000-0000-4000-8000-000000000003";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_rejection_replay_is_draft_with_history_not_a_fabricated_rejected_status(bool pending)
    {
        var run = new Run(); run.Source.Pending = pending; run.Source.Maker = null;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, default);
        Assert.Equal(pending, preview.Pending); Assert.True(preview.RejectionSupported);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId,
            new("1", false, "  SENTETIK red  "), "native-key", default);
        Assert.Equal(("Draft", true, "SENTETIK-original"), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(1, run.CommandReads);
        Assert.All(run.Raw.Asked, permission => Assert.Equal(ManufacturingPermissionManifestV1.PlansApprove, permission));
    }

    [Fact]
    public async Task Current_reapproval_requires_explicit_selection_without_invoking_any_native_command_or_selecting_labels()
    {
        var run = new Run(); run.Source.Reapproval = true;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, default);
        Assert.False(preview.RejectionSupported);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", true, null), "no-implicit-selection", default))).Status);
        Assert.Equal(0, run.CommandReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task First_approval_immutable_native_plan_receipt_replays_from_pending_or_terminal_purpose_metadata(bool pending)
    {
        var run = new Run { ApprovalAnswer = true }; run.Source.Pending = pending; run.Source.Maker = null;
        run.View = ApprovalView();
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId,
            new("1", true, null), "native-key", default);
        Assert.Equal(("Approved", true, "SENTETIK-original"), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(1, run.CommandReads);
        Assert.All(run.Raw.Asked, permission => Assert.Equal(ManufacturingPermissionManifestV1.PlansApprove, permission));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("id")]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("approver")]
    [InlineData("approval-time")]
    [InlineData("reapproval-required")]
    [InlineData("reapproval-history")]
    [InlineData("order")]
    [InlineData("maker")]
    public async Task First_approval_never_accepts_an_unproven_or_reapproval_native_plan_receipt(string fault)
    {
        var run = new Run { ApprovalAnswer = true }; var view = ApprovalView();
        run.View = fault switch
        {
            "company" => view with { CompanyId = Guid.NewGuid() }, "id" => view with { PlanId = Guid.NewGuid() },
            "status" => view with { Status = "Draft" }, "version" => view with { Version = 3 },
            "approver" => view with { ApprovedByUserId = Guid.NewGuid() }, "approval-time" => view with { ApprovedAtUtc = null },
            "reapproval-required" => view with { RequiresReapproval = true },
            "reapproval-history" => view with { Reapprovals = [new(1, ActorId, DateTimeOffset.UtcNow, 2)] },
            "order" => view with { OrderId = Guid.NewGuid() }, "maker" => view with { PreparedBy = Actor },
            _ => throw new NotSupportedException(),
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", true, null), "native-key", default))).Status);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("fingerprint")]
    public async Task First_approval_preserves_native_immutable_receipt_actor_and_fingerprint_conflicts(string mismatch)
    {
        var run = new Run { ApprovalAnswer = true }; run.View = ApprovalView();
        if (mismatch == "actor") run.StoredActor = Guid.NewGuid().ToString("D");
        else run.StoredHash = Hash("different canonical approval");
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", true, null), "native-key", default));
        Assert.Equal((409, ManufacturingApiCodes.IdempotencyConflict), (refusal.Status, refusal.Code));
        Assert.Equal(1, run.CommandReads);
    }

    private static PlanViewV1 ApprovalView() => new(PlanId, Company, Company, "Slitting", "Approved", 2,
        [], null, ActorId, DateTimeOffset.UtcNow, Reapprovals: []);

    [Theory]
    [InlineData("other-owner")]
    [InlineData("other-module")]
    [InlineData("blank-maker")]
    public async Task Legacy_null_maker_exception_is_limited_to_the_native_manufacturing_plan_kind(string fault)
    {
        var run = new Run(fault == "other-owner" ? WorkDecisionHostV1.PurchaseRequest : WorkDecisionHostV1.ManufacturingPlan);
        run.Source.Maker = fault == "blank-maker" ? " " : null;
        if (fault == "other-module") run.Source.OwnerModule = "purchasing";
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, PlanId, default))).Status);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("id")]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("history")]
    [InlineData("actor")]
    [InlineData("reason")]
    [InlineData("maker")]
    public async Task Malformed_native_immutable_rejection_answer_is_unavailable(string fault)
    {
        var run = new Run();
        run.View = fault switch
        {
            "company" => run.View with { CompanyId = Guid.NewGuid() }, "id" => run.View with { PlanId = Guid.NewGuid() },
            "status" => run.View with { Status = "Approved" }, "version" => run.View with { Version = 3 },
            "history" => run.View with { Rejections = [] },
            "actor" => run.View with { Rejections = [new(1, "SENTETIK red", Guid.NewGuid(), DateTimeOffset.UtcNow, 2)] },
            "reason" => run.View with { Rejections = [new(1, "SENTETIK other", ActorId, DateTimeOffset.UtcNow, 2)] },
            "maker" => run.View with { PreparedBy = Actor }, _ => throw new NotSupportedException(),
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", false, "SENTETIK red"), "native-key", default))).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("overlong")]
    [InlineData("control")]
    public async Task Native_rejection_reason_rules_remain_trimmed_256_and_control_free(string? input)
    {
        var run = new Run();
        var reason = input == "overlong" ? new string('x', 257) : input == "control" ? "SENTETIK\nred" : input;
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", false, reason), "native-key", default))).Status);
        Assert.Equal(0, run.CommandReads);
    }

    [Fact]
    public async Task Maker_guid_equivalence_and_late_delegation_loss_never_release_a_native_answer()
    {
        var run = new Run(); run.Source.Maker = ActorId.ToString("B").ToUpperInvariant();
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, default))).Status);
        run = new Run(); run.BeforeNativeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", false, "SENTETIK red", Guid.NewGuid()), "native-key", default))).Status);
        Assert.Equal(0, run.CommandReads);
    }

    private sealed class Run
    {
        public readonly RawAuthority Raw = new(); public readonly Source Source;
        public readonly Delegations Delegations = new(); public readonly WorkDecisionHostV1 Host;
        public Action? BeforeNativeFence; public int CommandReads; public bool ApprovalAnswer, ReapprovalAnswer;
        public Guid[] StoredSelection = [LabelA, LabelB];
        public string StoredActor = Actor; public string? StoredHash;
        public PlanViewV1 View = new(PlanId, Company, Company, "Slitting", "Draft", 2, [], null, null, null,
            Rejections: [new(1, "SENTETIK red", ActorId, DateTimeOffset.UtcNow, 2)]);
        public Run(string kind = WorkDecisionHostV1.ManufacturingPlan)
        {
            Source = new(kind); var company = new CompanyContext(); company.Set(Company);
            var user = new User(); var correlation = new CorrelationContext(); correlation.Set("SENTETIK-current", null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Raw, execution);
            var plans = DispatchProxy.Create<IManufacturingPlanStore, Proxy>();
            var orders = DispatchProxy.Create<IManufacturingOrderStore, Proxy>();
            var commands = DispatchProxy.Create<IManufacturingCommandLog, Proxy>();
            ((Proxy)(object)commands).Handler = (method, args) =>
            {
                if (method.Name != "FindCommandAsync") throw new NotSupportedException();
                CommandReads++;
                var command = ReapprovalAnswer ? ManufacturingCommandNames.ReapprovePlan : ApprovalAnswer ? ManufacturingCommandNames.ApprovePlan : ManufacturingCommandNames.RejectPlan;
                Assert.Equal(command, args[0]);
                object payload = ReapprovalAnswer ? new { planId = PlanId, expectedVersion = 1L, selectedLabelIds = StoredSelection.Order().ToArray() }
                    : ApprovalAnswer ? new { planId = PlanId, expectedVersion = 1L }
                    : new { planId = PlanId, expectedVersion = 1L, reason = "SENTETIK red" };
                var canonical = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var record = new ManufacturingCommandRecord(command, (string)args[1]!, StoredActor,
                    StoredHash ?? Hash(command + "|" + canonical),
                    JsonSerializer.Serialize(new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, View, false, null), new JsonSerializerOptions(JsonSerializerDefaults.Web)), "SENTETIK-original");
                return Task.FromResult<ManufacturingCommandRecord?>(record);
            };
            var lifecycle = DispatchProxy.Create<IManufacturingPlanLifecycleStore, Proxy>();
            async Task<ManufacturingApiResult<PlanViewV1>> Guard(object?[] args)
            {
                BeforeNativeFence?.Invoke();
                var fence = (Func<CancellationToken, Task>)args[1]!;
                await fence(default);
                var answer = await ((Func<CancellationToken, Task<ManufacturingApiResult<PlanViewV1>>>)args[0]!)(default);
                await fence(default); return answer;
            }
            ((Proxy)(object)lifecycle).Handler = (method, args) => method.Name == "ExecutePlanRejectionAsync" ? Guard(args) : throw new NotSupportedException();
            var native = new ManufacturingCommandService(plans, orders, commands, new ManufacturingMassVerifier(null!), authority,
                company, user, correlation, TimeProvider.System, planLifecycle: lifecycle);
            Host = new(company, user, correlation, authority, [Source], Delegations, execution, null!, null!, manufacturingPlans: native);
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args!);
    }
    private sealed class Source(string kind) : IWorkApprovalDecisionSourceV1
    {
        public string? Maker = Guid.NewGuid().ToString("D"); public bool Pending = true, Reapproval;
        public string Version = "1";
        public WorkManufacturingPlanReapprovalOptionsV1? Options = new([LabelA, LabelB]);
        public string OwnerModule = "manufacturing";
        public string Module => OwnerModule; public string Kind => kind;
        public string ApproverPermission => ManufacturingPermissionManifestV1.PlansApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company, new(Module, Kind, itemId, "SENTETIK plan", DateTimeOffset.UtcNow,
                "/uretim/plan/" + itemId, Maker, Reapproval ? [new("manufacturing.plan-reapproval-after-order-cancel", null, null, null, null)]
                    : Pending ? [new("manufacturing.plan-first-approval", null, null, null, null)] : [], []), Version, Pending, Reapproval ? Options : null));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class RawAuthority : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Asked.Add(permission); return Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Allowed = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, Company, item.Kind, ManufacturingPermissionManifestV1.PlansApprove,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow,
                "SENTETIK", null, null, null) : null);
    }
}
