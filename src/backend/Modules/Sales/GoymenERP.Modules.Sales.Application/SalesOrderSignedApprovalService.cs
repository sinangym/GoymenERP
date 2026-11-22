using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.SignedApprovals;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// WP13 (YOL-D11, DALGA_PLANI_v2 1.14) — the customer's signed approval of a sales order. The K3 skeleton of the S-11 contract service:
/// the operation's own name is measured before anything is read, after every awaited owner read and right before the single save; the
/// replay comes before any read; one save writes the record, the supersession of the previous active one, the attempt and the audit
/// events; a terminal refusal (order not approved, stale order version, the same document again, a passive record) is recorded under
/// the key and answered again on retry.
/// <list type="bullet">
/// <item><b>Record</b> (SAT-D471: <c>sales.orders.customer-acceptance.record</c>; no separate name): the order must be open and its current revision internally
/// approved, at the version the person saw. The document is checked by the Documents owner through
/// <see cref="ISalesOrderSignedApprovalDocumentPortV1"/> (a clean PDF/PNG/JPEG uploaded to this order, under the actor's own vault
/// read); its current version and SHA-256 are pinned on the record. A newer record supersedes the active one (nothing is deleted).</item>
/// <item><b>Withdraw</b> (same name): the active record becomes passive with a reason; the file stays in the vault.</item>
/// <item><b>Read</b> (<c>sales.drafts.read</c>): the order's records and whether the current revision is covered.</item>
/// </list>
/// SAT-D471: when the current revision still awaits the customer's acceptance, the record PERFORMS that acceptance once, through
/// the draft service's own acceptance command (<see cref="ISalesOrderCustomerAcceptanceWithSignedApprovalV1"/>: its fences, the
/// accepted revision's demand reduction, attempt and audit), with the record staged into the same save. An already accepted revision
/// (or a pre-Q9 order that seeks no acceptance) is recorded alone: no second acceptance is written. A revision the customer rejected
/// is refused. Withdrawing the record never undoes the acceptance (the existing acceptance correction paths apply). No amount moves.
/// </summary>
public sealed class SalesOrderSignedApprovalService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ISalesOrderSignedApprovalStore store,
    TimeProvider timeProvider,
    ISalesOrderSignedApprovalDocumentPortV1? documents = null,
    ISalesOrderCustomerAcceptanceWithSignedApprovalV1? acceptance = null) : ISalesOrderSignedApprovalServiceV1
{
    private const string RecordPermission = SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord;
    private const string ReadPermission = SalesPermissionManifestV1.DraftsRead;

    private static readonly SalesDraftStateAudit RecordedAudit =
        new("sales.order-signed-approval.recorded.v1", "RecordSignedApproval", "Müşterinin imzalı onay belgesi siparişe bağlandı");

    private static readonly SalesDraftStateAudit SupersededAudit =
        new("sales.order-signed-approval.superseded.v1", "SupersedeSignedApproval", "Yeni imzalı onay belgesi öncekinin yerine geçti");

    private static readonly SalesDraftStateAudit WithdrawnAudit =
        new("sales.order-signed-approval.withdrawn.v1", "WithdrawSignedApproval", "İmzalı onay kaydı gerekçeyle pasifleştirildi");

    public async Task<SalesOrderSignedApprovalCommandResultV1> RecordAsync(Guid orderId, RecordSalesOrderSignedApprovalRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(RecordPermission, cancellationToken);
        var expectedOrderVersion = ParseVersion(request.ExpectedOrderVersion, "expectedOrderVersion");
        if (request.DocumentId == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("documentId", "validation_failed")]);
        }

        var command = SalesOrderSignedApprovalCommands.RecordV1;
        var requestHash = SalesOrderSignedApprovalHash.Compute(orderId, request);
        var envelope = Envelope(scope, RecordPermission, metadata);
        var replay = await FindReplayAsync(scope, RecordPermission, envelope, requestHash, command, cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, RecordPermission, replay, cancellationToken);
        }

        if (orderId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var order = await StoreAsync(scope, RecordPermission, () => store.FindSignedApprovalOrderAsync(orderId, cancellationToken),
            cancellationToken) ?? throw new SalesDraftServiceException(404, "not_found");
        if (!order.IsOpen || !order.IsApproved || order.CurrentRevisionRejected)
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesOrderSignedApprovalProblemCodesV1.OrderNotApproved,
                    [new("state", SalesOrderSignedApprovalProblemCodesV1.OrderNotApproved)]), cancellationToken);
        }

        if (order.Version != expectedOrderVersion)
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                    [new("expectedOrderVersion", SalesRuleCodes.VersionConflict)]), cancellationToken);
        }

        var document = await CheckDocumentAsync(scope, orderId, request.DocumentId, cancellationToken);
        var history = await StoreAsync(scope, RecordPermission,
            () => store.ListSignedApprovalsAsync(orderId, SalesOrderSignedApprovalApiV1.MaximumListed, cancellationToken), cancellationToken);
        if (history.Any(item => item.State == SalesOrderSignedApprovalState.Active
                && item.Document.DocumentId == document.DocumentId && item.Document.VersionNo == document.VersionNo
                && item.OrderRevision == order.Revision))
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent,
                    [new("documentId", SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent)]), cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        SalesOrderSignedApproval approval;
        try
        {
            approval = SalesOrderSignedApproval.Record(Guid.NewGuid(), scope.CompanyId, orderId, order.Revision, order.Version, document,
                scope.ActorId, now);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, SalesOrderSignedApprovalProblemCodesV1.DocumentInvalid, [new("documentId", exception.Code)]);
        }

        if (order.AwaitsCustomerAcceptance)
        {
            return await RecordWithAcceptanceAsync(scope, envelope, requestHash, orderId, request, metadata, approval, cancellationToken);
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, RecordPermission, cancellationToken);
        SalesOrderSignedApprovalStoreResult stored;
        try
        {
            stored = await store.StoreSignedApprovalAsync(envelope, requestHash, expectedOrderVersion, approval, RecordedAudit,
                SupersededAudit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedOrderVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesOrderSignedApprovalProblemCodesV1.OrderNotApproved,
                    [new("state", SalesOrderSignedApprovalProblemCodesV1.OrderNotApproved)]), cancellationToken);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a racing caller of the same key answers the winner; a lost active-record race is the caller's concurrent_change.
            var won = await FindReplayAsync(scope, RecordPermission, envelope, requestHash, command, cancellationToken);
            return won is null ? throw Store(exception) : await ReplayedAsync(scope, RecordPermission, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return new SalesOrderSignedApprovalCommandResultV1(await DetailAsync(scope, RecordPermission, stored.OrderId, cancellationToken),
            stored.Outcome == SalesDraftWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    /// <summary>
    /// SAT-D471: the customer acceptance and the signed approval record in ONE save, through the draft service's acceptance command
    /// (expected version = the order version the person sent). Its refusals are its own answers (recorded under its key as usual);
    /// a replayed acceptance key wrote nothing now and is refused here (the key already carried an acceptance).
    /// </summary>
    private async Task<SalesOrderSignedApprovalCommandResultV1> RecordWithAcceptanceAsync(Scope scope, SalesDraftWriteEnvelope envelope,
        string requestHash, Guid orderId, RecordSalesOrderSignedApprovalRequest request, SalesCommandMetadata metadata,
        SalesOrderSignedApproval approval, CancellationToken cancellationToken)
    {
        if (acceptance is null)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, RecordPermission, cancellationToken);
        var accepted = await acceptance.RecordCustomerAcceptanceWithAsync(orderId,
            new SalesDraftStateCommandRequest(request.ExpectedOrderVersion), metadata,
            (draft, now, token) => store.StageSignedApprovalAsync(envelope, requestHash, draft, approval, RecordedAudit, SupersededAudit,
                now, token),
            cancellationToken);
        if (accepted.IsReplay)
        {
            // The acceptance key was used before: nothing was staged now. A same-key retry of THIS record meets its own replay first.
            var won = await FindReplayAsync(scope, RecordPermission, envelope, requestHash, SalesOrderSignedApprovalCommands.RecordV1,
                cancellationToken);
            return won is null
                ? throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch)
                : await ReplayedAsync(scope, RecordPermission, won, cancellationToken);
        }

        return new SalesOrderSignedApprovalCommandResultV1(await DetailAsync(scope, RecordPermission, orderId, cancellationToken), false,
            metadata.CorrelationId, CustomerAcceptanceRecorded: true);
    }

    public async Task<SalesOrderSignedApprovalCommandResultV1> DeactivateAsync(Guid orderId, Guid approvalId,
        DeactivateSalesOrderSignedApprovalRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(RecordPermission, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion, "expectedVersion");
        var command = SalesOrderSignedApprovalCommands.DeactivateV1;
        var requestHash = SalesOrderSignedApprovalHash.Compute(orderId, approvalId, request);
        var envelope = Envelope(scope, RecordPermission, metadata);
        var replay = await FindReplayAsync(scope, RecordPermission, envelope, requestHash, command, cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, RecordPermission, replay, cancellationToken);
        }

        if (orderId == Guid.Empty || approvalId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        await RequireStillAsync(scope, RecordPermission, cancellationToken);
        SalesOrderSignedApprovalStoreResult? stored;
        try
        {
            stored = await store.DeactivateSignedApprovalAsync(envelope, requestHash, orderId, approvalId,
                (approval, now) => approval.Withdraw(expectedVersion, scope.ActorId, request.Reason, now), WithdrawnAudit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, envelope, requestHash, command, orderId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.SignedApprovalReasonRequired
                                                       or SalesRuleCodes.SignedApprovalReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, RecordPermission, envelope, requestHash, command, cancellationToken);
            return won is null ? throw Store(exception) : await ReplayedAsync(scope, RecordPermission, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return stored is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : new SalesOrderSignedApprovalCommandResultV1(await DetailAsync(scope, RecordPermission, stored.OrderId, cancellationToken),
                stored.Outcome == SalesDraftWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    public async Task<SalesOrderSignedApprovalsResponseV1> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        if (orderId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        return await DetailAsync(scope, ReadPermission, orderId, cancellationToken);
    }

    // ---- owners -------------------------------------------------------------------------------------------------------------

    /// <summary>The Documents owner's answer, under the fence; anything but Accepted is never recorded.</summary>
    private async Task<SalesOrderSignedApprovalDocument> CheckDocumentAsync(Scope scope, Guid orderId, Guid documentId,
        CancellationToken cancellationToken)
    {
        if (documents is null)
        {
            throw new SalesDraftServiceException(503, SalesOrderSignedApprovalProblemCodesV1.DocumentUnavailable);
        }

        SalesSignedApprovalDocumentCheckV1? check;
        try
        {
            check = await GuardedAsync(scope, RecordPermission,
                () => documents.CheckAsync(scope.CompanyId, orderId, documentId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftServiceException)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SalesDraftServiceException(503, SalesOrderSignedApprovalProblemCodesV1.DocumentUnavailable);
        }

        return check?.Outcome switch
        {
            SalesSignedApprovalDocumentOutcomeV1.Accepted when check.VersionNo is { } versionNo && check.Sha256 is { } sha256 =>
                new SalesOrderSignedApprovalDocument(documentId, versionNo, sha256),
            SalesSignedApprovalDocumentOutcomeV1.Denied => throw new SalesDraftServiceException(403, "forbidden"),
            SalesSignedApprovalDocumentOutcomeV1.NotFound or SalesSignedApprovalDocumentOutcomeV1.OwnerMismatch
                or SalesSignedApprovalDocumentOutcomeV1.KindNotAllowed or SalesSignedApprovalDocumentOutcomeV1.NotDownloadable =>
                throw new SalesDraftServiceException(422, SalesOrderSignedApprovalProblemCodesV1.DocumentInvalid,
                    [new("documentId", DocumentErrorCode(check.Outcome))]),
            _ => throw new SalesDraftServiceException(503, SalesOrderSignedApprovalProblemCodesV1.DocumentUnavailable),
        };
    }

    private static string DocumentErrorCode(SalesSignedApprovalDocumentOutcomeV1 outcome) => outcome switch
    {
        SalesSignedApprovalDocumentOutcomeV1.NotFound => "document_not_found",
        SalesSignedApprovalDocumentOutcomeV1.OwnerMismatch => "document_not_on_order",
        SalesSignedApprovalDocumentOutcomeV1.KindNotAllowed => "document_kind_not_allowed",
        _ => "document_not_downloadable",
    };

    private async Task<SalesOrderSignedApprovalsResponseV1> DetailAsync(Scope scope, string permission, Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await StoreAsync(scope, permission, () => store.FindSignedApprovalOrderAsync(orderId, cancellationToken),
            cancellationToken) ?? throw new SalesDraftServiceException(404, "not_found");
        var items = await StoreAsync(scope, permission,
            () => store.ListSignedApprovalsAsync(orderId, SalesOrderSignedApprovalApiV1.MaximumListed, cancellationToken), cancellationToken);
        return Detail(order, items);
    }

    /// <summary>The response: the history newest first, the active record, and whether it covers the order's current revision.</summary>
    internal static SalesOrderSignedApprovalsResponseV1 Detail(SalesOrderSignedApprovalOrderFactsV1 order,
        IReadOnlyList<SalesOrderSignedApproval> items)
    {
        var views = items.OrderByDescending(item => item.RecordedAtUtc).ThenByDescending(item => item.Id).Select(View).ToArray();
        var active = items.SingleOrDefault(item => item.State == SalesOrderSignedApprovalState.Active);
        return new SalesOrderSignedApprovalsResponseV1(order.OrderId, Text(order.Version), Revision(order.Revision),
            active is not null && active.OrderRevision == order.Revision && order.IsApproved,
            active is null ? null : View(active), views, order.CustomerAccepted);
    }

    internal static SalesOrderSignedApprovalViewV1 View(SalesOrderSignedApproval item) => new(
        item.Id, item.OrderId, Revision(item.OrderRevision), item.Document.DocumentId, item.Document.VersionNo, item.Document.Sha256,
        item.State.ToString(), Text(item.Version), item.RecordedBy, item.RecordedAtUtc, item.Deactivation?.Kind.ToString(),
        item.Deactivation?.DeactivatedBy, item.Deactivation?.DeactivatedAtUtc, item.Deactivation?.Reason,
        item.Deactivation?.SupersededById);

    private static string Revision(int revision) => "Rev." + revision.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static long ParseVersion(string? text, string field) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            && string.Equals(version.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal)
            ? version
            : throw new SalesDraftServiceException(400, "validation_failed", [new(field, "validation_failed")]);

    // ---- replay ---------------------------------------------------------------------------------------------------------------

    private SalesDraftWriteEnvelope Envelope(Scope scope, string permission, SalesCommandMetadata metadata) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    private async Task<SalesOrderSignedApprovalReplay?> FindReplayAsync(Scope scope, string permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken) =>
        await StoreAsync(scope, permission,
            () => store.FindSignedApprovalReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);

    /// <summary>ADR-011 §4/§7: the order's records as they stand now as the replay, or the recorded refusal as the same refusal.</summary>
    private async Task<SalesOrderSignedApprovalCommandResultV1> ReplayedAsync(Scope scope, string permission,
        SalesOrderSignedApprovalReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        var orderId = replay.OrderId ?? throw new SalesDraftServiceException(503, "service_unavailable");
        return new SalesOrderSignedApprovalCommandResultV1(await DetailAsync(scope, permission, orderId, cancellationToken), true,
            replay.OriginalCorrelationId);
    }

    /// <summary>ADR-011 §7: the refusal is recorded under the key and then answered; a lost record race answers the winner.</summary>
    private async Task<SalesOrderSignedApprovalCommandResultV1> TerminalAsync(Scope scope, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid orderId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        var permission = envelope.PermissionCode;
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await store.RecordSignedApprovalRefusalAsync(envelope, requestHash, commandName, orderId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won is not null)
            {
                return await ReplayedAsync(scope, permission, won, cancellationToken);
            }
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    // ---- fences -------------------------------------------------------------------------------------------------------------

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(permission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    /// <summary>An owner read is released only under the authority a success would need; a failure measures the fence first.</summary>
    private async Task<T> GuardedAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    /// <summary>A Sales store read under the fence: a store refusal is mapped; the fence is measured again after the await.</summary>
    private async Task<T> StoreAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (SalesDraftStoreException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw Store(exception);
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private static SalesDraftServiceException Store(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.CompanyContextMismatch => new SalesDraftServiceException(403, "forbidden"),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
