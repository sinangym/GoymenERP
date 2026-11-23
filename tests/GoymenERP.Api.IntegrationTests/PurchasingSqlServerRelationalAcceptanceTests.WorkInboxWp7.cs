using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP7 (AI-D31/AI-D32): the service order Work inbox source on real SQL Server — the parent/current-revision join reads only the
/// caller company's current Draft revisions (an approved order's earlier Draft revision is not pending). SENTETIK data; the
/// caller and the authority are doubles.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Service_order_work_inbox_reads_only_current_draft_revisions_of_the_caller_company_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var pending = ServiceDraft(run);
        var approved = ServiceDraft(run);
        await using (var scope = ServiceScope(run, CompanyA))
        {
            foreach (var draft in new[] { pending, approved })
            {
                await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("wp7-create-" + draft.OrderId),
                    Hash("wp7-create-" + draft.OrderId), "SENTETIK hizmet", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
            }

            await scope.Store.WriteAsync(approved.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("wp7-approve"), Hash("wp7-approve"),
                "SENTETIK onay", (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, "SENTETIK-checker", old!.CreatedAtUtc)),
                _ => Task.CompletedTask, default);
        }

        await using (var reader = ServiceScope(run, CompanyA))
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var fence = new PurchasingWorkInboxFence(company, new Wp7User("SENTETIK-inbox"), new Wp7Authority());
            var source = new PurchaseServiceOrderWorkInboxSourceV1(reader.Database, fence);

            var result = await source.ListPendingAsync(new WorkInboxSourceContextV1(CompanyA, "SENTETIK-inbox", "SENTETIK-corr", null), 10, default);

            Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
            var item = Assert.Single(result.Items!);
            Assert.Equal(pending.OrderId, item.ItemId);
            Assert.Equal(run.Actor, item.PreparedBy);
            Assert.False(string.IsNullOrWhiteSpace(item.DocumentLabel));
            Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
        }

        await using (var foreign = ServiceScope(run, CompanyB))
        {
            var company = new CompanyContext();
            company.Set(CompanyB);
            var source = new PurchaseServiceOrderWorkInboxSourceV1(foreign.Database,
                new PurchasingWorkInboxFence(company, new Wp7User("SENTETIK-inbox"), new Wp7Authority()));
            var result = await source.ListPendingAsync(new WorkInboxSourceContextV1(CompanyB, "SENTETIK-inbox", "SENTETIK-corr", null), 10, default);
            Assert.Empty(result.Items!);
        }
    }

    private sealed class Wp7User(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Wp7Authority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(permission == PurchasingPermissionManifestV1.OrdersApprove, true,
                new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
