using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOpeningBalanceTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Purpose_read_uses_only_approval_authority_and_exact_retained_target_without_general_list_permission(bool reversal)
    {
        await using var run = new Run(); var proposal = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!;
        var id = proposal.Id;
        if (reversal)
        {
            await run.ApproveAsync(Manager, new(id, "1"));
            id = (await run.ProposeReversalAsync(Specialist, new(proposal.Id, "2", Reason))).Resource!.Reversal!.Id;
        }
        await using var scope = new OpeningReadScope(run, Manager); var callbacks = 0;
        var answer = await scope.Read(reversal, id, _ => { callbacks++; return Task.CompletedTask; });
        Assert.Equal(ReadOutcome.Found, answer.Outcome); Assert.NotNull(answer.Resource!.DecisionProof);
        Assert.Equal(reversal ? 8 : 6, callbacks); Assert.All(scope.Permissions.Actions, action => Assert.Equal(FinanceAction.ApproveOpeningBalance, action));
        Assert.Equal(proposal.Id, answer.Resource.OpeningBalance.Id); Assert.Equal(reversal ? (Guid?)id : null, answer.Resource.DecisionProof!.ReversalId);
        Assert.Equal(reversal ? (Guid?)id : null, answer.Resource.OpeningBalance.Reversal?.Id);
        Assert.False(scope.Database.ChangeTracker.HasChanges());
        Assert.Equal(ReadOutcome.Denied, (await scope.Service.ListAsync(scope.Context, default)).Outcome);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer.Resource, FinanceJson.CreateOptions()));
        Assert.Single(json.RootElement.EnumerateObject()); Assert.False(json.RootElement.TryGetProperty("decisionProof", out _));
        Assert.True(json.RootElement.TryGetProperty("openingBalance", out var openingJson));
        Assert.Equal(17, openingJson.EnumerateObject().Count());
        if (reversal) { Assert.True(openingJson.TryGetProperty("reversal", out var reversalJson)); Assert.Equal(9, reversalJson.EnumerateObject().Count()); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Purpose_read_denies_its_own_maker_and_foreign_company_but_reversal_does_not_exclude_the_opening_maker(bool reversal)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!; var id = opening.Id;
        const string reversalMaker = "SENTETIK-reversal-maker";
        if (reversal)
        {
            await run.ApproveAsync(Manager, new(opening.Id, "1"));
            id = (await run.ProposeReversalAsync(reversalMaker, new(opening.Id, "2", Reason))).Resource!.Reversal!.Id;
        }
        await using var maker = new OpeningReadScope(run, reversal ? reversalMaker : Specialist);
        Assert.Equal(ReadOutcome.Denied, (await maker.Read(reversal, id)).Outcome);
        await using var foreign = new OpeningReadScope(run, Manager, CompanyB);
        Assert.Equal(ReadOutcome.NotFound, (await foreign.Read(reversal, id)).Outcome);
        if (reversal)
        {
            await using var openingMaker = new OpeningReadScope(run, Specialist);
            Assert.Equal(ReadOutcome.Found, (await openingMaker.Read(true, id)).Outcome);
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Purpose_final_consumer_cancellation_never_releases_found_facts_even_when_the_callback_completes_normally(bool reversal)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!; var id = opening.Id;
        if (reversal) { await run.ApproveAsync(Manager, new(id, "1")); id = (await run.ProposeReversalAsync(Specialist, new(id, "2", Reason))).Resource!.Reversal!.Id; }
        for (var cancelAt = 1; cancelAt <= (reversal ? 8 : 6); cancelAt++)
        {
            await using var scope = new OpeningReadScope(run, Manager); using var cts = new CancellationTokenSource(); var calls = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Read(reversal, id, _ => { if (++calls == cancelAt) cts.Cancel(); return Task.CompletedTask; }, cts.Token));
            Assert.Equal(cancelAt, calls); Assert.False(scope.Database.ChangeTracker.HasChanges());
        }
    }

    [Theory] [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("delegation")]
    public async Task Purpose_read_rechecks_live_context_permission_and_consumer_delegation_before_release(string fault)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!;
        await using var scope = new OpeningReadScope(run, Manager); var calls = 0;
        async Task Fence(CancellationToken _)
        {
            await Task.CompletedTask;
            if (++calls != (fault == "permission" ? 5 : 6)) return;
            if (fault == "permission") scope.Permissions.Allowed = false;
            else if (fault == "actor") scope.Actor = Specialist;
            else if (fault == "company") scope.Company.Set(CompanyB);
            else throw new UnauthorizedAccessException("SENTETIK revoked delegation");
        }
        if (fault == "delegation") await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Read(false, opening.Id, Fence));
        else Assert.Equal(ReadOutcome.Denied, (await scope.Read(false, opening.Id, Fence)).Outcome);
        Assert.False(scope.Database.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(false, PermissionDecision.Denied)] [InlineData(true, PermissionDecision.Denied)]
    [InlineData(false, PermissionDecision.Unavailable)] [InlineData(true, PermissionDecision.Unavailable)]
    public async Task Purpose_owner_authority_is_rechecked_after_normal_completion_of_the_final_consumer(bool reversal, PermissionDecision decision)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!; var id = opening.Id;
        if (reversal) { await run.ApproveAsync(Manager, new(id, "1")); id = (await run.ProposeReversalAsync(Specialist, new(id, "2", Reason))).Resource!.Reversal!.Id; }
        await using var scope = new OpeningReadScope(run, Manager); var calls = 0; var final = reversal ? 8 : 6;
        var answer = await scope.Read(reversal, id, _ => { if (++calls == final) scope.Permissions.Override = decision; return Task.CompletedTask; });
        Assert.Equal(final, calls); Assert.Equal(decision == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, answer.Outcome);
        Assert.Null(answer.Resource); Assert.False(scope.Database.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData(PermissionDecision.Denied)] [InlineData(PermissionDecision.Unavailable)]
    public async Task Purpose_original_owner_refusal_is_not_overridden_when_a_later_consumer_restores_authority(PermissionDecision decision)
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!;
        await using var scope = new OpeningReadScope(run, Manager); scope.Permissions.Override = decision; var calls = 0;
        var answer = await scope.Read(false, opening.Id, _ => { if (++calls == 2) scope.Permissions.Override = PermissionDecision.Allowed; return Task.CompletedTask; });
        Assert.Equal(2, calls); Assert.Equal(decision == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
    }

    [Fact]
    public async Task Purpose_final_version_fence_refuses_a_mixed_pending_read_after_a_native_concurrent_decision()
    {
        await using var run = new Run(); var opening = (await run.ProposeAsync(Specialist, PartyRequest())).Resource!;
        await using var scope = new OpeningReadScope(run, Manager); var calls = 0;
        var answer = await scope.Read(false, opening.Id, async _ =>
        { if (++calls == 4) Assert.Equal(CommandOutcome.Succeeded, (await run.ApproveAsync("SENTETIK-other-checker", new(opening.Id, "1"))).Outcome); });
        Assert.Equal(ReadOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Native_receipt_private_proof_retains_original_command_identity_and_native_wire_with_clock_rollback(int operation)
    {
        var f = new OpeningReceiptFixture(operation); var proof = f.Restore(); Assert.NotNull(proof);
        Assert.Equal(f.Command, proof.CommandName); Assert.Equal("1", proof.ExpectedVersion); Assert.Equal("2", proof.ResultVersion);
        Assert.Equal(f.Result.OriginalCorrelationId, proof.OriginalCorrelationId); Assert.Equal(f.Target?.Id, proof.ReversalId);
        Assert.True(f.Receipt.RecordedAtUtc < (f.Target?.ProposedAtUtc ?? f.Opening.ProposedAtUtc));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(f.Result with { DecisionReceipt = proof }, FinanceJson.CreateOptions()));
        Assert.Equal(5, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(FinanceOpeningBalanceService.OriginalReceiptDigest(f.Result), FinanceOpeningBalanceService.OriginalReceiptDigest(f.Result with { IsReplay = true, DecisionReceipt = proof }));
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("key")] [InlineData("hash")] [InlineData("correlation")]
    [InlineData("command")] [InlineData("resource")] [InlineData("extra-json")] [InlineData("version")]
    [InlineData("decision-time")] [InlineData("facts")] [InlineData("movement")] [InlineData("movement-amount")]
    public void Native_receipt_refuses_inconsistent_or_fabricated_native_provenance_without_current_result_fallback(string fault)
    {
        var f = new OpeningReceiptFixture(0);
        switch (fault)
        {
            case "company": f.Receipt.CompanyId = CompanyB; break; case "actor": f.Receipt.ActorId = Specialist; break;
            case "key": f.Receipt.IdempotencyKeyHash = new string('0', 64); break; case "hash": f.Receipt.PayloadHash = new string('0', 64); break;
            case "correlation": f.Receipt.CorrelationId = "other"; break; case "command": f.Receipt.CommandName = FinanceOpeningBalanceService.RejectCommand; break;
            case "resource": f.Result = f.Result with { Resource = null }; break;
            case "extra-json": f.Receipt.ResultJson = f.Receipt.ResultJson[..^1] + ",\"extra\":true}"; break;
            case "version": f.Result = f.Result with { Resource = f.Result.Resource! with { Version = "3" } }; break;
            case "decision-time": f.Opening.DecidedAtUtc = At.AddDays(1); break;
            case "facts": f.Opening.AmountText = "99"; break; case "movement": f.Movement = null; break;
            case "movement-amount": f.Movement!.AmountText = "99"; break;
        }
        Assert.Null(f.Restore());
    }

    private sealed class OpeningReadScope : IAsyncDisposable
    {
        public FinanceDbContext Database { get; } public readonly OpeningDecisionCompany Company = new(); public readonly OpeningDecisionPermission Permissions = new();
        public string Actor; public FinanceOpeningBalanceService Service { get; } public FinanceContext Context => new(CompanyA, Actor, "SENTETIK-decision-read", "SENTETIK-cause");
        public OpeningReadScope(Run run, string actor, Guid? company = null)
        {
            Actor = actor; Company.Set(company ?? CompanyA); Database = run.Memory(company ?? CompanyA);
            Service = new(Database, Company, new OpeningDecisionUser(this), Permissions, new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK")]),
                new FixedParty(), new FixedPeriod(new(OpeningPeriodState.Open, PeriodVersion)), new AuditEventCollector(), Envelopes(), new FixedClock());
        }
        public Task<ReadResult<OpeningBalanceDecisionDetailV1>> Read(bool reversal, Guid id, Func<CancellationToken, Task>? fence = null, CancellationToken token = default)
        {
            var context = new FinanceContext(Company.CompanyId, Actor, "SENTETIK-decision-read", "SENTETIK-cause");
            return reversal ? Service.ReadReversalDecisionAsync(context, id, fence ?? (_ => Task.CompletedTask), token) : Service.ReadOpeningDecisionAsync(context, id, fence ?? (_ => Task.CompletedTask), token);
        }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    private sealed class OpeningDecisionCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class OpeningDecisionUser(OpeningReadScope scope) : GoymenERP.BuildingBlocks.Security.ICurrentUser
    { public bool IsAuthenticated => true; public string UserId => scope.Actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB }; public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company); }
    private sealed class OpeningDecisionPermission : IFinancePermissionPort
    {
        public bool Allowed = true; public PermissionDecision? Override; public readonly List<FinanceAction> Actions = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken token)
        { Actions.Add(a); return Task.FromResult(Override ?? (Allowed && a == FinanceAction.ApproveOpeningBalance ? PermissionDecision.Allowed : PermissionDecision.Denied)); }
    }

    // Pure verifier fixtures, not SQL provenance or a financial-event acceptance claim.
    private sealed class OpeningReceiptFixture
    {
        public readonly string Command; public readonly CommandMetadata Metadata = new("SENTETIK-receipt-key");
        public readonly FinanceContext Context = new(CompanyA, Manager, "SENTETIK-original", null);
        public OpeningBalanceRow Opening; public OpeningBalanceReversalRow? Target; public OpeningBalanceMovementRow? Movement;
        public OpeningBalanceCommandRow Receipt; public OpeningBalanceCommandResult Result;
        public OpeningReceiptFixture(int operation)
        {
            var reversal = operation >= 2; var approve = operation is 0 or 2; var decidedAt = At.AddMinutes(-1);
            Command = operation switch { 0 => FinanceOpeningBalanceService.ApproveCommand, 1 => FinanceOpeningBalanceService.RejectCommand, 2 => FinanceOpeningBalanceService.ApproveReversalCommand, _ => FinanceOpeningBalanceService.RejectReversalCommand };
            Opening = new() { Id = Guid.NewGuid(), CompanyId = CompanyA, TargetKind = FinanceOpeningBalanceService.PartyAccountKind, TargetId = Account, PartyId = Party,
                CurrencyId = Currency, Direction = FinanceOpeningBalanceService.DebitDirection, AmountText = "00125.40", AsOfDate = AsOf,
                OpeningPeriodVersion = PeriodVersion, Status = reversal ? approve ? 4 : 2 : approve ? 2 : 3, Version = reversal && approve ? 3 : 2,
                ProposedBy = Specialist, ProposedAtUtc = At, DecidedBy = reversal ? "SENTETIK-opening-checker" : Manager, DecidedAtUtc = reversal ? At.AddMinutes(-2) : decidedAt,
                RejectionReason = !reversal && !approve ? Reason : null, UpdatedAtUtc = decidedAt };
            if (reversal) Target = new() { Id = Guid.NewGuid(), CompanyId = CompanyA, OpeningBalanceId = Opening.Id, Status = approve ? 2 : 3,
                Reason = "SENTETIK reversal source", OpeningPeriodVersion = PeriodVersion, ProposedBy = "SENTETIK-reversal-maker", ProposedAtUtc = At,
                DecidedBy = Manager, DecidedAtUtc = decidedAt, RejectionReason = approve ? null : Reason, Version = 2, UpdatedAtUtc = decidedAt };
            var target = Target is null ? null : new OpeningBalanceReversalView(Target.Id, approve ? OpeningBalanceReversalStatus.Approved : OpeningBalanceReversalStatus.Rejected, Target.Reason, PeriodVersion, false, At, decidedAt, Target.RejectionReason, "2");
            var view = new OpeningBalanceView(Opening.Id, CompanyA, OpeningBalanceTargetKind.PartyAccount, Account, Party, Currency, OpeningBalanceDirection.Debit,
                Opening.AmountText, AsOf, (OpeningBalanceStatus)(Opening.Status - 1), PeriodVersion, false, At, Opening.DecidedAtUtc, Opening.RejectionReason, Opening.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), target);
            Result = new(CommandOutcome.Succeeded, view, false, Context.CorrelationId, []);
            var hash = operation switch { 0 => FinanceOpeningBalanceService.ApprovePayloadHash(new(Opening.Id, "1")), 1 => FinanceOpeningBalanceService.RejectPayloadHash(new(Opening.Id, "1", Reason)), 2 => FinanceOpeningBalanceService.ApproveReversalPayloadHash(new(Opening.Id, "1")), _ => FinanceOpeningBalanceService.RejectReversalPayloadHash(new(Opening.Id, "1", Reason)) };
            Receipt = new() { CompanyId = CompanyA, OpeningBalanceId = Opening.Id, CommandName = Command, IdempotencyKeyHash = FinanceCommandKeys.Hash(Metadata.IdempotencyKey),
                PayloadHash = hash, ActorId = Manager, CorrelationId = Context.CorrelationId, RecordedAtUtc = decidedAt, ResultJson = JsonSerializer.Serialize(Result, FinanceJson.CreateOptions()) };
            if (approve) Movement = new() { Id = Guid.NewGuid(), CompanyId = CompanyA, OpeningBalanceId = Opening.Id, Kind = reversal ? 2 : 1, TargetKind = Opening.TargetKind,
                TargetId = Account, PartyId = Party, CurrencyId = Currency, Direction = Opening.Direction, AmountText = Opening.AmountText, OccurredOn = AsOf, RecordedBy = Manager, RecordedAtUtc = decidedAt };
        }
        public OpeningBalanceDecisionReceiptV1? Restore() => FinanceOpeningBalanceService.RestoreDecisionReceipt(Receipt, Opening, Target, Movement, Context, Metadata, Opening.Id, "1", Command is FinanceOpeningBalanceService.RejectCommand or FinanceOpeningBalanceService.RejectReversalCommand ? Reason : null, Command, Result);
    }
}
