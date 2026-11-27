using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner transport. Native persistence, rollback and true Identity delegation remain separate SQL acceptance.
public sealed class WorkProductPriceListDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), List = Guid.NewGuid(), Target = Guid.NewGuid(), Product = Guid.NewGuid(), Unit = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-publisher", Maker = "SENTETIK-maker", Editor = "SENTETIK-editor";
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Publish_uses_exact_target_number_and_frozen_header_and_old_key_never_needs_current_price_read(bool replay)
    {
        var rig = new Rig(); rig.Source.Pending = !replay; rig.Owner.Replay = replay;
        var result = await rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList, Target, new("7", true, null), "original-key", default);
        Assert.Equal(("Published", replay), (result.State, result.Replayed));
        Assert.Equal("7", rig.Source.Selected); Assert.Equal(new PublishPriceListVersionRequestV1("7", 2), rig.Owner.Body);
        Assert.Equal(List, rig.Owner.Id); Assert.Equal(0, rig.Owner.Gets);
    }
    [Fact]
    public async Task Authorized_human_get_uses_native_price_view_without_recalculating_or_leaking_prices_into_generic_proof()
    {
        var rig = new Rig(); var preview = await rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPriceList, Target, default);
        Assert.False(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported);
        Assert.Equal((Target, 2, "2026-12-01"), (preview.ProductPriceList!.VersionId, preview.ProductPriceList.VersionNo, preview.ProductPriceList.ValidFrom));
        Assert.Equal(new WorkProductPriceListScopeV1("Company", null, null), preview.ProductPriceList.Scope);
        var line = Assert.Single(preview.ProductPriceList.Lines); Assert.Equal(("42000.5", "40000", "0", "10"), (line.UnitPrice, line.FloorUnitPrice, line.LowerQuantity, line.UpperQuantity));
        Assert.Contains(ProductPriceListPermissionsV1.Read, rig.Authority.Asked); Assert.Contains(ProductPriceListPermissionsV1.Publish, rig.Authority.Asked);
        var json = JsonSerializer.Serialize(rig.Source.Snapshot(), ProductPriceListWire.Json);
        Assert.DoesNotContain("42000", json); Assert.DoesNotContain("productPriceListProof", json);
        Assert.DoesNotContain("publishReceipt", JsonSerializer.Serialize(rig.Owner.Answer("key"), ProductPriceListWire.Json));
        Assert.DoesNotContain("productPriceList", JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), ProductPriceListWire.Json));
    }
    [Theory]
    [InlineData(ProductPriceListPermissionsV1.Read, false)] [InlineData(ProductPriceListPermissionsV1.Publish, false)]
    [InlineData(ProductPriceListPermissionsV1.Read, true)] [InlineData(ProductPriceListPermissionsV1.Publish, true)]
    public async Task Human_read_and_Work_publish_both_preserve_Read_and_Publish_companywide_gates(string denied, bool narrow)
    {
        var rig = new Rig(); rig.Authority.Denied = denied; rig.Authority.Narrow = narrow;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPriceList, Target, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList, Target, new("7", true, null), "key", default))).Status);
        Assert.Equal((0, 0), (rig.Owner.Gets, rig.Owner.Publishes));
    }
    [Theory]
    [InlineData("read")] [InlineData("publish")] [InlineData("actor")] [InlineData("company")]
    public async Task Native_human_read_await_cannot_release_prices_after_scope_drift(string fault)
    {
        var rig = new Rig(); rig.Owner.AfterGet = () =>
        {
            if (fault == "read") rig.Authority.Denied = ProductPriceListPermissionsV1.Read;
            if (fault == "publish") rig.Authority.Denied = ProductPriceListPermissionsV1.Publish;
            if (fault == "actor") rig.User.Value = "SENTETIK-other";
            if (fault == "company") rig.Company.Value = Guid.NewGuid();
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPriceList, Target, default))).Status);
    }
    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("list")] [InlineData("target")] [InlineData("number")]
    [InlineData("header")] [InlineData("expected")] [InlineData("actor")] [InlineData("key")] [InlineData("hash")]
    [InlineData("command")] [InlineData("snapshot")] [InlineData("correlation")]
    public async Task A_different_or_unproven_native_receipt_never_becomes_Work_success(string fault)
    {
        var rig = new Rig(); rig.Owner.Fault = fault;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.ProductPriceList, Target, new("7", true, null), "key", default))).Status);
    }
    [Theory]
    [InlineData("reject")] [InlineData("reason")] [InlineData("version")]
    public async Task No_reject_reason_or_header_normalization_is_invented(string fault)
    {
        var rig = new Rig(); var request = fault switch { "reject" => new WorkDecisionRequestV1("7", false, "reason"),
            "reason" => new("7", true, "reason"), _ => new("7\n", true, null) };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(WorkDecisionHostV1.ProductPriceList, Target, request, "key", default))).Status);
        Assert.Equal(0, rig.Owner.Publishes);
    }
    [Theory]
    [InlineData(Maker)] [InlineData(Editor)]
    public async Task All_original_preparers_remain_excluded(string actor)
    {
        var rig = new Rig(); rig.User.Value = actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.ProductPriceList, Target, default))).Status);
    }
    private sealed class Rig
    {
        public readonly CompanyContextProbe Company = new(); public readonly User User = new(); public readonly Authority Authority = new(); public readonly Source Source = new();
        public readonly Owner Owner; public readonly WorkDecisionHostV1 Host;
        public Rig()
        {
            var service = DispatchProxy.Create<IProductPriceListServiceV1, Owner>(); Owner = (Owner)(object)service;
            var correlation = new CorrelationContext(); correlation.Set("corr", null, "Web", null, null); var execution = new WorkDecisionExecutionFenceV1();
            Host = new(Company, User, correlation, new WorkDecisionAuthorityV1(Authority, execution), [Source], new Delegations(), execution, null!, null!, productPriceLists: service);
        }
    }
    private sealed class Source : IWorkApprovalDecisionVersionSourceV1
    {
        public bool Pending = true; public string? Selected; public string Module => "product"; public string Kind => WorkDecisionHostV1.ProductPriceList; public string ApproverPermission => ProductPriceListPermissionsV1.Publish;
        public WorkApprovalDecisionSnapshotV1 Snapshot() => new(Company, new(Module, Kind, Target, "SENTETIK list v2", At, "/fiyat-listeleri/" + List,
            Maker, [], [new("price-list", "SENTETIK list", List, "/fiyat-listeleri/" + List)], [Editor]), "7", Pending,
            ProductPriceListProof: new(Company, List, Target, 2, "7", Maker, At, [Maker, Editor]));
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(Snapshot());
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 c, Guid id, string version, CancellationToken t) { Selected = version; return ReadDecisionAsync(c, id, t); }
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken t) => throw new NotSupportedException();
    }
    public class Owner : DispatchProxy
    {
        public int Gets, Publishes; public bool Replay; public Action? AfterGet; public string? Fault; public Guid Id; public PublishPriceListVersionRequestV1? Body;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "GetAsync") { Gets++; AfterGet?.Invoke(); return Task.FromResult(View(false)); }
            if (method?.Name != "PublishAsync") throw new NotSupportedException();
            Publishes++; Id = (Guid)args![0]!; Body = (PublishPriceListVersionRequestV1)args[1]!;
            return Task.FromResult(Answer(((ProductPriceListCommandMetadata)args[2]!).IdempotencyKey));
        }
        public PriceListResultV1 Answer(string key)
        {
            PriceListPublishReceiptV1? proof = new(Company, List, Target, 2, "7", "8", ProductPriceListCommandsV1.PublishV1, Actor,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))), ProductPriceListWire.PublishRequestHash(List, new("7", 2)), At.AddHours(1), Actor, Replay ? "original-corr" : "corr");
            proof = Fault switch { "missing" => null, "company" => proof with { CompanyId = Guid.NewGuid() }, "list" => proof with { PriceListId = Guid.NewGuid() },
                "target" => proof with { VersionId = Guid.NewGuid() }, "number" => proof with { VersionNo = 3 }, "header" => proof with { HeaderVersion = "9" },
                "expected" => proof with { ExpectedHeaderVersion = "6" }, "actor" => proof with { ActorId = Maker }, "key" => proof with { IdempotencyKeyHash = "wrong" },
                "hash" => proof with { RequestHash = "wrong" }, "command" => proof with { CommandName = ProductPriceListCommandsV1.CreateV1 }, "correlation" => proof with { OriginalCorrelationId = "wrong" }, _ => proof };
            return new(Fault == "snapshot" ? View(true) with { Version = "400" } : View(true), Replay, Replay ? "original-corr" : "corr", proof);
        }
        private static PriceListViewV1 View(bool published) => new(List, "SENTETIK list", "TRY", "Sales", "ExcludingVat", published ? "8" : "7", At, At,
            null, published ? null : 2, [new(Target, 2, published ? "Published" : "Draft", published ? "Planned" : "Draft", "2026-12-01", null, At, Maker,
                published ? At.AddHours(1) : null, published ? Actor : null, [new(Guid.NewGuid(), 1, Product, "SENTETIK", "SENTETIK product", Unit, "42000.5", "40000", "0", "10")])], PriceListScopeViewV1.Company);
    }
    private sealed class CompanyContextProbe : ICompanyContext { public Guid Value = Company; public Guid CompanyId => Value; public bool IsResolved => true; public void Set(Guid id) => Value = id; }
    private sealed class User : ICurrentUser { public string Value = Actor; public bool IsAuthenticated => true; public string UserId => Value; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService
    { public string? Denied; public bool Narrow; public List<string> Asked = []; public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken t = default) { Asked.Add(p); return Task.FromResult(new PermissionScope(p != Denied || Narrow, !(p == Denied && Narrow), new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1 { public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(null); }
}
