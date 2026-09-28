using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// AT-025: the operations work list's two SEPARATE sections (see <see cref="ILogisticsOperationsWorkService"/>). The route
/// binds nothing but the server context; this service decides, with the loading-instruction service's fences (#276):
/// <list type="bullet">
/// <item><b>Authority</b>: each section measures ITS OWN name (<see cref="LogisticsOperationsWorkBindings"/>) afresh before
/// anything is read, and again after every awaited owner read. The owners' ports carry no authority (LOG-4 D6). Holding the
/// sales name reads nothing of purchasing and vice versa.</item>
/// <item><b>Owners</b>: the SALES section reads the Sales owner's price-free approved-line page (cancelled orders are not
/// offered — DEC-SAL-005), then Logistics's own recorded totals for those order lines, then the customer's name from the
/// Cari owner; the PURCHASE section reads the Purchasing owner's approved lines awaiting receipt, then the supplier's name.
/// Every item must be the context company's and well-formed, else the owner answered malformed (503).</item>
/// <item><b>No mixing</b>: a section's page is built from its own owner only and names its section.</item>
/// <item><b>Price-free</b>: neither owner answers a price member; none is added here (Sinan §4, #66 5738113623).</item>
/// </list>
/// Reading creates nothing: no attempt, audit, share or version effect in any module.
/// </summary>
public sealed class LogisticsOperationsWorkService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ISalesOrderLineReferencePortV1 salesLines,
    IPurchaseOpenLineReadPortV1 purchaseLines,
    ILogisticsOrderLineProgressReader progress,
    ILogisticsPartyAccountPortV1 accounts,
    LogisticsOperationsWorkBindings bindings) : ILogisticsOperationsWorkService
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int MaximumCursorLength = 8192;

    public async Task<LogisticsSalesWorkPage> GetSalesWorkAsync(LogisticsOperationsWorkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.SalesWorkPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var pageSize = ValidatedPageSize(query);

        SalesApprovedLinePageV1 page;
        try
        {
            page = await GuardedAsync(scope, permission, () => salesLines.ListApprovedLinesAsync(pageSize, query.Cursor, cancellationToken), cancellationToken);
        }
        catch (LogisticsInstructionServiceException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            // The Sales owner refused the cursor (another company's, or malformed): the caller's input, not an outage.
            throw new LogisticsInstructionServiceException(400, LogisticsProblemCodesV1.InvalidCursor);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.SalesProviderUnavailable);
        }

        if (page is null || page.Items is null || page.Items.Count > pageSize || page.Items.Any(item => item is null || item.CompanyId != scope.CompanyId
                || item.OrderId == Guid.Empty || item.LineId == Guid.Empty || item.ProductId == Guid.Empty || item.OrderUnitId == Guid.Empty
                || item.PartyId == Guid.Empty || item.CompanyAccountId == Guid.Empty || string.IsNullOrWhiteSpace(item.QuantityText)
                || string.IsNullOrWhiteSpace(item.DocumentNo)) || page.Items.Select(item => item!.LineId).Distinct().Count() != page.Items.Count)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        // Logistics's OWN totals for exactly these order lines (its company-filtered lines; never another module's table).
        IReadOnlyList<LogisticsOrderLineProgress> totals;
        var lineIds = page.Items.Select(item => item.LineId).ToArray();
        try
        {
            totals = lineIds.Length == 0
                ? []
                : await GuardedAsync(scope, permission, () => progress.ReadAsync(lineIds, cancellationToken), cancellationToken);
        }
        catch (LogisticsInstructionServiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LogisticsInstructionServiceException(503, "service_unavailable");
        }

        var wanted = lineIds.ToHashSet();
        if (totals is null || totals.Any(total => total is null || !wanted.Contains(total.OrderLineId) || total.UnitId == Guid.Empty
                || total.Delivered < 0m || total.Delivered > total.Shipped || total.Shipped > total.Loaded || total.Loaded > total.Instructed)
            || totals.GroupBy(total => (total.OrderLineId, total.UnitId)).Any(group => group.Count() > 1))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        var names = await NamesAsync(scope, permission, page.Items.Select(item => (item.PartyId, item.CompanyAccountId)), cancellationToken);
        var items = new List<LogisticsSalesWorkItemView>(page.Items.Count);
        foreach (var item in page.Items)
        {
            var own = totals.Where(total => total.OrderLineId == item.LineId).ToArray();
            // Ö2 (the AT-024 rule): totals are taken in the order unit only, never converted; a non-zero total recorded in another
            // unit cannot be shown against this line's quantity, so the answer is malformed rather than a silently wrong row.
            if (own.Any(total => total.UnitId != item.OrderUnitId && total.Instructed != 0m))
            {
                throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
            }

            var inUnit = own.SingleOrDefault(total => total.UnitId == item.OrderUnitId);
            items.Add(new LogisticsSalesWorkItemView(item.OrderId, item.DocumentNo, item.LineId, item.LineNo, item.PartyId,
                names[(item.PartyId, item.CompanyAccountId)], item.ProductId, item.ProductCode, item.ProductName, item.OrderUnitId, item.UnitCode,
                item.QuantityText, Text(inUnit?.Instructed ?? 0m), Text(inUnit?.Loaded ?? 0m), Text(inUnit?.Shipped ?? 0m),
                Text(inUnit?.Delivered ?? 0m), item.RequestedDeliveryDate));
        }

        return new LogisticsSalesWorkPage(LogisticsOperationsWorkSectionsV1.Sales, items, page.NextCursor);
    }

    public async Task<LogisticsPurchaseWorkPage> GetPurchaseWorkAsync(LogisticsOperationsWorkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.PurchaseWorkPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var pageSize = ValidatedPageSize(query);

        PurchaseOpenLinePageV1 page;
        try
        {
            page = await GuardedAsync(scope, permission, () => purchaseLines.ListOpenApprovedLinesAsync(pageSize, query.Cursor, cancellationToken),
                cancellationToken);
        }
        catch (LogisticsInstructionServiceException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw new LogisticsInstructionServiceException(400, LogisticsProblemCodesV1.InvalidCursor);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.PurchasingProviderUnavailable);
        }

        if (page is null || page.Items is null || page.Items.Count > pageSize || page.Items.Any(item => item is null || item.CompanyId != scope.CompanyId
                || item.OrderId == Guid.Empty || item.LineId == Guid.Empty || item.ProductId == Guid.Empty || item.UnitId == Guid.Empty
                || item.SupplierPartyId == Guid.Empty || item.CompanyAccountId == Guid.Empty || item.LineNo < 1 || string.IsNullOrWhiteSpace(item.Kind)
                || !IsQuantityText(item.CommittedText) || !IsQuantityText(item.AcceptedText) || !IsPositiveQuantityText(item.RemainingText))
            || page.Items.Select(item => item!.LineId).Distinct().Count() != page.Items.Count)
        {
            // A line with nothing left to receive is not purchase work: the owner contradicted its own contract.
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        var names = await NamesAsync(scope, permission, page.Items.Select(item => (item.SupplierPartyId, item.CompanyAccountId)), cancellationToken);
        return new LogisticsPurchaseWorkPage(LogisticsOperationsWorkSectionsV1.Purchase,
            [.. page.Items.Select(item => new LogisticsPurchaseWorkItemView(item.OrderId, item.OrderEffectiveOn, item.LineId, item.LineNo, item.Kind,
                item.SupplierPartyId, names[(item.SupplierPartyId, item.CompanyAccountId)], item.ProductId, item.UnitId, item.CommittedText,
                item.AcceptedText, item.RemainingText))],
            page.NextCursor);
    }

    /// <summary>
    /// The counterparty's NAME per distinct (party, account) of the page, one fenced Cari read each, memoized for this request only
    /// (the LOG-4b picker's rule, Business 5745076917): a party the owner does not answer for keeps its lines under a labelled
    /// "ref. …" reference, never dropped; a denial is 403 and an outage 503 for the whole read.
    /// </summary>
    private async Task<Dictionary<(Guid Party, Guid Account), string>> NamesAsync(Scope scope, string? permission,
        IEnumerable<(Guid Party, Guid Account)> parties, CancellationToken cancellationToken)
    {
        var names = new Dictionary<(Guid Party, Guid Account), string>();
        foreach (var party in parties.Distinct())
        {
            LogisticsPartyAccountAnswerV1 answer;
            try
            {
                answer = await GuardedAsync(scope, permission, () => accounts.GetAuthorizedAsync(party.Party, party.Account, cancellationToken), cancellationToken);
            }
            catch (LogisticsInstructionServiceException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.CounterpartyProviderUnavailable);
            }

            switch (answer?.Outcome)
            {
                case LogisticsPartyAccountOutcomeV1.Found when answer.Account is { } account:
                    if (account.PartyId != party.Party || account.CompanyAccountId != party.Account || account.CompanyId != scope.CompanyId
                        || string.IsNullOrWhiteSpace(account.DisplayName))
                    {
                        throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
                    }

                    names[party] = account.DisplayName;
                    break;
                case LogisticsPartyAccountOutcomeV1.NotFound:
                    names[party] = LogisticsInstructionService.CustomerReference(party.Party);
                    break;
                case LogisticsPartyAccountOutcomeV1.Denied:
                    throw new LogisticsInstructionServiceException(403, "forbidden");
                default:
                    throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.CounterpartyProviderUnavailable);
            }
        }

        return names;
    }

    private static int ValidatedPageSize(LogisticsOperationsWorkQuery query)
    {
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new LogisticsInstructionServiceException(400, "validation_failed");
        }

        return pageSize;
    }

    /// <summary>The exact value in canonical text: trailing zeros dropped (Ö2), never rounded.</summary>
    public static string Text(decimal value) => (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static bool IsQuantityText(string? text) =>
        !string.IsNullOrEmpty(text) && text.Length <= 64 && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && value >= 0m;

    private static bool IsPositiveQuantityText(string? text) =>
        IsQuantityText(text) && decimal.Parse(text!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) > 0m;

    // ---- authority (the loading-instruction service's fences, #276) -----------------------------

    private sealed record Scope(Guid CompanyId, string ActorId);

    private async Task<Scope> RequireAsync(string? permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new LogisticsInstructionServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new LogisticsInstructionServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var granted = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!granted.IsGranted || !granted.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new LogisticsInstructionServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId);
    }

    private async Task RequireStillAsync(Scope scope, string? permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new LogisticsInstructionServiceException(403, "forbidden");
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
}
