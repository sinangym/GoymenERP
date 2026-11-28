using GoymenERP.Modules.Sales.Application;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesCustomerReturnServiceTests
{
    [Theory]
    [InlineData(true, "2")] [InlineData(false, "2")]
    [InlineData(true, "02")] [InlineData(false, "02")]
    public async Task Customer_return_decision_safety_requires_a_typed_boundary_before_any_native_store_IO(bool approve, string version)
    {
        var f = Build(Approver); var target = Draft(); target.Submit(1, "SENTETIK-submitter", Now); f.Store.Stored = target;
        f.Service.WithCustomerReturnDecisionSafety(); var body = new SalesDraftStateCommandRequest(version, approve ? null : "  SENTETIK ret  ");
        var failure = await Refused(() => approve ? f.Service.ApproveCustomerReturnAsync(ReturnId, body, Metadata)
            : f.Service.RejectCustomerReturnAsync(ReturnId, body, Metadata));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(f.Store.Calls); Assert.Empty(f.Store.Refusals); Assert.Equal(2L, target.Version);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Guarded_return_native_core_keeps_versions_literal_reason_and_no_invoice_or_finance_effect(bool approve)
    {
        var f = SafetyWorld(); Assert.Same(f.Service, f.Service.WithCustomerReturnDecisionSafety());
        var body = new SalesDraftStateCommandRequest("02", approve ? null : "  SENTETIK ret  ");
        var answer = await DecideSafe(f, body, approve);
        Assert.Equal((approve ? "Approved" : "Rejected", "3", false), (answer.CustomerReturn.State, answer.CustomerReturn.Version, answer.IsReplay));
        Assert.Equal(1, f.Store.DecisionProtocolCalls); Assert.Equal(1, f.Store.Calls.Count(x => x == "decide")); Assert.Equal(0, f.Invoices.Calls);
        var command = approve ? SalesCustomerReturnCommands.ApproveV1 : SalesCustomerReturnCommands.RejectV1;
        Assert.Equal(approve ? SalesCustomerReturnHash.Compute(command, ReturnId, body) : SalesCustomerReturnHash.ComputeDecision(command, ReturnId, body), f.Store.DecisionHash);
        Assert.Equal((Metadata.IdempotencyKey, Metadata.CorrelationId, Approver, SalesPermissionManifestV1.ReturnsApprove),
            (f.Store.DecisionEnvelope!.IdempotencyKey, f.Store.DecisionEnvelope.CorrelationId, f.Store.DecisionEnvelope.ActorId, f.Store.DecisionEnvelope.PermissionCode));
        Assert.Contains(SalesPermissionManifestV1.ReturnsApprove, f.Authority.Asked); Assert.Contains(SalesPermissionManifestV1.OrdersPriceRead, f.Authority.Asked);
    }

    [Theory] [InlineData(SalesPermissionManifestV1.ReturnsApprove)] [InlineData(SalesPermissionManifestV1.OrdersPriceRead)]
    public async Task Both_native_permissions_are_mandatory_before_any_store_decision(string permission)
    {
        var f = SafetyWorld(); f.Authority.Denied.Add(permission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideSafe(f, new("2"), true))).Status);
        Assert.Equal(0, f.Store.DecisionProtocolCalls); Assert.Empty(f.Store.Calls); Assert.Equal(2L, f.Store.Stored!.Version);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Initial_unsafe_state_and_new_state_during_authority_cannot_be_admitted(bool initiallyUnsafe)
    {
        var f = SafetyWorld(); f.Store.DecisionClean = !initiallyUnsafe;
        f.Authority.AfterRead = _ => f.Store.DecisionClean = initiallyUnsafe;
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideSafe(f, new("2"), false))).Status);
        Assert.Equal(initiallyUnsafe, f.Store.DecisionClean); Assert.Empty(f.Store.Calls); Assert.Equal(0, f.Store.DecisionProtocolCalls); Assert.Empty(f.Store.Refusals);
    }

    [Theory] [InlineData("0")] [InlineData("x")] [InlineData("9223372036854775808")]
    public async Task Authorized_version_shape_still_precedes_unsafe_entry(string version)
    {
        var f = SafetyWorld(); f.Store.DecisionClean = false;
        Assert.Equal(400, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideSafe(f, new(version), true))).Status);
        Assert.Empty(f.Store.Calls); Assert.Equal(0, f.Store.DecisionProtocolCalls);
    }

    [Theory] [InlineData(true, true)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(false, false)]
    public async Task Native_maker_and_submitter_refusal_remain_terminal409_for_both_decisions(bool approve, bool preparer)
    {
        var f = SafetyWorld(preparer ? Preparer : "SENTETIK-submitter");
        var e = await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideSafe(f, new("2", approve ? null : "SENTETIK"), approve));
        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval), (e.Status, e.Code)); Assert.Equal("actor", Assert.Single(e.Errors).Field);
        Assert.Single(f.Store.Refusals); Assert.Equal(1, f.Store.Calls.Count(x => x == "decide")); Assert.Equal(2L, f.Store.Stored!.Version);
    }

    [Theory] [InlineData("price")] [InlineData("approve")] [InlineData("actor")] [InlineData("company")] [InlineData("member")] [InlineData("cancel")]
    public async Task Native_final_release_rechecks_price_operation_and_context_or_completed_cancellation(string fault)
    {
        var f = SafetyWorld(); using var cts = new CancellationTokenSource();
        f.Store.AfterDecisionBoundary = () => { if (fault == "price") f.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
            if (fault == "approve") f.Authority.Denied.Add(SalesPermissionManifestV1.ReturnsApprove); if (fault == "actor") f.User.Id = "SENTETIK-other";
            if (fault == "company") f.Company.Id = Guid.NewGuid(); if (fault == "member") f.User.Member = false; if (fault == "cancel") cts.Cancel(); };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecideSafe(f, new("2"), true, cts.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideSafe(f, new("2"), true, cts.Token))).Status);
        Assert.Equal(3L, f.Store.Stored!.Version); Assert.Equal(1, f.Store.Calls.Count(x => x == "decide")); // Synthetic retained mutation; no SQL rollback claim.
    }

    [Fact]
    public async Task Already_cancelled_token_never_reads_authority_or_enters_the_native_boundary()
    {
        var f = SafetyWorld(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecideSafe(f, new("2"), false, cts.Token));
        Assert.Empty(f.Authority.Asked); Assert.Empty(f.Store.Calls); Assert.Equal(0, f.Store.DecisionProtocolCalls);
    }

    private static Task<SalesCustomerReturnCommandResult> DecideSafe(ReturnSafetyWorld f, SalesDraftStateCommandRequest body, bool approve, CancellationToken token = default) => approve
        ? f.Service.ApproveCustomerReturnAsync(ReturnId, body, Metadata, token) : f.Service.RejectCustomerReturnAsync(ReturnId, body, Metadata, token);
    private static ReturnSafetyWorld SafetyWorld(string actor = Approver)
    {
        var store = new Store { DecisionSyntheticProtocol = true, Stored = Draft() }; store.Stored.Submit(1, "SENTETIK-submitter", Now);
        var company = new ReturnDecisionCompany(); var user = new ReturnDecisionUser(actor); var authority = new ReturnDecisionAuthority(); var invoices = InvoiceProxy.Create();
        var service = new SalesCustomerReturnService(company, user, authority, new Correlation(), store, invoices.Service, TimeProvider.System).WithCustomerReturnDecisionSafety();
        return new(service, store, invoices, company, user, authority);
    }
    private sealed record ReturnSafetyWorld(SalesCustomerReturnService Service, Store Store, InvoiceProxy Invoices, ReturnDecisionCompany Company, ReturnDecisionUser User, ReturnDecisionAuthority Authority);
    private sealed class ReturnDecisionCompany : ICompanyContext
    { internal Guid Id = Company; public Guid CompanyId => Id; public bool IsResolved => Id != Guid.Empty; public void Set(Guid id) => Id = id; }
    private sealed class ReturnDecisionUser(string actor) : ICurrentUser
    { internal string Id = actor; internal bool Member = true; public bool IsAuthenticated => true; public string UserId => Id; public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class ReturnDecisionAuthority : IErpAuthorityRevalidationService
    {
        internal readonly HashSet<string> Denied = []; internal readonly List<string> Asked = []; internal Action<string>? AfterRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken token = default)
        { Asked.Add(p); AfterRead?.Invoke(p); return Task.FromResult(new PermissionScope(!Denied.Contains(p), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    // Explicit synthetic protocol only. These tests preserve the native business core; The SQL test sources cover actual ownership/audit/abort; they are not executed here.
    private sealed partial class Store : ISalesCustomerReturnDecisionSafetyStoreV1
    {
        internal bool DecisionSyntheticProtocol, DecisionClean = true; internal int DecisionProtocolCalls; internal string? DecisionHash; internal SalesDraftWriteEnvelope? DecisionEnvelope; internal Action? AfterDecisionBoundary;
        public bool CustomerReturnDecisionEntryIsClean => DecisionSyntheticProtocol && DecisionClean;
        public async Task<SalesCustomerReturnCommandResult> RunCustomerReturnDecisionAsync(SalesDraftWriteEnvelope envelope, string hash, string command, bool clean,
            Func<CancellationToken, Task<SalesCustomerReturnCommandResult>> execute, Func<CancellationToken, Task<SalesCustomerReturnCommandResult?>> original,
            Func<CancellationToken, Task> fence, CancellationToken token)
        {
            if (!clean || !CustomerReturnDecisionEntryIsClean) throw new InvalidOperationException("Synthetic unsafe entry");
            DecisionProtocolCalls++; DecisionHash = hash; DecisionEnvelope = envelope; await fence(token); var result = await execute(token); await fence(token);
            AfterDecisionBoundary?.Invoke(); return result;
        }
    }
}
