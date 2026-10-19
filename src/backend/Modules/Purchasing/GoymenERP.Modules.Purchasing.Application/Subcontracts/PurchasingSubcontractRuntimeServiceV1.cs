using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Subcontracts;

namespace GoymenERP.Modules.Purchasing.Application.Subcontracts;

public sealed class PurchasingSubcontractRuntimeServiceV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    IPurchasingSubcontractStoreV1 store,
    IPurchasingSubcontractPhysicalContextSourceV1 physicalContexts,
    PurchasingSubcontractAdmissionV1 admission,
    TimeProvider timeProvider) : IPurchasingSubcontractRuntimeServiceV1
{
    public async Task<PurchasingSubcontractResponseV1> CreateDraftAsync(
        CreatePurchasingSubcontractDraftV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersCreate, cancellationToken);
        var hash = PurchasingRequestHash.Compute(PurchasingSubcontractCommandsV1.Create, request);
        var envelope = Envelope(scope, metadata, PurchasingPermissionManifestV1.OrdersCreate);
        var replay = await store.FindReplayAsync(envelope, hash, PurchasingSubcontractCommandsV1.Create, cancellationToken);
        if (replay is not null) return Replay(replay);
        await RequireStillAsync(scope, PurchasingPermissionManifestV1.OrdersCreate, cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var result = await store.StoreDraftAsync(envelope, hash, () => SubcontractOrderV1.Create(
                Guid.NewGuid(), scope.CompanyId, request.SupplierPartyId, request.ExpectedSupplierPartyVersion,
                request.CompanyAccountId, request.ExpectedCompanyAccountVersion,
                [.. request.Lines.Select(line => new SubcontractOrderLineV1(line.LineId, line.ServiceId,
                    line.ExpectedServiceVersion, line.ServiceUnitId, line.ServiceQuantity,
                    line.SourcePhysicalRequestId, line.ExpectedSourcePhysicalRequestVersion))], now), cancellationToken);
            return Response(result);
        }
        catch (ArgumentException) { throw new PurchasingServiceException(400, "validation_failed"); }
        catch (PurchasingSubcontractStoreExceptionV1 exception) { throw StoreProblem(exception); }
    }

    public async Task<PurchasingSubcontractViewV1> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(PurchasingPermissionManifestV1.OrdersRead, cancellationToken);
        if (id == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var stored = await store.FindAsync(id, cancellationToken);
        await RequireStillAsync(scope, PurchasingPermissionManifestV1.OrdersRead, cancellationToken);
        return stored is null ? throw new PurchasingServiceException(404, "not_found") : View(stored);
    }

    public async Task<PurchasingSubcontractResponseV1> AdmitAsync(Guid id,
        AdmitPurchasingSubcontractV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(PurchasingPermissionManifestV1.SubcontractsAdmit, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var hash = PurchasingRequestHash.Compute(PurchasingSubcontractCommandsV1.Admit, id, request);
        var envelope = Envelope(scope, metadata, PurchasingPermissionManifestV1.SubcontractsAdmit);
        var replay = await store.FindReplayAsync(envelope, hash, PurchasingSubcontractCommandsV1.Admit, cancellationToken);
        if (replay is not null) return Replay(replay);
        if (id == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var stored = await store.FindAsync(id, cancellationToken);
        await RequireStillAsync(scope, PurchasingPermissionManifestV1.SubcontractsAdmit, cancellationToken);
        if (stored is null) throw new PurchasingServiceException(404, "not_found");

        var order = stored.Order;
        var admissionOrder = new PurchasingSubcontractOrderV1(order.Id, order.CompanyId,
            order.SupplierPartyId, order.CompanyAccountId, expectedVersion.ToString(CultureInfo.InvariantCulture),
            order.SupplierPartyVersion, order.CompanyAccountVersion,
            [.. order.Lines.Select(line => new PurchasingSubcontractOrderLineV1(line.LineId, line.ServiceId,
                line.ServiceVersion, line.ServiceUnitId, line.ServiceQuantity,
                line.SourcePhysicalRequestId, line.SourcePhysicalRequestVersion))]);
        var snapshots = new List<PurchasingSubcontractAdmissionSnapshotStoreV1>(order.Lines.Count);
        foreach (var line in order.Lines)
        {
            PurchasingSubcontractPhysicalContextAnswerV1 answer;
            try
            {
                answer = await physicalContexts.GetAuthorizedAsync(scope.CompanyId,
                    line.SourcePhysicalRequestId, line.SourcePhysicalRequestVersion, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new PurchasingServiceException(503, "service_unavailable"); }
            await RequireStillAsync(scope, PurchasingPermissionManifestV1.SubcontractsAdmit, cancellationToken);
            if (answer is null || answer.Outcome == PurchasingSubcontractPhysicalContextOutcomeV1.Unavailable)
                throw new PurchasingServiceException(503, "service_unavailable");
            if (answer.Outcome == PurchasingSubcontractPhysicalContextOutcomeV1.Denied)
                throw new PurchasingServiceException(403, "forbidden");
            if (answer.Outcome != PurchasingSubcontractPhysicalContextOutcomeV1.Found || answer.Context is null)
                throw new PurchasingServiceException(503, "service_unavailable");

            PurchasingSubcontractAdmissionSnapshotV1 admitted;
            try { admitted = await admission.AdmitAsync(admissionOrder, line.LineId, answer.Context, cancellationToken); }
            catch (PurchasingSubcontractAdmissionExceptionV1 exception) { throw OwnerProblem(exception); }
            await RequireStillAsync(scope, PurchasingPermissionManifestV1.SubcontractsAdmit, cancellationToken);
            snapshots.Add(new(line.LineId, admitted.Service, admitted.ExpectedOrderVersion,
                admitted.PhysicalContext.MaterialOwnerId, (int)admitted.PhysicalContext.MaterialOwnerKind,
                admitted.PhysicalContext.ExternalLocationId, admitted.PhysicalContext.ExternalLocationVersion,
                admitted.PhysicalContext.OperatorPartyId, admitted.PhysicalContext.OperatorPartyVersion,
                admitted.PhysicalContext.SourcePhysicalRequestId, admitted.PhysicalContext.SourcePhysicalRequestVersion,
                timeProvider.GetUtcNow()));
        }

        await RequireStillAsync(scope, PurchasingPermissionManifestV1.SubcontractsAdmit, cancellationToken);
        try
        {
            var result = await store.AdmitAsync(envelope, hash, id, expectedVersion, snapshots, cancellationToken);
            return result is null ? throw new PurchasingServiceException(404, "not_found") : Response(result);
        }
        catch (PurchasingSubcontractTerminalStoreExceptionV1 exception)
        {
            throw new PurchasingServiceException(exception.Refusal.Status, exception.Refusal.Code, exception.Refusal.Errors);
        }
        catch (PurchasingSubcontractStoreExceptionV1 exception) { throw StoreProblem(exception); }
    }

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)) throw new PurchasingServiceException(401, "unauthenticated");
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
            throw new PurchasingServiceException(403, "forbidden");
        if (!ErpPermissions.IsKnown(permission)) throw new PurchasingServiceException(503, PurchasingProblemCodesV1.PermissionUnregistered);
        var company = companyContext.CompanyId; var actor = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(company, actor)) throw new PurchasingServiceException(403, "forbidden");
        return new(company, actor, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }
    private async Task RequireStillAsync(Scope expected, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != expected.CompanyId || !string.Equals(current.ActorId, expected.ActorId, StringComparison.Ordinal))
            throw new PurchasingServiceException(403, "forbidden");
    }
    private bool Matches(Guid company, string actor) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actor, StringComparison.Ordinal) && companyContext.IsResolved
        && companyContext.CompanyId == company && currentUser.CanAccessCompany(company);
    private PurchasingWriteEnvelope Envelope(Scope scope, PurchasingCommandMetadata metadata, string permission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId,
            metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    private static long ParseVersion(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value : throw new PurchasingServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
    private static PurchasingServiceException OwnerProblem(PurchasingSubcontractAdmissionExceptionV1 ex) => ex.Problem switch
    {
        PurchasingSubcontractAdmissionProblemV1.PartyUnavailable or PurchasingSubcontractAdmissionProblemV1.SubjectUnavailable
            or PurchasingSubcontractAdmissionProblemV1.PartyNotFound or PurchasingSubcontractAdmissionProblemV1.SubjectNotFound
            => new(503, "service_unavailable"),
        PurchasingSubcontractAdmissionProblemV1.PartyDenied or PurchasingSubcontractAdmissionProblemV1.SubjectDenied
            => new(403, "forbidden"),
        _ => new(409, PurchasingSubcontractProblemCodesV1.OwnerRefused, [new("owner", ex.Problem.ToString())]),
    };
    private static PurchasingServiceException StoreProblem(PurchasingSubcontractStoreExceptionV1 ex) => ex.Code switch
    {
        PurchasingSubcontractStoreCodesV1.SameKeyPayloadMismatch => new(409, PurchasingSubcontractProblemCodesV1.SameKeyPayloadMismatch),
        PurchasingSubcontractStoreCodesV1.ConcurrentChange => new(409, PurchasingSubcontractProblemCodesV1.ConcurrentChange),
        PurchasingSubcontractStoreCodesV1.CompanyContextMismatch => new(403, "forbidden"),
        _ => new(503, "service_unavailable"),
    };
    private static PurchasingSubcontractResponseV1 Replay(PurchasingSubcontractReplayV1 replay)
    {
        if (replay.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code,
            refusal.Errors, true, replay.OriginalCorrelationId);
        return replay.Order is null ? throw new PurchasingServiceException(503, "service_unavailable")
            : new(View(replay.Order), true, replay.OriginalCorrelationId);
    }
    private static PurchasingSubcontractResponseV1 Response(PurchasingSubcontractStoreResultV1 result) =>
        new(View(result.Order), result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId);
    private static PurchasingSubcontractViewV1 View(StoredPurchasingSubcontractV1 stored) => new(
        stored.Order.Id, stored.Order.CompanyId, stored.Order.Status.ToString(), stored.Order.Version.ToString(CultureInfo.InvariantCulture),
        stored.Order.SupplierPartyId, stored.Order.SupplierPartyVersion, stored.Order.CompanyAccountId,
        stored.Order.CompanyAccountVersion, stored.Order.CreatedAtUtc, stored.Order.UpdatedAtUtc,
        [.. stored.Order.Lines.Select((line, index) => new PurchasingSubcontractLineViewV1(index + 1,
            line.LineId, line.ServiceId, line.ServiceVersion, line.ServiceUnitId, line.ServiceQuantity,
            line.SourcePhysicalRequestId, line.SourcePhysicalRequestVersion))],
        [.. stored.Admissions.Select(x => new PurchasingSubcontractAdmissionViewV1(x.LineId, x.Service.Version,
            x.MaterialOwnerId, x.ExternalLocationId, x.ExternalLocationVersion, x.OperatorPartyId,
            x.OperatorPartyVersion, x.AdmittedAtUtc))]);
}
