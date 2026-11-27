using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Native-command composition only. This fake does not prove SQL or granted-role acceptance.
public sealed class WorkLogisticsPlanExcessDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("f6200000-0000-4000-8000-000000000001"), Item = Guid.Parse("f6200000-0000-4000-8000-000000000002"),
        Instruction = Guid.Parse("f6200000-0000-4000-8000-000000000003"), Line = Guid.Parse("f6200000-0000-4000-8000-000000000004");
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker";
    private static readonly DateTimeOffset Proposed = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
    private static WorkDecisionRequestV1 Body(string exceptionVersion = "1", string instructionVersion = "3") =>
        new(exceptionVersion, true, null, LogisticsPlanExcess: new(instructionVersion));

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Exact_native_approval_uses_the_two_frozen_request_versions_not_current_snapshot_or_mutable_usage(bool replay)
    {
        var run = new Run(); run.Source.Pending = !replay; run.Source.ExceptionVersion = replay ? "2" : "1"; run.Source.InstructionVersion = replay ? "99" : "3";
        run.Owner.Answer = run.Owner.Answer with { IsReplay = replay, OriginalCorrelationId = replay ? "SENTETIK-original" : "SENTETIK-correlation" };
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Item, default);
        Assert.Equal(!replay, preview.Pending); Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        Assert.Equal((replay ? "2" : "1", replay ? "99" : "3"), (preview.SourceVersion, preview.LogisticsPlanExcess!.InstructionVersion));
        var answer = await run.Host.DecideAsync(run.Source.Kind, Item, Body(), "original-key", default);
        Assert.Equal((Company, Item, "Approved", replay, replay ? "SENTETIK-original" : "SENTETIK-correlation"),
            (answer.CompanyId, answer.ItemId, answer.State, answer.Replayed, answer.OriginalCorrelationId));
        Assert.Equal(Item, run.Owner.Id); Assert.Equal(new ApproveLogisticsPlanExcessV1("1", "3"), run.Owner.Body);
        Assert.Equal(("original-key", "SENTETIK-correlation"), (run.Owner.Metadata!.IdempotencyKey, run.Owner.Metadata.CorrelationId));
        Assert.Equal("7", run.Owner.Answer.Exception.LoadedUsed); // Historical receipt counters need not equal the current display.
    }

    [Fact]
    public async Task Missing_selection_reject_reason_other_typed_selection_and_noncanonical_versions_never_reach_the_native_owner()
    {
        var run = new Run();
        WorkDecisionRequestV1[] invalid = [new("1", true, null), Body() with { Approve = false }, Body() with { Reason = "" },
            Body() with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) }, Body() with { InventoryTransferLoss = new(1, Proposed) }];
        foreach (var body in invalid)
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, body, "key", default))).Status);
        foreach (var version in new[] { "0", "01", " 1", "+1", "1.0", "9223372036854775808" })
        {
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(exceptionVersion: version), "key", default))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(instructionVersion: version), "key", default))).Status);
        }
        Assert.Null(run.Owner.Body);
        run.Source.Kind = WorkDecisionHostV1.OpeningCost; run.Source.Options = false; run.Source.Proof = null;
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
            new("2", true, "SENTETIK", LogisticsPlanExcess: new("3")), "key", default))).Status);
    }

    [Fact]
    public async Task Legacy_wire_stays_unchanged_when_the_optional_field_is_null_and_internal_proposal_proof_never_serializes()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); var run = new Run();
        var oldWire = JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options);
        Assert.DoesNotContain("logisticsPlanExcess", oldWire);
        var wire = JsonSerializer.Serialize(Body(), options);
        Assert.Contains("\"logisticsPlanExcess\":{\"expectedInstructionVersion\":\"3\"}", wire);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Item, default);
        var snapshotWire = JsonSerializer.Serialize(snapshot, options);
        Assert.DoesNotContain("logisticsPlanExcessProof", snapshotWire);
        Assert.DoesNotContain("SENTETIK proposal reason", snapshotWire);
        Assert.DoesNotContain("SENTETIK proposal reason", JsonSerializer.Serialize(snapshot!.Item, options));
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Item, default);
        Assert.Equal(new WorkLogisticsPlanExcessPreviewV1("99", Instruction, Line, "20", "SENTETIK proposal reason", Maker, Proposed), preview.LogisticsPlanExcess);
        var previewWire = JsonSerializer.Serialize(preview, options);
        using var document = JsonDocument.Parse(previewWire);
        var human = document.RootElement.TryGetProperty("logisticsPlanExcess", out var value) ? value : throw new InvalidOperationException();
        Assert.Equal("99", human.TryGetProperty("instructionVersion", out value) ? value.GetString() : null);
        Assert.Equal(Instruction, human.TryGetProperty("instructionId", out value) ? value.GetGuid() : Guid.Empty);
        Assert.Equal(Line, human.TryGetProperty("lineId", out value) ? value.GetGuid() : Guid.Empty);
        Assert.Equal("20", human.TryGetProperty("quantity", out value) ? value.GetString() : null);
        Assert.Equal("SENTETIK proposal reason", human.TryGetProperty("reason", out value) ? value.GetString() : null);
        Assert.Equal(Maker, human.TryGetProperty("proposedBy", out value) ? value.GetString() : null);
        Assert.Equal(Proposed, human.TryGetProperty("proposedAtUtc", out value) ? value.GetDateTimeOffset() : default);
        Assert.Equal(7, human.EnumerateObject().Count());
        Assert.DoesNotContain("logisticsPlanExcessProof", previewWire);
    }

    [Fact]
    public async Task Missing_or_misbound_owner_proof_is_unavailable_and_foreign_maker_or_permission_loss_is_never_forwarded()
    {
        foreach (var fault in new[] { "missing", "company", "exception", "instruction", "line", "quantity", "reason", "maker", "options" })
        {
            var run = new Run(); var proof = run.Source.Proof!;
            run.Source.Proof = fault switch { "missing" => null, "company" => proof with { CompanyId = Guid.NewGuid() },
                "exception" => proof with { ExceptionId = Guid.NewGuid() }, "instruction" => proof with { InstructionId = Guid.NewGuid() },
                "line" => proof with { LineId = Guid.Empty }, "quantity" => proof with { Quantity = "20.0" }, "reason" => proof with { Reason = " " },
                "maker" => proof with { ProposedBy = "SENTETIK-other" }, _ => proof };
            if (fault == "options") run.Source.InstructionVersion = "01";
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Item, default))).Status);
            Assert.Null(run.Owner.Body);
        }
        var denied = new Run(); denied.Raw.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => denied.Host.PreviewAsync(denied.Source.Kind, Item, default))).Status);
        denied.Raw.Allowed = true; denied.Source.Maker = Actor; denied.Source.Proof = denied.Source.Proof! with { ProposedBy = Actor };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => denied.Host.PreviewAsync(denied.Source.Kind, Item, default))).Status);
    }

    [Fact]
    public async Task Only_the_exact_immutable_approved_native_receipt_is_success()
    {
        var mutations = new Func<LogisticsPlanExcessViewV1, LogisticsPlanExcessViewV1>[] {
            value => value with { CompanyId = Guid.NewGuid() }, value => value with { ExceptionId = Guid.NewGuid() },
            value => value with { InstructionId = Guid.NewGuid() }, value => value with { LineId = Guid.NewGuid() },
            value => value with { Quantity = "21" }, value => value with { Reason = "different" }, value => value with { ProposedBy = "other" },
            value => value with { ProposedAtUtc = Proposed.AddSeconds(1) }, value => value with { ApprovedBy = Maker },
            value => value with { ApprovedAtUtc = null }, value => value with { ApprovedAtUtc = Proposed.ToOffset(TimeSpan.FromHours(3)) },
            value => value with { Version = "3" }, value => value with { Version = "02" }, value => value with { InstructionVersion = "99" },
            value => value with { Status = "Pending" } };
        foreach (var mutate in mutations)
        {
            var run = new Run(); run.Owner.Answer = run.Owner.Answer with { Exception = mutate(run.Owner.Answer.Exception) };
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(), "key", default))).Status);
        }
    }

    [Fact]
    public async Task Native_terminal_refusal_and_original_correlation_propagate_and_late_delegation_revocation_is_checked_inside_owner()
    {
        var run = new Run(); run.Owner.Refusal = new(409, LogisticsPlanExcessV1.Conflict, isReplay: true, originalCorrelationId: "SENTETIK-refusal");
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(), "key", default));
        Assert.Equal((409, true, "SENTETIK-refusal"), (refusal.Status, refusal.Replayed, refusal.OriginalCorrelationId));
        run.Owner.Refusal = null; run.Owner.BeforeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
            Body() with { DelegationId = Guid.NewGuid() }, "late", default))).Status);
        Assert.False(run.Owner.Effect); Assert.True(run.Delegations.Calls >= 2);
    }

    private sealed class Run
    {
        public readonly Source Source = new(); public readonly RawAuthority Raw = new(); public readonly Delegations Delegations = new();
        public readonly Owner Owner; public readonly WorkDecisionHostV1 Host;
        public Run()
        {
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set("SENTETIK-correlation", null, "Web", null, null);
            var fence = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Raw, fence);
            Owner = new(authority);
            Host = new(company, new User(), correlation, authority, [Source], Delegations, fence, null!, null!, logisticsPlanExcess: Owner);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public bool Pending, Options = true; public string ExceptionVersion = "2"; public string Maker = WorkLogisticsPlanExcessDecisionHostTests.Maker; public string InstructionVersion = "99";
        public WorkLogisticsPlanExcessProofV1? Proof = new(Company, Item, Instruction, Line, "20", "SENTETIK proposal reason", WorkLogisticsPlanExcessDecisionHostTests.Maker, Proposed);
        public string Kind { get; set; } = WorkDecisionHostV1.LogisticsPlanExcess;
        public string Module => Kind == WorkDecisionHostV1.LogisticsPlanExcess ? "logistics" : "inventory";
        public string ApproverPermission => Kind == WorkDecisionHostV1.LogisticsPlanExcess ? LogisticsPlanExcessV1.ApprovePermission : ErpPermissions.InventoryOpeningCostApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company, new(Module, Kind, Item, "SENTETIK", Proposed, "/lojistik/plan-ustu-sevk-onay", Maker, [],
                [new("loading-instruction", "SENTETIK", Instruction, "/lojistik/yukleme-talimatlari/" + Instruction)]), ExceptionVersion, Pending,
                LogisticsPlanExcess: Options ? new(InstructionVersion) : null, LogisticsPlanExcessProof: Proof));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Owner(IErpAuthorityRevalidationService authority) : ILogisticsPlanExcessServiceV1
    {
        public Guid Id; public ApproveLogisticsPlanExcessV1? Body; public LogisticsCommandMetadata? Metadata; public Action? BeforeFence; public bool Effect;
        public LogisticsInstructionServiceException? Refusal;
        public LogisticsPlanExcessResultV1 Answer = new(new(Company, Item, Instruction, Line, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2", "3", "Approved",
            "100", "20", "7", "5", "SENTETIK proposal reason", Maker, Proposed, Actor, Proposed.AddHours(1), "13", "2"), true, "SENTETIK-original");
        public async Task<LogisticsPlanExcessResultV1> ApproveAsync(Guid id, ApproveLogisticsPlanExcessV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default)
        {
            Id = id; Body = request; Metadata = metadata; BeforeFence?.Invoke();
            if (!(await authority.RevalidatePermissionScopeAsync(LogisticsPlanExcessV1.ApprovePermission, Company, token)).IsGranted) throw new LogisticsInstructionServiceException(403, "forbidden");
            if (Refusal is not null) throw Refusal; Effect = true; return Answer;
        }
        public Task<LogisticsPlanExcessPageV1> ListAsync(string purpose, Guid? instructionId, string? cursor, CancellationToken token = default) => throw new NotSupportedException();
        public Task<LogisticsPlanExcessResultV1> ProposeAsync(Guid instructionId, Guid lineId, ProposeLogisticsPlanExcessV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class RawAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default) =>
            Task.FromResult(Allowed ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Allowed = true; public int Calls;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default)
        { Calls++; return Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, Company, item.Kind, LogisticsPlanExcessV1.ApprovePermission,
            Guid.NewGuid(), Guid.NewGuid(), Proposed.AddDays(-1), Proposed.AddDays(1), "1", Guid.NewGuid(), Proposed, "SENTETIK", null, null, null) : null); }
    }
}
