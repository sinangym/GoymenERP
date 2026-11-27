using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkManufacturingPlanDecisionHostTests
{
    private static readonly Guid LabelA = Guid.Parse("e8230000-0000-4000-8000-000000000071");
    private static readonly Guid LabelB = Guid.Parse("e8230000-0000-4000-8000-000000000072");
    private static WorkDecisionRequestV1 ReapprovalRequest(params Guid[] labels) => new("1", true, null,
        ManufacturingPlanReapproval: new(labels));
    private static PlanViewV1 ReapprovalView() => ApprovalView() with
    {
        Inputs = [new(LabelA, "20"), new(LabelB, "30")],
        Reapprovals = [new(1, ActorId, DateTimeOffset.UtcNow, 2)],
    };

    [Fact]
    public async Task Optional_typed_selection_preserves_the_old_wire_shape_and_is_not_inferred_from_preview()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
        const string legacy = "{\"expectedVersion\":\"1\",\"approve\":true,\"reason\":null,\"delegationId\":null}";
        Assert.Equal(legacy, JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), json));
        Assert.Null(JsonSerializer.Deserialize<WorkDecisionRequestV1>(legacy, json)!.ManufacturingPlanReapproval);
        var run = new Run();
        var ordinary = await run.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, default);
        Assert.Null(ordinary.ManufacturingPlanReapproval);
        using var ordinaryWire = JsonDocument.Parse(JsonSerializer.Serialize(ordinary, json));
        Assert.False(ordinaryWire.RootElement.TryGetProperty("manufacturingPlanReapproval", out _));
        run.Source.Reapproval = true;
        var reapproval = await run.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, default);
        Assert.Equal([LabelA, LabelB], reapproval.ManufacturingPlanReapproval!.ParentRollIds);
        using var selected = JsonDocument.Parse(JsonSerializer.Serialize(ReapprovalRequest(LabelB), json));
        Assert.True(selected.RootElement.TryGetProperty("manufacturingPlanReapproval", out var selection));
        Assert.True(selection.TryGetProperty("selectedLabelIds", out var labelIds));
        Assert.Equal(LabelB, Assert.Single(labelIds.EnumerateArray()).GetGuid());
        Assert.Equal(0, run.CommandReads);
    }

    [Theory]
    [InlineData("other-kind")]
    [InlineData("first-approval")]
    [InlineData("rejection")]
    public async Task A_selection_payload_on_another_action_is_rejected_not_silently_discarded(string misuse)
    {
        var run = new Run(misuse == "other-kind" ? WorkDecisionHostV1.PurchaseRequest : WorkDecisionHostV1.ManufacturingPlan);
        var request = misuse == "rejection" ? ReapprovalRequest(LabelA) with { Approve = false, Reason = "SENTETIK red" } : ReapprovalRequest(LabelA);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, PlanId, request, "misuse", default))).Status);
        Assert.Equal(0, run.CommandReads);
    }

    [Theory]
    [InlineData("missing-options")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    public async Task Malformed_owner_option_arrays_are_unavailable_before_they_reach_the_preview(string fault)
    {
        var run = new Run(); run.Source.Reapproval = true;
        run.Source.Options = fault switch
        {
            "missing-options" => null, "null" => new(null!), "empty" => new([]), "empty-id" => new([Guid.Empty]),
            "duplicate" => new([LabelA, LabelA]), _ => throw new NotSupportedException(),
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, default))).Status);
        Assert.Equal(0, run.CommandReads);
    }

    [Fact]
    public async Task An_old_initial_approval_key_still_replays_its_native_initial_receipt_after_a_later_order_release()
    {
        var run = new Run { ApprovalAnswer = true, View = ApprovalView() };
        run.Source.Reapproval = true; run.Source.Version = "3";
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, new("1", true, null), "native-key", default);
        Assert.Equal(("Approved", true, "SENTETIK-original"), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(1, run.CommandReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_reapproval_replays_the_immutable_approved_plan_even_after_the_current_plan_moves_on(bool currentReapproval)
    {
        var run = new Run { ReapprovalAnswer = true, View = ReapprovalView() };
        run.Source.Reapproval = currentReapproval; run.Source.Pending = currentReapproval;
        run.Source.Version = currentReapproval ? "1" : "9";
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, ReapprovalRequest(LabelB, LabelA), "native-key", default);
        Assert.Equal(("Approved", true, "SENTETIK-original"), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal(1, run.CommandReads);
        Assert.All(run.Raw.Asked, permission => Assert.Equal(ManufacturingPermissionManifestV1.PlansApprove, permission));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("selection")]
    [InlineData("legacy-no-selection-hash")]
    public async Task Reapproval_native_actor_and_selection_fingerprint_remain_authoritative(string fault)
    {
        var run = new Run { ReapprovalAnswer = true, View = ReapprovalView() }; run.Source.Reapproval = true;
        if (fault == "actor") run.StoredActor = Guid.NewGuid().ToString("D");
        if (fault == "legacy-no-selection-hash") run.StoredHash = Hash(ManufacturingCommandNames.ReapprovePlan + "|" + JsonSerializer.Serialize(new { planId = PlanId, expectedVersion = 1L }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var request = fault == "selection" ? ReapprovalRequest(LabelA) : ReapprovalRequest(LabelA, LabelB);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, request, "native-key", default));
        Assert.Equal((409, ManufacturingApiCodes.IdempotencyConflict), (error.Status, error.Code));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("id")]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("history")]
    [InlineData("null-history-entry")]
    [InlineData("null-input-entry")]
    [InlineData("actor")]
    [InlineData("history-version")]
    [InlineData("history-time")]
    [InlineData("history-duplicate")]
    [InlineData("inputs")]
    [InlineData("required")]
    [InlineData("order")]
    public async Task Reapproval_success_requires_the_native_immutable_latest_history_and_selected_input_proof(string fault)
    {
        var run = new Run { ReapprovalAnswer = true }; run.Source.Reapproval = true;
        var view = ReapprovalView(); var history = view.Reapprovals![0];
        run.View = fault switch
        {
            "company" => view with { CompanyId = Guid.NewGuid() }, "id" => view with { PlanId = Guid.NewGuid() },
            "status" => view with { Status = "Draft" }, "version" => view with { Version = 3 },
            "history" => view with { Reapprovals = [] },
            "null-history-entry" => view with { Reapprovals = [null!] },
            "null-input-entry" => view with { Inputs = [null!] }, "actor" => view with { Reapprovals = [history with { ReapprovedByUserId = Guid.NewGuid() }] },
            "history-version" => view with { Reapprovals = [history with { PlanVersion = 1 }] },
            "history-time" => view with { Reapprovals = [history with { ReapprovedAtUtc = default }] },
            "history-duplicate" => view with { Reapprovals = [history, history with { Number = 2 }] },
            "inputs" => view with { Inputs = [new(LabelA, "20")] }, "required" => view with { RequiresReapproval = true },
            "order" => view with { OrderId = Guid.NewGuid() }, _ => throw new NotSupportedException(),
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.ManufacturingPlan, PlanId, ReapprovalRequest(LabelA, LabelB), "native-key", default))).Status);
    }
}
