using System.Reflection;
using System.Text.Json;
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
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Human_proposal_proof_is_exact_only_in_the_authorized_preview_and_never_in_snapshot_or_inbox_serialization(bool tax)
    {
        var run = new Run(tax); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Item, default);
        Assert.NotNull(snapshot);
        var snapshotWire = JsonSerializer.Serialize(snapshot, options);
        Assert.DoesNotContain("counterpartyTaxIdentityChange", snapshotWire); Assert.DoesNotContain("counterpartyLegalNameChange", snapshotWire);
        Assert.DoesNotContain("1234567890", snapshotWire); Assert.DoesNotContain("9876543217", snapshotWire);
        Assert.DoesNotContain("1234567890", JsonSerializer.Serialize(snapshot.Item, options));
        if (!tax) run.Source.LegalProof = run.Source.LegalProof! with { CurrentLegalName = new string('A', 250), ProposedLegalName = new string('B', 250) };
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Item, default);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(preview, options));
        Assert.True(document.RootElement.TryGetProperty(tax ? "counterpartyTaxIdentityChange" : "counterpartyLegalNameChange", out var proof));
        Assert.Equal(Party, proof.GetProperty("partyId").GetGuid()); Assert.Equal(Item, proof.GetProperty("changeRequestId").GetGuid());
        Assert.Equal("SENTETIK reason", proof.GetProperty("reason").GetString());
        if (tax)
        { Assert.Equal("1234567890", proof.GetProperty("currentTaxIdentityNumber").GetString()); Assert.Equal("9876543217", proof.GetProperty("proposedTaxIdentityNumber").GetString()); Assert.Equal("vkn", proof.GetProperty("proposedKind").GetString()); }
        else
        { Assert.Equal(new string('A', 250), proof.GetProperty("currentLegalName").GetString()); Assert.Equal(new string('B', 250), proof.GetProperty("proposedLegalName").GetString()); }
        Assert.False(document.RootElement.TryGetProperty(tax ? "counterpartyLegalNameChange" : "counterpartyTaxIdentityChange", out _));
        var command = JsonSerializer.Serialize(new WorkDecisionRequestV1("9", true, null), options);
        Assert.DoesNotContain("counterparty", command);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Missing_misbound_wrong_kind_unjustified_or_out_of_native_bounds_human_proof_is_never_approvable(bool tax)
    {
        foreach (var fault in new[] { "missing", "party", "request", "wrong-kind", "reason", "value", "bounds", "kind" })
        {
            var run = new Run(tax);
            if (tax)
            {
                var proof = run.Source.TaxProof!;
                run.Source.TaxProof = fault switch {
                    "missing" => null, "party" => proof with { PartyId = Guid.NewGuid() }, "request" => proof with { ChangeRequestId = Guid.NewGuid() },
                    "reason" => proof with { Reason = " " }, "value" => proof with { ProposedTaxIdentityNumber = "9876543210" },
                    "bounds" => proof with { Reason = new string('n', 1001) }, "kind" => proof with { ProposedKind = "tckn" }, _ => proof };
                if (fault == "wrong-kind") run.Source.LegalProof = new(Party, Item, "old", "new", "SENTETIK");
            }
            else
            {
                var proof = run.Source.LegalProof!;
                run.Source.LegalProof = fault switch {
                    "missing" => null, "party" => proof with { PartyId = Guid.NewGuid() }, "request" => proof with { ChangeRequestId = Guid.NewGuid() },
                    "reason" => proof with { Reason = " " }, "value" => proof with { ProposedLegalName = proof.CurrentLegalName },
                    "bounds" => proof with { ProposedLegalName = new string('n', 251) }, "kind" => proof with { CurrentLegalName = " " }, _ => proof };
                if (fault == "wrong-kind") run.Source.TaxProof = new(Party, Item, "1234567890", "9876543217", "vkn", "SENTETIK");
            }
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Item, default))).Status);
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, new("9", true, null), "key", default))).Status);
            Assert.Empty(run.Calls);
        }
    }

    [Fact]
    public async Task Human_tax_proof_preserves_a_historical_checksum_invalid_current_value_for_correction_without_a_new_policy()
    {
        var run = new Run(true);
        run.Source.TaxProof = run.Source.TaxProof! with { CurrentTaxIdentityNumber = "10000000147" };
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Item, default);
        Assert.Equal("10000000147", preview.CounterpartyTaxIdentityChange!.CurrentTaxIdentityNumber);
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
        public WorkCounterpartyTaxIdentityChangePreviewV1? TaxProof = tax ? new(Party, Item, "1234567890", "9876543217", "vkn", "SENTETIK reason") : null;
        public WorkCounterpartyLegalNameChangePreviewV1? LegalProof = tax ? null : new(Party, Item, "SENTETIK current", "SENTETIK proposed", "SENTETIK reason");
        public IReadOnlyList<WorkApprovalReferenceV1> References = [new("party", "SENTETIK", Party, "/cari/" + Party)];
        public string Kind => tax ? WorkDecisionHostV1.CounterpartyTaxIdentity : WorkDecisionHostV1.CounterpartyLegalName;
        public string Module => "counterparty";
        public string ApproverPermission => tax ? CounterpartyTaxIdentityPermissionsV1.Approve : CounterpartyLegalNamePermissionsV1.Approve;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(CompanyId, new(Module, Kind, Item, "SENTETIK", DateTimeOffset.UtcNow, "/cari/" + Party, Maker, [], References), "9", Pending,
                CounterpartyTaxIdentityChange: TaxProof, CounterpartyLegalNameChange: LegalProof));
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
