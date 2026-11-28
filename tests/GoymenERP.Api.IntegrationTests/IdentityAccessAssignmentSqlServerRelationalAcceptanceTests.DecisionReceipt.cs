using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    // SENTETIK actor bridge only: native authority still queries the real synthetic assignments in this SQL fixture.
    // No endpoint, production grant or production current-user registration is added.
    [SqlServerFact]
    public async Task Purpose_impact_is_compared_inside_native_transaction_and_original_audit_proof_survives_later_role_change()
    {
        var actor = new PurposeActorSlot();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
            using (client)
            {
                actor.Id = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
                await using var scope = factory.Services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var service = Assert.IsAssignableFrom<IIdentityAccessGrantDecisionV1>(scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var metadata = PurposeMetadata(actor.Id.Value, NewKey(), NewKey());
                var preview = await service.ReadDecisionAsync(pending.Id, metadata, _ => Task.CompletedTask);
                Assert.NotNull(preview);
                await AddSyntheticPurposePermissionAsync(factory, role);
                var stale = await service.DecidePurposeAsync(pending.Id, true, "1", null, preview.CurrentImpactDigest, metadata, _ => Task.CompletedTask);
                Assert.Equal(409, stale.Answer.StatusCode);
                Assert.Equal(0, await CountAssignmentsAsync(factory, target, role));
                Assert.Empty(await db.OutboxMessages.AsNoTracking().Where(x => x.CorrelationId == metadata.CorrelationId).ToListAsync());
                metadata = PurposeMetadata(actor.Id.Value, NewKey(), NewKey());
                var current = await service.ReadDecisionAsync(pending.Id, metadata, _ => Task.CompletedTask);
                Assert.NotNull(current); Assert.NotEqual(preview.CurrentImpactDigest, current.CurrentImpactDigest);
                var accepted = await service.DecidePurposeAsync(pending.Id, true, "1", null, current.CurrentImpactDigest, metadata, _ => Task.CompletedTask);
                Assert.Equal(200, accepted.Answer.StatusCode);
                var receipt = await service.ReadDecisionReceiptAsync(pending.Id, true, "1", null, metadata, _ => Task.CompletedTask);
                Assert.NotNull(receipt); Assert.Equal(current.CurrentImpactDigest, receipt.AppliedImpactDigest);
                Assert.Equal(current.CurrentPermissions.Count, receipt.AppliedPermissionCount);
                Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
                Assert.Equal(current.CurrentPermissions.Count, await db.OutboxMessages.CountAsync(x => x.CorrelationId == metadata.CorrelationId));
                var storedAnswer = await db.CommandExecutions.AsNoTracking().SingleAsync(x => x.OriginalCorrelationId == metadata.CorrelationId);
                Assert.DoesNotContain("appliedPermission", storedAnswer.ResponseJson, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("impactDigest", storedAnswer.ResponseJson, StringComparison.OrdinalIgnoreCase);
                var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
                Assert.Equal(2, await audit.AuditEvents.CountAsync(x => x.CorrelationId == metadata.CorrelationId));
                await AddSyntheticPurposePermissionAsync(factory, role, IdentityPermissionManifestV1.AssignmentsRead);
                var replay = await service.DecidePurposeAsync(pending.Id, true, "1", null, preview.CurrentImpactDigest,
                    metadata with { CorrelationId = NewKey() }, _ => Task.CompletedTask);
                Assert.True(replay.IsReplay); Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
                var original = await service.ReadDecisionReceiptAsync(pending.Id, true, "1", null, metadata, _ => Task.CompletedTask);
                Assert.NotNull(original); Assert.Equal(receipt, original);
                Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
                Assert.Equal(2, await audit.AuditEvents.CountAsync(x => x.CorrelationId == metadata.CorrelationId));
                actor.Id = null;
            }
        }, services => ConfigurePurposeActor(services, actor));
    }

    [SqlServerFact]
    public async Task Legacy_native_decision_keeps_original_answer_but_cannot_invent_historical_impact_and_cancelled_receipt_read_releases_nothing()
    {
        var actor = new PurposeActorSlot();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
            using (client)
            {
                var key = NewKey(); var correlation = NewKey();
                using var response = await GrantDecisionAsync(client, pending.Id, true, key, correlation);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                actor.Id = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
                await using var scope = factory.Services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var service = Assert.IsAssignableFrom<IIdentityAccessGrantDecisionV1>(scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
                var metadata = PurposeMetadata(actor.Id.Value, key, correlation);
                var receipt = await service.ReadDecisionReceiptAsync(pending.Id, true, "1", null, metadata, _ => Task.CompletedTask);
                Assert.NotNull(receipt); Assert.Null(receipt.AppliedImpactDigest); Assert.Null(receipt.AppliedPermissionCount);
                using var cancellation = new CancellationTokenSource(); var callbacks = 0;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionReceiptAsync(pending.Id, true, "1", null, metadata, _ =>
                { if (++callbacks == 2) cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
                Assert.Equal(2, callbacks); Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Single(await db.CommandExecutions.Where(x => x.OriginalCorrelationId == correlation).ToListAsync());
                actor.Id = null;
            }
        }, services => ConfigurePurposeActor(services, actor));
    }

    private static IdentityAccessCommandMetadataV1 PurposeMetadata(Guid actor, string key, string correlation) =>
        new(TestCompanyIds.CompanyA, actor.ToString("D"), correlation, null, "Web", null, null, key);

    private static async Task AddSyntheticPurposePermissionAsync(SqlServerRelationalApiFactory factory, Guid role,
        string permission = ErpPermissions.OrganizationLocationsStatusUpdate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.Roles.Include(x => x.Permissions).SingleAsync(x => x.Id == role);
        row.AddPermission(permission);
        await db.SaveChangesAsync();
    }

    private sealed class PurposeActorSlot { internal Guid? Id; }
    private sealed class PurposeCurrentUser(ICurrentUser fallback, PurposeActorSlot slot) : ICurrentUser
    {
        public bool IsAuthenticated => slot.Id is not null || fallback.IsAuthenticated;
        public string? UserId => slot.Id?.ToString("D") ?? fallback.UserId;
        public IReadOnlySet<Guid> AllowedCompanyIds => slot.Id is null ? fallback.AllowedCompanyIds : new HashSet<Guid> { TestCompanyIds.CompanyA };
        public bool CanAccessCompany(Guid company) => slot.Id is null ? fallback.CanAccessCompany(company) : company == TestCompanyIds.CompanyA;
    }
    private static void ConfigurePurposeActor(IServiceCollection services, PurposeActorSlot actor)
    {
        var descriptor = services.Last(x => x.ServiceType == typeof(ICurrentUser));
        services.Remove(descriptor);
        services.AddScoped<ICurrentUser>(provider => new PurposeCurrentUser((ICurrentUser)(descriptor.ImplementationFactory?.Invoke(provider)
            ?? descriptor.ImplementationInstance ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), actor));
    }
}
