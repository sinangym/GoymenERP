using System.Data.Common;
using System.Text.Json;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Package_approval_committed_before_authority_loss_is_retained_while_response_is_suppressed_and_original_key_replays() =>
        PackagePostCommitFenceAsync(approve: true);

    [SqlServerFact]
    public Task Package_rejection_committed_before_authority_loss_is_retained_while_response_is_suppressed_and_original_key_replays() =>
        PackagePostCommitFenceAsync(approve: false);

    private static async Task PackagePostCommitFenceAsync(bool approve)
    {
        // Controlled observations, not actual Identity revocation or a simultaneously scheduled race.
        foreach (var revoked in new[] { "permission", "actor", "membership" })
        {
            await using var run = await Run.StartAsync();
            await PackageSafetyPrepare(run);
            var checker = Guid.NewGuid();
            var saved = new PackageDecisionSaveProbe();
            var committed = new PackagePostCommitRevocation();
            await using var scope = new PackageDecisionScope(run, checker, saved, extra: committed);
            var key = $"postcommit-{approve}-{revoked}";
            PackagePostCommitProof? original = null;
            committed.AfterCommit = async () =>
            {
                // Read through an independent native fixture scope AFTER the actual SQL commit. These rows must already
                // exist when the release fence loses authority: refusal cannot claim that this decision was rolled back.
                await PackageSafetyDurable(run, decisions: 1, attempts: 1, audits: 1);
                original = await PackagePostCommitReadAsync(run, approve, checker, key);
                if (revoked == "actor") scope.User.Actor = Guid.NewGuid().ToString("D");
                else if (revoked == "membership") scope.User.Access = false;
                else scope.Authority.Granted = false;
            };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Decide(approve, key));
            Assert.Equal(1, committed.Commits);
            Assert.Equal(1, committed.Revocations);
            Assert.Equal(1, saved.Attempts);
            Assert.NotNull(original);
            scope.AssertClean();
            await PackageSafetyDurable(run, 1, 1, 1);
            Assert.Equal(original, await PackagePostCommitReadAsync(run, approve, checker, key));

            scope.Reset();
            // Reuse the same guarded native composition and the actual fixture database/writer. Only the retry's
            // correlation differs, proving that the immutable receipt supplies the original one rather than the caller.
            var service = new GuardedProductPackageStandardService(new ProductPackageStandardService(scope.Database,
                scope.ServiceCompany, scope.User, scope.Authority, scope.Collector, new FixedClock(PackageClockNow)), scope.Writer);
            var metadata = new ProductPackageStandardCommandMetadataV2(TestCompanyIds.CompanyA, checker.ToString("D"),
                "SENTETIK-new-retry-correlation", null, "Web", null, null, key);
            var replay = approve
                ? await service.ApproveAsync(1, new("2026-11-16", "SENTETIK native reason"), metadata)
                : await service.RejectAsync(1, new("SENTETIK native reason"), metadata);
            Assert.True(replay.IsReplay);
            Assert.Equal(204, replay.Answer.StatusCode);
            Assert.Equal("corr-" + key, replay.OriginalCorrelationId);
            Assert.NotEqual(metadata.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Deserialize<ProductPackageStandardAnswerV2>(original.AttemptAnswerJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), replay.Answer);
            Assert.Equal(2, committed.Commits);
            Assert.Equal(1, committed.Revocations);
            Assert.Equal(1, saved.Attempts);
            scope.AssertClean();
            await PackageSafetyDurable(run, 1, 1, 1);
            Assert.Equal(original, await PackagePostCommitReadAsync(run, approve, checker, key));
        }
    }

    private sealed record PackagePostCommitProof(string DecisionJson, string AttemptJson, string AttemptAnswerJson,
        Guid AuditId, string AuditIntegrityHash);

    private static async Task<PackagePostCommitProof> PackagePostCommitReadAsync(Run run, bool approve, Guid actor, string key)
    {
        await using var reader = run.Request();
        var decision = Assert.Single(await reader.Context.PackageStandardDecisions.AsNoTracking().ToArrayAsync());
        var command = approve ? ProductPackageStandardService.ApproveCommandName : ProductPackageStandardService.RejectCommandName;
        var attempt = Assert.Single(await reader.Context.PackageStandardAttempts.AsNoTracking()
            .Where(row => row.CommandName == command).ToArrayAsync());
        var eventType = approve ? "product.package-standards.version-approved.v3" : "product.package-standards.version-rejected.v3";
        var audit = Assert.Single(await reader.Services.GetRequiredService<AuditDbContext>().AuditEvents.AsNoTracking()
            .Where(row => row.EventType == eventType).ToArrayAsync());
        var actorId = actor.ToString("D");
        var correlation = "corr-" + key;
        Assert.Equal(TestCompanyIds.CompanyA, decision.CompanyId);
        Assert.Equal(1, decision.Version);
        Assert.Equal(approve ? ProductPackageStandardDecisionsV3.Approved : ProductPackageStandardDecisionsV3.Rejected, decision.Decision);
        Assert.Equal(approve ? new DateOnly(2026, 11, 16) : (DateOnly?)null, decision.EffectiveFrom);
        Assert.Equal("SENTETIK native reason", decision.Reason);
        Assert.Equal(actorId, decision.DecidedBy);
        Assert.Equal(correlation, decision.CorrelationId);
        Assert.NotEqual(Guid.Empty, decision.DecisionId);
        Assert.Equal(TestCompanyIds.CompanyA, attempt.CompanyId);
        Assert.Equal(204, attempt.StatusCode);
        Assert.Equal(actorId, attempt.ActorId);
        Assert.Equal(correlation, attempt.CorrelationId);
        Assert.Equal(TestCompanyIds.CompanyA, audit.CompanyId);
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal(correlation, audit.CorrelationId);
        Assert.Equal(attempt.IdempotencyKeyHash, audit.IdempotencyKeyHash);
        Assert.Equal(ProductPackageStandardPermissionsV3.Approve, audit.PermissionCode);
        return new(JsonSerializer.Serialize(decision), JsonSerializer.Serialize(attempt), attempt.AnswerJson, audit.Id, audit.IntegrityHash);
    }

    private sealed class PackagePostCommitRevocation : DbTransactionInterceptor
    {
        public int Commits, Revocations;
        public Func<Task>? AfterCommit;

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Assert.IsType<ProductPriceListDbContext>(eventData.Context);
            Commits++;
            if (AfterCommit is not { } revoke) return;
            AfterCommit = null; // One controlled revocation; the subsequent native replay can commit its read-only boundary.
            Revocations++;
            await revoke();
        }
    }
}
