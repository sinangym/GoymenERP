using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

// Actual Settings + Audit + shared command-attempt SQL. Warehouse/capability/authority faults below are explicit
// synthetic owner-boundary faults, not Organization HTTP authorization or GİB integration evidence.
public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Warehouse_scale_Down_guard_passes_empty_and_refuses_with_51990_while_any_explicit_choice_exists()
    {
        await using var run = await HostRun.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddWarehouseTransferScalePolicies().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await using var port = run.Numbers(CompanyA);
        var location = Guid.NewGuid();
        Assert.Equal(204, (await ScaleOwner(port, CompanyA, new(location)).RecordAsync(location,
            new("0", false, "SENTETIK guard"), NumberMetadata("scale-guard"))).Answer.StatusCode);
        Assert.Equal(51990, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM settings.WarehouseTransferScaleVersions"));
    }
    [SqlServerFact]
    public async Task Warehouse_scale_virgin_default_off_is_distinct_from_explicit_off_and_original_replay_survives_source_outage()
    {
        await using var run = await HostRun.StartAsync();
        await using var port = run.Numbers(CompanyA);
        var location = Guid.NewGuid(); var source = new ScaleWarehouseSource(location);
        var owner = ScaleOwner(port, CompanyA, source);
        var virgin = await owner.GetAsync(CompanyA, location);
        Assert.Equal((SettingsWarehouseTransferScaleStatusV1.NotConfigured, (bool?)false, (long?)null),
            (virgin.Status, virgin.RequireExitWeighing, virgin.RecordedVersion));
        var request = new RecordWarehouseTransferScaleV1("0", false, "SENTETIK açık kapalı seçimi");
        var metadata = NumberMetadata("scale-first-off");
        Assert.Equal(204, (await owner.RecordAsync(location, request, metadata)).Answer.StatusCode);
        var explicitOff = await owner.GetAsync(CompanyA, location);
        Assert.Equal((SettingsWarehouseTransferScaleStatusV1.Configured, false, 1L),
            (explicitOff.Status, explicitOff.RequireExitWeighing, explicitOff.RecordedVersion));
        source.Unavailable = true;
        var failedRead = await owner.GetAsync(CompanyA, location);
        Assert.Equal(SettingsWarehouseTransferScaleStatusV1.Unavailable, failedRead.Status);
        Assert.Null(failedRead.RequireExitWeighing); Assert.Null(failedRead.RecordedVersion);
        var replay = await owner.RecordAsync(location, request, metadata with { CorrelationId = "SENTETIK retry" });
        Assert.True(replay.IsReplay); Assert.Equal(metadata.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM settings.WarehouseTransferScaleVersions"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Settings.WarehouseTransferScale'"));
        var changed = await owner.RecordAsync(location, request with { RequireExitWeighing = true }, metadata);
        Assert.Equal((409, "settings_warehouse_transfer_scale_idempotency_conflict"), (changed.Answer.StatusCode, changed.Answer.Code));
    }

    [SqlServerFact]
    public async Task Warehouse_scale_on_refuses_unsupported_source_without_a_key_and_retries_after_a_positive_owner_answer()
    {
        await using var run = await HostRun.StartAsync();
        await using var port = run.Numbers(CompanyA);
        var location = Guid.NewGuid(); var warehouses = new ScaleWarehouseSource(location);
        var capability = new ScaleCapability { Supported = false };
        var owner = ScaleOwner(port, CompanyA, warehouses, capability);
        var request = new RecordWarehouseTransferScaleV1("0", true, "SENTETIK kantar açık");
        var metadata = NumberMetadata("scale-on-support");
        await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => owner.RecordAsync(location, request, metadata));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM settings.WarehouseTransferScaleVersions"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM idempotency.CommandExecutions WHERE CommandName='settings.warehouse-transfer-scale.record.v1'"));
        capability.Supported = true;
        Assert.Equal(204, (await owner.RecordAsync(location, request, metadata)).Answer.StatusCode);
        Assert.True((await owner.GetAsync(CompanyA, location)).RequireExitWeighing);
        var stale = await owner.RecordAsync(location, request, metadata with { IdempotencyKey = "scale-stale" });
        Assert.Equal((409, "settings_warehouse_transfer_scale_version_conflict"), (stale.Answer.StatusCode, stale.Answer.Code));
    }

    [SqlServerFact]
    public async Task Warehouse_scale_post_audit_authority_fault_rolls_back_policy_and_real_audit_and_attempt_together()
    {
        await using var run = await HostRun.StartAsync();
        await using var port = run.Numbers(CompanyA);
        var location = Guid.NewGuid();
        var owner = ScaleOwner(port, CompanyA, new(location), authority: new InvoiceAuthority { RevokeOnFresh = 3 });
        var request = new RecordWarehouseTransferScaleV1("0", false, "SENTETIK gerçek Audit sonrası ret");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.RecordAsync(location, request, NumberMetadata("scale-after-audit")));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM settings.WarehouseTransferScaleVersions"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Settings.WarehouseTransferScale'"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM idempotency.CommandExecutions WHERE CommandName='settings.warehouse-transfer-scale.record.v1'"));
        Assert.False(port.AuditWriter.HasPendingDrafts); Assert.False(port.Database.ChangeTracker.HasChanges());
        Assert.Equal(204, (await ScaleOwner(port, CompanyA, new(location)).RecordAsync(location, request, NumberMetadata("scale-after-audit"))).Answer.StatusCode);
    }

    [SqlServerFact]
    public async Task Warehouse_scale_cross_company_and_borrowed_transaction_refusals_preserve_the_callers_state()
    {
        await using var run = await HostRun.StartAsync();
        await using var port = run.Numbers(CompanyA);
        var location = Guid.NewGuid(); var owner = ScaleOwner(port, CompanyA, new(location));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.GetAsync(CompanyB, location));
        await using var transaction = await port.Database.Database.BeginTransactionAsync();
        var caller = new WarehouseTransferScaleVersionRow { CompanyId = CompanyA, LocationId = location, Version = 1,
            RecordId = Guid.NewGuid(), RequireExitWeighing = false, LocationVersion = 1, RecordedBy = "SENTETIK",
            RecordedAtUtc = DateTimeOffset.UtcNow, Reason = "SENTETIK caller staged" };
        port.Database.Add(caller);
        await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => owner.RecordAsync(location,
            new("0", false, "SENTETIK nested"), NumberMetadata("scale-nested")));
        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Added, port.Database.Entry(caller).State);
        await transaction.RollbackAsync();
    }

    private static SettingsWarehouseTransferScalePolicy ScaleOwner(NumberPortScope port, Guid companyId,
        ScaleWarehouseSource warehouses, ScaleCapability? capability = null, InvoiceAuthority? authority = null)
    {
        var company = new CompanyContext(); company.Set(companyId);
        var permission = authority ?? new InvoiceAuthority();
        return new(port.Database, company, new FixedActor(companyId), permission, permission, port.Audit,
            port.AuditWriter, TimeProvider.System, warehouses, capability);
    }
    private sealed class ScaleWarehouseSource(Guid locationId) : ISettingsTransferWarehouseSourceV1
    {
        public bool Unavailable { get; set; }
        public Task<SettingsTransferWarehouseV1?> ReadAsync(Guid companyId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable || id != locationId ? null : new SettingsTransferWarehouseV1(companyId, id, true, false, 1));
    }
    private sealed class ScaleCapability : ISettingsTransferDepartureCapabilityV1
    {
        public bool Supported { get; set; }
        public Task<bool> IsSupportedAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default) => Task.FromResult(Supported);
    }
}
