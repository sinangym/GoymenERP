using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [Fact]
    public void Output_correction_inventory_keys_are_bounded_stable_and_separate_each_owner_fact()
    {
        var company = Guid.NewGuid();
        var order = Guid.NewGuid();
        var reversal = Guid.NewGuid();
        var metadata = new ManufacturingCommandMetadata("SYNTHETIC-maker", ErpPermissions.ManufacturingFactsCorrect,
            ["SYNTHETIC-MANUFACTURING"], "SYNTHETIC-correlation", "SYNTHETIC-causation", "SYNTHETIC-caller-key", "Integration");
        var actual = ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, order, reversal);
        Assert.InRange(actual.IdempotencyKey.Length, 1, 128);
        Assert.Equal(actual, ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, order, reversal));
        // The internal fact key remains stable when an outer caller uses another command key or conversation.
        var repeated = ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(
            metadata with { IdempotencyKey = "SYNTHETIC-other-key", CorrelationId = "SYNTHETIC-other-conversation" },
            company, order, reversal);
        Assert.Equal(actual.IdempotencyKey, repeated.IdempotencyKey);
        Assert.Equal(metadata.ActorId, actual.ActorId);
        Assert.Equal(metadata.PermissionCode, actual.PermissionCode);
        Assert.Equal(metadata.RoleCodes, actual.RoleCodes);
        Assert.Equal(metadata.CorrelationId, actual.CorrelationId);
        Assert.Equal(metadata.CausationId, actual.CausationId);
        Assert.Equal(metadata.SourceChannel, actual.SourceChannel);
        var distinctKeys = new[]
        {
            actual.IdempotencyKey,
            ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, Guid.NewGuid(), order, reversal).IdempotencyKey,
            ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, Guid.NewGuid(), reversal).IdempotencyKey,
            ManufacturingInventoryReturnOutputHost.OutputCorrectionInventoryMetadata(metadata, company, order, Guid.NewGuid()).IdempotencyKey,
        };
        Assert.Equal(4, distinctKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(distinctKeys, key =>
        {
            Assert.InRange(key.Length, 1, 128);
            Assert.StartsWith("mfg-output-correction:", key, StringComparison.Ordinal);
            Assert.False(key.StartsWith("manufacturing-output:", StringComparison.Ordinal));
            Assert.False(key.StartsWith("manufacturing-return:", StringComparison.Ordinal));
        });
    }

    [SqlServerFact]
    public async Task An_output_correction_commits_both_owners_once_and_replays_the_original_fact_and_movement()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-CORRECTION", 100m);
        var authority = new CorrectionHostAuthority();
        await using var inventory = InventoryHostContext(run, CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var dispositions = new InventoryLabelDispositionReadPort(inventory, company);
        await using var scope = ReturnOutputHostScope.Create(run, authority, new ManufacturingOutputAdmission(),
            labelDispositions: dispositions);
        var declaration = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m) with
            { OutputLine = PlanOutputLineReference.ClassLine };
        var output = await scope.Host.DeclareOutputAsync(declaration, ManufacturingHostMetadata(run, "correction-output"));
        var command = new ManufacturingInventoryOutputCorrectionPortCommandV1(seed.OrderId, output.OrderVersion,
            declaration.OutputId, Guid.NewGuid(), declaration.LabelId, output.LabelVersion, Guid.NewGuid(),
            new Kilograms(10m), "SENTETIK output correction", seed.OccurredAtUtc.AddMinutes(5));
        var metadata = ManufacturingHostMetadata(run, "correction-commit") with
            { PermissionCode = ErpPermissions.ManufacturingFactsCorrect };
        var applied = await scope.Host.CorrectOutputAsync(command, metadata);
        var replay = await scope.Host.CorrectOutputAsync(command, metadata with { CorrelationId = "correction-replay" });
        Assert.False(applied.IsReplay); Assert.True(replay.IsReplay);
        Assert.Equal(applied.OrderVersion, replay.OrderVersion);
        Assert.Equal(applied.LabelVersion, replay.LabelVersion);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputReversals WHERE Id = @id", ("@id", command.ReversalId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal("70", (await dispositions.GetAsync(CompanyA, declaration.LabelId)).Disposition!.PhysicalKg);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = N'correction-replay'"));
    }

    [SqlServerFact]
    public async Task An_output_correction_failure_after_inventory_staging_rolls_back_both_facts_audits_and_versions()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await PrepareIssuedOrderAsync(run, "SYN-MFG-CORRECTION-ROLLBACK", 100m);
        var authority = new CorrectionHostAuthority();
        var declaration = OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 80m) with
            { OutputLine = PlanOutputLineReference.ClassLine };
        long orderVersion; long labelVersion;
        await using (var initial = ReturnOutputHostScope.Create(run, authority, new ManufacturingOutputAdmission()))
        {
            var output = await initial.Host.DeclareOutputAsync(declaration, ManufacturingHostMetadata(run, "rollback-output"));
            orderVersion = output.OrderVersion; labelVersion = output.LabelVersion;
        }
        await using var inventory = InventoryHostContext(run, CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var dispositions = new InventoryLabelDispositionReadPort(inventory, company);
        await using var scope = ReturnOutputHostScope.Create(run, authority, new ManufacturingOutputAdmission(),
            (stage, _) => stage == ManufacturingInventoryReturnOutputStage.OutputCorrectionInventoryStaged
                ? Task.FromException(new SyntheticReturnOutputFailure()) : Task.CompletedTask,
            labelDispositions: dispositions);
        var command = new ManufacturingInventoryOutputCorrectionPortCommandV1(seed.OrderId, orderVersion,
            declaration.OutputId, Guid.NewGuid(), declaration.LabelId, labelVersion, Guid.NewGuid(),
            new Kilograms(10m), "SENTETIK rollback correction", seed.OccurredAtUtc.AddMinutes(5));
        var metadata = ManufacturingHostMetadata(run, "correction-rollback") with
            { PermissionCode = ErpPermissions.ManufacturingFactsCorrect };
        await Assert.ThrowsAsync<SyntheticReturnOutputFailure>(() => scope.Host.CorrectOutputAsync(command, metadata));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputReversals WHERE Id = @id", ("@id", command.ReversalId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(orderVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", seed.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation", ("@correlation", metadata.CorrelationId)));
        Assert.Equal(labelVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (await dispositions.GetAsync(CompanyA, declaration.LabelId)).Disposition!.LabelVersion);
    }

    private sealed class CorrectionHostAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Contains(permission, new[] { ErpPermissions.ManufacturingFactsRecord, ErpPermissions.ManufacturingFactsCorrect });
            Assert.Equal(CompanyA, companyId);
            return Task.FromResult(GrantedManufacturingScope());
        }
    }
}
