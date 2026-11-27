using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingJournalReversalLifecycleV1Tests
{
    private static readonly JsonSerializerOptions ReversalJson = new(JsonSerializerDefaults.Web);
    private static AccountingJournalReversalProposalViewV1 DecisionView(AccountingJournalReversalProposalV1 p) => new(p.ProposalId, p.CompanyId,
        p.SourceJournalId, p.ReversalJournalId, p.PeriodId, p.TransactionDate, p.AccountingDate, p.ValueDate, p.Reason,
        p.ProposedBy, p.ProposedAtUtc, p.Status.ToString(), p.DecidedBy, p.DecidedAtUtc, p.DecisionReason, p.Version);
    private static (AccountingCommandRecord Command, AccountingJournalReversalDecisionSourceV1 Source,
        AccountingApiResult<AccountingJournalReversalProposalViewV1> Answer) ReceiptFixture()
    {
        var approved = AccountingJournalReversalPolicyV1.Approve(Pending(), new(Proposal, 1), Checker, Guid.NewGuid(), Now.AddMinutes(1));
        var answer = new AccountingApiResult<AccountingJournalReversalProposalViewV1>(AccountingApiStatus.Ok, DecisionView(approved), false, null);
        var command = new AccountingCommandRecord(AccountingJournalReversalCommandNamesV1.Approve,
            AccountingJournalReversalServiceV1.JournalReversalApprovalKeyHash("key"), Checker.ToString("D"),
            AccountingJournalReversalServiceV1.JournalReversalApprovalPayloadHash(Proposal, 1), JsonSerializer.Serialize(answer, ReversalJson), "original-correlation");
        return (command, new(approved, true, true, command.OriginalCorrelationId, null), answer);
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("key")] [InlineData("payload")] [InlineData("command")]
    [InlineData("version")] [InlineData("source")] [InlineData("reversal")] [InlineData("header-correlation")]
    [InlineData("unknown-root")] [InlineData("unknown-resource")] [InlineData("private-proof")] [InlineData("missing-resource")]
    public void Reversal_receipt_rejects_unbound_or_noncanonical_original_native_answers(string fault)
    {
        var (command, source, answer) = ReceiptFixture();
        var json = JsonNode.Parse(command.ResultJson)!;
        if (fault == "company") source = source with { Proposal = source.Proposal with { CompanyId = Guid.NewGuid() } };
        if (fault == "actor") command = command with { ActorId = Maker.ToString("D") };
        if (fault == "key") command = command with { IdempotencyKeyHash = "wrong" };
        if (fault == "payload") command = command with { RequestHash = "wrong" };
        if (fault == "command") command = command with { CommandName = AccountingJournalReversalCommandNamesV1.Propose };
        if (fault == "version") answer = answer with { Resource = answer.Resource! with { Version = 3 } };
        if (fault == "source") source = source with { Proposal = source.Proposal with { Reason = "another recorded reason" } };
        if (fault == "reversal") source = source with { ReversalJournalBound = false };
        if (fault == "header-correlation") source = source with { ReversalCorrelationId = "foreign" };
        if (fault == "unknown-root") json["unexpected"] = true;
        if (fault == "unknown-resource") json["resource"]!["unexpected"] = true;
        if (fault == "private-proof") json["resource"]!["approvalReceipt"] = new JsonObject();
        if (fault == "missing-resource") json.AsObject().Remove("resource");
        if (fault is "unknown-root" or "unknown-resource" or "private-proof" or "missing-resource") command = command with { ResultJson = json.ToJsonString() };
        Assert.Null(AccountingJournalReversalServiceV1.BindJournalReversalApprovalReceipt(command, source, answer, 1, "key", Checker));
    }

    [Fact]
    public void Genuine_original_receipt_and_replay_preserve_native_sixteen_field_wire_and_original_hash()
    {
        var (command, source, answer) = ReceiptFixture();
        var proof = AccountingJournalReversalServiceV1.BindJournalReversalApprovalReceipt(command, source, answer, 1, "key", Checker);
        Assert.NotNull(proof); Assert.True(proof.CancellationSourceActorProofEstablished); Assert.Null(proof.CancellationSourceActorId);
        Assert.Equal(AccountingApiService.Sha256(command.ResultJson), proof.OriginalResultDigest);
        var withProof = answer with { Resource = answer.Resource! with { ApprovalReceipt = proof } };
        Assert.Equal(command.ResultJson, JsonSerializer.Serialize(withProof, ReversalJson));
        Assert.Equal(AccountingApiService.Sha256(command.ResultJson), AccountingJournalReversalServiceV1.JournalReversalOriginalResultDigest(withProof));
        var replay = withProof with { IsReplay = true, OriginalCorrelationId = command.OriginalCorrelationId };
        Assert.Equal(proof, AccountingJournalReversalServiceV1.BindJournalReversalApprovalReceipt(command, source, replay, 1, "key", Checker));
        Assert.Equal(16, JsonSerializer.SerializeToElement(withProof.Resource, ReversalJson).EnumerateObject().Count());
        // Approval changes mutable status/version only, never the proposal's recorded source digest.
        Assert.Equal(AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(DecisionView(Pending())), proof.SourceFactsDigest);
    }

    [Fact]
    public void Genuine_reversal_receipt_privately_binds_actual_cancellation_actor_and_distinguishes_unestablished_proof()
    {
        var (command, source, answer) = ReceiptFixture(); var cancellationActor = Guid.NewGuid();
        var bound = AccountingJournalReversalServiceV1.BindJournalReversalApprovalReceipt(command,
            source with { CancellationSourceActorId = cancellationActor }, answer, 1, "key", Checker);
        Assert.NotNull(bound); Assert.True(bound.CancellationSourceActorProofEstablished); Assert.Equal(cancellationActor, bound.CancellationSourceActorId);
        var json = JsonSerializer.SerializeToElement(bound, ReversalJson);
        Assert.Equal(15, json.EnumerateObject().Count()); Assert.False(json.TryGetProperty("cancellationSourceActorId", out _));
        Assert.False(json.TryGetProperty("cancellationSourceActorProofEstablished", out _));
        var serializedView = JsonSerializer.Serialize(answer with { Resource = answer.Resource! with { ApprovalReceipt = bound } }, ReversalJson);
        Assert.Equal(command.ResultJson, serializedView);
        var legacyBody = JsonSerializer.Deserialize<AccountingJournalReversalApprovalReceiptV1>(JsonSerializer.Serialize(bound, ReversalJson), ReversalJson);
        Assert.NotNull(legacyBody); Assert.False(legacyBody.CancellationSourceActorProofEstablished);
    }

    [Theory]
    [InlineData("maker")] [InlineData("cancel-maker")] [InlineData("permission")] [InlineData("company")]
    [InlineData("source")] [InlineData("final-revoke")] [InlineData("final-cancel")]
    [InlineData("final-company")] [InlineData("final-actor")] [InlineData("final-member")]
    public async Task Purpose_reversal_read_preserves_native_authority_source_and_final_consumer_fence(string fault)
    {
        var source = new AccountingJournalReversalDecisionSourceV1(Pending(), true, false, null, fault == "cancel-maker" ? Checker : null);
        var port = new DecisionReadPort(source); var authority = new ReversalReadAuthority();
        var company = new DecisionReadCompany(Company); var actor = fault == "maker" ? Maker : Checker;
        if (fault == "permission") authority.Allowed = false;
        if (fault == "company") port.Source = source with { Proposal = source.Proposal with { CompanyId = Guid.NewGuid() } };
        if (fault == "source") port.Source = source with { SourceJournalExists = false };
        var user = new DecisionReadUser(actor);
        var service = new AccountingJournalReversalServiceV1(port, new MemoryStore(), authority, company,
            user, new FixedCorrelation(), new FixedTimeProvider(Now));
        using var cancellation = new CancellationTokenSource(); var consumers = 0;
        Task Consumer(CancellationToken _) { consumers++; if (consumers == 3 && fault == "final-revoke") authority.Allowed = false;
            if (consumers == 3 && fault == "final-cancel") cancellation.Cancel();
            if (consumers == 3 && fault == "final-company") company.Set(Guid.NewGuid());
            if (consumers == 3 && fault == "final-actor") user.Actor = Guid.NewGuid();
            if (consumers == 3 && fault == "final-member") user.Member = false;
            return Task.CompletedTask; }
        if (fault == "final-cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(Proposal, Consumer, cancellation.Token));
        else
        {
            var answer = await service.ReadDecisionAsync(Proposal, Consumer);
            Assert.Equal(fault is "company" or "source" ? AccountingApiStatus.Unavailable : AccountingApiStatus.Denied, answer.Status);
            Assert.Null(answer.Resource);
        }
        Assert.Equal(0, port.Writes);
    }

    [Fact]
    public async Task Purpose_reversal_read_returns_full_native_proposal_with_no_general_journal_permission_or_write()
    {
        var port = new DecisionReadPort(new(Pending(), true, false, null, null)); var company = new DecisionReadCompany(Company);
        var service = new AccountingJournalReversalServiceV1(port, new MemoryStore(), new FixedAuthority(Granted()), company,
            new FixedUser(Checker), new FixedCorrelation(), new FixedTimeProvider(Now));
        var answer = await service.ReadDecisionAsync(Proposal, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, answer.Status); Assert.Equal(DecisionView(Pending()), answer.Resource!.Proposal);
        Assert.Equal(AccountingJournalReversalServiceV1.JournalReversalSourceFactsDigest(answer.Resource.Proposal), answer.Resource.SourceFactsDigest);
        Assert.Equal(0, port.Writes);
    }
    private sealed class DecisionReadUser(Guid actor) : ICurrentUser
    {
        public Guid Actor = actor; public bool Member = true;
        public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid id) => Member && id == Company;
    }
    private sealed class DecisionReadCompany(Guid id) : ICompanyContext
    { public bool IsResolved => true; public Guid CompanyId { get; private set; } = id; public void Set(Guid company) => CompanyId = company; }
    private sealed class DecisionReadPort(AccountingJournalReversalDecisionSourceV1 source) : IAccountingJournalReversalProposalStoreV1, IAccountingJournalReversalDecisionStoreV1
    {
        public AccountingJournalReversalDecisionSourceV1 Source = source; public int Writes;
        public async Task<AccountingJournalReversalDecisionSourceV1?> ReadJournalReversalDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); return Source; }
        public Task<AccountingJournalReversalProposalV1?> FindAsync(Guid id, CancellationToken token = default) => Task.FromResult<AccountingJournalReversalProposalV1?>(Source.Proposal);
        public Task<(AccountingJournalReversalProposalV1 Proposal, AccountingWriteOutcome Outcome)> CreateAsync(AccountingJournalReversalProposalV1 proposal, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        { Writes++; throw new InvalidOperationException("Read-only fixture"); }
        public Task<AccountingJournalReversalProposalV1> ApproveAndReverseAsync(Guid id, int version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        { Writes++; throw new InvalidOperationException("Read-only fixture"); }
    }
}
