using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingPostingAccountMappingLifecycleV1Tests
{
    [Fact]
    public async Task Mapping_workspace_does_not_disclose_accounts_after_read_permission_is_revoked()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        var store = new WorkspaceStore(company.CompanyId);
        var service = new AccountingPostingAccountMappingServiceV1(store, new RecordingLog(), new LosingAuthority(2),
            company, new FixedUser(Checker, company.CompanyId), new FixedCorrelation("SENTETIK-workspace"), new FixedTimeProvider(Now));
        var answer = await service.WorkspaceAsync();
        Assert.Equal(AccountingApiStatus.Denied, answer.Status);
        Assert.Null(answer.Resource);
        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task Mapping_workspace_requires_read_authority_and_rejects_a_foreign_owner_answer()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        var store = new WorkspaceStore(Guid.NewGuid());
        AccountingPostingAccountMappingServiceV1 Service(string[] grants) => new(store, new RecordingLog(), new GrantingAuthority(grants),
            company, new FixedUser(Checker, company.CompanyId), new FixedCorrelation("SENTETIK-workspace"), new FixedTimeProvider(Now));
        Assert.Equal(AccountingApiStatus.Denied, (await Service([]).WorkspaceAsync()).Status);
        Assert.Equal(0, store.Reads);
        var answer = await Service([AccountingPostingAccountMappingPermissionNamesV1.Read]).WorkspaceAsync();
        Assert.Equal(AccountingApiStatus.Denied, answer.Status);
        Assert.Null(answer.Resource);
        Assert.Equal(1, store.Reads);
    }

    private sealed class WorkspaceStore(Guid company) : IAccountingPostingAccountMappingStoreV1,
        IAccountingPostingAccountMappingWorkspaceStoreV1
    {
        public int Reads { get; private set; }
        public Task<AccountingPostingAccountMappingWorkspaceV1> WorkspaceAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(new AccountingPostingAccountMappingWorkspaceV1(company, 0, "Unavailable", null, null,
                [], AccountingCogsMappingDefaultsV1.FromApprovedPlan(null), null, []));
        }
        public Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(long version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(long version, DateOnly date,
            IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows, Guid actor, DateTimeOffset now,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(long version, long expected, long active, Guid actor,
            DateTimeOffset now, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
