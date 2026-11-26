using GoymenERP.Api.WorkRuntime;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Host composition and fresh owner fence; synthetic owners do not prove SQL acceptance.</summary>
public sealed class WorkDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("d8210000-0000-4000-8000-000000000001");
    private static readonly Guid ItemId = Guid.Parse("d8210000-0000-4000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("d8210000-0000-4000-8000-000000000003");
    [Theory]
    [InlineData("finance.cash-account.v1", true)]
    [InlineData("finance.cash-account.v1", false)]
    [InlineData("finance.cash-account-passivation.v1", true)]
    [InlineData("finance.cash-account-passivation.v1", false)]
    [InlineData("finance.cash-account-reactivation.v1", true)]
    [InlineData("finance.cash-account-reactivation.v1", false)]
    [InlineData("inventory.opening-cost.v1", true)]
    [InlineData("inventory.opening-cost.v1", false)]
    public async Task The_four_screenless_kinds_call_the_exact_owner_command_with_the_same_version_key_and_correlation(string kind, bool approve)
    {
        var run = new Run(kind);
        var version = (await run.Host.PreviewAsync(kind, ItemId, default)).SourceVersion;
        var result = await run.Host.DecideAsync(kind, ItemId, new(version, approve, kind == WorkDecisionHostV1.OpeningCost || !approve ? "SENTETIK" : null), "same-key", default);
        Assert.Equal(Company, result.CompanyId); Assert.Equal(ItemId, result.ItemId);
        Assert.Equal("SENTETIK-original", result.OriginalCorrelationId); Assert.True(result.Replayed);
        if (kind == WorkDecisionHostV1.OpeningCost)
        { Assert.Equal(approve ? InventoryOpeningCostDecisionKindV1.Approve : InventoryOpeningCostDecisionKindV1.Reject, run.Owner.InventoryDecision); }
        else
        { Assert.Equal((kind, approve, "7"), run.Owner.Command);
          Assert.Equal(kind == "finance.cash-account-passivation.v1" ? CashAccountStateChange.Passivation
              : kind == "finance.cash-account-reactivation.v1" ? CashAccountStateChange.Reactivation : (CashAccountStateChange?)null, run.Owner.Change); }
        Assert.Equal("same-key", run.Owner.Key); Assert.Equal("SENTETIK-correlation", run.Owner.Correlation);
    }
    [Fact]
    public async Task A_delegation_that_expires_inside_the_owner_command_is_denied_at_the_owners_next_fresh_fence()
    {
        var run = new Run("finance.cash-account.v1");
        run.Owner.BeforeLastFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, ItemId,
            new("7", true, null, Guid.NewGuid()), "late", default))).Status);
        Assert.False(run.Owner.Written);
        Assert.True(run.Delegations.Calls >= 2);
        // Re-entrant Identity permission checks inside the delegation reader do not bypass the underlying authority or recurse forever.
        Assert.True(run.Raw.Calls >= run.Delegations.Calls);
    }
    [Fact]
    public async Task Maker_foreign_company_missing_authority_and_unknown_kinds_never_reach_an_owner_command()
    {
        var run = new Run("finance.cash-account.v1");
        run.Source.Maker = Actor.ToString("D");
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, ItemId, default))).Status);
        run.Source.Maker = "SENTETIK-maker"; run.Source.CompanyId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, ItemId, default))).Status);
        run.Source.CompanyId = Company; run.Raw.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, ItemId, default))).Status);
        run.Raw.Allowed = true;
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync("payments.execute.v1", ItemId, default))).Status);
        Assert.Null(run.Owner.Key); Assert.False(run.Owner.Written);
    }
    [Fact]
    public async Task A_completed_owner_record_can_still_reach_the_original_receipt_and_the_scope_cannot_leak_to_the_next_command()
    {
        var run = new Run("finance.cash-account.v1"); run.Source.Pending = false;
        var result = await run.Host.DecideAsync(run.Source.Kind, ItemId, new("7", true, null, Guid.NewGuid()), "original-key", default);
        Assert.True(result.Replayed); var checkedBefore = run.Delegations.Calls;
        run.Delegations.Allowed = false;
        Assert.True((await run.Authority.RevalidatePermissionScopeAsync(run.Source.ApproverPermission, Company)).IsGranted);
        Assert.Equal(checkedBefore, run.Delegations.Calls);
    }
    [Fact]
    public async Task Registration_preserves_the_scoped_owner_authority_factory_and_removes_the_extra_fence_after_disposal()
    {
        var raw = new RawAuthority(); var services = new ServiceCollection(); var factories = 0;
        services.AddScoped<IErpAuthorityRevalidationService>(_ => { factories++; return raw; });
        services.AddWorkDecisionHostV1();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var authority = scope.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>();
        var fence = scope.ServiceProvider.GetRequiredService<WorkDecisionExecutionFenceV1>();
        Assert.True((await authority.RevalidatePermissionScopeAsync(ErpPermissions.FinanceCashAccountApprove, Company)).IsGranted);
        using (fence.Enter(_ => Task.FromResult(false)))
            Assert.False((await authority.RevalidatePermissionScopeAsync(ErpPermissions.FinanceCashAccountApprove, Company)).IsGranted);
        Assert.True((await authority.RevalidatePermissionScopeAsync(ErpPermissions.FinanceCashAccountApprove, Company)).IsGranted);
        Assert.Equal(1, factories); Assert.Equal(3, raw.Calls);
    }

    private sealed class Run
    {
        public readonly RawAuthority Raw = new(); public readonly Delegations Delegations = new(); public readonly Source Source;
        public readonly Owner Owner; public readonly WorkDecisionAuthorityV1 Authority; public readonly WorkDecisionHostV1 Host;
        public Run(string kind)
        {
            var company = new CompanyContext(); company.Set(Company);
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-correlation", null, "api", null, null);
            var fence = new WorkDecisionExecutionFenceV1(); Authority = new(Raw, fence); Delegations.Authority = Authority;
            Source = new(kind); Owner = new(Authority, Source);
            Host = new(company, new User(), correlation, Authority, [Source], Delegations, fence, Owner, Owner);
        }
    }
    private sealed class RawAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true; public int Calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Allowed ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Allowed = true; public int Calls; public IErpAuthorityRevalidationService Authority = null!;
        public async Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default)
        {
            Calls++; await Authority.RevalidatePermissionScopeAsync(ErpPermissions.FinanceCashAccountApprove, Company, cancellationToken);
            return Allowed ? new(id, Company, item.Kind, ErpPermissions.FinanceCashAccountApprove, Guid.NewGuid(), Actor,
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Actor, DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null;
        }
    }
    private sealed class Source(string kind) : IWorkApprovalDecisionSourceV1
    {
        public Guid CompanyId = Company; public string Maker = "SENTETIK-maker"; public bool Pending = true;
        public string Module => kind.StartsWith("finance", StringComparison.Ordinal) ? "finance" : "inventory";
        public string Kind => kind;
        public string ApproverPermission => kind switch {
            WorkDecisionHostV1.OpeningCost => ErpPermissions.InventoryOpeningCostApprove,
            "finance.cash-account-passivation.v1" => ErpPermissions.FinanceCashAccountPassivationApprove,
            "finance.cash-account-reactivation.v1" => ErpPermissions.FinanceCashAccountReactivationApprove,
            _ => ErpPermissions.FinanceCashAccountApprove };
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(CompanyId, new(Module, Kind, itemId, "SENTETIK source", DateTimeOffset.UtcNow,
                "/onaylarim", Maker, [], []), kind == WorkDecisionHostV1.OpeningCost ? "submission/" + itemId.ToString("D") : "7", Pending));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Owner(IErpAuthorityRevalidationService authority, Source source) : IFinanceCashAccountMaster, IInventoryOpeningCostCommandV1
    {
        public (string, bool, string)? Command; public string? Key; public string? Correlation; public bool Written; public Action? BeforeLastFence;
        public InventoryOpeningCostDecisionKindV1 InventoryDecision; public CashAccountStateChange? Change;
        private async Task<CashAccountCommandResult> Decide(FinanceContext context, CommandMetadata metadata, string version, bool approve)
        {
            Key = metadata.IdempotencyKey; Correlation = context.CorrelationId; Command = (source.Kind, approve, version);
            BeforeLastFence?.Invoke();
            if (!(await authority.RevalidatePermissionScopeAsync(source.ApproverPermission, context.CompanyId)).IsGranted)
                return new(CommandOutcome.Denied, null, false, context.CorrelationId, []);
            Written = true;
            return new(CommandOutcome.Succeeded, new(ItemId, Company, Guid.NewGuid(), "SENTETIK cash", CashAccountStatus.Approved, true, null, null, "8"), true, "SENTETIK-original", []);
        }
        public Task<CashAccountCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, CashAccountApproveRequest r, CancellationToken t) => Decide(c,m,r.ExpectedVersion,true);
        public Task<CashAccountCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, CashAccountRejectRequest r, CancellationToken t) => Decide(c,m,r.ExpectedVersion,false);
        public Task<CashAccountCommandResult> ApproveStateChangeAsync(FinanceContext c, CommandMetadata m, CashAccountStateChange k, CashAccountStateChangeRequest r, CancellationToken t) { Change = k; return Decide(c,m,r.ExpectedVersion,true); }
        public Task<CashAccountCommandResult> RejectStateChangeAsync(FinanceContext c, CommandMetadata m, CashAccountStateChange k, CashAccountStateChangeRejectRequest r, CancellationToken t) { Change = k; return Decide(c,m,r.ExpectedVersion,false); }
        public Task<CashAccountCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, CashAccountProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> ProposeStateChangeAsync(FinanceContext c, CommandMetadata m, CashAccountStateChange k, CashAccountStateChangeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashAccountView>> GetAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<InventoryOpeningCostResultV1> SubmitAsync(SubmitInventoryOpeningCostV1 r, InventoryCommandMetadata m, CancellationToken t = default) => throw new NotSupportedException();
        public Task<InventoryOpeningCostResultV1> DecideAsync(Guid id, DecideInventoryOpeningCostV1 r, InventoryCommandMetadata m, CancellationToken t = default)
        { InventoryDecision = r.Decision; Key = m.IdempotencyKey; Correlation = m.CorrelationId;
          return Task.FromResult(new InventoryOpeningCostResultV1(new(id, Company, Guid.NewGuid(), Guid.NewGuid(), "12.3", "SENTETIK", DateTimeOffset.UtcNow,
              r.Decision == InventoryOpeningCostDecisionKindV1.Approve ? "Approved" : "Rejected", null), true, "SENTETIK-original")); }
    }
}
