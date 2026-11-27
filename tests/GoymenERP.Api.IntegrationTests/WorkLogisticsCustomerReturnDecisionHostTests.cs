using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Controlled native-owner boundary, not native Inventory or HTTP acceptance.</summary>
public sealed class WorkLogisticsCustomerReturnDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Return = Guid.NewGuid(), Movement = Guid.NewGuid();
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker";
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
    private static WorkLogisticsCustomerReturnPreviewV1 Human() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Kilogram", "1.250", "10.000", "SENTETIK reason", Maker, At, At, "Pending");

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Both_native_decisions_and_original_key_replay_forward_only_frozen_payload_and_immutable_result(bool approve, bool replay)
    {
        var r = new Rig { Replay = replay }; if (replay) r.Source.Facts = r.Source.Facts with { Status = approve ? "Approved" : "Rejected" };
        var note = new string('x', 5001); // Native optional notes have no 500-character policy.
        var result = await r.Host.DecideAsync(r.Source.Kind, Return, Request(approve, note), "SENTETIK-key", default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(Return, r.Target); Assert.Equal("SENTETIK-key", r.Metadata!.IdempotencyKey);
        if (approve) Assert.Equal(new ApproveCustomerReturnV1("1", Movement, note), r.Approval);
        else Assert.Equal(new RejectCustomerReturnV1("1", note), r.Rejection);
    }

    [Theory]
    [InlineData("flag")] [InlineData("id")] [InlineData("company")] [InlineData("actor")] [InlineData("version")]
    [InlineData("status")] [InlineData("source")] [InlineData("note")] [InlineData("movement")] [InlineData("original")] [InlineData("correlation")]
    public async Task Domain_shaped_or_malformed_native_answer_is_not_a_Work_success(string fault)
    {
        var r = new Rig { Fault = fault };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Return, Request(true), "key", default))).Status);
        Assert.Equal(1, r.Calls);
    }

    [Fact]
    public async Task Typed_selection_is_approve_only_and_invalid_or_foreign_payloads_never_call_the_owner()
    {
        var r = new Rig();
        foreach (var request in new WorkDecisionRequestV1[] { new("1", true, null), Request(false) with { LogisticsCustomerReturn = new(Movement) },
            Request(true) with { LogisticsCustomerReturn = new(Guid.Empty) }, Request(true) with { ExpectedVersion = "01" },
            Request(true) with { InventoryTransferLoss = new(1, At) } })
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Return, request, "key", default))).Status);
        Assert.Equal(0, r.Calls);
    }

    [Fact]
    public async Task Maker_foreign_company_and_live_delegation_refusal_preserve_native_fences()
    {
        var own = new Rig(Maker); Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => own.Host.PreviewAsync(own.Source.Kind, Return, default))).Status);
        var foreign = new Rig(); foreign.Source.CompanyId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => foreign.Host.PreviewAsync(foreign.Source.Kind, Return, default))).Status);
        var late = new Rig(); late.BeforeFence = () => late.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => late.Host.DecideAsync(late.Source.Kind, Return, Request(false) with { DelegationId = Guid.NewGuid() }, "key", default))).Status);
        Assert.False(late.Effect);
    }

    [Fact]
    public async Task Missing_or_foreign_kind_private_proof_never_becomes_a_native_decision()
    {
        var missing = new Rig(); missing.Source.IncludeProof = false;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => missing.Host.PreviewAsync(missing.Source.Kind, Return, default))).Status);
        var foreign = new Rig(); foreign.Source.Kind = WorkDecisionHostV1.OpeningCost;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => foreign.Host.PreviewAsync(foreign.Source.Kind, Return, default))).Status);
        Assert.Equal(0, missing.Calls); Assert.Equal(0, foreign.Calls);
    }

    [Fact]
    public async Task Human_preview_is_complete_price_free_and_Piece_approval_is_honestly_unsupported_while_reject_remains_available()
    {
        var r = new Rig(); var preview = await r.Host.PreviewAsync(r.Source.Kind, Return, default);
        Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported); Assert.True(preview.ApprovalSupported);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(preview, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(15, json.RootElement.GetProperty("logisticsCustomerReturn").EnumerateObject().Count());
        Assert.DoesNotContain("price", json.RootElement.GetProperty("logisticsCustomerReturn").GetRawText(), StringComparison.OrdinalIgnoreCase);
        var snapshot = await r.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Return, default);
        Assert.DoesNotContain("SENTETIK reason", JsonSerializer.Serialize(snapshot));
        r.Source.Facts = r.Source.Facts with { QuantityUnit = "Piece", Quantity = "1", SourceDeliveredQuantity = "2" };
        Assert.False((await r.Host.PreviewAsync(r.Source.Kind, Return, default)).ApprovalSupported);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(r.Source.Kind, Return, Request(true), "piece", default))).Status);
        Assert.Equal("Rejected", (await r.Host.DecideAsync(r.Source.Kind, Return, Request(false), "piece-reject", default)).State);
        // A pre-existing native receipt is not a new conversion. The native key lookup precedes
        // the new Pending Piece guard; retaining that immutable replay does not stage stock again.
        r.Replay = true; r.Source.Facts = r.Source.Facts with { Status = "Approved" };
        Assert.True((await r.Host.DecideAsync(r.Source.Kind, Return, Request(true), "original-piece-key", default)).Replayed);
        var other = new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.OpeningCost, Return, "SENTETIK", "1", true, true, [], []);
        Assert.DoesNotContain("approvalSupported", JsonSerializer.Serialize(other, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Native_minimum_supplied_occurrence_date_remains_visible_and_rejectable_in_Work()
    {
        var r = new Rig(); r.Source.Facts = r.Source.Facts with { OccurredAtUtc = DateTimeOffset.MinValue };
        var preview = await r.Host.PreviewAsync(r.Source.Kind, Return, default);
        Assert.Equal(DateTimeOffset.MinValue, preview.LogisticsCustomerReturn!.OccurredAtUtc);
        Assert.True(preview.Pending);
        Assert.Equal("Rejected", (await r.Host.DecideAsync(r.Source.Kind, Return, Request(false), "minimum-date", default)).State);
        Assert.Equal(1, r.Calls);
    }

    private static WorkDecisionRequestV1 Request(bool approve, string? note = null) => new("1", approve, note,
        LogisticsCustomerReturn: approve ? new(Movement) : null);
    private sealed class Rig
    {
        public Source Source { get; } = new(); public Delegations Delegations { get; } = new();
        public WorkDecisionHostV1 Host { get; } public bool Replay, Effect; public int Calls; public string? Fault;
        public Guid Target; public ApproveCustomerReturnV1? Approval; public RejectCustomerReturnV1? Rejection;
        public LogisticsCommandMetadata? Metadata; public Action? BeforeFence;
        public Rig(string actor = Actor)
        {
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(new WorkInventoryTransferLossDecisionHostTests.LossAuthority(), execution);
            var owner = System.Reflection.DispatchProxy.Create<ILogisticsCustomerReturnServiceV1, WorkPurchasingRequestDecisionHostTests.NativeOwnerProxy>();
            ((WorkPurchasingRequestDecisionHostTests.NativeOwnerProxy)(object)owner).Handler = (method, args) =>
                Invoke(method.Name == nameof(ILogisticsCustomerReturnServiceV1.ApproveAsync), args, authority);
            Host = new(company, new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor), correlation, authority,
                [Source], Delegations, execution, null!, null!, logisticsCustomerReturns: owner);
        }
        private async Task<CustomerReturnResponseV1> Invoke(bool approve, object?[] args, IErpAuthorityRevalidationService authority)
        {
            Calls++; Target = (Guid)args[0]!; Metadata = (LogisticsCommandMetadata)args[2]!;
            if (approve) Approval = (ApproveCustomerReturnV1)args[1]!; else Rejection = (RejectCustomerReturnV1)args[1]!;
            BeforeFence?.Invoke();
            if (!(await authority.RevalidatePermissionScopeAsync(LogisticsPermissionManifestV1.CustomerReturnsDecision, Company)).IsGranted)
                throw new LogisticsCustomerReturnServiceException(403, "forbidden");
            if (approve && !Replay && Source.Facts.QuantityUnit == "Piece") throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.InventoryUnavailable);
            var p = Source.Facts; var reason = approve ? Approval!.Note : Rejection!.Note;
            var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            var result = new CustomerReturnViewV1(Return, Company, p.LoadingInstructionId, p.LoadingInstructionLineId, p.SourceDeliveryId,
                p.SourceShipmentId, p.SourceOrderId, p.SourceOrderLineId, p.UnitId, p.Quantity, p.SourceDeliveredQuantity,
                approve ? "Approved" : "Rejected", "2", p.Reason, p.RequestedBy, p.OccurredAtUtc, p.RequestedAtUtc, Actor, At.AddMinutes(1), note,
                approve ? Movement : null, approve ? Guid.NewGuid() : null, approve ? Guid.NewGuid() : null);
            result = Fault switch
            {
                "id" => result with { CustomerReturnId = Guid.NewGuid() }, "company" => result with { CompanyId = Guid.NewGuid() },
                "actor" => result with { DecidedBy = Maker }, "version" => result with { Version = "3" }, "status" => result with { Status = "Pending" },
                "source" => result with { SourceDeliveryId = Guid.NewGuid() }, "note" => result with { DecisionNote = "wrong" },
                "movement" => result with { InventoryMovementId = Guid.NewGuid() }, "original" => result with { OriginalLabelId = null }, _ => result,
            };
            Effect = true; return new(result, Replay, Fault == "correlation" ? "wrong" : Replay ? "SENTETIK-original" : "SENTETIK-work", Fault != "flag");
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public Guid CompanyId = Company; public WorkLogisticsCustomerReturnPreviewV1 Facts = Human(); public bool IncludeProof = true;
        public string Module => "logistics"; public string Kind { get; set; } = WorkDecisionHostV1.LogisticsCustomerReturn;
        public string ApproverPermission => LogisticsPermissionManifestV1.CustomerReturnsDecision;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(CompanyId, new(Module, Kind, Return, Return.ToString("D"), At,
                "/lojistik/yukleme-talimatlari/" + Facts.LoadingInstructionId, Maker,
                [new("logistics.customer-return-request", Facts.QuantityUnit == "Piece" ? "2" : "10", Facts.QuantityUnit == "Piece" ? "1" : "1.25", null, Facts.QuantityUnit == "Piece" ? "adet" : "kg")],
                [new("loading-instruction", "SENTETIK", Facts.LoadingInstructionId, "/lojistik/yukleme-talimatlari/" + Facts.LoadingInstructionId)]),
                Facts.Status == "Pending" ? "1" : "2", Facts.Status == "Pending", LogisticsCustomerReturnProof: IncludeProof ? new(CompanyId, Return, Facts) : null));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, LogisticsPermissionManifestV1.CustomerReturnsDecision, Guid.NewGuid(), Guid.NewGuid(), At,
                DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
