using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Composition tests only: the fake native owner is not relational or Critical-user acceptance.
public sealed class WorkCounterpartyIdentityDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("f6110000-0000-4000-8000-000000000001");
    private static readonly Guid Item = Guid.Parse("f6110000-0000-4000-8000-000000000002");
    private static readonly Guid Party = Guid.Parse("f6110000-0000-4000-8000-000000000003");
    private const string Actor = "SENTETIK-checker";
    [Theory]
    [InlineData(true, true)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(false, false)]
    public async Task Both_native_commands_receive_only_the_owner_bound_party_request_and_original_version_key_metadata(bool tax, bool approve)
    {
        var run = new Run(tax); run.Source.Pending = false;
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Item, default);
        Assert.False(preview.Pending); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal("9", preview.SourceVersion);
        var note = new string('n', 1000);
        var answer = await run.Host.DecideAsync(run.Source.Kind, Item, new("3", approve, note), "original-key", default);
        Assert.Equal((Company, run.Source.Kind, Item, approve ? "Approved" : "Rejected", true, "SENTETIK-original"),
            (answer.CompanyId, answer.Kind, answer.ItemId, answer.State, answer.Replayed, answer.OriginalCorrelationId));
        var called = Assert.Single(run.Calls); Assert.Equal(Party, called.PartyId);
        Assert.Equal(tax ? "DecideTaxIdentityChangeAsync" : "DecideLegalNameChangeAsync", called.Method);
        if (tax)
        { var body = Assert.IsType<DecideCounterpartyTaxIdentityChange>(called.Body); Assert.Equal(("3", Item, approve ? "approve" : "reject", note), (body.ExpectedVersion, body.ChangeRequestId, body.Decision, body.Note)); }
        else
        { var body = Assert.IsType<DecideCounterpartyLegalNameChange>(called.Body); Assert.Equal(("3", Item, approve ? "approve" : "reject", note), (body.ExpectedVersion, body.ChangeRequestId, body.Decision, body.Note)); }
        Assert.Equal((Company, Actor, "original-key", "SENTETIK-correlation"), (called.Metadata.CompanyId, called.Metadata.ActorId, called.Metadata.IdempotencyKey, called.Metadata.CorrelationId));
        Assert.Contains(ErpPermissions.CounterpartyPartiesRead, run.Raw.Asked);
        Assert.Contains(run.Source.ApproverPermission, run.Raw.Asked);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Optional_notes_on_both_decisions_and_native_refusal_receipts_are_preserved(bool tax)
    {
        var run = new Run(tax);
        foreach (var approve in new[] { true, false })
            Assert.Equal(approve ? "Approved" : "Rejected", (await run.Host.DecideAsync(run.Source.Kind, Item, new("9", approve, null), "key", default)).State);
        foreach (var refusal in new[] { CounterpartyCommandResult.NotFound, CounterpartyCommandResult.VersionConflict, CounterpartyCommandResult.IdempotencyConflict })
        {
            run.Answer = new(new(refusal, refusal == CounterpartyCommandResult.NotFound ? 404 : 409, "native", "native", null, null), true, "SENTETIK-old-refusal");
            var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, new("1", false, null), "old-key", default));
            Assert.Equal(refusal == CounterpartyCommandResult.NotFound ? 404 : 409, error.Status);
            Assert.True(error.Replayed); Assert.Equal("SENTETIK-old-refusal", error.OriginalCorrelationId);
        }
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Native_note_limit_and_typed_selection_rejections_do_not_reach_the_owner(bool tax)
    {
        var run = new Run(tax);
        foreach (var approve in new[] { true, false })
        {
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
                new("9", approve, new string('n', 1001)), "key", default))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
                new("9", approve, null, ManufacturingPlanReapproval: new([Guid.NewGuid()])), "key", default))).Status);
        }
        Assert.Empty(run.Calls);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Foreign_company_maker_read_permission_and_ambiguous_owner_identity_are_never_forwarded(bool tax)
    {
        var run = new Run(tax);
        run.Source.Maker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Item, default))).Status);
        run.Source.Maker = "SENTETIK-maker"; run.Source.CompanyId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Item, default))).Status);
        run.Source.CompanyId = Company; run.Raw.Denied.Add(ErpPermissions.CounterpartyPartiesRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Item, default))).Status);
        run.Raw.Denied.Clear(); run.Source.References = [];
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, new("9", true, null), "key", default))).Status);
        run.Source.References = [new("party", "SENTETIK", Party, "/cari/" + Party), new("party", "SENTETIK", Party, "/cari/" + Party)];
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, new("9", true, null), "key", default))).Status);
        Assert.Empty(run.Calls);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Delegation_and_native_authority_are_rechecked_inside_the_native_owner(bool tax)
    {
        var run = new Run(tax); run.BeforeNativeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
            new("9", false, null, Guid.NewGuid()), "key", default))).Status);
        Assert.False(run.NativeEffect); Assert.True(run.Delegations.Calls >= 2);
        run.BeforeNativeFence = () => run.Raw.Denied.Add(run.Source.ApproverPermission); run.Delegations.Allowed = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
            new("9", true, null), "key2", default))).Status);
        Assert.False(run.NativeEffect);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Malformed_successes_and_unrelated_native_results_are_unavailable_instead_of_current_party_receipts(bool tax)
    {
        var run = new Run(tax);
        foreach (var bad in new CounterpartyCommandExecution[] {
            new CounterpartyCommandExecution(new(CounterpartyCommandResult.Succeeded, 200, null, null, null, null), true, "SENTETIK-original"),
            new(new(CounterpartyCommandResult.Succeeded, 204, "unexpected", null, null, null), true, "SENTETIK-original"),
            new(new(CounterpartyCommandResult.Succeeded, 204, null, null, null, null), true, ""),
            new(new(CounterpartyCommandResult.Succeeded, 204, null, null, null, null), false, "wrong-new-correlation"),
            new(new(CounterpartyCommandResult.Created, 409, null, null, null, null), true, "SENTETIK-original") })
        { run.Answer = bad; Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, new("9", true, null), "key", default))).Status); }
    }
    private sealed class Run
    {
        public Source Source; public RawAuthority Raw = new(); public Delegations Delegations; public WorkDecisionHostV1 Host;
        public List<(string Method, Guid PartyId, object Body, CounterpartyCommandMetadata Metadata)> Calls = [];
        public Action? BeforeNativeFence; public bool NativeEffect;
        public CounterpartyCommandExecution Answer = new(new(CounterpartyCommandResult.Succeeded, 204, null, null, null, null), true, "SENTETIK-original");
        public Run(bool tax)
        {
            Source = new(tax); Delegations = new(Source); var company = new CompanyContext(); company.Set(Company);
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-correlation", null, "Web", null, null);
            var fence = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Raw, fence);
            var taxOwner = DispatchProxy.Create<ICounterpartyTaxIdentityService, Owner>();
            var legalOwner = DispatchProxy.Create<ICounterpartyLegalNameService, Owner>();
            async Task<CounterpartyCommandExecution> Execute(MethodInfo method, object?[] args)
            {
                Calls.Add((method.Name, (Guid)args[0]!, args[1]!, (CounterpartyCommandMetadata)args[2]!));
                BeforeNativeFence?.Invoke();
                foreach (var permission in new[] { Source.ApproverPermission, ErpPermissions.CounterpartyPartiesRead })
                    if (!(await authority.RevalidatePermissionScopeAsync(permission, Company)).IsGranted) throw new UnauthorizedAccessException();
                NativeEffect = true; return Answer;
            }
            ((Owner)(object)taxOwner).Execute = Execute; ((Owner)(object)legalOwner).Execute = Execute;
            Host = new(company, new User(), correlation, authority, [Source], Delegations, fence, null!, null!,
                counterpartyTaxIdentity: taxOwner, counterpartyLegalName: legalOwner);
        }
    }
    public class Owner : DispatchProxy
    {
        public Func<MethodInfo, object?[], Task<CounterpartyCommandExecution>> Execute = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Execute(targetMethod!, args!);
    }
    private sealed class Source(bool tax) : IWorkApprovalDecisionSourceV1
    {
        public Guid CompanyId = Company; public bool Pending = true; public string Maker = "SENTETIK-maker";
        public IReadOnlyList<WorkApprovalReferenceV1> References = [new("party", "SENTETIK", Party, "/cari/" + Party)];
        public string Kind => tax ? WorkDecisionHostV1.CounterpartyTaxIdentity : WorkDecisionHostV1.CounterpartyLegalName;
        public string Module => "counterparty";
        public string ApproverPermission => tax ? CounterpartyTaxIdentityPermissionsV1.Approve : CounterpartyLegalNamePermissionsV1.Approve;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(CompanyId, new(Module, Kind, Item, "SENTETIK", DateTimeOffset.UtcNow, "/cari/" + Party, Maker, [], References), "9", Pending));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class RawAuthority : IErpAuthorityRevalidationService
    {
        public HashSet<string> Denied = []; public List<string> Asked = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Asked.Add(permission); return Task.FromResult(Denied.Contains(permission) ? PermissionScope.Denied : new(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Delegations(Source source) : IWorkDelegationReaderV1
    {
        public bool Allowed = true; public int Calls;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default)
        { Calls++; return Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, Company, source.Kind, source.ApproverPermission,
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null); }
    }
}
