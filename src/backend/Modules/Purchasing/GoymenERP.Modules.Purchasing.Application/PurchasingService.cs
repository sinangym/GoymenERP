using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// The purchasing use cases (PUR-4a). The route binds nothing but the server context; this service
/// decides, the Sales draft service's way:
/// <list type="bullet">
/// <item><b>Authority</b>: a fresh central measurement of the Purchasing name (#276) before anything is
/// read (ADR-011 §5), after every awaited owner read, and right before the single save; none after the
/// commit (S03).</item>
/// <item><b>Replay</b> (§2–§4, §7): the attempt of (company, command, key hash) answers first.</item>
/// <item><b>Owners, in order</b> (an order): the Cari supplier account (party + account, active, this
/// company, Supplier role) → the SUBMITTED source request of this company, whose lines a bound line must
/// match (product, unit) → per line the Product owner's purchase eligibility (fail-closed 503 until the
/// Product owner publishes it, ÜRÜN #288 P3-3) → the Settings kilogram profile through
/// <see cref="PurchaseUnitVerifier"/> (Ö2 scale, never rounded). This is the first command handler, so the
/// verifier-before-line contract becomes an invariant here (README PUR-3 dependency_wait).</item>
/// <item><b>Terminal refusals</b> (§7): a stale version, a wrong state, a stale product version and the
/// Σ-shares rule at approval (Sinan #66 5738113623 §1) are recorded under the key and replayed.</item>
/// <item><b>Not here</b>: nothing physical (receipt, kantar, stock-in are PUR-4b + Inventory T09), no
/// price (T28), no approval policy (§8), no re-approval after a breakdown (§8).</item>
/// </list>
/// </summary>
public sealed partial class PurchasingService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    IPurchasingPartyAccountPortV1 accounts,
    IProductPurchaseEligibilityPortV1? products,
    PurchaseUnitVerifier units,
    IPurchasingStore store,
    PurchasingServiceBindings bindings,
    TimeProvider timeProvider,
    IPurchaseGoodsReceiptTransactionV1? goodsReceipts = null,
    IPurchaseGoodsReceiptReadsV1? receiptReads = null) : IPurchasingService, IPurchaseGoodsReceiptServiceV1, IPurchaseGoodsReceiptQuantityComparisonServiceV1
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int MaximumCursorLength = 8192;
    private const int MaximumProductVersionLength = 64;
    // decimal(18,3): at most 15 integer digits; a wider text is refused at the wire, never a provider exception at the save (FİNANS #350 P3-2).
    private const int MaximumIntegerDigits = 15;

    // ---- requests -------------------------------------------------------------------------------

    public async Task<PurchaseRequestResponse> CreateRequestAsync(CreatePurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);
        var requestHash = PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, request);
        var envelope = Envelope(scope, metadata, bindings.CreatePermission!);
        var replay = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, PurchasingCommands.RequestCreateV1, cancellationToken);
        if (replay is not null)
        {
            return ReplayedRequest(replay);
        }

        var lines = new List<(PurchaseRequestLineRequest Line, string ProductVersion, Quantity Quantity)>(request.Lines.Count);
        try
        {
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var line = request.Lines[index];
                var productVersion = await ReadProductAsync(scope, envelope, requestHash, PurchasingCommands.RequestCreateV1, line.ProductId,
                    line.ExpectedProductVersion, line.UnitId, request.EffectiveOn, index, cancellationToken);
                var quantity = await VerifyUnitAsync(scope, bindings.CreatePermission, line.UnitId, line.Quantity, request.EffectiveOn, $"lines[{index}]",
                    cancellationToken);
                lines.Add((line, productVersion, quantity));
            }
        }
        catch (ReplayWinnerException winner)
        {
            return ReplayedRequest(winner.Replay);
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, bindings.CreatePermission, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var versions = lines.ToDictionary(item => item.Line.LineId, item => item.ProductVersion);
        PurchasingRequestStoreResult stored;
        try
        {
            stored = await store.StoreRequestAsync(envelope, requestHash, request.EffectiveOn, () =>
            {
                var purchaseRequest = PurchaseRequest.Create(Guid.NewGuid(), scope.CompanyId, now);
                foreach (var (line, _, quantity) in lines)
                {
                    purchaseRequest.AddLine(purchaseRequest.Version, line.LineId, line.ProductId, line.UnitId, quantity, now);
                }

                return purchaseRequest;
            }, versions, cancellationToken);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("request", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, PurchasingCommands.RequestCreateV1, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedRequest(won);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return RequestResponse(stored.Request, stored.Outcome == PurchasingWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    public async Task<PurchaseRequestResponse> SubmitRequestAsync(Guid requestId, SubmitPurchaseRequestRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var requestHash = PurchasingRequestHash.Compute(PurchasingCommands.RequestSubmitV1, requestId, request);
        var envelope = Envelope(scope, metadata, bindings.CreatePermission!);
        var replay = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, PurchasingCommands.RequestSubmitV1, cancellationToken);
        if (replay is not null)
        {
            return ReplayedRequest(replay);
        }

        if (requestId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        await RequireStillAsync(scope, bindings.CreatePermission, cancellationToken);
        PurchasingRequestStoreResult? result;
        try
        {
            result = await store.MutateRequestAsync(envelope, requestHash, PurchasingCommands.RequestSubmitV1, requestId,
                (purchaseRequest, now) => purchaseRequest.Submit(expectedVersion, now),
                new PurchasingMutationAudit("purchasing.purchase-request.submitted.v1", "Submit", "Satınalma talebi gönderildi"), cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await TerminalAsync(envelope, requestHash, PurchasingCommands.RequestSubmitV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")]), ReplayedRequest, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.RequestNotDraft)
        {
            return await TerminalAsync(envelope, requestHash, PurchasingCommands.RequestSubmitV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.RequestNotDraft, [new("status", exception.Code)]), ReplayedRequest, cancellationToken);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("request", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, PurchasingCommands.RequestSubmitV1, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedRequest(won);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new PurchasingServiceException(404, "not_found")
            : RequestResponse(result.Request, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    public async Task<PurchaseRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (requestId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        StoredPurchaseRequest? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindRequestAsync(requestId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return stored is null ? throw new PurchasingServiceException(404, "not_found") : RequestDetail(stored);
    }

    public async Task<PurchaseRequestListResponse> GetRequestListAsync(PurchaseRequestListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var pageSize = RequirePage(query.PageSize, query.Cursor);
        var status = ParseStatus<PurchaseRequestStatus>(query.Status);
        PurchasingRequestPage page;
        try
        {
            page = await GuardedAsync(scope, bindings.ReadPermission, () => store.ListRequestsAsync(pageSize, query.Cursor, status, cancellationToken),
                cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return new PurchaseRequestListResponse([.. page.Items.Select(RequestSummary)], page.NextCursor);
    }

    // ---- orders ---------------------------------------------------------------------------------

    public async Task<PurchaseOrderResponse> CreateOrderAsync(CreatePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);
        var requestHash = PurchasingRequestHash.Compute(PurchasingCommands.OrderCreateV1, request);
        var envelope = Envelope(scope, metadata, bindings.CreatePermission!);
        var replay = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, PurchasingCommands.OrderCreateV1, cancellationToken);
        if (replay is not null)
        {
            return ReplayedOrder(replay);
        }

        await RequireSupplierAsync(scope, bindings.CreatePermission, request.SupplierPartyId, request.CompanyAccountId, cancellationToken);
        var source = request.SourceRequestId is { } sourceRequestId
            ? await ReadSourceRequestAsync(scope, request, sourceRequestId, cancellationToken)
            : null;
        var lines = new List<(PurchaseOrderLineRequest Line, string ProductVersion, Quantity Quantity)>(request.Lines.Count);
        try
        {
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var line = request.Lines[index];
                var productVersion = await ReadProductAsync(scope, envelope, requestHash, PurchasingCommands.OrderCreateV1, line.ProductId,
                    line.ExpectedProductVersion, line.UnitId, request.EffectiveOn, index, cancellationToken);
                var quantity = await VerifyUnitAsync(scope, bindings.CreatePermission, line.UnitId, line.Quantity, request.EffectiveOn, $"lines[{index}]",
                    cancellationToken);
                lines.Add((line, productVersion, quantity));
            }
        }
        catch (ReplayWinnerException winner)
        {
            return ReplayedOrder(winner.Replay);
        }

        await RequireStillAsync(scope, bindings.CreatePermission, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var versions = lines.ToDictionary(item => item.Line.LineId, item => item.ProductVersion);
        PurchasingOrderStoreResult stored;
        try
        {
            stored = await store.StoreOrderAsync(envelope, requestHash, request.CompanyAccountId, request.EffectiveOn, () =>
            {
                var order = source is { } sourced
                    ? PurchaseOrder.CreateForRequest(Guid.NewGuid(), scope.CompanyId, request.SupplierPartyId, sourced.Request.Id, now)
                    : PurchaseOrder.Create(Guid.NewGuid(), scope.CompanyId, request.SupplierPartyId, now);
                foreach (var (line, _, quantity) in lines)
                {
                    order.AddLine(order.Version, line.LineId, line.ProductId, line.UnitId, quantity, line.SourceRequestLineId, now);
                }

                return order;
            }, versions, cancellationToken);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("order", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, PurchasingCommands.OrderCreateV1, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedOrder(won);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return OrderResponse(stored.Order, stored.Outcome == PurchasingWriteOutcome.Replayed, stored.OriginalCorrelationId);
    }

    public async Task<PurchaseOrderResponse> ApproveOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ApprovePermission, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var requestHash = PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, orderId, request);
        var envelope = Envelope(scope, metadata, bindings.ApprovePermission!);
        var replay = await FindReplayAsync(scope, bindings.ApprovePermission, envelope, requestHash, PurchasingCommands.OrderApproveV1, cancellationToken);
        if (replay is not null)
        {
            return ReplayedOrder(replay);
        }

        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        await RequireStillAsync(scope, bindings.ApprovePermission, cancellationToken);
        PurchasingOrderStoreResult? result;
        try
        {
            result = await store.MutateOrderAsync(envelope, requestHash, PurchasingCommands.OrderApproveV1, orderId,
                (order, now) => order.Approve(expectedVersion, now),
                new PurchasingMutationAudit("purchasing.purchase-order.approved.v1", "Approve", "Satınalma siparişi onaylandı"), cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await TerminalAsync(envelope, requestHash, PurchasingCommands.OrderApproveV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")]), ReplayedOrder, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.OrderNotDraft)
        {
            return await TerminalAsync(envelope, requestHash, PurchasingCommands.OrderApproveV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.OrderState, [new("status", exception.Code)]), ReplayedOrder, cancellationToken);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("order", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.RequestLineOverSourced)
        {
            // Sinan 5738113623 §1: the shares of a request line never exceed its need; terminal for this key (§7).
            return await TerminalAsync(envelope, requestHash, PurchasingCommands.OrderApproveV1,
                new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.RequestLineOverSourced,
                    exception.Errors.Count > 0 ? exception.Errors : [new("lines", exception.Code)]), ReplayedOrder, cancellationToken);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, bindings.ApprovePermission, envelope, requestHash, PurchasingCommands.OrderApproveV1, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedOrder(won);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new PurchasingServiceException(404, "not_found")
            : OrderResponse(result.Order, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    public async Task<PurchaseOrderDetailView> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        StoredPurchaseOrder? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return stored is null ? throw new PurchasingServiceException(404, "not_found") : OrderDetail(stored);
    }

    public async Task<PurchaseOrderListResponse> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var pageSize = RequirePage(query.PageSize, query.Cursor);
        var status = ParseStatus<PurchaseOrderStatus>(query.Status);
        PurchasingOrderPage page;
        try
        {
            page = await GuardedAsync(scope, bindings.ReadPermission, () => store.ListOrdersAsync(pageSize, query.Cursor, status, cancellationToken),
                cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return new PurchaseOrderListResponse([.. page.Items.Select(OrderSummary)], page.NextCursor);
    }

    // ---- owners ---------------------------------------------------------------------------------

    private async Task RequireSupplierAsync(Scope scope, string? permission, Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        var answer = await GuardedAsync(scope, permission, () => accounts.GetAuthorizedAsync(partyId, companyAccountId, cancellationToken), cancellationToken);
        switch (answer.Outcome)
        {
            case PurchasingPartyAccountOutcomeV1.Found when answer.Account is { } account:
                if (account.PartyId != partyId || account.CompanyAccountId != companyAccountId)
                {
                    throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
                }

                if (account.CompanyId != scope.CompanyId)
                {
                    throw new PurchasingServiceException(409, PurchasingProblemCodesV1.AccountState, [new("companyAccountId", "company_mismatch")]);
                }

                if (!account.PartyIsActive || !account.AccountIsActive)
                {
                    throw new PurchasingServiceException(409, PurchasingProblemCodesV1.AccountState, [new("companyAccountId", "passive_company_account")]);
                }

                if (!account.AccountHasSupplierRole)
                {
                    throw new PurchasingServiceException(409, PurchasingProblemCodesV1.AccountState, [new("companyAccountId", "not_supplier")]);
                }

                return;
            case PurchasingPartyAccountOutcomeV1.NotFound:
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.AccountState,
                    [new("supplierPartyId", "invalid_reference"), new("companyAccountId", "invalid_reference")]);
            case PurchasingPartyAccountOutcomeV1.Denied:
                throw new PurchasingServiceException(403, "forbidden");
            default:
                throw new PurchasingServiceException(503, PurchasingProblemCodesV1.CounterpartyProviderUnavailable);
        }
    }

    /// <summary>Only a SUBMITTED request of this company is a need to source; a bound line keeps the request line's product and unit (multi-sourcing splits quantity, nothing else).</summary>
    private async Task<StoredPurchaseRequest> ReadSourceRequestAsync(Scope scope, CreatePurchaseOrderRequest request, Guid sourceRequestId,
        CancellationToken cancellationToken)
    {
        StoredPurchaseRequest? stored;
        try
        {
            stored = await GuardedAsync(scope, bindings.CreatePermission, () => store.FindRequestAsync(sourceRequestId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        if (stored is null)
        {
            throw new PurchasingServiceException(409, PurchasingProblemCodesV1.RequestState, [new("sourceRequestId", "invalid_reference")]);
        }

        if (stored.Request.CompanyId != scope.CompanyId)
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
        }

        if (stored.Request.Status != PurchaseRequestStatus.Submitted)
        {
            throw new PurchasingServiceException(409, PurchasingProblemCodesV1.RequestState,
                [new("sourceRequestId", "not_submitted"), new("sourceRequestStatus", stored.Request.Status.ToString())]);
        }

        var errors = new List<PurchasingFieldError>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line.SourceRequestLineId is not { } sourceLineId)
            {
                continue;
            }

            var requestLine = stored.Request.Lines.FirstOrDefault(candidate => candidate.Id == sourceLineId);
            if (requestLine is null)
            {
                errors.Add(new($"lines[{index}].sourceRequestLineId", "invalid_reference"));
                continue;
            }

            if (requestLine.ProductId != line.ProductId)
            {
                errors.Add(new($"lines[{index}].productId", "source_line_mismatch"));
            }

            if (requestLine.UnitId != line.UnitId)
            {
                errors.Add(new($"lines[{index}].unitId", "source_line_mismatch"));
            }
        }

        return errors.Count == 0 ? stored : throw new PurchasingServiceException(409, PurchasingProblemCodesV1.RequestState, errors);
    }

    /// <summary>The Product owner's answer for one line; a stale product version is the terminal refusal of ADR-011 §7 (the Sales shape).</summary>
    private async Task<string> ReadProductAsync(Scope scope, PurchasingWriteEnvelope envelope, string requestHash, string commandName, Guid productId,
        string expectedProductVersion, Guid unitId, DateOnly effectiveOn, int index, CancellationToken cancellationToken)
    {
        if (products is null)
        {
            // ÜRÜN #288 P3-3: purchase eligibility is not published; fail-closed, exactly as Sales before IProductSalesEligibilityV1.
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ProductEligibilityUnavailable);
        }

        ProductPurchaseEligibilityAnswerV1 answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.CreatePermission,
                () => products.EvaluateAsync(new ProductPurchaseSelectionV1(productId, expectedProductVersion, unitId, effectiveOn), cancellationToken),
                cancellationToken);
        }
        catch (PurchasingServiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ProductEligibilityUnavailable);
        }

        switch (answer.Outcome)
        {
            case ProductPurchaseEligibilityOutcomeV1.Eligible:
                if (string.IsNullOrWhiteSpace(answer.ProductVersion) || answer.ProductVersion.Length > MaximumProductVersionLength
                    || !string.Equals(answer.ProductVersion, answer.ProductVersion.Trim(), StringComparison.Ordinal))
                {
                    throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
                }

                return answer.ProductVersion;
            case ProductPurchaseEligibilityOutcomeV1.Refused:
                var errors = (answer.Errors ?? []).Where(error => error is not null && !string.IsNullOrWhiteSpace(error.Field) && !string.IsNullOrWhiteSpace(error.Code))
                    .Select(error => new PurchasingFieldError($"lines[{index}].{error.Field}", error.Code)).ToArray();
                if (errors.Length == 0)
                {
                    throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
                }

                if (errors.Any(error => error.Code == "version_conflict"))
                {
                    return await TerminalAsync<string>(envelope, requestHash, commandName,
                        new PurchasingTerminalRefusal(409, PurchasingProblemCodesV1.VersionConflict, errors),
                        replay => throw new ReplayWinnerException(replay), cancellationToken);
                }

                throw new PurchasingServiceException(422, PurchasingProblemCodesV1.UnsupportedProduct, errors);
            case ProductPurchaseEligibilityOutcomeV1.Denied:
                throw new PurchasingServiceException(403, "forbidden");
            default:
                throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ProductEligibilityUnavailable);
        }
    }

    private async Task<Quantity> VerifyUnitAsync(Scope scope, string? permission, Guid unitId, string quantityText, DateOnly effectiveOn,
        string field, CancellationToken cancellationToken)
    {
        var value = ReadQuantityText(quantityText, field);
        var verification = await GuardedAsync(scope, permission, () => units.VerifyAsync(scope.CompanyId, unitId, value, effectiveOn, cancellationToken),
            cancellationToken);
        if (verification.Unit is { } unit)
        {
            if (unit.UnitId != unitId)
            {
                throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
            }

            return unit.Quantity;
        }

        throw verification.Code switch
        {
            "settings_access_denied" => new PurchasingServiceException(403, "forbidden"),
            "settings_provider_unavailable" => new PurchasingServiceException(503, "settings_provider_unavailable"),
            "settings_profile_invalid" => new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid),
            _ => new PurchasingServiceException(422, PurchasingProblemCodesV1.UnsupportedUnit,
                [new($"{field}.unitId", verification.Code ?? PurchasingProblemCodesV1.UnsupportedUnit)]),
        };
    }

    /// <summary>
    /// Ö2 "never rounded" (FİNANS #350 P2-1, the same pattern here): the text is validated lexically FIRST — digits and one inner '.',
    /// a non-zero digit, at most the kilogram cap of decimals (Settings' <see cref="ExactPositiveDecimalV1"/>, the Finance / Inventory
    /// shape) and at most 15 integer digits (the column) — and only the validated text is converted, so a text with more digits than a
    /// decimal holds can never round ("2.9999999999999999999999999999999" is refused, it never becomes 3). The owner's profile scale
    /// (≤ 3) is then measured by the verifier on the exact value.
    /// </summary>
    private static decimal ReadQuantityText(string quantityText, string field)
    {
        try
        {
            ExactPositiveDecimalV1.Parse(quantityText, Quantity.KilogramScale);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == "settings_decimal_scale_exceeded")
        {
            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.UnsupportedUnit, [new($"{field}.unitId", PurchaseUnitCodes.QuantityScaleExceeded)]);
        }
        catch (SettingsPrerequisiteException)
        {
            throw new PurchasingServiceException(400, "validation_failed", [new($"{field}.quantity", "invalid_decimal_text")]);
        }

        var point = quantityText.IndexOf('.', StringComparison.Ordinal);
        var integerPart = point < 0 ? quantityText : quantityText[..point];
        if (integerPart.TrimStart('0').Length > MaximumIntegerDigits)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new($"{field}.quantity", PurchasingStoreCodes.ValueDoesNotFit)]);
        }

        // At most 18 significant digits by now: the conversion is exact.
        return decimal.Parse(quantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    // ---- replay / terminal ----------------------------------------------------------------------

    private async Task<PurchasingReplay?> FindReplayAsync(Scope scope, string? permission, PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(scope, permission, () => store.FindReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    private static PurchaseRequestResponse ReplayedRequest(PurchasingReplay replay)
    {
        ThrowIfRefusal(replay);
        return replay.Request is { } request
            ? RequestResponse(request, isReplay: true, replay.OriginalCorrelationId)
            : throw new PurchasingServiceException(503, "service_unavailable");
    }

    private static PurchaseOrderResponse ReplayedOrder(PurchasingReplay replay)
    {
        ThrowIfRefusal(replay);
        return replay.Order is { } order
            ? OrderResponse(order, isReplay: true, replay.OriginalCorrelationId)
            : throw new PurchasingServiceException(503, "service_unavailable");
    }

    private static void ThrowIfRefusal(PurchasingReplay replay)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true, originalCorrelationId: replay.OriginalCorrelationId);
        }
    }

    /// <summary>
    /// §7: the terminal refusal is recorded before it is answered. §8: if the same key was taken meanwhile, the winner's record is
    /// the answer — its refusal replayed, or its stored record returned through <paramref name="winner"/> as a replay (FİNANS #350
    /// P3-1, the same pattern here: never <c>concurrent_change</c> for a loser that lands on a terminal path after the winner committed).
    /// </summary>
    private async Task<TResponse> TerminalAsync<TResponse>(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
        PurchasingTerminalRefusal refusal, Func<PurchasingReplay, TResponse> winner, CancellationToken cancellationToken)
    {
        try
        {
            await store.RecordRefusalAsync(envelope, requestHash, commandName, refusal, cancellationToken);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            PurchasingReplay? won;
            try
            {
                won = await store.FindReplayAsync(envelope, requestHash, commandName, cancellationToken);
            }
            catch (PurchasingStoreException raced)
            {
                throw Store(raced);
            }

            if (won?.Refusal is { } recorded)
            {
                throw new PurchasingServiceException(recorded.Status, recorded.Code, recorded.Errors, isReplay: true,
                    originalCorrelationId: won.OriginalCorrelationId);
            }

            if (won is { Request: not null } or { Order: not null })
            {
                return winner(won);
            }
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    /// <summary>A create's stale-product-version refusal lost the key to a winner that stored the record (§8): the create answers that record as a replay.</summary>
    private sealed class ReplayWinnerException(PurchasingReplay replay) : Exception("§8 winner")
    {
        public PurchasingReplay Replay { get; } = replay;
    }

    private static PurchasingServiceException Store(PurchasingStoreException exception) => exception.Code switch
    {
        PurchasingStoreCodes.SameKeyPayloadMismatch => new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch),
        PurchasingStoreCodes.ConcurrentChange => new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange),
        PurchasingStoreCodes.RequestLineOverSourced => new PurchasingServiceException(409, PurchasingProblemCodesV1.RequestLineOverSourced, exception.Errors),
        PurchasingStoreCodes.InvalidCursor => new PurchasingServiceException(400, PurchasingProblemCodesV1.InvalidCursor),
        PurchasingStoreCodes.CompanyContextMismatch => new PurchasingServiceException(403, "forbidden"),
        PurchasingStoreCodes.ValueDoesNotFit => new PurchasingServiceException(422, "validation_failed", [new("record", exception.Code)]),
        _ => new PurchasingServiceException(503, "service_unavailable"),
    };

    // ---- authority ------------------------------------------------------------------------------

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    private async Task<Scope> RequireAsync(string? permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new PurchasingServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new PurchasingServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new PurchasingServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string? permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new PurchasingServiceException(403, "forbidden");
        }
    }

    private async Task<T> GuardedAsync<T>(Scope scope, string? permission, Func<Task<T>> read, CancellationToken cancellationToken)
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

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == companyId && currentUser.CanAccessCompany(companyId);

    private PurchasingWriteEnvelope Envelope(Scope scope, PurchasingCommandMetadata metadata, string permission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    // ---- parsing --------------------------------------------------------------------------------

    private static long ParseVersion(string expectedVersionText)
    {
        if (!long.TryParse(expectedVersionText, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion) || expectedVersion < 1)
        {
            throw new PurchasingServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        return expectedVersion;
    }

    private static int RequirePage(int requested, string? cursor)
    {
        var pageSize = requested == 0 ? DefaultPageSize : requested;
        if (pageSize is < 1 or > MaximumPageSize || cursor?.Length > MaximumCursorLength || (cursor is not null && string.IsNullOrWhiteSpace(cursor)))
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }

        return pageSize;
    }

    private static TStatus? ParseStatus<TStatus>(string? text) where TStatus : struct, Enum
    {
        if (text is null)
        {
            return null;
        }

        if (!Enum.TryParse<TStatus>(text, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)
            || !string.Equals(parsed.ToString(), text, StringComparison.Ordinal))
        {
            throw new PurchasingServiceException(400, "validation_failed", [new("status", "validation_failed")]);
        }

        return parsed;
    }

    // ---- mapping --------------------------------------------------------------------------------

    private static string Text(Quantity quantity) => quantity.Value.ToString(CultureInfo.InvariantCulture);

    private static string VersionOf(IReadOnlyDictionary<Guid, string> versions, Guid lineId) =>
        versions.TryGetValue(lineId, out var version) ? version : string.Empty;

    private static PurchaseRequestResponse RequestResponse(StoredPurchaseRequest stored, bool isReplay, string originalCorrelationId) =>
        new(RequestDetail(stored), isReplay, originalCorrelationId);

    private static PurchaseRequestSummaryView RequestSummary(StoredPurchaseRequest stored)
    {
        var request = stored.Request;
        return new PurchaseRequestSummaryView(request.Id, request.CompanyId, request.Status.ToString(), request.Version.ToString(CultureInfo.InvariantCulture),
            stored.EffectiveOn, request.CreatedAtUtc, request.UpdatedAtUtc, request.Lines.Count);
    }

    private static PurchaseRequestDetailView RequestDetail(StoredPurchaseRequest stored) =>
        new(RequestSummary(stored),
            [.. stored.Request.Lines.Select((line, index) => new PurchaseRequestLineView(line.Id, index + 1, line.ProductId,
                VersionOf(stored.ProductVersionsByLineId, line.Id), line.UnitId, Text(line.Quantity)))]);

    private static PurchaseOrderResponse OrderResponse(StoredPurchaseOrder stored, bool isReplay, string originalCorrelationId) =>
        new(OrderDetail(stored), isReplay, originalCorrelationId);

    private static PurchaseOrderSummaryView OrderSummary(StoredPurchaseOrder stored)
    {
        var order = stored.Order;
        return new PurchaseOrderSummaryView(order.Id, order.CompanyId, order.Status.ToString(), order.Version.ToString(CultureInfo.InvariantCulture),
            order.SupplierPartyId, stored.CompanyAccountId, order.SourceRequestId, stored.EffectiveOn, order.CreatedAtUtc, order.UpdatedAtUtc, order.Lines.Count);
    }

    private static PurchaseOrderDetailView OrderDetail(StoredPurchaseOrder stored) =>
        new(OrderSummary(stored),
            [.. stored.Order.Lines.Select((line, index) => new PurchaseOrderLineView(line.Id, index + 1, line.Kind.ToString(), line.ParentLineId,
                line.SourceRequestLineId, line.ProductId, VersionOf(stored.ProductVersionsByLineId, line.Id), line.UnitId, Text(line.Quantity),
                Text(line.TechnicalQuantity), Text(line.CommittedQuantity), Text(line.AcceptedQuantity), Text(line.RemainingQuantity)))])
        {
            Acceptances = [.. stored.Order.Acceptances.Select(acceptance => new PurchaseOrderAcceptanceHistoryView(
                acceptance.AcceptanceId, acceptance.LineId, Text(acceptance.Quantity), acceptance.RecordedAtUtc))],
        };
}
