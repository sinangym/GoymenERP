using System.Data;
using System.Globalization;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static GoymenERP.Modules.Logistics.Application.Contracts.LogisticsSubcontractDeliveryV1;

namespace GoymenERP.Api.LogisticsRuntime;

/// <summary>LOJ-D1..D5/D12/D14: real owner composition, never a normal sale or a caller-authorized ownership exception.</summary>
public sealed partial class LogisticsSubcontractDeliveryHost(LogisticsDbContext db, LogisticsStore store,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, ISettingsPrerequisiteProvider settings,
    ISalesOrderLineSerializationLockBinderV1 lineLocks, ISalesOrderLineReferencePortV1 salesLines,
    ILogisticsPartyAccountPortV1 parties, ISalesShipmentRiskGateReadPortV1 risk, TimeProvider clock,
    IServiceProvider services)
    : ILogisticsSubcontractDeliveryHostV1
{
    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> Roles, string Permission, string[] Additional);
    private static LogisticsInstructionServiceException Error(int status, string code) => new(status, code);
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Kg(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static decimal Quantity(string text) => InventorySubcontractDeliveryContractV1.Quantity(text).Value;
    private static long Version(string text) => InventorySubcontractDeliveryContractV1.Version(text);

    private async Task<Scope> RequireAsync(string permission, string[] additional, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Error(401, "unauthenticated");
        var actor = user.UserId; var id = company.IsResolved ? company.CompanyId : Guid.Empty;
        if (id == Guid.Empty || !user.CanAccessCompany(id)) throw Error(403, "forbidden");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { permission }.Concat(additional).Distinct(StringComparer.Ordinal))
        {
            if (!ErpPermissions.IsKnown(name)) throw Error(503, "permission_unregistered");
            var measured = await authority.RevalidatePermissionScopeAsync(name, id, token);
            if (!measured.IsGranted || !measured.IsCompanyWide || !Member(id, actor)) throw Error(403, "forbidden");
            roles.UnionWith(measured.RoleCodes);
        }
        return new(id, actor, roles.Order(StringComparer.Ordinal).ToArray(), permission, additional);
    }
    private bool Member(Guid id, string actor) => company.IsResolved && company.CompanyId == id && user.IsAuthenticated
        && user.UserId == actor && user.CanAccessCompany(id);
    private async Task StillAsync(Scope scope, CancellationToken token)
    {
        if (!Member(scope.CompanyId, scope.ActorId)) throw Error(403, "forbidden");
        var current = await RequireAsync(scope.Permission, scope.Additional, token);
        if (current.CompanyId != scope.CompanyId || current.ActorId != scope.ActorId) throw Error(403, "forbidden");
    }
    private static LogisticsWriteEnvelope Envelope(Scope scope, LogisticsCommandMetadata metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId)) throw Error(400, Invalid);
        return new(scope.ActorId, scope.Permission, scope.Roles, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, "Web");
    }
    private InventoryCommandMetadata InventoryMetadata(Scope scope, LogisticsWriteEnvelope envelope) => new(scope.ActorId,
        ErpPermissions.InventoryPhysicalAllocationWrite, scope.Roles, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel);
    private async Task<T> TransactionAsync<T>(Scope scope, Func<CancellationToken, Task<T>> work, CancellationToken token)
    {
        RequireOwnedContext();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var answer = await work(token); await StillAsync(scope, token);
            await auditWriter.WritePendingAsync(tx.GetDbTransaction(), token);
            await StillAsync(scope, token); await tx.CommitAsync(token); return answer;
        }
        catch (Exception failure)
        {
            var rolledBack = false;
            try { await tx.RollbackAsync(CancellationToken.None); rolledBack = true; } catch { }
            auditWriter.DiscardPending(); db.ChangeTracker.Clear();
            if (!rolledBack) throw Error(503, Unavailable);
            if (InventoryNegativeStockAttemptEvidenceException.Find(failure) is not null)
            {
                // SAT-D328: only the actual physical owner proof survives a confirmed rollback. The recorder
                // owns a fresh transaction; neither a reservation refusal nor an uncertain rollback may use it.
                await StillAsync(scope, CancellationToken.None);
                try { await InventoryNegativeStockAttemptRecording.AfterRollbackAsync(services, failure); }
                catch (Exception) { throw Error(503, Unavailable); }
            }
            if (!token.IsCancellationRequested) await StillAsync(scope, token);
            throw;
        }
    }
    private void RequireOwnedContext()
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Error(503, Unavailable);
    }
    private async Task LockLinesAsync(IEnumerable<Guid> ids, CancellationToken token)
    {
        var bound = lineLocks.Bind(db.Database.GetDbConnection(), db.Database.CurrentTransaction!.GetDbTransaction());
        foreach (var id in ids.Distinct().Order())
        {
            if (id == Guid.Empty) throw Error(400, Invalid);
            await bound.AcquireAsync(id, token);
        }
    }
    private Task<InventorySubcontractDeliveryParticipant> InventoryAsync(Scope scope, CancellationToken token) =>
        InventorySubcontractDeliveryParticipant.EnlistAsync(db.Database.GetDbConnection(), db.Database.CurrentTransaction!.GetDbTransaction(),
            scope.CompanyId, settings, audit, auditWriter, lineLocks, token, clock);
    private Task<SalesSubcontractServiceOrderReadSession> SalesAsync(CancellationToken token) =>
        SalesSubcontractServiceOrderReadSession.EnlistAsync(db.Database.GetDbConnection(), db.Database.CurrentTransaction!.GetDbTransaction(), company, token);
    private Task<ManufacturingSubcontractCompletionReadSessionV2> ManufacturingAsync(CancellationToken token) =>
        ManufacturingSubcontractCompletionReadSessionV2.EnlistAsync(db.Database.GetDbConnection(), db.Database.CurrentTransaction!.GetDbTransaction(), company, token);

    private async Task<(LogisticsSubcontractSourceV1 Source, SalesSubcontractServiceOrderV1 Order, SalesSubcontractServiceLineV1 Line,
        InventorySubcontractLabelReadV1 Label)> SourceAsync(Scope scope, Guid orderId, Guid lineId, Guid manufacturingId,
        Guid outputId, Guid labelId, SalesSubcontractServiceOrderReadSession sales,
        ManufacturingSubcontractCompletionReadSessionV2 manufacturing, InventorySubcontractDeliveryParticipant stock, CancellationToken token)
    {
        var order = await OrderAsync(scope, sales, orderId, token);
        var line = order.Lines.SingleOrDefault(candidate => candidate.LineId == lineId && candidate.ItemType == "Service")
            ?? throw Error(409, SourceConflict);
        var read = await manufacturing.GetAsync(scope.CompanyId, manufacturingId, token); await StillAsync(scope, token);
        if (read.Outcome == ManufacturingSubcontractCompletionOutcomeV1.NotFound) throw Error(404, "not_found");
        if (read.Outcome != ManufacturingSubcontractCompletionOutcomeV1.Available || read.Value is not { } proof) throw Error(503, Unavailable);
        if (proof.SalesOrderId != orderId || proof.SalesOrderLineId != lineId || proof.SalesLineRevision != order.BasisRevision
            || proof.CustomerPartyId != order.PartyId || proof.CustomerAccountId != order.CompanyAccountId || proof.ProductId != line.ProductId)
            throw Error(409, SourceConflict);
        var output = proof.Outputs.SingleOrDefault(candidate => candidate.OutputFactId == outputId && candidate.LabelId == labelId)
            ?? throw Error(409, SourceConflict);
        var label = await stock.ReadLabelAsync(labelId, token) ?? throw Error(404, "not_found"); await StillAsync(scope, token);
        if (label.CompanyId != scope.CompanyId || label.LabelId != labelId) throw Error(503, Unavailable);
        var source = new LogisticsSubcontractSourceV1(scope.CompanyId, order.PartyId, order.CompanyAccountId, order.OrderId,
            line.LineId, order.Version, order.BasisRevision, label.ProductId, label.ProductVersion, line.ProductId, manufacturingId,
            proof.OrderVersion, proof.PlanId, proof.PlanVersion, proof.SalesLinkRevision, output.OutputFactId, labelId,
            output.RemainingKg, proof.SourceVersion, label.LabelCode, order.DocumentNo, line.ProductCode, line.ProductName);
        return (source, order, line, label);
    }
    private async Task<SalesSubcontractServiceOrderV1> OrderAsync(Scope scope, SalesSubcontractServiceOrderReadSession sales, Guid id, CancellationToken token)
    {
        var read = await sales.GetAsync(scope.CompanyId, id, token); await StillAsync(scope, token);
        if (read.Outcome == SalesSubcontractServiceOrderOutcomeV1.NotFound) throw Error(404, "not_found");
        if (read.Outcome != SalesSubcontractServiceOrderOutcomeV1.Found || read.Order is not { } order) throw Error(503, Unavailable);
        var usable = order.Basis == SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision
            || order.Basis == SalesSubcontractServiceOrderBasesV1.CurrentRevision && order.IsApproved && order.CustomerAcceptanceState is "Accepted" or "NotRequired";
        if (order.CompanyId != scope.CompanyId || order.OrderId != id || !order.IsOpen || !usable || order.Version < 1 || order.BasisRevision < 0)
            throw Error(409, SourceConflict);
        return order;
    }
    private static InventorySubcontractDeliverySourceV1 InventorySource(LogisticsSubcontractSourceV1 s) => new(s.CompanyId,
        s.CustomerPartyId, s.CustomerAccountId, s.SalesOrderId, s.SalesOrderLineId, s.SalesOrderVersion, s.SalesLineRevision,
        s.ProductId, s.ProductVersion, s.ManufacturingOrderId, s.ManufacturingOrderVersion, s.ManufacturingPlanId,
        s.ManufacturingPlanVersion, s.OutputFactId, s.LabelId, s.OutputNetKg, InventorySubcontractDeliveryContractV1.SourceVersion,
        s.ManufacturingSalesLinkRevision, s.SalesServiceProductId);
}

public static class LogisticsSubcontractDeliveryComposition
{
    public static IServiceCollection AddLogisticsSubcontractDeliveryRuntime(this IServiceCollection services)
    {
        services.TryAddScoped<ILogisticsSubcontractDeliveryHostV1, LogisticsSubcontractDeliveryHost>(); return services;
    }
}
