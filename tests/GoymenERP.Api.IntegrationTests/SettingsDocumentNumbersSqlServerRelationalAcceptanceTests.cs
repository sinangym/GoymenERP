using GoymenERP.BuildingBlocks.Observability;
using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Annual_document_numbers_join_owner_rollback_replay_reset_cancelled_gap_and_company_fences_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsDocumentNumberSeriesPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        const string root = "/api/v1/settings/company-policies/document-number-series";
        await run.CommandAsync(editor, root + "/versions", "number-format", new RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1.PurchaseOrder, "0", "ALI", 6, "SENTETIK biçim"));
        var document = Guid.NewGuid();
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var reserved = await port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyA, document, new DateOnly(2026, 11, 17), "1", NumberMetadata("number-one"));
            Assert.Equal(("ALI-2026-000001", 1L, false), (reserved.Number, reserved.Sequence, reserved.Replayed));
            await tx.RollbackAsync();
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberCounters]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.document-number.reserved.v1'"));
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var reserved = await port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyA, document, new DateOnly(2026, 11, 17), "1", NumberMetadata("number-one"));
            Assert.Equal("ALI-2026-000001", reserved.Number);
            await tx.CommitAsync();
        }
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var replay = await port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyA, document, new DateOnly(2026, 11, 17), "1", NumberMetadata("number-one"));
            Assert.True(replay.Replayed);
            await tx.CommitAsync();
        }
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await port.Value.CancelAsync(tx.GetDbTransaction(), CompanyA, SettingsDocumentSeriesV1.PurchaseOrder, document, "SENTETIK iptal", NumberMetadata("number-cancel"));
            await tx.CommitAsync();
        }
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var next = await port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyA, Guid.NewGuid(), new DateOnly(2026, 11, 18), "1", NumberMetadata("number-two"));
            var annual = await port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyA, Guid.NewGuid(), new DateOnly(2027, 1, 1), "1", NumberMetadata("number-next-year"));
            Assert.Equal(("ALI-2026-000002", "ALI-2027-000001"), (next.Number, annual.Number));
            await tx.CommitAsync();
        }
        Assert.Equal(3, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberCancellations]"));
        Assert.Equal(3, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.document-number.reserved.v1'"));
        await run.CommandAsync(editor, root + "/versions", "sales-number-format", new RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1.SalesOrder, "0", "SAT", 6, "SENTETIK satış biçimi"));
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var otherOwner = await port.Value.ReserveAsync(tx.GetDbTransaction(), CompanyA, SettingsDocumentSeriesV1.SalesOrder, Guid.NewGuid(), new DateOnly(2026, 11, 17), "1", NumberMetadata("number-one"));
            Assert.Equal("SAT-2026-000001", otherOwner.Number);
            await tx.CommitAsync();
        }
        Assert.Equal(4, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
        await using (var port = run.Numbers(CompanyA))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyB, Guid.NewGuid(), new DateOnly(2026, 11, 17), "1", NumberMetadata("foreign") with { CompanyId = CompanyB }));
        }
    }

    [SqlServerFact]
    public async Task A_document_number_reservation_refuses_revoked_authority_after_the_owner_read_and_never_commits_a_counter_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsDocumentNumberSeriesPermissionsV1.Manage);
        using var editor = run.Client(subject, CompanyA);
        await run.CommandAsync(editor, "/api/v1/settings/company-policies/document-number-series/versions", "number-format-fence",
            new RecordSettingsDocumentNumberFormatV1(SettingsDocumentSeriesV1.PurchaseOrder, "0", "ALI", 6, "SENTETIK biçim"));
        var authority = new NumberAuthority { DenyOnMeasurement = 2 };
        await using (var port = run.Numbers(CompanyA, authority))
        {
            await using var tx = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.Value.ReservePurchaseOrderNumberAsync(tx.GetDbTransaction(), CompanyA, Guid.NewGuid(), new DateOnly(2026, 11, 17), "1", NumberMetadata("revoked")));
            await tx.RollbackAsync();
        }
        Assert.Equal(2, authority.Measurements);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberReservations]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[DocumentNumberCounters]"));
    }
    private static SettingsCommandMetadataV1 NumberMetadata(string key) => new(CompanyA, "SENTETIK-reference-reader", "SENTETIK-number-" + key, null, SourceChannels.Web, null, null, key);
    private sealed class NumberAuthority : IErpAuthorityRevalidationService
    {
        public int Measurements { get; private set; }
        public int DenyOnMeasurement { get; init; } = int.MaxValue;
        public string? OnlyPermission { get; init; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken cancellationToken = default)
        {
            Measurements++;
            return Task.FromResult(Measurements >= DenyOnMeasurement || OnlyPermission is not null && OnlyPermission != permission
                ? PermissionScope.Denied : new(true, true, new HashSet<string> { "SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class NumberPortScope(AsyncServiceScope scope, SettingsDbContext database, ISettingsDocumentNumberSeriesV1 value) : IAsyncDisposable
    {
        public SettingsDbContext Database { get; } = database;
        public ISettingsDocumentNumberSeriesV1 Value { get; } = value;
        public IAuditEventCollector Audit => scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        public IAuditTransactionWriterV1 AuditWriter => scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
