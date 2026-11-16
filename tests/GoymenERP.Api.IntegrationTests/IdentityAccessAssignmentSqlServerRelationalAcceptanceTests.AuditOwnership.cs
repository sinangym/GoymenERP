using System.Data;
using System.Net;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task An_Audit_owner_failure_rolls_back_the_assignment_outbox_answer_and_audit_then_the_same_key_can_succeed()
    {
        var fault = new AuditOwnerFault();
        var adminRoleCode = SyntheticCode("ADMIN");
        await WithDatabaseAsync(adminRoleCode, async factory =>
        {
            var managerRole = await CreateRoleAsync(factory, adminRoleCode, IdentityPermissionManifestV1.RolesManage);
            var manager = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyA);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            var operatorRole = await CreateRoleAsync(factory, SyntheticCode("OPERATOR"),
                ErpPermissions.OrganizationLocationsStatusUpdate);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var request = new AssignIdentityRoleRequestV1(target, operatorRole, null, null, "Sentetik atomiklik denemesi");
            var key = NewKey();
            var correlation = NewKey();

            using (var response = await PostAsync(client, AssignmentsRoute, request, key, correlation))
                Assert.InRange((int)response.StatusCode, 500, 599);
            Assert.Equal(1, fault.AppendCalls);
            Assert.Equal(0, await CountAssignmentsAsync(factory, target, operatorRole));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Null(identity.Model.FindEntityType("GoymenERP.Modules.Audit.Domain.Events.AuditEvent"));
                Assert.Equal(0, await identity.OutboxMessages.CountAsync(row => row.CorrelationId == correlation));
                Assert.Equal(0, await identity.CommandExecutions.CountAsync(row => row.OriginalCorrelationId == correlation));
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                    .CountAsync(row => row.CorrelationId == correlation));
            }

            fault.FailAfterAppend = false;
            Outcome assigned;
            using (var retry = await PostAsync(client, AssignmentsRoute, request, key, correlation))
            {
                Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
                assigned = await ReadOutcomeAsync(retry);
            }
            var effects = await ReadAssignmentEffectsAsync(factory, assigned.Id);
            Assert.Equal(new AssignmentEffects(1, 1, 1, 0, 1, 0), effects);
            Assert.Equal(2, fault.AppendCalls);
            using (var replay = await PostAsync(client, AssignmentsRoute, request, key, NewKey()))
            {
                Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
                Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ReplayedHeader)));
            }
            Assert.Equal(2, fault.AppendCalls);
            Assert.Equal(effects, await ReadAssignmentEffectsAsync(factory, assigned.Id));
        }, services =>
        {
            var registration = services.Single(value => value.ServiceType == typeof(IAuditTransactionWriterV1));
            services.Remove(registration);
            services.AddScoped<IAuditTransactionWriterV1>(provider => new FaultingAuditOwner(
                (IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
    }

    [SqlServerFact]
    public async Task The_transactional_Audit_owner_rejects_a_foreign_company_batch_and_discards_its_pending_drafts()
    {
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await using var transaction = await identity.Database.BeginTransactionAsync();
            var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var foreignCorrelation = NewKey();
            collector.Enqueue(AuditOwnerProbe(TestCompanyIds.CompanyB, foreignCorrelation));
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WritePendingAsync(transaction.GetDbTransaction()));
            // The rejected batch cannot contaminate the next batch in the same request scope.
            var ownCorrelation = NewKey();
            collector.Enqueue(AuditOwnerProbe(TestCompanyIds.CompanyA, ownCorrelation));
            await writer.WritePendingAsync(transaction.GetDbTransaction());
            await transaction.CommitAsync();

            var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            Assert.Equal(1, await audit.AuditEvents.CountAsync(row => row.CorrelationId == ownCorrelation));
            Assert.Equal(0, await audit.AuditEvents.CountAsync(row => row.CorrelationId == foreignCorrelation));
            await using var foreignScope = factory.Services.CreateAsyncScope();
            foreignScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyB);
            Assert.Equal(0, await foreignScope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                .CountAsync(row => row.CorrelationId == foreignCorrelation || row.CorrelationId == ownCorrelation));
        });
    }

    private static AuditEventDraft AuditOwnerProbe(Guid company, string correlation) => new(
        company, "identity.audit-owner.probe.v1", "IdentityAuditOwnerProbe", Guid.NewGuid().ToString("D"), "Probe",
        null, null, AuditActorTypes.System, "synthetic-audit-owner-probe", IdentityPermissionManifestV1.RolesManage,
        [], "{}", "{}", "Sentetik şirket izolasyonu", null, AuditAiInvolvement.None, null,
        "System", null, null, correlation, null, NewKey());

    private sealed class AuditOwnerFault
    {
        public bool FailAfterAppend { get; set; } = true;
        public int AppendCalls { get; set; }
    }

    private sealed class FaultingAuditOwner(IAuditTransactionWriterV1 inner, AuditOwnerFault fault) : IAuditTransactionWriterV1
    {
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        {
            await inner.WritePendingAsync(transaction, cancellationToken);
            fault.AppendCalls++;
            if (fault.FailAfterAppend)
                throw new InvalidOperationException("Synthetic failure after the Audit owner's append.");
        }

        public void DiscardPending() => inner.DiscardPending();
    }
}
