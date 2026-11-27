using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Owner-command composition with the existing DispatchProxy/authority harness; not HTTP/SQL acceptance.
public sealed class WorkLogisticsDispatchNoteDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Note = Guid.NewGuid(), Instruction = Guid.NewGuid(), Customer = Guid.NewGuid(), Account = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker";
    private static WorkLogisticsDispatchNoteProofV1 Proof() => new(Company, "7", new(Instruction, Note, Customer, Account,
        new DateOnly(2026, 11, 27), "Draft", null, null, Maker, At,
        [new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.250", "0.750")]));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Approval_and_original_key_replay_forward_native_instruction_identity_and_version_without_current_view_substitution(bool replay)
    {
        var r = new Rig(); r.Replayed = replay; r.Source.Pending = !replay;
        if (replay) r.Source.Version = "10";
        var preview = await r.Host.PreviewAsync(r.Source.Kind, Note, default);
        Assert.False(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported);
        Assert.Equal(r.Source.Proof!.Note, preview.LogisticsDispatchNote);
        var result = await r.Host.DecideAsync(r.Source.Kind, Note, new("7", true, null), "SENTETIK-key", default);
        Assert.Equal(("Approved", replay, replay ? "SENTETIK-original" : "SENTETIK-work"), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(1, r.Calls); Assert.Equal((Instruction, Note), (r.InstructionId, r.NoteId));
        Assert.Equal(new ApproveDispatchNoteRequest("7"), r.Captured); Assert.Equal("SENTETIK-key", r.Metadata!.IdempotencyKey);
        Assert.All(r.Raw.Permissions, permission => Assert.Equal(LogisticsPermissionManifestV1.DispatchNotesApprove, permission));
    }
    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("+7")] [InlineData("7.0")] [InlineData("9223372036854775807")]
    [InlineData("9223372036854775808")]
    public async Task Noncanonical_or_nonincrementable_Int64_request_never_reaches_native_owner(string version)
    {
        var r = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Note, new(version, true, null), "key", default))).Status);
        Assert.Equal(0, r.Calls);
    }
    [Fact]
    public async Task Reject_reason_and_foreign_typed_request_are_not_a_dispatch_approval_or_cancellation()
    {
        var r = new Rig();
        foreach (var request in new WorkDecisionRequestV1[] { new("7", false, "SENTETIK"), new("7", true, ""),
            new("7", true, null, InventoryTransferLoss: new(1, At)), new("7", true, null, ManufacturingPlanReapproval: new([])) })
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Note, request, "key", default))).Status);
        Assert.Equal(0, r.Calls);
    }
    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("note")] [InlineData("instruction")] [InlineData("actor")]
    [InlineData("current-version")] [InlineData("current-state")] [InlineData("version")] [InlineData("hash")] [InlineData("correlation")] [InlineData("command")]
    public async Task Current_approved_view_cannot_replace_missing_or_mismatched_immutable_native_receipt(string fault)
    {
        var r = new Rig(); r.ReceiptFault = fault;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Note, new("7", true, null), "key", default))).Status);
        Assert.Equal(1, r.Calls);
    }
    [Fact]
    public async Task Native_terminal_refusal_replay_metadata_is_preserved()
    {
        var r = new Rig(); r.Refusal = new(409, LogisticsProblemCodesV1.VersionConflict, isReplay: true, originalCorrelationId: "SENTETIK-first");
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Note, new("7", true, null), "key", default));
        Assert.Equal((409, LogisticsProblemCodesV1.VersionConflict, true, "SENTETIK-first"), (error.Status, error.Code, error.Replayed, error.OriginalCorrelationId));
    }
    [Fact]
    public async Task Maker_company_site_scope_and_revoked_delegation_fail_before_effects()
    {
        var own = new Rig(Maker);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => own.Host.PreviewAsync(own.Source.Kind, Note, default))).Status);
        Assert.Equal(0, own.Calls);
        var foreign = new Rig(); foreign.Source.CompanyId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => foreign.Host.PreviewAsync(foreign.Source.Kind, Note, default))).Status);
        var denied = new Rig(); denied.Raw.Inner.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => denied.Host.PreviewAsync(denied.Source.Kind, Note, default))).Status);
        Assert.Equal(0, denied.Calls);
        var delegation = new Rig(); delegation.BeforeNativeFence = () => delegation.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => delegation.Host.DecideAsync(delegation.Source.Kind, Note, new("7", true, null, Guid.NewGuid()), "key", default))).Status);
        Assert.False(delegation.Effect);
    }
    [Fact]
    public async Task Private_proof_is_absent_from_snapshot_AI_and_legacy_wire_while_authorized_GET_exposes_full_native_fields()
    {
        var r = new Rig(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await r.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Note, default);
        Assert.DoesNotContain("logisticsDispatchNote", JsonSerializer.Serialize(snapshot, options));
        var preview = await r.Host.PreviewAsync(r.Source.Kind, Note, default);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(preview, options));
        var human = wire.RootElement.GetProperty("logisticsDispatchNote");
        Assert.Equal(11, human.EnumerateObject().Count()); Assert.Equal(9, human.GetProperty("lines")[0].EnumerateObject().Count());
        Assert.Equal("1.250", human.GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.DoesNotContain("price", human.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logisticsDispatchNote", JsonSerializer.Serialize(new WorkDecisionRequestV1("7", true, null), options));
        Assert.DoesNotContain("logisticsDispatchNote", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.OpeningCost, Note, "SENTETIK", "1", true, true, [], []), options));
        r.Source.Kind = WorkDecisionHostV1.OpeningCost;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.PreviewAsync(r.Source.Kind, Note, default))).Status);
    }

    private sealed class Rig
    {
        public readonly Source Source = new(); public readonly RecordingAuthority Raw = new(); public readonly Delegations Delegations = new();
        public readonly WorkDecisionHostV1 Host; public int Calls; public bool Effect, Replayed; public string? ReceiptFault;
        public Guid InstructionId, NoteId; public ApproveDispatchNoteRequest? Captured; public LogisticsCommandMetadata? Metadata;
        public Action? BeforeNativeFence; public LogisticsInstructionServiceException? Refusal;
        public Rig(string actor = Actor)
        {
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Raw, execution);
            var owner = DispatchProxy.Create<ILogisticsInstructionService, WorkPurchasingRequestDecisionHostTests.NativeOwnerProxy>();
            ((WorkPurchasingRequestDecisionHostTests.NativeOwnerProxy)(object)owner).Handler = (method, args) =>
                method.Name == nameof(ILogisticsInstructionService.ApproveDispatchNoteAsync) ? Invoke(args, authority) : throw new NotSupportedException();
            Host = new(company, new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor), correlation, authority,
                [Source], Delegations, execution, null!, null!, logisticsInstructions: owner);
        }
        private async Task<LoadingInstructionResponse> Invoke(object?[] args, IErpAuthorityRevalidationService authority)
        {
            Calls++; InstructionId = (Guid)args[0]!; NoteId = (Guid)args[1]!; Captured = (ApproveDispatchNoteRequest)args[2]!; Metadata = (LogisticsCommandMetadata)args[3]!;
            BeforeNativeFence?.Invoke();
            if (!(await authority.RevalidatePermissionScopeAsync(LogisticsPermissionManifestV1.DispatchNotesApprove, Company)).IsGranted)
                throw new LogisticsInstructionServiceException(403, "forbidden");
            if (Refusal is not null) throw Refusal;
            var p = Source.Proof!.Note; var corr = Replayed ? "SENTETIK-original" : "SENTETIK-work";
            var hash = LogisticsRequestHash.Compute(LogisticsCommands.ApproveDispatchNoteV1, Instruction, new { dispatchNoteId = Note, request = Captured });
            LogisticsDispatchNoteApprovalReceiptV1? receipt = new(Company, LogisticsCommands.ApproveDispatchNoteV1, Instruction, Note, 7, 8, Actor, At.AddHours(1), corr, hash);
            receipt = ReceiptFault switch
            {
                "missing" => null, "company" => receipt with { CompanyId = Guid.NewGuid() }, "note" => receipt with { DispatchNoteId = Guid.NewGuid() },
                "instruction" => receipt with { InstructionId = Guid.NewGuid() }, "actor" => receipt with { ApprovedBy = Maker },
                "version" => receipt with { ApprovedVersion = 9 }, "hash" => receipt with { RequestHash = new string('a', 64) },
                "correlation" => receipt with { OriginalCorrelationId = "SENTETIK-other" }, "command" => receipt with { CommandName = LogisticsCommands.CancelDispatchNoteV1 }, _ => receipt,
            };
            var summary = new LoadingInstructionSummaryView(Instruction, Company, "Issued", ReceiptFault == "current-version" ? "9" : Replayed ? "10" : "8", Customer, Account,
                p.EffectiveOn, At, At.AddHours(2), 1, null, null, null, "NotRecorded");
            var note = new DispatchNoteView(Note, ReceiptFault == "current-state" ? "Draft" : Replayed ? "Cancelled" : "Approved", "IRS-000001", Maker, At, Actor, At.AddHours(1),
                Replayed ? Actor : null, Replayed ? At.AddHours(2) : null, Replayed ? "SENTETIK correction" : null, null,
                p.Lines.Select(line => new DispatchNoteLineView(line.LineNo, line.ShipmentId, line.InstructionLineId, line.SourceOrderId,
                    line.SourceOrderLineId, line.ProductId, line.UnitId, line.Quantity, line.QuantityKg)).ToArray());
            Effect = true;
            return new(new(summary, [], [], null, [note], null), Replayed, corr, receipt);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public Guid CompanyId = Company; public string Version = "7"; public bool Pending = true; public string Kind { get; set; } = WorkDecisionHostV1.LogisticsDispatchNote;
        public WorkLogisticsDispatchNoteProofV1? Proof = WorkLogisticsDispatchNoteDecisionHostTests.Proof();
        public string Module => "logistics"; public string ApproverPermission => LogisticsPermissionManifestV1.DispatchNotesApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(CompanyId, new(Module, Kind, Note, Note.ToString("D"), At,
                "/lojistik/yukleme-talimatlari/" + Instruction, Maker, [new("logistics.dispatch-note-draft", null, null, null, null)],
                [new("loading-instruction", Instruction.ToString("D"), Instruction, "/lojistik/yukleme-talimatlari/" + Instruction)]),
                Version, Pending, LogisticsDispatchNoteProof: Proof is null ? null : Proof with { InstructionVersion = Version }));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class RecordingAuthority : IErpAuthorityRevalidationService
    {
        public readonly WorkInventoryTransferLossDecisionHostTests.LossAuthority Inner = new(); public List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Permissions.Add(permission); return Inner.RevalidatePermissionScopeAsync(permission, companyId, token); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, LogisticsPermissionManifestV1.DispatchNotesApprove,
                Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
