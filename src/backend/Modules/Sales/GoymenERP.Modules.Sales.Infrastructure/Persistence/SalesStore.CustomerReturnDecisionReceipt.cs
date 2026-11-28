using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    public async Task<SalesCustomerReturnCommandResult> AttachCustomerReturnOriginalReceiptAsync(SalesCustomerReturnCommandResult response,
        Guid returnId, SalesDraftStateCommandRequest request, CancellationToken cancellationToken = default)
    {
        if (ownedCustomerReturnDecision is not { } state) throw new InvalidOperationException("İade makbuzu yalnız sahip karar işleminde okunabilir.");
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n == long.MaxValue
            || response.OriginalDecisionReceipt is not null) return response;
        var key = IdempotencyKeyHash(state.Envelope.IdempotencyKey);
        var artifacts = await CustomerReturnDecisionReadAsync(() => (from a in dbContext.CustomerReturnAttempts.AsNoTracking()
            join audit in dbContext.Set<AuditEvent>().AsNoTracking() on new { CompanyId = (Guid?)a.CompanyId, a.IdempotencyKeyHash, a.CorrelationId }
                equals new { audit.CompanyId, audit.IdempotencyKeyHash, audit.CorrelationId }
            where a.CompanyId == state.CompanyId && a.TargetId == returnId && a.CustomerReturnId == returnId && a.CommandName == state.Command
                && a.IdempotencyKeyHash == key && a.RequestHash == state.Hash && a.ActorId == state.Envelope.ActorId && audit.ActorId == state.Envelope.ActorId
                && audit.EntityType == CustomerReturnEntityType && audit.EntityId == returnId.ToString() && audit.PermissionCode == SalesPermissionManifestV1.ReturnsApprove
                && audit.EntityVersionBefore == n && audit.EntityVersionAfter == n + 1
                && (a.CommandName == SalesCustomerReturnCommands.ApproveV1 && audit.EventType == "sales.customer-return.approved.v1"
                    || a.CommandName == SalesCustomerReturnCommands.RejectV1 && audit.EventType == "sales.customer-return.rejected.v1")
            select new CustomerReturnDecisionArtifact(a, audit)).Take(2).ToArrayAsync(cancellationToken), cancellationToken);
        var item = await ReadCustomerReturnReceiptItem(returnId, cancellationToken);
        if (artifacts.Length != 1 || item is null) return response;
        var receipt = BindCustomerReturnReceipt(artifacts[0], item, n, state.Envelope.ActorId, request, response);
        return receipt is null ? response : response with { OriginalDecisionReceipt = receipt };
    }

    public async Task<SalesCustomerReturnOriginalReadResultV1> ReadCustomerReturnOriginalVersionAsync(Guid returnId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        if (expectedVersion < 1 || expectedVersion == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        EnterCustomerReturnDecisionRead(freshFence, cancellationToken);
        try
        {
            var company = RequireResolvedCompany();
            // Exact native decision identity/actor/version filters precede the ambiguity bound; no all-history scan.
            var artifacts = await CustomerReturnDecisionReadAsync(() => (from a in dbContext.CustomerReturnAttempts.AsNoTracking()
                join audit in dbContext.Set<AuditEvent>().AsNoTracking() on new { CompanyId = (Guid?)a.CompanyId, a.IdempotencyKeyHash, a.CorrelationId }
                    equals new { audit.CompanyId, audit.IdempotencyKeyHash, audit.CorrelationId }
                where a.CompanyId == company && a.TargetId == returnId && a.CustomerReturnId == returnId && a.ActorId == actorId && audit.ActorId == actorId
                    && audit.EntityType == CustomerReturnEntityType && audit.EntityId == returnId.ToString() && audit.PermissionCode == SalesPermissionManifestV1.ReturnsApprove
                    && audit.EntityVersionBefore == expectedVersion && audit.EntityVersionAfter == expectedVersion + 1
                    && (a.CommandName == SalesCustomerReturnCommands.ApproveV1 && audit.EventType == "sales.customer-return.approved.v1"
                        || a.CommandName == SalesCustomerReturnCommands.RejectV1 && audit.EventType == "sales.customer-return.rejected.v1")
                select new CustomerReturnDecisionArtifact(a, audit)).Take(2).ToArrayAsync(cancellationToken), cancellationToken);
            var item = await ReadCustomerReturnReceiptItem(returnId, cancellationToken);
            if (item is null) return new(SalesCustomerReturnDecisionReadStateV1.NotFound, null);
            if (artifacts.Length == 0) return new(SalesCustomerReturnDecisionReadStateV1.NotFound, null) { CurrentVersion = item.Version };
            if (artifacts.Length != 1) return new(SalesCustomerReturnDecisionReadStateV1.Unavailable, null);
            var receipt = BindCustomerReturnReceipt(artifacts[0], item, expectedVersion, actorId, null, null);
            return receipt is null ? new(SalesCustomerReturnDecisionReadStateV1.Unavailable, null)
                : new(SalesCustomerReturnDecisionReadStateV1.Found, receipt) { CurrentVersion = item.Version };
        }
        finally { customerReturnDecisionReadFence = null; }
    }
    private async Task<SalesCustomerReturn?> ReadCustomerReturnReceiptItem(Guid id, CancellationToken token)
    {
        var company = RequireResolvedCompany();
        var row = await CustomerReturnDecisionReadAsync(() => dbContext.CustomerReturns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == id, token), token);
        return row is null ? null : SalesCustomerReturnRows.Restore(row);
    }
    internal static SalesCustomerReturnOriginalReceiptV1? RestoreCustomerReturnDecisionReceipt(CustomerReturnAttemptRow attempt,
        AuditEvent audit, SalesCustomerReturn current, long expectedVersion, string actorId, SalesDraftStateCommandRequest? request,
        SalesCustomerReturnCommandResult? observed) => BindCustomerReturnReceipt(new(attempt, audit), current, expectedVersion, actorId, request, observed);
    private static SalesCustomerReturnOriginalReceiptV1? BindCustomerReturnReceipt(CustomerReturnDecisionArtifact artifact,
        SalesCustomerReturn current, long expectedVersion, string actorId, SalesDraftStateCommandRequest? request, SalesCustomerReturnCommandResult? observed)
    {
        var a = artifact.Attempt; var audit = artifact.Audit; var approve = a.CommandName == SalesCustomerReturnCommands.ApproveV1;
        if (!approve && a.CommandName != SalesCustomerReturnCommands.RejectV1 || a.CompanyId != current.CompanyId || a.TargetId != current.Id || a.CustomerReturnId != current.Id
            || a.Outcome != AttemptOutcomeRecorded || a.RefusalJson is not null || a.ActorId != actorId || string.IsNullOrWhiteSpace(actorId)
            || !LowerOrUpperHash(a.IdempotencyKeyHash) || !LowerOrUpperHash(a.RequestHash) || string.IsNullOrWhiteSpace(a.CorrelationId)
            || expectedVersion < 1 || expectedVersion == long.MaxValue || current.Version < expectedVersion + 1 || a.RecordedAtUtc.Offset != TimeSpan.Zero
            || current.CreatedBy == actorId || current.Facts.SubmittedBy == actorId || current.Facts.SubmittedBy is null || current.Facts.SubmittedAtUtc is null
            || audit.Id == Guid.Empty || audit.SubjectKind != AuditEvent.CompanySubject || audit.CompanyId != current.CompanyId
            || audit.EntityType != CustomerReturnEntityType || audit.EntityId != current.Id.ToString("D") || audit.ActorType != AuditActorTypes.User
            || audit.ActorId != actorId || audit.PermissionCode != SalesPermissionManifestV1.ReturnsApprove
            || audit.EventType != (approve ? "sales.customer-return.approved.v1" : "sales.customer-return.rejected.v1")
            || audit.Operation != (approve ? "ApproveCustomerReturn" : "RejectCustomerReturn") || audit.Outcome != AuditOutcomes.Succeeded
            || audit.CorrelationId != a.CorrelationId || audit.IdempotencyKeyHash != a.IdempotencyKeyHash || audit.EntityVersionBefore != expectedVersion
            || audit.EntityVersionAfter != expectedVersion + 1 || audit.SchemaVersion != 1 || audit.IntegrityHash != AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope())) return null;
        try
        {
            if (approve ? current.Facts.ApprovedBy != actorId || current.Facts.ApprovedAtUtc != a.RecordedAtUtc
                : current.Facts.RejectedBy != actorId || current.Facts.RejectedAtUtc != a.RecordedAtUtc) return null;
            var submitted = new SalesCustomerReturnFacts(current.Facts.SubmittedBy, current.Facts.SubmittedAtUtc, null, null, null, null);
            var pending = SalesCustomerReturn.Restore(current.Id, current.CompanyId, current.Header, current.ShipmentId, current.CustomerDocumentNo,
                current.CustomerDocumentDate, SalesCustomerReturnState.PendingApproval, submitted, current.CreatedBy, current.CreatedAtUtc,
                expectedVersion, current.Facts.SubmittedAtUtc.Value, current.Lines);
            var decisionFacts = approve ? submitted with { ApprovedBy = actorId, ApprovedAtUtc = a.RecordedAtUtc }
                : submitted with { RejectedBy = actorId, RejectedAtUtc = a.RecordedAtUtc, RejectionReason = current.Facts.RejectionReason };
            var original = SalesCustomerReturn.Restore(current.Id, current.CompanyId, current.Header, current.ShipmentId, current.CustomerDocumentNo,
                current.CustomerDocumentDate, approve ? SalesCustomerReturnState.Approved : SalesCustomerReturnState.Rejected, decisionFacts,
                current.CreatedBy, current.CreatedAtUtc, expectedVersion + 1, a.RecordedAtUtc, current.Lines);
            using var before = JsonDocument.Parse(audit.BeforeJson); using var after = JsonDocument.Parse(audit.AfterJson);
            using var expectedBefore = JsonDocument.Parse(CustomerReturnAuditState(pending)); using var expectedAfter = JsonDocument.Parse(CustomerReturnAuditState(original));
            // Exact structural equality also rejects unknown/private fields in either persisted native Audit summary.
            if (!JsonElement.DeepEquals(before.RootElement, expectedBefore.RootElement) || !JsonElement.DeepEquals(after.RootElement, expectedAfter.RootElement)) return null;
            if (request is not null && (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var rawN) || rawN != expectedVersion
                || (approve ? SalesCustomerReturnHash.Compute(a.CommandName, current.Id, request) : SalesCustomerReturnHash.ComputeDecision(a.CommandName, current.Id, request)) != a.RequestHash)) return null;
            var projected = SalesCustomerReturnService.ProjectDecisionReturn(original);
            var view = projected with { Lines = Array.AsReadOnly(projected.Lines.ToArray()) };
            if (observed is not null && (observed.OriginalDecisionReceipt is not null || observed.OriginalCorrelationId != a.CorrelationId
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(observed.CustomerReturn, SalesDraftJson.Options), JsonSerializer.SerializeToElement(SalesCustomerReturnService.ProjectDecisionReturn(current), SalesDraftJson.Options)))) return null;
            var receipt = new SalesCustomerReturnOriginalReceiptV1(current.CompanyId, current.Id, a.CommandName, actorId, a.IdempotencyKeyHash, a.RequestHash,
                expectedVersion, expectedVersion + 1, a.CorrelationId, a.RecordedAtUtc, view, audit.Id, audit.IntegrityHash,
                SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(current.CompanyId, view), "")
            { CurrentResponseDigest = observed is null ? "" : SalesCustomerReturnDecisionDigestsV1.CurrentResponseDigest(observed) };
            return receipt with { OriginalDecisionDigest = SalesCustomerReturnDecisionDigestsV1.OriginalDecisionDigest(receipt) };
        }
        catch (JsonException) { return null; } catch (InvalidOperationException) { return null; } catch (ArgumentException) { return null; } catch (FormatException) { return null; }
    }
    private sealed record CustomerReturnDecisionArtifact(CustomerReturnAttemptRow Attempt, AuditEvent Audit);
}
