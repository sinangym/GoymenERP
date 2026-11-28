using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesContractServiceTests
{
    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "1")]
    [InlineData(true, "01")]
    [InlineData(false, "01")]
    public async Task Contract_discount_safety_requires_its_typed_owner_before_native_replay_or_mutation(bool approve, string version)
    {
        var f = Build(Manager);
        f.Store.Stored = Pending();
        f.Service.WithContractDiscountDecisionSafety();
        var body = new SalesDraftStateCommandRequest(version, approve ? null : "  SENTETIK raw reason  ");
        var failure = await Refused(() => approve
            ? f.Service.ApproveContractDiscountAsync(ContractId, body, Metadata)
            : f.Service.RejectContractDiscountAsync(ContractId, body, Metadata));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(f.Store.Calls);
        Assert.Empty(f.Store.Refusals);
        Assert.Equal(Pending().State, f.Store.Stored.State);
        Assert.Equal(1L, f.Store.Stored.Version);
    }
    [Theory]
    [InlineData(true, "1", null)] [InlineData(false, "1", "  SENTETIK ret  ")]
    [InlineData(true, "01", "legacy ignored approval note")] [InlineData(false, "01", "  SENTETIK ret\n  ")]
    public async Task Typed_contract_boundary_forwards_the_unchanged_native_core_hash_raw_version_reason_and_actor(bool approve, string version, string? reason)
    {
        var f = ContractSafetyFixture(); var body = new SalesDraftStateCommandRequest(version, reason);
        var answer = await ContractDecide(f.Service, approve, body);
        var command = approve ? SalesContractCommands.ApproveDiscountV1 : SalesContractCommands.RejectDiscountV1;
        Assert.Equal(SalesContractHash.Compute(command, ContractId, body), f.Boundary.Hash);
        Assert.Equal(command, f.Boundary.Command); var envelope = f.Boundary.Envelope; Assert.NotNull(envelope);
        Assert.Equal(Manager, envelope.ActorId); Assert.Equal(SalesPermissionManifestV1.ContractsDiscountApprove, envelope.PermissionCode);
        Assert.Equal((approve ? "Active" : "DiscountRejected", "2", false, Metadata.CorrelationId),
            (answer.Contract.State, answer.Contract.Version, answer.IsReplay, answer.OriginalCorrelationId));
        Assert.Equal(approve ? null : reason!.Trim(), answer.Contract.RejectionReason);
        Assert.Equal(1, f.Boundary.Starts); Assert.Equal(1, f.Boundary.Executions);
        Assert.Equal(1, f.Store.Calls.Count(x => x == "decide")); Assert.Empty(f.Store.Refusals);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task Entry_cleanliness_is_captured_before_and_after_the_first_authority_await(bool initial, bool after)
    {
        var f = ContractSafetyFixture(); f.Boundary.Clean = initial;
        f.Authority.AfterRead = _ => { f.Boundary.Clean = after; return Task.CompletedTask; };
        var failure = await Refused(() => ContractDecide(f.Service, true, new("1")));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Equal(0, f.Boundary.Starts); Assert.Empty(f.Store.Calls); Assert.Empty(f.Store.Refusals);
        Assert.Equal(1L, f.Store.Stored!.Version);
    }

    [Theory]
    [InlineData(true, "self")] [InlineData(false, "self")]
    [InlineData(true, "version")] [InlineData(false, "version")]
    public async Task Native_self_and_stale_decisions_keep_their_original_terminal_code_and_raw_payload(bool approve, string fault)
    {
        var f = ContractSafetyFixture(fault == "self" ? Seller : Manager);
        var body = new SalesDraftStateCommandRequest(fault == "version" ? "02" : "1", approve ? null : "  SENTETIK ret  ");
        var failure = await Refused(() => ContractDecide(f.Service, approve, body));
        Assert.Equal(409, failure.Status);
        Assert.Equal(fault == "self" ? SalesContractProblemCodesV1.SelfDecision : SalesDraftProblemCodesV1.VersionConflict, failure.Code);
        var refusal = Assert.Single(f.Store.Refusals); Assert.Equal(failure.Code, refusal.Refusal.Code);
        Assert.Equal(SalesContractHash.Compute(refusal.Command, ContractId, body), f.Boundary.Hash);
        Assert.Equal(1L, f.Store.Stored!.Version);
    }

    [Fact]
    public async Task Typed_boundary_replays_the_original_key_before_mutation_or_general_owner_reads()
    {
        var f = ContractSafetyFixture(); f.Store.Stored!.ApproveDiscount(1, Manager, Now);
        f.Store.Replay = new(f.Store.Stored, null, "SENTETIK original retained correlation");
        var answer = await ContractDecide(f.Service, true, new("01"));
        Assert.True(answer.IsReplay); Assert.Equal("SENTETIK original retained correlation", answer.OriginalCorrelationId);
        Assert.DoesNotContain("decide", f.Store.Calls); Assert.DoesNotContain("find", f.Store.Calls);
        Assert.Equal(0, f.Accounts.Calls); Assert.Empty(f.Store.Refusals);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Final_native_price_read_is_optional_reduces_only_and_never_releases_cached_visible_amounts(bool lateGrant)
    {
        var f = ContractSafetyFixture(); f.Authority.PriceGranted = !lateGrant;
        f.Authority.AfterRead = permission =>
        {
            if (permission == SalesPermissionManifestV1.OrdersPriceRead && f.Authority.PriceReads == 3) f.Authority.PriceGranted = lateGrant;
            return Task.CompletedTask;
        };
        var answer = await ContractDecide(f.Service, true, new("1"));
        Assert.Equal(SalesPriceDisclosure.Withheld, answer.Contract.AmountDisclosure);
        Assert.Null(answer.Contract.PlannedTotalAmount); Assert.Null(answer.Contract.DiscountLimitPercent);
        Assert.All(answer.Contract.Installments, x => Assert.Null(x.Amount)); Assert.All(answer.Contract.DiscountRules, x => Assert.Null(x.RatePercent));
        Assert.Equal(3, f.Authority.PriceReads); Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, f.Authority.Asked[^1]);
        Assert.DoesNotContain(SalesPermissionManifestV1.ContractsRead, f.Authority.Asked);
        Assert.Equal(1, f.Store.Calls.Count(x => x == "decide"));
    }

    [Theory]
    [InlineData("price")] [InlineData("actor")] [InlineData("membership")] [InlineData("company")] [InlineData("cancel")]
    public async Task Final_price_callback_cannot_release_a_native_answer_under_changed_context_or_cancellation(string fault)
    {
        var f = ContractSafetyFixture(); using var cancellation = new CancellationTokenSource(); var fired = false;
        f.Authority.AfterRead = permission =>
        {
            if (permission != SalesPermissionManifestV1.OrdersPriceRead || f.Authority.PriceReads != 3) return Task.CompletedTask;
            fired = true;
            if (fault == "actor") f.User.Actor = "SENTETIK another actor";
            if (fault == "membership") f.User.Member = false;
            if (fault == "company") f.Company.Set(Guid.Parse("00000000-0000-4000-8000-000000000001"));
            if (fault == "cancel") cancellation.Cancel();
            // No authority await may follow the final optional disclosure: only context/token are synchronized here.
            if (fault == "price") f.Authority.PriceGranted = false;
            return Task.CompletedTask;
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContractDecide(f.Service, true, new("1"), cancellation.Token));
        else if (fault is "actor" or "membership" or "company") Assert.Equal(403, (await Refused(() => ContractDecide(f.Service, true, new("1")))).Status);
        else Assert.Equal(SalesPriceDisclosure.Withheld, (await ContractDecide(f.Service, true, new("1"))).Contract.AmountDisclosure);
        Assert.True(fired); Assert.Equal(1, f.Store.Calls.Count(x => x == "decide"));
    }

    private static Task<SalesContractCommandResult> ContractDecide(SalesContractService service, bool approve, SalesDraftStateCommandRequest body, CancellationToken token = default) => approve
        ? service.ApproveContractDiscountAsync(ContractId, body, Metadata, token)
        : service.RejectContractDiscountAsync(ContractId, body, Metadata, token);
    private static (SalesContractService Service, Store Store, ContractProtocolBoundary Boundary, ContractSafetyAuthority Authority, ContractSafetyUser User, Accounts Accounts, ContractSafetyCompany Company) ContractSafetyFixture(string actor = Manager)
    {
        var store = new Store { Stored = Pending() }; var boundary = new ContractProtocolBoundary(store); var authority = new ContractSafetyAuthority(); var user = new ContractSafetyUser(actor); var accounts = new Accounts(); var company = new ContractSafetyCompany();
        var service = new SalesContractService(company, user, authority, new Correlation(), boundary, accounts, new Settings(), new IstanbulNoonClock()).WithContractDiscountDecisionSafety();
        return (service, store, boundary, authority, user, accounts, company);
    }
    private sealed class ContractSafetyCompany : ICompanyContext
    {
        public bool IsResolved => true; public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid company) => CompanyId = company;
    }
    private sealed class ContractSafetyUser(string actor) : ICurrentUser
    {
        internal string Actor = actor; internal bool Member = true;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Member ? new HashSet<Guid>([Company]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid company) => Member && company == Company;
    }
    private sealed class ContractSafetyAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal bool PriceGranted = true; internal int PriceReads; internal Func<string, Task>? AfterRead; internal List<string> Asked = [];
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, company); Asked.Add(permission); if (permission == SalesPermissionManifestV1.OrdersPriceRead) PriceReads++;
            await Task.Yield(); if (AfterRead is not null) await AfterRead(permission);
            return new(permission == SalesPermissionManifestV1.OrdersPriceRead ? PriceGranted : Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    /// <summary>Only an application-call protocol seam forwarding the original fixture/core. It makes no transaction or SQL acceptance claim.</summary>
    private sealed class ContractProtocolBoundary(Store inner) : ISalesContractStore, ISalesContractDiscountDecisionSafetyStoreV1
    {
        internal bool Clean = true; internal int Starts; internal int Executions; internal string? Hash; internal string? Command; internal SalesDraftWriteEnvelope? Envelope;
        public bool ContractDiscountDecisionEntryIsClean => Clean;
        public async Task<SalesContractCommandResult> RunContractDiscountDecisionAsync(SalesDraftWriteEnvelope envelope, string hash, string command, bool clean,
            Func<CancellationToken, Task<SalesContractCommandResult>> execute, Func<CancellationToken, Task<SalesContractCommandResult?>> readOriginal,
            Func<CancellationToken, Task> fence, CancellationToken token)
        { Assert.True(clean); Starts++; Hash = hash; Command = command; Envelope = envelope; await fence(token); Executions++; var result = await execute(token); await fence(token); return result; }
        public Task<SalesContractReplay?> FindContractReplayAsync(SalesDraftWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.FindContractReplayAsync(e,h,c,t);
        public Task<SalesContractStoreResult> StoreContractAsync(SalesDraftWriteEnvelope e, string h, Func<int,DateTimeOffset,SalesContract> build, SalesDraftStateAudit a, CancellationToken t = default) => inner.StoreContractAsync(e,h,build,a,t);
        public Task RecordContractRefusalAsync(SalesDraftWriteEnvelope e, string h, string c, Guid id, SalesDraftTerminalRefusal r, CancellationToken t = default) => inner.RecordContractRefusalAsync(e,h,c,id,r,t);
        public Task<SalesContractStoreResult?> DecideContractAsync(SalesDraftWriteEnvelope e, string h, string c, Guid id, Action<SalesContract,DateTimeOffset> mutate, SalesDraftStateAudit a, CancellationToken t = default) => inner.DecideContractAsync(e,h,c,id,mutate,a,t);
        public Task<SalesContract?> FindContractAsync(Guid id, CancellationToken t = default) => inner.FindContractAsync(id,t);
        public Task<IReadOnlyList<SalesContract>> ListContractsAsync(int limit, CancellationToken t = default) => inner.ListContractsAsync(limit,t);
    }

}
