using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// Fresh company-wide transfer authority around the Inventory writer and its destination-owner read. B-22a: dispatch
/// moves stock only after the typed Logistics outgoing-document proof answers <c>CurrentEffective</c> for the exact
/// requested version and the transfer's complete ordered line set; a missing or failing proof port is a 503.
/// B-22b: dispatch also stores the source warehouse "kantardan geçer" value read from its Settings owner (an absent
/// or failing read is stored as unreadable, never as a chosen value). Dispatch cancellation (KLN-D20) runs under its own
/// permission, evaluates the KLN-D25/KLN-D27 window from stored and fresh owner facts, records a closed window as a
/// terminal refusal (the closure is final), and writes the reverse movement only after the required confirmations.
/// </summary>
public sealed class InventoryTransferCommandService : IInventoryTransferCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryTransferDestinationPortV1 destination;
    private readonly IInventoryTransferReaderV1 reader;
    private readonly IInventoryTransferWriterV1 writer;
    private readonly IInventoryTransferOutgoingDocumentProofPortV1? outgoingDocumentProof;
    private readonly IInventoryTransferSourceScaleSettingPortV1? sourceScaleSetting;
    private readonly IInventoryTransferDispatchCancelGatePortV1? dispatchCancelGate;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryTransferCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferWriterV1 writer,
        IInventoryTransferOutgoingDocumentProofPortV1? outgoingDocumentProof = null,
        IInventoryTransferSourceScaleSettingPortV1? sourceScaleSetting = null,
        IInventoryTransferDispatchCancelGatePortV1? dispatchCancelGate = null)
        : this(companyContext, currentUser, authority, destination, reader, writer, ErpPermissions.IsKnown,
            outgoingDocumentProof, sourceScaleSetting, dispatchCancelGate)
    {
    }

    internal InventoryTransferCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferWriterV1 writer,
        Func<string, bool> isKnownPermission,
        IInventoryTransferOutgoingDocumentProofPortV1? outgoingDocumentProof = null,
        IInventoryTransferSourceScaleSettingPortV1? sourceScaleSetting = null,
        IInventoryTransferDispatchCancelGatePortV1? dispatchCancelGate = null)
    {
        this.outgoingDocumentProof = outgoingDocumentProof;
        this.sourceScaleSetting = sourceScaleSetting;
        this.dispatchCancelGate = dispatchCancelGate;
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.destination = destination ?? throw new ArgumentNullException(nameof(destination));
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.isKnownPermission = isKnownPermission ?? throw new ArgumentNullException(nameof(isKnownPermission));
    }

    public async Task<InventoryTransferResultV1> PlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferWrite, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayPlanAsync(request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        await RequireDestinationAsync(scope, request.DestinationLocationId, request.DestinationCustodianKind,
            request.DestinationCustodianId, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.PlanAsync(request, metadata, token => FreshAuthorityFenceAsync(scope, token),
            cancellationToken);
    }

    public async Task<InventoryTransferResultV1> DispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferWrite, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        // The key's stored answer (dispatch or recorded refusal) comes before any read or proof.
        if (await writer.ReplayDispatchAsync(transferId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        if (!InventoryTransferApiV1.TryParseOutgoingDocumentVersion(request.ExpectedOutgoingDocumentVersion,
                out var documentVersion))
        {
            throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var transfer = await ReadTransferAsync(scope, transferId, cancellationToken);
        scope = await MeasureAsync(scope, cancellationToken);
        InventoryTransferOutgoingDocumentProofQueryV1 query;
        try
        {
            query = new(scope.CompanyId, transfer.TransferId, request.OutgoingDocumentId, documentVersion,
                transfer.Lines.Select(line => line.LineId));
        }
        catch (ArgumentException exception)
        {
            throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed, exception);
        }

        var binding = await RequireCurrentEffectiveOutgoingDocumentAsync(scope, query, cancellationToken);
        if (binding.Refusal is { } refusal)
        {
            if (refusal.Status != 409)
            {
                throw refusal;
            }

            var refusalMetadata = await FinalizeAsync(scope, transport, cancellationToken);
            if (await writer.RecordDispatchRefusalAsync(transferId, request, refusal, refusalMetadata,
                    token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } winner)
            {
                return winner;
            }

            throw refusal;
        }

        // KLN-D25 (7)(f): the source warehouse setting at dispatch is stored with the dispatch.
        var settingAtDispatch = await ReadSourceScaleSettingAsync(scope, transfer.SourceLocationId, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.DispatchAsync(transferId, request, binding.Effective!,
            InventoryTransferDispatchCancelMapping.ToDomain(settingAtDispatch), metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    /// <summary>
    /// B-22b dispatch cancellation (KLN-D20). Order: the key's stored answer; the stored context and a persisted closure
    /// (final, KLN-D27 (5)); the request's version and the in-transit state; a fresh Settings read and a fresh owner read
    /// evaluated by the pure window rule; a closed window is persisted and refused under the key; an open window asks
    /// the owners for exactly the confirmations it needs, re-evaluates their confirmation-time facts, and only then
    /// writes the reverse movement. Unreadable readings fail closed and are not recorded (the same key may retry).
    /// </summary>
    public async Task<InventoryTransferResultV1> CancelDispatchAsync(
        Guid transferId,
        CancelInventoryTransferDispatchV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferCancel, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayCancelDispatchAsync(transferId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        if (transferId == Guid.Empty
            || !long.TryParse(request.ExpectedTransferVersion, NumberStyles.None, CultureInfo.InvariantCulture,
                out var expectedVersion)
            || expectedVersion < 1
            || request.CancelledAtUtc.Offset != TimeSpan.Zero
            || CancelInventoryTransferDispatchV1.NormalizeReason(request.Reason) is null)
        {
            throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);
        }

        var context = await ReadDispatchCancelContextAsync(scope, transferId, cancellationToken);
        if (context.ClosureBasis is { } storedBasis && context.ClosedAtUtc is { } storedClosedAtUtc)
        {
            return await RefuseClosedWindowAsync(scope, transferId, request, storedBasis, storedClosedAtUtc,
                transport, cancellationToken);
        }

        if (context.Version != expectedVersion)
        {
            throw new InventoryTransferException(409, InventoryTransferCodesV1.TransferVersionConflict);
        }

        if (context.Status != InventoryTransferStatus.InTransit)
        {
            throw new InventoryTransferException(409, InventoryTransferCodesV1.TransferStateConflict);
        }

        if (context.HasPendingLossRequest)
        {
            // KLN-D98: refused before the Logistics/e-İrsaliye owners are asked. The request is approved, rejected or
            // withdrawn first; the cancel never withdraws it. The writer checks it again in its own transaction.
            throw new InventoryTransferException(409, InventoryTransferCodesV1.CancelLossRequestPending);
        }

        if (context.DispatchedAtUtc is not { } dispatchedAtUtc
            || context.StoredScaleSettingAtDispatch is not { } storedSetting)
        {
            // An in-transit transfer always carries its dispatch row; anything else is a storage fault.
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }
        // This existing physical invariant must be checked before an irreversible Logistics/e-document fence.
        if (request.CancelledAtUtc < dispatchedAtUtc)
            throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);

        var currentSetting = InventoryTransferDispatchCancelMapping.ToDomain(
            await ReadSourceScaleSettingAsync(scope, context.SourceLocationId, cancellationToken));
        if (dispatchCancelGate is null)
        {
            // Composition gap: without the Logistics/e-İrsaliye owner no window can be proved open.
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        InventoryTransferDispatchCancelGateQueryV1 query;
        try
        {
            query = new(scope.CompanyId, context.TransferId, context.SourceLocationId, dispatchedAtUtc);
        }
        catch (ArgumentException exception)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        InventoryTransferDispatchCancelFactsV1? facts;
        try
        {
            facts = await dispatchCancelGate.ReadAsync(query, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        scope = await MeasureAsync(scope, cancellationToken);
        if (facts is null)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        var decision = InventoryTransferCancelWindow.Evaluate(storedSetting, currentSetting,
            InventoryTransferDispatchCancelMapping.ToDomain(facts), dispatchedAtUtc);
        if (await RefuseUnlessOpenAsync(scope, transferId, request, decision, transport, cancellationToken)
            is { } closedWinner)
        {
            return closedWinner;
        }

        InventoryTransferDispatchCancelConfirmationV1? confirmation;
        try
        {
            confirmation = dispatchCancelGate is IInventoryTransferDispatchCancelGatePortV2 versioned
                ? await versioned.ConfirmAsync(query, request, decision.RequiresLogisticsNoExitConfirmation,
                    decision.RequiresEDispatchCancelConfirmation, await FinalizeAsync(scope, transport, cancellationToken),
                    token => FreshAuthorityFenceAsync(scope, token), cancellationToken)
                : await dispatchCancelGate.ConfirmAsync(query, decision.RequiresLogisticsNoExitConfirmation,
                    decision.RequiresEDispatchCancelConfirmation, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        scope = await MeasureAsync(scope, cancellationToken);
        if (confirmation is null)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        // The owners' confirmation-time facts are evaluated again: a departure that happened in between closes it.
        var confirmed = InventoryTransferCancelWindow.Evaluate(storedSetting, currentSetting,
            InventoryTransferDispatchCancelMapping.ToDomain(confirmation.Facts), dispatchedAtUtc);
        if (await RefuseUnlessOpenAsync(scope, transferId, request, confirmed, transport, cancellationToken)
            is { } confirmedWinner)
        {
            return confirmedWinner;
        }

        var requiresLogistics = decision.RequiresLogisticsNoExitConfirmation
            || confirmed.RequiresLogisticsNoExitConfirmation;
        var requiresEDispatch = decision.RequiresEDispatchCancelConfirmation
            || confirmed.RequiresEDispatchCancelConfirmation;
        if ((requiresLogistics && confirmation.LogisticsNoExitConfirmationVersion is not >= 1)
            || (requiresEDispatch && confirmation.EDispatchCancelConfirmationVersion is not >= 1))
        {
            throw new InventoryTransferException(409, InventoryTransferCodesV1.CancelConfirmationMissing);
        }

        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.CancelDispatchAsync(transferId, request,
            requiresLogistics ? confirmation.LogisticsNoExitConfirmationVersion : null,
            requiresEDispatch ? confirmation.EDispatchCancelConfirmationVersion : null,
            metadata, token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    /// <summary>
    /// Closed is recorded and refused; unreadable is refused without a record; open returns null. A non-null result is
    /// the answer the key already held when the closure was about to be recorded (an exact-replay race winner).
    /// </summary>
    private async Task<InventoryTransferResultV1?> RefuseUnlessOpenAsync(
        Scope scope,
        Guid transferId,
        CancelInventoryTransferDispatchV1 request,
        InventoryTransferCancelWindowDecision decision,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        switch (decision.State)
        {
            case InventoryTransferCancelWindowState.Open:
                return null;
            case InventoryTransferCancelWindowState.Closed
                when decision.ClosureBasis is { } basis && decision.ClosedAtUtc is { } closedAtUtc:
                return await RefuseClosedWindowAsync(scope, transferId, request, basis, closedAtUtc, transport,
                    cancellationToken);
            default:
                throw new InventoryTransferException(409, InventoryTransferCodesV1.CancelWindowUnreadable);
        }
    }

    /// <summary>
    /// Persists the closure (first one wins) and the terminal refusal, then throws it. When the key already holds an
    /// answer for this exact request (a concurrent same-key winner), that answer is returned instead.
    /// </summary>
    private async Task<InventoryTransferResultV1> RefuseClosedWindowAsync(
        Scope scope,
        Guid transferId,
        CancelInventoryTransferDispatchV1 request,
        InventoryTransferCancelClosureBasis basis,
        DateTimeOffset closedAtUtc,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.RecordDispatchCancelClosureAsync(transferId, request, basis, closedAtUtc, metadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } winner)
        {
            return winner;
        }

        throw new InventoryTransferException(409, InventoryTransferCodesV1.CancelWindowClosed);
    }

    private async Task<InventoryTransferDispatchCancelContextV1> ReadDispatchCancelContextAsync(
        Scope scope,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        InventoryTransferDispatchCancelContextV1? context;
        try
        {
            context = await writer.ReadDispatchCancelContextAsync(transferId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        scope = await MeasureAsync(scope, cancellationToken);
        if (context is null)
        {
            throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (context.CompanyId != scope.CompanyId || context.TransferId != transferId)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        return context;
    }

    /// <summary>
    /// The Settings-owned "kantardan geçer" value for the source warehouse. A missing port, a failing read or an
    /// undefined answer is <see cref="InventoryTransferScaleSettingV1.Unreadable"/>: never a chosen value.
    /// </summary>
    private async Task<InventoryTransferScaleSettingV1> ReadSourceScaleSettingAsync(
        Scope scope,
        Guid sourceLocationId,
        CancellationToken cancellationToken)
    {
        if (sourceScaleSetting is null)
        {
            return InventoryTransferScaleSettingV1.Unreadable;
        }

        InventoryTransferScaleSettingV1 value;
        try
        {
            value = await sourceScaleSetting.ReadAsync(scope.CompanyId, sourceLocationId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            value = InventoryTransferScaleSettingV1.Unreadable;
        }

        await MeasureAsync(scope, cancellationToken);
        return Enum.IsDefined(value) ? value : InventoryTransferScaleSettingV1.Unreadable;
    }

    private async Task<InventoryTransferViewV1> ReadTransferAsync(
        Scope scope,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        InventoryTransferViewV1? transfer;
        try
        {
            transfer = await reader.ReadAsync(transferId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        scope = await MeasureAsync(scope, cancellationToken);
        if (transfer is null)
        {
            throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (transfer.CompanyId != scope.CompanyId)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        return transfer;
    }

    /// <summary>
    /// Asks the typed outgoing-document proof port inside the permission fence. Only <c>CurrentEffective</c> with the
    /// exact requested binding passes; every other outcome is an explicit refusal and nothing is written for it here.
    /// </summary>
    private async Task<(InventoryTransferOutgoingDocumentOwnerSnapshotV1? Effective, InventoryTransferException? Refusal)>
        RequireCurrentEffectiveOutgoingDocumentAsync(
            Scope scope,
            InventoryTransferOutgoingDocumentProofQueryV1 query,
            CancellationToken cancellationToken)
    {
        if (outgoingDocumentProof is null)
        {
            // Composition gap: without the Logistics owner's proof no dispatch can be proved.
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        InventoryTransferOutgoingDocumentProofV1? proof;
        try
        {
            proof = await outgoingDocumentProof.ReadAsync(query, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        await MeasureAsync(scope, cancellationToken);
        if (proof is null)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        if (proof.Outcome == InventoryTransferDocumentProofOutcomeV1.CurrentEffective)
        {
            var effective = proof.EffectiveBinding;
            return effective is not null
                && effective.CompanyId == query.CompanyId
                && effective.TransferId == query.TransferId
                && effective.DocumentId == query.DocumentId
                && effective.DocumentVersion == query.ExpectedDocumentVersion
                && effective.OrderedTransferLineIds.SequenceEqual(query.OrderedTransferLineIds)
                ? (effective, null)
                : throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        return (null, OutgoingDocumentRefusal(proof.Outcome));
    }

    /// <summary>The closed outcome-to-problem map; an unknown outcome fails closed as unavailable.</summary>
    internal static InventoryTransferException OutgoingDocumentRefusal(InventoryTransferDocumentProofOutcomeV1 outcome) =>
        outcome switch
        {
            InventoryTransferDocumentProofOutcomeV1.Missing =>
                new(422, InventoryTransferCodesV1.OutgoingDocumentNotFound),
            InventoryTransferDocumentProofOutcomeV1.BindingMismatch =>
                new(422, InventoryTransferCodesV1.OutgoingDocumentBindingMismatch),
            InventoryTransferDocumentProofOutcomeV1.Draft =>
                new(409, InventoryTransferCodesV1.OutgoingDocumentNotFinal),
            InventoryTransferDocumentProofOutcomeV1.Cancelled =>
                new(409, InventoryTransferCodesV1.OutgoingDocumentCancelled),
            InventoryTransferDocumentProofOutcomeV1.Reversed =>
                new(409, InventoryTransferCodesV1.OutgoingDocumentReversed),
            InventoryTransferDocumentProofOutcomeV1.Stale =>
                new(409, InventoryTransferCodesV1.OutgoingDocumentVersionConflict),
            _ => new(503, InventoryTransferCodesV1.Unavailable),
        };

    public async Task<InventoryTransferResultV1> AcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferWrite, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayAcceptLineAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        InventoryTransferViewV1? transfer;
        try
        {
            transfer = await reader.ReadAsync(transferId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        scope = await MeasureAsync(scope, cancellationToken);
        if (transfer is null)
        {
            throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
        }
        if (transfer.CompanyId != scope.CompanyId)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        await RequireDestinationAsync(scope, transfer.DestinationLocationId, transfer.DestinationCustodianKind,
            transfer.DestinationCustodianId, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.AcceptLineAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    private async Task RequireDestinationAsync(
        Scope scope,
        Guid locationId,
        string custodianKind,
        Guid custodianId,
        CancellationToken cancellationToken)
    {
        InventoryTransferDestinationStatusV1 destinationStatus;
        try
        {
            destinationStatus = await destination.CheckAsync(scope.CompanyId, locationId, custodianKind,
                custodianId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        await MeasureAsync(scope, cancellationToken);
        if (destinationStatus != InventoryTransferDestinationStatusV1.Active)
        {
            throw destinationStatus switch
            {
                InventoryTransferDestinationStatusV1.NotFound =>
                    new InventoryTransferException(422, InventoryTransferCodesV1.DestinationNotFound),
                InventoryTransferDestinationStatusV1.Inactive =>
                    new InventoryTransferException(422, InventoryTransferCodesV1.DestinationInactive),
                InventoryTransferDestinationStatusV1.ContextMismatch =>
                    new InventoryTransferException(422, InventoryTransferCodesV1.DestinationContextMismatch),
                _ => new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable),
            };
        }
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryTransferException(401, InventoryTransferCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        if (!isKnownPermission(permission))
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.PermissionUnregistered);
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId, permission, []);
        return await MeasureAsync(scope, cancellationToken);
    }

    private async Task<InventoryCommandMetadata> FinalizeAsync(
        Scope scope,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!string.IsNullOrEmpty(transport.ActorId)
            && !string.Equals(transport.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var finalScope = await MeasureAsync(scope, cancellationToken);
        return transport with
        {
            ActorId = finalScope.ActorId,
            PermissionCode = finalScope.Permission,
            RoleCodes = finalScope.RoleCodes,
        };
    }

    private async Task<Scope> MeasureAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope)) throw Forbidden();
        var measured = await authority.RevalidatePermissionScopeAsync(
            scope.Permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope)) throw Forbidden();
        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    private async Task FreshAuthorityFenceAsync(Scope scope, CancellationToken cancellationToken) =>
        _ = await MeasureAsync(scope, cancellationToken);

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryTransferException Forbidden() =>
        new(403, InventoryTransferCodesV1.Forbidden);

    private sealed record Scope(
        Guid CompanyId,
        string ActorId,
        string Permission,
        IReadOnlyCollection<string> RoleCodes);
}
