using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL / native owner / audit provenance. Only the existing synthetic current-actor bridge is reused.
    [SqlServerFact]
    public async Task Work_selected_access_request_keeps_original_applied_impact_after_current_role_changes_and_replays_without_a_second_assignment()
    {
        var actor = new PurposeActorSlot();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
            using (client)
            {
                actor.Id = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
                var metadata = PurposeMetadata(actor.Id.Value, NewKey(), NewKey());
                WorkApprovalDecisionSnapshotV1 selected;
                IdentityAccessGrantDecisionReceiptV1 original;
                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(TestCompanyIds.CompanyA);
                    var owner = Assert.IsAssignableFrom<IIdentityAccessGrantDecisionV1>(scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
                    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    var source = SelectedAccessSource(scope.ServiceProvider, owner);
                    var context = new WorkInboxSourceContextV1(metadata.CompanyId, metadata.ActorId, metadata.CorrelationId, metadata.CausationId);
                    var before = await db.CommandExecutions.CountAsync();
                    selected = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, pending.Id, default));
                    var detail = await owner.ReadDecisionAsync(pending.Id,
                        new IdentityAccessGrantDecisionReadContextV1(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), _ => Task.CompletedTask);
                    Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesDecisionSnapshot(selected, detail));
                    Assert.True(selected.Pending); Assert.Equal("1", selected.SourceVersion);
                    Assert.Equal(before, await db.CommandExecutions.CountAsync());
                    Assert.Equal(0, await CountAssignmentsAsync(factory, target, role));
                    Assert.False(db.ChangeTracker.HasChanges());
                    using var json = JsonDocument.Parse(JsonSerializer.Serialize(selected, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                    Assert.False(json.RootElement.TryGetProperty("identityAccessGrantProof", out _));
                    Assert.DoesNotContain(detail!.CurrentImpactDigest, json.RootElement.GetRawText());
                    var accepted = await owner.DecidePurposeAsync(pending.Id, true, "1", null, detail.CurrentImpactDigest, metadata, _ => Task.CompletedTask);
                    Assert.Equal(200, accepted.Answer.StatusCode);
                    original = Assert.IsType<IdentityAccessGrantDecisionReceiptV1>(await owner.ReadDecisionReceiptAsync(pending.Id, true, "1", null, metadata, _ => Task.CompletedTask));
                    Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, original, accepted, true, "1", null, metadata.IdempotencyKey, metadata.ActorId));
                    Assert.Equal(detail.CurrentImpactDigest, original.AppliedImpactDigest);
                    Assert.Equal(detail.CurrentPermissions.Count, original.AppliedPermissionCount);
                }
                await AddSyntheticPurposePermissionAsync(factory, role);
                await using (var restarted = factory.Services.CreateAsyncScope())
                {
                    restarted.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                    var owner = Assert.IsAssignableFrom<IIdentityAccessGrantDecisionV1>(restarted.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
                    var current = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await SelectedAccessSource(restarted.ServiceProvider, owner).ReadDecisionAsync(
                        new(metadata.CompanyId, metadata.ActorId, NewKey(), null), pending.Id, default));
                    Assert.False(current.Pending); Assert.Equal("2", current.SourceVersion);
                    Assert.NotEqual(selected.IdentityAccessGrantProof!.CurrentImpactDigest, current.IdentityAccessGrantProof!.CurrentImpactDigest);
                    var replay = await owner.DecidePurposeAsync(pending.Id, true, "1", null, current.IdentityAccessGrantProof.CurrentImpactDigest,
                        metadata with { CorrelationId = NewKey() }, _ => Task.CompletedTask);
                    Assert.True(replay.IsReplay); Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
                    var receipt = await owner.ReadDecisionReceiptAsync(pending.Id, true, "1", null, metadata, _ => Task.CompletedTask);
                    Assert.Equal(original, receipt);
                    Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(current, receipt, replay, true, "1", null, metadata.IdempotencyKey, metadata.ActorId));
                    Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(current, receipt, replay, true, "1", null, NewKey(), metadata.ActorId));
                    Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(current with { CompanyId = TestCompanyIds.CompanyB }, receipt, replay, true, "1", null, metadata.IdempotencyKey, metadata.ActorId));
                    Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
                    var db = restarted.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    Assert.Single(await db.CommandExecutions.Where(x => x.OriginalCorrelationId == metadata.CorrelationId).ToListAsync());
                }
                actor.Id = null;
            }
        }, services => ConfigurePurposeActor(services, actor));
    }

    [SqlServerFact]
    public async Task Work_access_rejection_binds_the_actual_native_reason_payload_and_never_fills_missing_historical_impact_with_zero()
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
                var owner = Assert.IsAssignableFrom<IIdentityAccessGrantDecisionV1>(scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
                var metadata = PurposeMetadata(actor.Id.Value, NewKey(), NewKey());
                var source = SelectedAccessSource(scope.ServiceProvider, owner);
                var selected = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(new(metadata.CompanyId, metadata.ActorId, metadata.CorrelationId, null), pending.Id, default));
                const string reason = "not_required";
                var answer = await owner.DecidePurposeAsync(pending.Id, false, "1", reason, selected.IdentityAccessGrantProof!.CurrentImpactDigest, metadata, _ => Task.CompletedTask);
                Assert.Equal(200, answer.Answer.StatusCode);
                var receipt = Assert.IsType<IdentityAccessGrantDecisionReceiptV1>(await owner.ReadDecisionReceiptAsync(pending.Id, false, "1", reason, metadata, _ => Task.CompletedTask));
                Assert.Null(receipt.AppliedImpactDigest); Assert.Null(receipt.AppliedPermissionCount);
                Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, receipt, answer, false, "1", reason, metadata.IdempotencyKey, metadata.ActorId));
                Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, receipt, answer, false, "1", "other_reason", metadata.IdempotencyKey, metadata.ActorId));
                Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, receipt with { AppliedPermissionCount = 0 }, answer, false, "1", reason, metadata.IdempotencyKey, metadata.ActorId));
                Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, receipt, answer with { OriginalCorrelationId = NewKey() }, false, "1", reason, metadata.IdempotencyKey, metadata.ActorId));
                Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, receipt with { OriginalAnswerDigest = new('A', 64) }, answer, false, "1", reason, metadata.IdempotencyKey, metadata.ActorId));
                Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesOriginalDecisionReceipt(selected, null, answer, false, "1", reason, metadata.IdempotencyKey, metadata.ActorId));
                Assert.Equal(0, await CountAssignmentsAsync(factory, target, role));
                Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OutboxMessages.CountAsync(x => x.CorrelationId == metadata.CorrelationId));
                actor.Id = null;
            }
        }, services => ConfigurePurposeActor(services, actor));
    }

    private static IdentityAccessGrantRequestWorkInboxSourceV1 SelectedAccessSource(IServiceProvider services, IIdentityAccessGrantDecisionV1 owner) =>
        new(services.GetRequiredService<IdentityDbContext>(), services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(), services.GetRequiredService<IErpAuthorityRevalidationService>(), owner);
}
