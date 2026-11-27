using System.Transactions;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LogisticsCustomerReturnDecisionSafetyTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Nonrelational_entry_refuses_without_callback_or_cleanup_of_clean_or_pending_caller(bool approval, bool pending)
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        await using var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var audit = new EntryAudit { HasPendingDrafts = pending };
        using var provider = new ServiceCollection().AddSingleton<IAuditTransactionWriterV1>(audit).BuildServiceProvider();
        var user = new SafetyUser(); var runner = new LogisticsCustomerReturnTransactionRunner(db, provider, company, user, new SafetyAuthority(), TimeProvider.System);
        var calls = 0;
        var failure = approval
            ? await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => runner.ExecuteApprovalAsync(company.CompanyId,
                (_, _) => Task.FromResult(++calls), (_, _) => { calls++; return Task.CompletedTask; }))
            : await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => runner.ExecuteLogisticsAsync(company.CompanyId,
                _ => Task.FromResult(++calls), _ => { calls++; return Task.CompletedTask; }));
        Assert.Equal(503, failure.Status); Assert.Equal(0, calls); Assert.Equal(0, audit.Discards);
        Assert.Equal(pending, audit.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_audit_writer_or_ambient_entry_never_initializes_the_unconfigured_context(bool ambient)
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        await using var db = new LogisticsDbContext(new DbContextOptions<LogisticsDbContext>(), company);
        var audit = new EntryAudit(); var services = new ServiceCollection();
        if (ambient) services.AddSingleton<IAuditTransactionWriterV1>(audit);
        using var provider = services.BuildServiceProvider();
        using var scope = ambient ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var runner = new LogisticsCustomerReturnTransactionRunner(db, provider, company, new SafetyUser(), new SafetyAuthority(), TimeProvider.System);
        var calls = 0;
        Assert.Equal(503, (await Assert.ThrowsAsync<LogisticsCustomerReturnServiceException>(() => runner.ExecuteLogisticsAsync(company.CompanyId,
            _ => Task.FromResult(++calls), _ => { calls++; return Task.CompletedTask; }))).Status);
        Assert.Equal(0, calls); Assert.Equal(0, audit.Discards);
    }

    private sealed class EntryAudit : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public int Discards;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken token = default) => throw new NotSupportedException();
        public void DiscardPending() => Discards++;
    }
    internal sealed class SafetyUser : ICurrentUser
    {
        public string Actor { get; set; } = "safety-decider";
        public bool IsAuthenticated => true; public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    internal sealed class SafetyAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default) =>
            Task.FromResult(Allowed ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
}
