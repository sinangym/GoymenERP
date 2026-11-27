using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Controlled host/owner boundary, not actual HTTP, Identity, SQL or business acceptance.
public sealed class WorkPartyCreditLimitDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid(), Party = Guid.NewGuid(), Account = Guid.NewGuid(), Currency = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-proposer", Key = "SENTETIK-original-key", Note = "SENTETIK reason";
    private static WorkPartyCreditLimitPreviewV1 Human() => new(Party, Account, "Set", Currency, "00123.4500", "0", "0", Maker, At);
    private static PartyCreditLimitCommandResult Answer(bool approve, bool replay = false)
    {
        var command = approve ? FinancePartyCreditLimitService.ApproveCommand : FinancePartyCreditLimitService.RejectCommand;
        var hash = approve ? FinancePartyCreditLimitService.PayloadHash(command, new PartyCreditLimitApproveRequest(Id, "1"))
            : FinancePartyCreditLimitService.PayloadHash(command, new PartyCreditLimitRejectRequest(Id, "1", Note));
        var correlation = replay ? "SENTETIK-old-correlation" : "SENTETIK-work";
        return new(CommandOutcome.Succeeded, new(Id, Company, Party, Account, PartyCreditLimitKind.Set, Currency, Human().Amount,
            approve ? PartyCreditLimitStatus.Approved : PartyCreditLimitStatus.Rejected, false, approve ? "1" : null,
            approve ? null : Note, At, At.AddHours(1), null, "2"), replay, correlation, [])
        { DecisionReceipt = new(Company, Id, command, IdempotencyHash.ComputeKey(Key), hash, Actor, "1", "2", correlation, At.AddHours(1)) };
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Native_decision_and_old_key_replay_forward_the_original_proposal_version_and_keep_the_immutable_result(bool approve, bool replay)
    {
        var run = new Rig(); run.Source.Pending = !replay; run.Source.Version = replay ? "3" : "1"; run.Owner.Answer = Answer(approve, replay);
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Id, default);
        Assert.Equal(Human(), preview.PartyCreditLimit); Assert.False(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        var result = await run.Host.DecideAsync(run.Source.Kind, Id, new("1", approve, approve ? null : Note), Key, default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(Key, run.Owner.Key); Assert.Equal("1", run.Owner.Version); Assert.Equal(Id, run.Owner.Id);
        Assert.Equal(approve ? null : Note, run.Owner.Reason); Assert.Equal(1, run.Owner.Calls);
        Assert.Equal(run.Owner.Answer.OriginalCorrelationId, result.OriginalCorrelationId);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("proposal")] [InlineData("party")] [InlineData("account")]
    [InlineData("amount")] [InlineData("currency")] [InlineData("version")] [InlineData("direct")]
    [InlineData("key")] [InlineData("hash")] [InlineData("actor")] [InlineData("command")] [InlineData("expected")]
    [InlineData("result-version")] [InlineData("correlation")] [InlineData("time")] [InlineData("status")]
    public async Task Forged_missing_or_current_row_reconstructed_success_is_unavailable(string fault)
    {
        var run = new Rig(); var answer = Answer(true); var r = answer.Resource!; var proof = answer.DecisionReceipt!;
        r = fault switch { "company" => r with { CompanyId = Guid.NewGuid() }, "proposal" => r with { Id = Guid.NewGuid() },
            "party" => r with { PartyId = Guid.NewGuid() }, "account" => r with { CompanyAccountId = Guid.NewGuid() },
            "amount" => r with { Amount = "123.45" }, "currency" => r with { CurrencyId = Guid.NewGuid() },
            "version" => r with { Version = "3" }, "direct" => r with { IsDirect = true },
            "status" => r with { Status = PartyCreditLimitStatus.Superseded }, _ => r };
        proof = fault switch { "key" => proof with { IdempotencyKeyHash = "forged" }, "hash" => proof with { PayloadHash = "forged" },
            "actor" => proof with { ActorId = Maker }, "command" => proof with { CommandName = FinancePartyCreditLimitService.SetCommand },
            "expected" => proof with { ExpectedVersion = "2" }, "result-version" => proof with { ResultVersion = "3" },
            "correlation" => proof with { OriginalCorrelationId = "different" }, "time" => proof with { RecordedAtUtc = At }, _ => proof };
        run.Owner.Answer = answer with { Resource = r, DecisionReceipt = fault == "missing" ? null : proof };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null), Key, default))).Status);
    }

    [Theory]
    [InlineData("amount")] [InlineData("kind")] [InlineData("maker")] [InlineData("reference")] [InlineData("company")] [InlineData("id")]
    public async Task Malformed_or_misbound_source_never_releases_human_facts(string fault)
    {
        var run = new Rig(); run.Source.Fault = fault;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Id, default))).Status);
        Assert.Equal(0, run.Owner.Calls);
    }

    [Theory]
    [InlineData(true, "note", "1")] [InlineData(false, null, "1")] [InlineData(false, " ", "1")]
    [InlineData(false, " untrimmed ", "1")] [InlineData(true, null, "01")] [InlineData(true, null, "0")]
    [InlineData(true, null, "9223372036854775807")]
    public async Task Invalid_native_body_never_calls_the_owner(bool approve, string? reason, string version)
    {
        var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new(version, approve, reason), Key, default))).Status);
        Assert.Equal(0, run.Owner.Calls);
    }

    [Fact]
    public async Task Maker_and_revoked_permission_or_delegation_fail_closed_and_unsupported_selection_is_refused()
    {
        var maker = new Rig(Maker);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => maker.Host.PreviewAsync(maker.Source.Kind, Id, default))).Status);
        var run = new Rig(); run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null, Guid.NewGuid()), Key, default))).Status);
        Assert.Equal(0, run.Owner.Calls); run.Delegations.Live = true;
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null, ProductPackageStandard: new("2026-12-01")), Key, default))).Status);
        run.Owner.BeforeReturn = () => run.Authority.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null), Key, default))).Status);
    }

    [Fact]
    public async Task Corrupt_native_receipt_json_is_unavailable_without_releasing_a_success()
    {
        var run = new Rig(); run.Owner.BeforeReturn = () => throw new JsonException("SENTETIK malformed receipt");
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null), Key, default))).Status);
        Assert.Equal(1, run.Owner.Calls);
    }

    [Fact]
    public async Task Native_terminal_refusal_and_readonly_human_wire_keep_the_old_command_contract()
    {
        var run = new Rig(); run.Owner.Answer = new(CommandOutcome.VersionConflict, null, true, "SENTETIK-terminal", [new("expectedVersion", "version_conflict")]);
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Id, new("1", true, null), Key, default));
        Assert.Equal((409, true, "SENTETIK-terminal"), (refusal.Status, refusal.Replayed, refusal.OriginalCorrelationId));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var preview = JsonSerializer.Serialize(await run.Host.PreviewAsync(run.Source.Kind, Id, default), options);
        Assert.Contains("\"partyCreditLimit\"", preview); Assert.Contains(Human().Amount!, preview);
        Assert.DoesNotContain("partyCreditLimitProof", JsonSerializer.Serialize(await run.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Id, default), options));
        Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(Answer(true), FinanceJson.CreateOptions()));
        using var command = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options)); Assert.Equal(4, command.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("partyCreditLimit", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.OpeningCost, Id, "SENTETIK", "1", true, true, [], []), options));
        Assert.True(FinancePartyCreditLimitService.IsExactAmount(Human().Amount)); Assert.False(FinancePartyCreditLimitService.IsExactAmount("1e2"));
    }

    private sealed class Rig
    {
        public readonly Source Source = new(); public readonly Owner Owner = new(); public readonly Delegations Delegations = new();
        public readonly Authority Authority = new(); public WorkDecisionHostV1 Host { get; }
        public Rig(string actor = Actor)
        {
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            var fence = new WorkDecisionExecutionFenceV1();
            Host = new(company, new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor), correlation,
                new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!, partyCreditLimits: Owner);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public bool Pending = true; public string Version = "1", Fault = ""; public string Module => "finance";
        public string Kind => WorkDecisionHostV1.PartyCreditLimit; public string ApproverPermission => ErpPermissions.FinanceCreditLimitsApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        {
            var p = Human(); p = Fault switch { "amount" => p with { Amount = "1e2" }, "kind" => p with { Kind = "Bogus" }, "maker" => p with { ProposedBy = "wrong" }, _ => p };
            return Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company, new(Module, Kind, Id, "SENTETIK", At, "/finans/kredi-limitleri", Maker, [],
                [new("party", "SENTETIK", Fault == "reference" ? Guid.NewGuid() : Party, null), new("company-account", "SENTETIK", Account, null), new("currency", "SENTETIK", Currency, null)]), Version, Pending,
                PartyCreditLimitProof: new(Fault == "company" ? Guid.NewGuid() : Company, Fault == "id" ? Guid.NewGuid() : Id, p)));
        }
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int count, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class Owner : IFinancePartyCreditLimitMaster
    {
        public PartyCreditLimitCommandResult Answer = WorkPartyCreditLimitDecisionHostTests.Answer(true); public Action? BeforeReturn;
        public string? Key, Version, Reason; public Guid Id; public int Calls;
        public Task<PartyCreditLimitCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitApproveRequest r, CancellationToken t)
        { Calls++; Key = m.IdempotencyKey; Version = r.ExpectedVersion; Id = r.ProposalId; BeforeReturn?.Invoke(); return Task.FromResult(Answer); }
        public Task<PartyCreditLimitCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitRejectRequest r, CancellationToken t)
        { Calls++; Key = m.IdempotencyKey; Version = r.ExpectedVersion; Id = r.ProposalId; Reason = r.Reason; BeforeReturn?.Invoke(); return Task.FromResult(Answer); }
        public Task<PartyCreditLimitCommandResult> SetAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitSetRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<PartyCreditLimitCommandResult> RemoveAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitRemoveRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<PartyCreditLimitCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, PartyCreditLimitProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<PartyCreditLimitListPage>> ListAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<PartyCreditLimitAccountView>> GetAccountAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<PartyCreditLimitHistoryPage>> HistoryAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<PartyCreditLimitCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, ErpPermissions.FinanceCreditLimitsApprove, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(ErpPermissions.FinanceCreditLimitsApprove, permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
}
