using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using CommandMetadata = GoymenERP.Modules.Finance.Contracts.CommandMetadata;

namespace GoymenERP.Api.IntegrationTests;

// Controlled native receipt/Organization transport boundary; no claim of SQL or a complete Identity delegation E2E.
public sealed class WorkBankAccountIbanDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-original-maker", Iban = "TR060006100519786457841327", Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Approve_or_original_key_replay_selects_the_frozen_version_and_original_maker_not_a_replacement(bool replay)
    {
        var run = new Rig(); run.Source.Historical = replay; run.Owner.Answer = Answer(replay);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, new("3", true, null), "original-key", default);
        Assert.Equal(("Approved", replay), (result.State, result.Replayed));
        Assert.Equal("3", run.Source.Selected); Assert.Equal(new BankAccountIbanChangeApproveRequest(Id, "3"), run.Owner.Request);
        Assert.Equal("original-key", run.Owner.Key); Assert.Equal(0, run.Organizations.Reads);
    }

    [Fact]
    public async Task Only_authorized_human_preview_discloses_actual_values_reason_and_exact_organization_owner_name()
    {
        var run = new Rig(); var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, default);
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        Assert.Equal((Company, "SENTETIK legal owner", Iban, Maker), (preview.BankAccountIbanChange!.CompanyId,
            preview.BankAccountIbanChange.CompanyLegalName, preview.BankAccountIbanChange.ProposedIban, preview.BankAccountIbanChange.ProposedBy));
        Assert.Equal("SENTETIK reason", preview.BankAccountIbanChange.Reason);
        Assert.DoesNotContain(Iban, JsonSerializer.Serialize(await run.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Id, default)));
        Assert.All(run.Authority.Permissions, permission => Assert.Equal(ErpPermissions.FinanceBankAccountApprove, permission));
        Assert.DoesNotContain("bankAccountIbanChange", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.OpeningCost, Id, "SENTETIK", "1", true, true, [], []), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("3", true, null), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(4, body.RootElement.EnumerateObject().Count()); Assert.DoesNotContain(Iban, body.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("placeholder")] [InlineData("foreign-owner")]
    public async Task Organization_await_cannot_release_old_authority_context_or_an_unproven_owner(string fault)
    {
        var run = new Rig();
        run.Organizations.AfterRead = () =>
        {
            if (fault == "permission") run.Authority.Granted = false;
            if (fault == "company") run.Company.Current = Guid.NewGuid();
            if (fault == "actor") run.User.Actor = "SENTETIK-other";
        };
        if (fault == "placeholder") run.Organizations.Values = [new(Company, "SENTETIK", "SENTETIK placeholder", true)];
        if (fault == "foreign-owner") run.Organizations.Values = [new(Guid.NewGuid(), "SENTETIK", "SENTETIK foreign", false)];
        Assert.Equal(fault is "placeholder" or "foreign-owner" ? 503 : 403,
            (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, default))).Status);
        Assert.Null(run.Owner.Request);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("version")] [InlineData("maker")]
    [InlineData("time")] [InlineData("digest")] [InlineData("actor")] [InlineData("command")] [InlineData("correlation")]
    [InlineData("resource-company")] [InlineData("resource-version")] [InlineData("receipt-key")]
    public async Task A_native_success_needs_its_exact_original_receipt_binding(string fault)
    {
        var run = new Rig(); var answer = Answer(false); var proof = answer.IbanApprovalReceipt!;
        proof = fault switch
        {
            "company" => proof with { CompanyId = Guid.NewGuid() }, "id" => proof with { AccountId = Guid.NewGuid() },
            "version" => proof with { ProposalVersion = "9" }, "maker" => proof with { ChangeProposedBy = "SENTETIK-new-maker" },
            "time" => proof with { ChangeProposedAtUtc = At.AddHours(1) }, "digest" => proof with { FactsDigest = new string('b', 64) },
            "actor" => proof with { ActorId = Maker }, "command" => proof with { CommandName = "finance.bank-account.approve" }, _ => proof,
        };
        if (fault == "receipt-key") proof = proof with { IdempotencyKeyHash = IdempotencyHash.ComputeKey("SENTETIK-different-key") };
        run.Owner.Answer = answer with { IbanApprovalReceipt = fault == "missing" ? null : proof,
            OriginalCorrelationId = fault == "correlation" ? "SENTETIK-wrong" : answer.OriginalCorrelationId,
            Resource = fault == "resource-company" ? answer.Resource! with { CompanyId = Guid.NewGuid() }
                : fault == "resource-version" ? answer.Resource! with { Version = "10" } : answer.Resource };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, new("3", true, null), "key", default))).Status);
    }

    [Fact]
    public async Task Historical_human_fields_are_unavailable_but_original_receipt_post_and_historical_delegation_target_remain_distinct()
    {
        var run = new Rig(); run.Source.Historical = true; run.Owner.Answer = Answer(true);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, default))).Status);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, new("3", true, null, Guid.NewGuid()), "old-key", default);
        Assert.True(result.Replayed); Assert.Equal(Maker, run.Delegations.Maker);
        run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, new("3", true, null, Guid.NewGuid()), "old-key", default))).Status);
    }

    [Theory]
    [InlineData(false, null, "3")] [InlineData(true, "note", "3")] [InlineData(true, null, "03")]
    public async Task No_reject_reason_or_raw_payload_is_forwarded_as_a_native_iban_decision(bool approve, string? reason, string version)
    {
        var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.BankAccountIbanChange, Id, new(version, approve, reason), "key", default))).Status);
        Assert.Null(run.Owner.Request);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.BankAccountIbanChange, Id,
            new("3", true, null, LogisticsCustomerReturn: new(Guid.NewGuid())), "key", default))).Status);
    }

    private static BankAccountCommandResult Answer(bool replay) => new(CommandOutcome.Succeeded,
        new(Id, Company, Guid.NewGuid(), "SENTETIK account", true, false, "4"), replay, replay ? "SENTETIK-original" : "SENTETIK-work", [],
        IbanApprovalReceipt: new(Company, Id, "finance.bank-account.iban-change.approve", Actor, At.AddHours(1), "3", "4", Maker, At, Digest, ""));
    private sealed class Rig
    {
        public readonly MutableCompany Company = new(); public readonly User User = new(); public readonly Authority Authority = new();
        public readonly Source Source = new(); public readonly Owner Owner = new(); public readonly Delegations Delegations = new();
        public readonly OrganizationOwner Organizations; public readonly WorkDecisionHostV1 Host;
        public Rig()
        {
            var reader = DispatchProxy.Create<IOrganizationService, OrganizationOwner>(); Organizations = (OrganizationOwner)(object)reader;
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1();
            Host = new(Company, User, correlation, new WorkDecisionAuthorityV1(Authority, execution), [Source], Delegations, execution,
                null!, null!, bankAccounts: Owner, bankAccountCompanies: reader);
        }
    }
    private sealed class Source : IWorkApprovalDecisionVersionSourceV1
    {
        public bool Historical; public string? Selected; public string Module => "finance"; public string Kind => WorkDecisionHostV1.BankAccountIbanChange;
        public string ApproverPermission => ErpPermissions.FinanceBankAccountApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(Snapshot());
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid id, string version, CancellationToken token)
        { Selected = version; return Task.FromResult<WorkApprovalDecisionSnapshotV1?>(Snapshot()); }
        private WorkApprovalDecisionSnapshotV1 Snapshot() => new(Company, new(Module, Kind, Id, "SENTETIK account", At, "/finans/banka-hesaplari", Maker,
            [], [new("bank-account", "SENTETIK account", Id, null)]), "3", !Historical,
            BankAccountIbanChangeProof: new(Company, Id, Maker, At, "3", Digest, Historical ? null : new(null, Iban, "SENTETIK reason")));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Owner : IFinanceBankAccountMaster
    {
        public BankAccountCommandResult Answer = WorkBankAccountIbanDecisionHostTests.Answer(false); public BankAccountIbanChangeApproveRequest? Request; public string? Key;
        public Task<BankAccountCommandResult> ApproveIbanChangeAsync(FinanceContext c, CommandMetadata m, BankAccountIbanChangeApproveRequest r, CancellationToken t)
        {
            Request = r; Key = m.IdempotencyKey;
            return Task.FromResult(Answer.IbanApprovalReceipt is { IdempotencyKeyHash: "" } receipt
                ? Answer with { IbanApprovalReceipt = receipt with { IdempotencyKeyHash = IdempotencyHash.ComputeKey(m.IdempotencyKey) } } : Answer);
        }
        public Task<BankAccountCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, BankAccountApproveRequest r, CancellationToken t) => throw new InvalidOperationException("Not bank-card approval.");
        public Task<BankAccountCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, BankAccountProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<BankAccountCommandResult> ProposeIbanChangeAsync(FinanceContext c, CommandMetadata m, BankAccountIbanChangeProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<BankAccountMasterPage>> ListMasterAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<BankAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
    }
    public class OrganizationOwner : DispatchProxy
    {
        public IReadOnlyList<CompanySummary> Values = [new(Company, "SENTETIK", "SENTETIK legal owner", false)]; public Action? AfterRead; public int Reads;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        { if (method?.Name != nameof(IOrganizationService.GetAuthorizedCompaniesAsync)) throw new NotSupportedException(); Reads++; AfterRead?.Invoke(); return Task.FromResult(Values); }
    }
    private sealed class MutableCompany : ICompanyContext
    { public Guid Current = WorkBankAccountIbanDecisionHostTests.Company; public Guid CompanyId => Current; public bool IsResolved => true; public void Set(Guid id) => Current = id; }
    private sealed class User : ICurrentUser
    { public string Actor = WorkBankAccountIbanDecisionHostTests.Actor; public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted = true; public List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid company, CancellationToken token = default)
        { Permissions.Add(p); return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true; public string? Maker;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default)
        { Maker = item.PreparedBy; return Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, ErpPermissions.FinanceBankAccountApprove,
            Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null); }
    }
}
