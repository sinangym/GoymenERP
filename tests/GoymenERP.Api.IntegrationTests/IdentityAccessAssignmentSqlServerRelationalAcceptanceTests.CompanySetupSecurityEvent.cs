using System.Data;
using System.Net;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    /// <summary>
    /// K3-A P3 / ADR-SEC-001 §4: the first Admin's identity.permission.changed is appended in the grant's own SQL transaction. A
    /// failure after the append (here: right after it, before the commit) leaves neither the assignment nor the security event;
    /// the same key then commits exactly one of each. Before the fix the event was written on its own connection and stayed.
    /// </summary>
    [SqlServerFact]
    public async Task Explicit_first_Admin_security_event_is_in_the_grant_transaction_and_a_later_failure_leaves_neither()
    {
        var fault = new SecurityEventOwnerFault();
        var code = SyntheticCode("SETUP-ADMIN");
        await WithDatabaseAsync(code, async factory =>
        {
            await CreateRoleAsync(factory, code, IdentityPermissionManifestV1.RolesManage);
            var opener = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var opened = await CreatePendingCompanyFixtureAsync(factory, opener);
            await DeliverCompanyOpenedAsync(factory, opened);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            await GrantSetupAuthorityFixtureAsync(factory, opener, CompanySetupGroupOperationV1.AssignFirstAdmin);
            using var client = CreateClient(factory, TestUsers.NoAssignment, opened.CompanyId);
            var key = NewKey(); var request = new AssignFirstCompanyAdminV1(target, 1, "Sentetik açık kullanıcı seçimi");

            using (var failed = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey()))
                Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(1, fault.AppendCalls);
            Assert.Equal((0, 0, 0), await FirstAdminFactsAsync(factory, opened.CompanyId));

            fault.FailAfterAppend = false;
            using (var retried = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey()))
                Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
            using (var replay = await PostAsync(client, "/api/v1/identity/company-setup/first-admin", request, key, NewKey()))
                Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(2, fault.AppendCalls);
            Assert.Equal((1, 1, 1), await FirstAdminFactsAsync(factory, opened.CompanyId));
            await using var check = factory.Services.CreateAsyncScope();
            check.ServiceProvider.GetRequiredService<ICompanyContext>().Set(opened.CompanyId);
            var security = await check.ServiceProvider.GetRequiredService<SecurityEventsDbContext>().SecurityEvents.AsNoTracking()
                .SingleAsync(x => x.EventType == SecurityEventTypesV1.PermissionChanged);
            Assert.Equal((opened.CompanyId, ErpPermissions.IdentityCompanyFirstAdminAssign, SecurityEventOutcomesV1.Allowed),
                (security.CompanyId!.Value, security.PermissionCode, security.Outcome));
        }, services =>
        {
            var registration = services.Single(x => x.ServiceType == typeof(IPermissionChangedSecurityEventWriterV1));
            services.Remove(registration);
            services.AddScoped<IPermissionChangedSecurityEventWriterV1>(provider => new FaultingSecurityEventOwner(
                (IPermissionChangedSecurityEventWriterV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
    }

    /// <summary>(role assignments, first-Admin outbox rows, permission-changed security events) of the company as SQL Server holds them.</summary>
    private static async Task<(int Assignments, int FirstAdminEvents, int SecurityEvents)> FirstAdminFactsAsync(
        SqlServerRelationalApiFactory factory, Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await identity.RoleAssignments.CountAsync(x => x.CompanyId == companyId),
            await identity.OutboxMessages.CountAsync(x => x.CompanyId == companyId && x.EventType == IdentityCompanyFirstAdminAssignedV1.EventType),
            await scope.ServiceProvider.GetRequiredService<SecurityEventsDbContext>().SecurityEvents
                .CountAsync(x => x.EventType == SecurityEventTypesV1.PermissionChanged));
    }

    private sealed class SecurityEventOwnerFault
    {
        public bool FailAfterAppend { get; set; } = true;
        public int AppendCalls { get; set; }
    }

    private sealed class FaultingSecurityEventOwner(IPermissionChangedSecurityEventWriterV1 inner, SecurityEventOwnerFault fault)
        : IPermissionChangedSecurityEventWriterV1
    {
        public async Task AppendAsync(IDbTransaction transaction, SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            await inner.AppendAsync(transaction, securityEvent, cancellationToken);
            fault.AppendCalls++;
            if (fault.FailAfterAppend)
                throw new InvalidOperationException("Synthetic failure after the SecurityEvents owner's append.");
        }
    }
}
