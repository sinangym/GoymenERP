using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Synthetic native receipts/authority for the host boundary. Native SQL composition is separate source coverage.
public sealed class WorkLogisticsWeighingDifferenceDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Record = Guid.NewGuid(), Note = Guid.NewGuid(), Instruction = Guid.NewGuid(), Product = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-recorder", Enterer = "SENTETIK-enterer";
    private static WorkLogisticsWeighingDifferenceProofV1 Proof() => new(Company, Record, Note, Instruction, 100, 110, 10,
        "NotEvaluated", null, null, null, 10, 5, "ApprovalRequired", [new(Product, 100, 110, 10, 0, 0, 10, 1)],
        [Enterer, Maker], true, Maker, At, null);
    private static WeighingDifferenceViewV1 Value() => new(Company, Record, Note, Instruction, 100, 110, 10, "NotEvaluated",
        null, null, null, 10, 10, 5, "ApprovalRequired", "Approved", 2,
        [new(Product, 100, 110, 10, 0, 0, 10, 1)], [Enterer, Maker], true, Maker, At, Actor, At.AddHours(1));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Approve_and_original_key_replay_use_exact_native_integer_version_and_immutable_evaluation(bool replay)
    {
        var run = new Rig(); run.Owner.Answer = new(Value(), replay, replay ? "SENTETIK-original" : "SENTETIK-work");
        run.Source.Pending = !replay; run.Source.Version = replay ? "2" : "1";
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Record, default);
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var result = await run.Host.DecideAsync(run.Source.Kind, Record, new("1", true, null), "original-key", default);
        Assert.Equal(("Approved", replay), (result.State, result.Replayed)); Assert.Equal(new ApproveWeighingDifferenceV1(1), run.Owner.Captured);
        Assert.Equal(Record, run.Owner.Id);
    }

    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData(" 1")] [InlineData("+1")] [InlineData("1.0")] [InlineData("2147483648")]
    public async Task Noncanonical_or_non_Int32_versions_never_reach_native_owner(string version)
    {
        var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Record, new(version, true, null), "key", default))).Status);
        Assert.Null(run.Owner.Captured);
    }

    [Fact]
    public async Task Reject_reason_and_other_typed_requests_are_not_reinterpreted_as_native_approval()
    {
        var run = new Rig(); WorkDecisionRequestV1[] invalid = [new("1", false, "SENTETIK"), new("1", true, ""),
            new("1", true, null, ManufacturingPlanReapproval: new([Guid.NewGuid()])),
            new("1", true, null, InventoryTransferLoss: new(1, At)), new("1", true, null, InventoryTransferOverage: new(1, At)),
            new("1", true, null, LogisticsPlanExcess: new("1"))];
        foreach (var body in invalid) Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Record, body, "key", default))).Status);
        Assert.Null(run.Owner.Captured);
    }

    [Theory]
    [InlineData("missing")] [InlineData("incomplete")] [InlineData("actors")] [InlineData("note")] [InlineData("company")]
    public async Task Incomplete_or_unbound_owner_proof_is_unavailable(string fault)
    {
        var run = new Rig(); var proof = Proof(); run.Source.Proof = fault switch
        {
            "missing" => null, "incomplete" => proof with { EnteredByComplete = false }, "actors" => proof with { EnteredBy = [Maker] },
            "note" => proof with { DispatchNoteId = Guid.NewGuid() }, _ => proof with { CompanyId = Guid.NewGuid() }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Record, default))).Status);
        Assert.Null(run.Owner.Captured);
    }

    [Theory]
    [InlineData("company")] [InlineData("record")] [InlineData("note")] [InlineData("instruction")]
    [InlineData("version")] [InlineData("status")] [InlineData("evaluation")] [InlineData("products")]
    [InlineData("actors")] [InlineData("incomplete")] [InlineData("recorder")] [InlineData("time")] [InlineData("approver")]
    public async Task Wrong_native_receipt_cannot_be_projected_as_Work_success(string fault)
    {
        var run = new Rig(); var value = Value(); run.Owner.Answer = new(fault switch
        {
            "company" => value with { CompanyId = Guid.NewGuid() }, "record" => value with { RecordId = Guid.NewGuid() },
            "note" => value with { DispatchNoteId = Guid.NewGuid() }, "instruction" => value with { InstructionId = Guid.NewGuid() },
            "version" => value with { Version = 3 }, "status" => value with { Status = "PendingApproval" },
            "evaluation" => value with { TotalExcessKg = 11 }, "products" => value with { Products = [value.Products[0] with { ScaleKg = 111 }] },
            "actors" => value with { EnteredBy = [Maker] }, "incomplete" => value with { EnteredByComplete = false },
            "recorder" => value with { RecordedBy = Enterer }, "time" => value with { ApprovedAtUtc = At.ToOffset(TimeSpan.FromHours(3)) },
            _ => value with { ApprovedBy = Maker }
        }, false, "SENTETIK-work");
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Record, new("1", true, null), "key", default))).Status);
    }

    [Fact]
    public async Task Recorder_enterer_and_revoked_delegation_are_refused_without_native_effects()
    {
        foreach (var actor in new[] { Maker, Enterer })
        {
            var own = new Rig(actor);
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => own.Host.PreviewAsync(own.Source.Kind, Record, default))).Status);
            Assert.Null(own.Owner.Captured);
        }
        var run = new Rig(); run.Owner.BeforeFence = () => run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Record,
            new("1", true, null, Guid.NewGuid()), "key", default))).Status);
        Assert.False(run.Owner.Effect);
    }

    [Fact]
    public async Task Generic_snapshot_and_human_preview_never_serialize_internal_receipt_proof()
    {
        var run = new Rig(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("logisticsWeighingDifferenceProof", JsonSerializer.Serialize(await run.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Record, default), options));
        Assert.DoesNotContain("logisticsWeighingDifferenceProof", JsonSerializer.Serialize(await run.Host.PreviewAsync(run.Source.Kind, Record, default), options));
    }

    private sealed class Rig
    {
        public readonly Source Source = new(); public readonly Delegations Delegations = new(); public readonly Owner Owner;
        public readonly WorkDecisionHostV1 Host;
        public Rig(string actor = Actor)
        {
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new WorkInventoryTransferLossDecisionHostTests.LossAuthority(), execution);
            Owner = new(authority);
            Host = new(company, new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor), correlation, authority, [Source], Delegations, execution,
                null!, null!, logisticsWeighingDifferences: Owner);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public string Version = "1"; public bool Pending = true; public WorkLogisticsWeighingDifferenceProofV1? Proof = WorkLogisticsWeighingDifferenceDecisionHostTests.Proof();
        public string Module => "logistics"; public string Kind => WorkDecisionHostV1.LogisticsWeighingDifference;
        public string ApproverPermission => LogisticsPermissionManifestV1.WeighingDifferencesApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company, new(Module, Kind, Record, "SENTETIK-note", At,
                "/lojistik/yukleme-talimatlari/" + Instruction, Maker, [], [new("dispatch-note", "SENTETIK", Note, null),
                    new("loading-instruction", "SENTETIK", Instruction, null)], [Enterer]), Version, Pending, LogisticsWeighingDifferenceProof: Proof));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Owner(IErpAuthorityRevalidationService authority) : ILogisticsWeighingDifferenceServiceV1
    {
        public Guid Id; public ApproveWeighingDifferenceV1? Captured; public Action? BeforeFence; public bool Effect;
        public WeighingDifferenceResponseV1 Answer = new(Value(), false, "SENTETIK-work");
        public async Task<WeighingDifferenceResponseV1> ApproveAsync(Guid recordId, ApproveWeighingDifferenceV1 request, LogisticsCommandMetadata metadata, CancellationToken token)
        {
            Id = recordId; Captured = request; BeforeFence?.Invoke();
            if (!(await authority.RevalidatePermissionScopeAsync(LogisticsPermissionManifestV1.WeighingDifferencesApprove, Company, token)).IsGranted)
                throw new LogisticsWeighingDifferenceServiceException(403, "forbidden");
            Effect = true; return Answer;
        }
        public Task<WeighingDifferenceResponseV1> RecordAsync(RecordWeighingDifferenceV1 request, LogisticsCommandMetadata metadata, CancellationToken token) => throw new NotSupportedException();
        public Task<WeighingDifferenceViewV1> GetByDispatchNoteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, LogisticsPermissionManifestV1.WeighingDifferencesApprove,
                Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
