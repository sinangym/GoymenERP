using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingInventoryCostServiceFenceTests
{
    private static readonly Guid Company = Guid.Parse("a5193000-0000-4000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("a5193000-0000-4000-8000-000000000002");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_create_or_reversal_propose_does_not_authorize_a_component_cancellation_approval(bool reversalReader)
    {
        var fixture = new Fixture(reversalReader);
        var result = await fixture.Service.ApproveCancellationAsync(Guid.NewGuid(), new(3, 1), "SENTETIK-cancellation-key");
        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve, Assert.Single(fixture.Authority.Reads));
    }

    [Theory]
    [InlineData("company", false)]
    [InlineData("actor", false)]
    [InlineData("membership", false)]
    [InlineData("permission", false)]
    [InlineData("company", true)]
    [InlineData("actor", true)]
    [InlineData("membership", true)]
    [InlineData("permission", true)]
    public async Task Options_store_failure_after_context_or_authority_loss_returns_denied_without_plan_details(
        string change, bool reversalReader)
    {
        var fixture = new Fixture(reversalReader);
        fixture.Store.BeforeFailure = () =>
        {
            switch (change)
            {
                case "company": fixture.Company.Set(Guid.NewGuid()); break;
                case "actor": fixture.User.UserId = Guid.NewGuid().ToString("D"); break;
                case "membership": fixture.User.HasMembership = false; break;
                case "permission": fixture.Authority.Granted = false; break;
            }
        };

        var result = await fixture.Service.OptionsAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment);

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Null(result.Code);
        Assert.False(result.IsReplay);
        Assert.Equal(1, fixture.Store.Reads);
        if (change == "permission")
            Assert.Equal(reversalReader ? 3 : 2, fixture.Authority.Reads.Count);
        Assert.Equal(fixture.Authority.Permission, fixture.Authority.Reads[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Options_store_failure_with_fresh_unchanged_authority_preserves_unavailable(bool reversalReader)
    {
        var fixture = new Fixture(reversalReader);

        var result = await fixture.Service.OptionsAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment);

        Assert.Equal(AccountingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal("inventory_cost_account_plan_missing", result.Code);
        Assert.Equal(1, fixture.Store.Reads);
        Assert.Equal(reversalReader ? 3 : 2, fixture.Authority.Reads.Count);
        Assert.Equal(fixture.Authority.Permission, fixture.Authority.Reads[^1]);
    }

    private sealed class Fixture
    {
        public MutableCompany Company { get; } = new();
        public MutableUser User { get; } = new();
        public Authority Authority { get; }
        public FailingOptionsStore Store { get; } = new();
        public AccountingInventoryCostServiceV1 Service { get; }

        public Fixture(bool reversalReader)
        {
            Authority = new(reversalReader ? AccountingJournalReversalPermissionNamesV1.Propose : NormalJournalPermissionNamesV1.DraftCreate);
            var correlation = new CorrelationContext();
            correlation.Set("SENTETIK-inventory-cost-options", null, SourceChannels.Web, null, null);
            Service = new(Store, new UnusedCommands(), Authority, Company, User, correlation);
        }
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public Guid CompanyId { get; private set; } = Company;
        public bool IsResolved => CompanyId != Guid.Empty;
        public void Set(Guid id) => CompanyId = id;
    }

    private sealed class MutableUser : ICurrentUser
    {
        public bool HasMembership = true;
        public string? UserId { get; set; } = Actor.ToString("D");
        public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds => HasMembership ? new HashSet<Guid>([Company]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid id) => HasMembership && id == Company;
    }

    private sealed class Authority(string permission) : IErpAuthorityRevalidationService
    {
        public string Permission { get; } = permission;
        public bool Granted = true;
        public List<string> Reads { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string requested, Guid companyId, CancellationToken token = default)
        {
            Assert.Equal(Company, companyId);
            Reads.Add(requested);
            return Task.FromResult(Granted && requested == Permission
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class FailingOptionsStore : IAccountingInventoryCostStoreV1
    {
        public int Reads;
        public Action? BeforeFailure;

        public async Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            await Task.Yield();
            BeforeFailure?.Invoke();
            throw new AccountingStoreException("SENTETIK-plan-unavailable", "SENTETIK owner store failure.");
        }

        public Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
        public Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id, AccountingInventoryCostKindV1 kind,
            PrepareAccountingInventoryCostV1 request, Guid actor, Func<CancellationToken, Task> fence,
            AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id, AccountingInventoryCostKindV1 kind,
            long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata,
            CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id, ProposeAccountingInventoryCostReversalV1 request,
            Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata,
            CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id, ApproveInventoryCostCancellationV1 request,
            Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata,
            CancellationToken token = default) => throw new NotSupportedException();
    }

    private sealed class UnusedCommands : IAccountingCommandLog
    {
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => throw new NotSupportedException();
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) => throw new NotSupportedException();
    }
}
