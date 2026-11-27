using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner receipts measure the Work boundary, not a real SQL/Identity delegation journey.
public sealed class WorkProductPackageStandardDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Date = "2026-12-01", Reason = "SENTETIK human reason";

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Native_decision_preserves_human_date_reason_and_same_version_and_original_replay(bool approve, bool replay)
    {
        var rig = new Rig(); rig.Source.Pending = !replay; rig.Owner.Replay = replay;
        var result = await rig.Host.DecideAsync(WorkDecisionHostV1.ProductPackageStandard, Id, Request(approve), "SENTETIK-key", default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(7L, rig.Owner.Version); Assert.Equal("SENTETIK-key", rig.Owner.Metadata!.IdempotencyKey);
        if (approve) Assert.Equal(new ApproveProductPackageStandardVersionRequestV3(Date, " " + Reason + " "), rig.Owner.Body);
        else Assert.Equal(new RejectProductPackageStandardVersionRequestV3(" " + Reason + " "), rig.Owner.Body);
    }

    [Fact]
    public async Task Human_preview_has_actual_table_facts_and_required_reason_while_generic_proof_and_other_kind_fields_are_absent()
    {
        var rig = new Rig();
        var preview = await rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPackageStandard, Id, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal("7", preview.SourceVersion); Assert.Equal("SENTETIK preparation", preview.ProductPackageStandard!.Reason);
        Assert.Equal(12, Assert.Single(preview.ProductPackageStandard.Entries).PiecesPerPackage);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("productPackageStandardProof", JsonSerializer.Serialize(rig.Source.Snapshot(), options));
        var old = new WorkDecisionRequestV1("1", true, null);
        Assert.DoesNotContain("productPackageStandard", JsonSerializer.Serialize(old, options));
        Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(rig.Owner.Success(true, new(Company, Actor, "corr", null, "Web", null, null, "key")), options));
    }

    [Theory]
    [InlineData("missing-date")] [InlineData("bad-date")] [InlineData("missing-reason")] [InlineData("overlong-reason")]
    [InlineData("reject-date")] [InlineData("version-format")]
    public async Task Missing_or_incompatible_human_inputs_never_call_native_owner(string fault)
    {
        var rig = new Rig(); var request = fault switch
        {
            "missing-date" => Request(true) with { ProductPackageStandard = null },
            "bad-date" => Request(true) with { ProductPackageStandard = new("2026-12-01\n") },
            "missing-reason" => Request(true) with { Reason = " " },
            "overlong-reason" => Request(true) with { Reason = new string('x', 4001) },
            "reject-date" => Request(false) with { ProductPackageStandard = new(Date) },
            _ => Request(true) with { ExpectedVersion = "07" },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.ProductPackageStandard, Id, request, "key", default))).Status);
        Assert.Equal(0, rig.Owner.Calls);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("version")] [InlineData("actor")]
    [InlineData("key")] [InlineData("hash")] [InlineData("maker")] [InlineData("date")] [InlineData("decision")]
    public async Task Malformed_or_different_command_receipt_cannot_become_Work_success(string fault)
    {
        var rig = new Rig(); rig.Owner.Fault = fault;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.ProductPackageStandard, Id, Request(true), "key", default))).Status);
        Assert.Equal(1, rig.Owner.Calls);
    }

    [Fact]
    public async Task Native_4000_character_reason_is_not_silently_restricted_to_another_Work_kinds_500_limit()
    {
        var rig = new Rig(); rig.Owner.Error = true;
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.ProductPackageStandard, Id, Request(false) with { Reason = new string('x', 4000) }, "key", default));
        Assert.Equal(ProductPackageStandardProblemCodesV3.EffectiveDateInvalid, error.Code);
        Assert.Equal(1, rig.Owner.Calls);
    }

    [Fact]
    public async Task Maker_and_revoked_authority_are_denied_and_native_terminal_error_is_preserved()
    {
        var rig = new Rig(); rig.User.Value = Maker;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPackageStandard, Id, default))).Status);
        rig.User.Value = Actor; rig.Authority.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(WorkDecisionHostV1.ProductPackageStandard, Id, Request(true), "key", default))).Status);
        rig.Authority.Granted = true; rig.Owner.Error = true;
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(WorkDecisionHostV1.ProductPackageStandard, Id, Request(true), "key", default));
        Assert.Equal((409, ProductPackageStandardProblemCodesV3.EffectiveDateInvalid), (error.Status, error.Code));
    }

    private static WorkDecisionRequestV1 Request(bool approve) => new("7", approve, " " + Reason + " ", ProductPackageStandard: approve ? new(Date) : null);
    private sealed class Rig
    {
        public readonly User User = new(); public readonly Authority Authority = new(); public readonly Source Source = new();
        public readonly Owner Owner; public readonly WorkDecisionHostV1 Host;
        public Rig()
        {
            var service = DispatchProxy.Create<IProductPackageStandardServiceV3, Owner>(); Owner = (Owner)(object)service;
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set("corr", null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1();
            Host = new(company, User, correlation, new WorkDecisionAuthorityV1(Authority, execution), [Source], new Delegations(), execution,
                null!, null!, productPackageStandards: service);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public bool Pending = true; public string Module => "product"; public string Kind => WorkDecisionHostV1.ProductPackageStandard;
        public string ApproverPermission => ProductPackageStandardPermissionsV3.Approve;
        public WorkApprovalDecisionSnapshotV1 Snapshot() => new(Company,
            new(Module, Kind, Id, "v7", At, "/urun", Maker, [], []), "7", Pending,
            ProductPackageStandardProof: new(Company, Id, "7", new("Import", "SENTETIK source", null, "SENTETIK preparation", Maker, At, 1, 0, [new(Guid.NewGuid(), null, 12)])));
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(Snapshot());
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => throw new NotSupportedException();
    }
    public class Owner : DispatchProxy
    {
        public bool Replay, Error; public string? Fault; public int Calls; public long Version; public object? Body; public ProductPackageStandardCommandMetadataV2? Metadata;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name is not ("ApproveAsync" or "RejectAsync")) throw new NotSupportedException();
            Calls++; Version = (long)args![0]!; Body = args[1]; Metadata = (ProductPackageStandardCommandMetadataV2)args[2]!;
            return Task.FromResult(Error ? new ProductPackageStandardResultV2(new(409, ProductPackageStandardProblemCodesV3.EffectiveDateInvalid, "SENTETIK", "SENTETIK"), false, "corr") : Success(method.Name == "ApproveAsync", Metadata));
        }
        public ProductPackageStandardResultV2 Success(bool approve, ProductPackageStandardCommandMetadataV2 metadata)
        {
            var hash = approve ? IdempotencyHash.ComputePayload(new { Version = 7L, EffectiveFrom = Date, Reason }) : IdempotencyHash.ComputePayload(new { Version = 7L, Reason });
            ProductPackageStandardDecisionReceiptV3? receipt = new(Company, Id, 7, approve ? "product.package-standards.approve.v3" : "product.package-standards.reject.v3",
                Actor, IdempotencyHash.ComputeKey(metadata.IdempotencyKey), hash, approve ? "Approved" : "Rejected", approve ? Date : null, Reason, At.AddHours(1), Maker, At);
            receipt = Fault switch { "missing" => null, "company" => receipt with { CompanyId = Guid.NewGuid() }, "id" => receipt with { VersionId = Guid.NewGuid() },
                "version" => receipt with { Version = 8 }, "actor" => receipt with { ActorId = Maker }, "key" => receipt with { IdempotencyKeyHash = "wrong" },
                "hash" => receipt with { RequestHash = "wrong" }, "maker" => receipt with { PreparedBy = Actor }, "date" => receipt with { EffectiveFrom = "2026-12-02" },
                "decision" => receipt with { Decision = "Rejected" }, _ => receipt };
            return new(new(204, null, "SENTETIK", "SENTETIK"), Replay, Replay ? "original-corr" : metadata.CorrelationId, receipt);
        }
    }
    private sealed class User : ICurrentUser
    { public string Value = Actor; public bool IsAuthenticated => true; public string UserId => Value; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService
    { public bool Granted = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid id, CancellationToken token = default) => Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    private sealed class Delegations : IWorkDelegationReaderV1
    { public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(null); }
}
