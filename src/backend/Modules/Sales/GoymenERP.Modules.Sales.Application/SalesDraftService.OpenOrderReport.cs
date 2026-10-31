using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-41 (KPI No 124, "açık sipariş tonajı") under <c>sales.reports.read</c>. Read-only in every module.
/// <list type="bullet">
/// <item><b>Which quantity is open</b> — the owner's own acceptance rule (Q9; SAT-D113…D115; the shipping line standing and the
/// SAT-D114 invoicing gate apply the same): the quantity of the last internally approved revision the CUSTOMER ACCEPTED, or of the
/// last approved revision when acceptance is not required (the API-08 basis, <see cref="Basis"/>). An approved revision that awaits
/// customer acceptance adds NOTHING to the open quantity; what it adds above the accepted quantity (all of it when no revision is
/// accepted yet) is answered apart as <c>awaitingAcceptanceQuantity</c> — Sinan 2026-10-31 (EK19 pending): "Açık tonaja eklenmez;
/// 'müşteri onayı bekliyor' olarak ayrı görünür".</item>
/// <item><b>Open</b> = accepted − shipped (the Logistics owner's recorded shipped total, AT-024 validation), never below zero; a line
/// shipped short WITHIN the SAT-D5 tolerance is "Tamamlandı" and has nothing open (SAT-D130: <see cref="SalesRealizationCalculator"/>
/// with the Settings tolerance of the order's business date, read once per business date per request); an unreadable tolerance
/// makes the line unmeasured (never open, never closed).</item>
/// <item><b>Closed history is not in the report</b>: a line with nothing open and nothing awaiting (fully shipped, completed within
/// the tolerance) is left out, and a group with no listed and no unmeasured line is skipped.</item>
/// <item><b>Bounded work</b> (<see cref="SalesReportApiV1"/>): group keys are scanned in key order through the service's own cursor;
/// each group's orders are read and their shipments asked in batches of <see cref="SalesReportApiV1.OrderBatchSize"/> (one store
/// read and one Logistics read per batch); a group above <see cref="SalesReportApiV1.MaximumOrdersPerGroup"/> orders is answered
/// <c>TooManyOrders</c> without sums and passed; a request measures at most
/// <see cref="SalesReportApiV1.MaximumOrdersPerRequest"/> orders and then ends its page with a cursor (fewer groups, or none, than
/// asked). Cost of one request: at most ⌈1000 / 100⌉ = 10 order batches (each one restoring store read and one Logistics read, each
/// fenced), ⌈500 / 100⌉ key scans, one tolerance read per distinct business date, one Cari read — never a lockout.</item>
/// <item><b>Isolation</b>: a malformed owner answer for ONE order makes that order's lines unmeasured
/// (<c>DeliveryAnswerInvalid</c>); an owner that is not composed, throws or answers the batch malformed is 503.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    private const int GroupKeysPerScan = 100;

    public async Task<SalesOpenOrderReportResponse> GetOpenOrderReportAsync(SalesOpenOrderReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReportReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        RequireReportPage(query.PageSize, query.Cursor);
        RequireRange(query.From, query.To);
        RequireAccount(query.CompanyAccountId);
        var store = reports ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var filter = new SalesOpenOrderReportFilter(query.From, query.To, query.CompanyAccountId);
        var after = query.Cursor is null ? null : OpenOrderCursor.Decode(query.Cursor, scope.CompanyId, filter);

        var context = new OpenOrderRun(scope, permission, store, filter);
        var results = new List<GroupResult>();
        var budget = SalesReportApiV1.MaximumOrdersPerRequest;
        var scanned = 0;
        var measuredAny = false;
        var exhausted = false;
        var position = after;
        while (results.Count < query.PageSize && scanned < SalesReportApiV1.MaximumGroupsScannedPerRequest)
        {
            var take = Math.Min(GroupKeysPerScan, SalesReportApiV1.MaximumGroupsScannedPerRequest - scanned);
            var keyFrom = position;
            var groups = await ReadAsync(context, () => store.ListOpenOrderGroupsAsync(filter, keyFrom, take, cancellationToken),
                cancellationToken);
            if (groups is null || groups.Count > take || groups.Any(group => group is null || group.Key is null || group.OrderCount < 1))
            {
                throw new SalesDraftServiceException(503, "service_unavailable");
            }

            var stopped = false;
            foreach (var group in groups)
            {
                if (results.Count >= query.PageSize)
                {
                    stopped = true;
                    break;
                }

                if (group.OrderCount <= SalesReportApiV1.MaximumOrdersPerGroup && group.OrderCount > budget && measuredAny)
                {
                    // The request's work bound: this group is the next page's first, never a cut sum.
                    stopped = true;
                    break;
                }

                var result = group.OrderCount > SalesReportApiV1.MaximumOrdersPerGroup
                    ? await TooManyOrdersAsync(context, group, cancellationToken)
                    : await MeasureGroupAsync(context, group, cancellationToken);
                if (result.State == SalesOpenOrderGroupStateView.Measured)
                {
                    budget -= group.OrderCount;
                    measuredAny = true;
                }

                position = group.Key;
                scanned++;
                if (result.HasAnything)
                {
                    results.Add(result);
                }
            }

            if (stopped)
            {
                break;
            }

            if (groups.Count < take)
            {
                exhausted = true;
                break;
            }
        }

        var customers = await ReportCustomersAsync(scope, permission, results.Select(result => result.Key.PartyId), cancellationToken);
        var views = results.Select(result => result.View(ReportCustomer(customers, result.Key.PartyId, result.Key.CompanyAccountId)))
            .ToArray();
        var next = exhausted || position is null ? null : OpenOrderCursor.Encode(scope.CompanyId, filter, position);
        return new SalesOpenOrderReportResponse(SalesOpenOrderMeasureView.ApprovedMinusShipped, views, next);
    }

    private sealed record OpenOrderRun(Scope Scope, string? Permission, ISalesReportReadStore Store, SalesOpenOrderReportFilter Filter)
    {
        /// <summary>SAT-D130: the tolerance per business date, read once per request (null = not readable, fail-closed).</summary>
        public Dictionary<DateOnly, ToleranceRead?> Tolerances { get; } = [];
    }

    /// <summary>A store read under the report fence; a store refusal is its own answer (the cursor is the service's).</summary>
    private async Task<T> ReadAsync<T>(OpenOrderRun run, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(run.Scope, run.Permission, read, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>A group too large to measure in one request: no sums, its product and unit texts from one order of its scope.</summary>
    private async Task<GroupResult> TooManyOrdersAsync(OpenOrderRun run, SalesOpenOrderGroupScope group, CancellationToken cancellationToken)
    {
        var first = await ReadAsync(run, () => run.Store.ListOpenOrdersOfGroupAsync(run.Filter, group.Key, null, 1, cancellationToken),
            cancellationToken);
        var sample = first?.Count == 1 ? KeyLines(first[0], group.Key).FirstOrDefault() : null;
        if (sample is null)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        return new GroupResult(group.Key, group.OrderCount, SalesOpenOrderGroupStateView.TooManyOrders, sample, [], []);
    }

    /// <summary>
    /// One group measured over every order of its scope, batch by batch: the acceptance basis, the shipped totals (one Logistics
    /// read per batch), the SAT-D5 judgement where a line is shipped short, and only the lines with something open or awaiting.
    /// </summary>
    private async Task<GroupResult> MeasureGroupAsync(OpenOrderRun run, SalesOpenOrderGroupScope group, CancellationToken cancellationToken)
    {
        var lines = new List<OpenLine>();
        var unmeasured = new List<SalesOpenOrderUnmeasuredLineView>();
        SalesOrderDraftLine? sample = null;
        Guid? afterOrder = null;
        var read = 0;
        while (true)
        {
            var from = afterOrder;
            var orders = await ReadAsync(run, () => run.Store.ListOpenOrdersOfGroupAsync(run.Filter, group.Key, from,
                SalesReportApiV1.OrderBatchSize, cancellationToken), cancellationToken);
            if (orders is null || orders.Count > SalesReportApiV1.OrderBatchSize)
            {
                throw new SalesDraftServiceException(503, "service_unavailable");
            }

            if (orders.Count == 0)
            {
                break;
            }

            read += orders.Count;
            if (read > SalesReportApiV1.MaximumOrdersPerGroup)
            {
                // The scope grew past the bound while it was read: answered as too large, never a cut sum.
                return await TooManyOrdersAsync(run, group with { OrderCount = read }, cancellationToken);
            }

            foreach (var order in orders)
            {
                // Defence in depth: the store's open-status rule is checked again (and a null row is a malformed store answer).
                if (order is null || order.CompanyId != run.Scope.CompanyId || order.State != SalesDraftState.Draft
                    || order.ApprovalState != InternalApprovalState.Approved && order.ArchivedRevisions.Count == 0)
                {
                    throw new SalesDraftServiceException(503, "service_unavailable");
                }
            }

            var answers = await ShipmentsAsync(run, orders, cancellationToken);
            foreach (var order in orders)
            {
                var keyLines = KeyLines(order, group.Key).ToArray();
                sample ??= keyLines.FirstOrDefault();
                if (keyLines.Length == 0)
                {
                    continue;
                }

                if (AcceptanceBasis(order) is not { } basis)
                {
                    unmeasured.AddRange(keyLines.Select(line => Unmeasured(order, line, SalesOpenOrderUnmeasuredReasonView.PendingReapproval)));
                    continue;
                }

                var recorded = answers.TryGetValue(order.Id, out var answer) ? RecordedQuantities(run.Scope, order, answer) : null;
                if (recorded is null)
                {
                    unmeasured.AddRange(keyLines.Select(line => Unmeasured(order, line, SalesOpenOrderUnmeasuredReasonView.DeliveryAnswerInvalid)));
                    continue;
                }

                foreach (var line in keyLines)
                {
                    var (accepted, awaiting) = basis.Lines[line.Id];
                    var shipped = recorded[line.Id].Shipped;
                    ToleranceRead? tolerance = null;
                    if (IsShortShipped(accepted, shipped))
                    {
                        tolerance = await ToleranceOfAsync(run, order, cancellationToken);
                        if (tolerance is null)
                        {
                            unmeasured.Add(Unmeasured(order, line, SalesOpenOrderUnmeasuredReasonView.TolerancePolicyUnavailable));
                            continue;
                        }
                    }

                    var (remaining, completedWithinTolerance) = Remaining(accepted, shipped, tolerance);
                    if (remaining.IsZero && awaiting.IsZero)
                    {
                        // Closed history: nothing open, nothing awaiting — not part of the report.
                        continue;
                    }

                    lines.Add(new OpenLine(order, line, basis.AcceptedRevision, accepted, shipped, remaining, completedWithinTolerance,
                        awaiting, awaiting.IsZero ? null : basis.AwaitingRevision));
                }
            }

            afterOrder = orders[^1].Id;
            if (orders.Count < SalesReportApiV1.OrderBatchSize)
            {
                break;
            }
        }

        return sample is null
            ? new GroupResult(group.Key, group.OrderCount, SalesOpenOrderGroupStateView.Measured, null, [], [])
            : new GroupResult(group.Key, group.OrderCount, SalesOpenOrderGroupStateView.Measured, sample, lines, unmeasured);
    }

    /// <summary>The current lines of <paramref name="order"/> in the group key (the store's grouping, applied to the restored order).</summary>
    private static IEnumerable<SalesOrderDraftLine> KeyLines(SalesOrderDraft order, SalesOpenOrderGroupKey key) =>
        order.Header.CompanyAccountId == key.CompanyAccountId && order.Header.PartyId == key.PartyId
            ? order.Lines.Where(line => line.Product.ProductId == key.ProductId && line.OrderUnit.UnitId == key.UnitId).OrderBy(line => line.LineNo)
            : [];

    private static SalesOpenOrderUnmeasuredLineView Unmeasured(SalesOrderDraft order, SalesOrderDraftLine line,
        SalesOpenOrderUnmeasuredReasonView reason) => new(order.Id, order.DocumentNo, line.Id, line.LineNo, reason);

    /// <summary>
    /// The batch's shipped totals from the Logistics owner, one read: answers by order id. Not composed, throwing, or an answer that
    /// is not one per requested order is 503 (the whole batch is unreadable); a single order's malformed CONTENT is that order's
    /// own unmeasured lines (validated per order by <see cref="RecordedQuantities"/>).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, SalesOrderDeliveryReadAnswerV1>> ShipmentsAsync(OpenOrderRun run,
        IReadOnlyList<SalesOrderDraft> orders, CancellationToken cancellationToken)
    {
        var owner = deliveries ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        var ids = orders.Select(order => order.Id).ToArray();
        IReadOnlyList<SalesOrderDeliveryReadAnswerV1>? answers;
        try
        {
            answers = await GuardedAsync(run.Scope, run.Permission, () => owner.ReadOrdersDeliveriesAsync(ids, cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        }

        var byOrder = new Dictionary<Guid, SalesOrderDeliveryReadAnswerV1>();
        foreach (var answer in answers ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable))
        {
            if (answer is null || !ids.Contains(answer.OrderId) || !byOrder.TryAdd(answer.OrderId, answer))
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
            }
        }

        return byOrder;
    }

    /// <summary>SAT-D130: the Settings tolerance of the order's business date, asked once per date in a request (API-08's read).</summary>
    private async Task<ToleranceRead?> ToleranceOfAsync(OpenOrderRun run, SalesOrderDraft order, CancellationToken cancellationToken)
    {
        var on = order.Header.BusinessDate;
        if (!run.Tolerances.TryGetValue(on, out var tolerance))
        {
            tolerance = await ToleranceAsync(run.Scope, run.Permission, order, cancellationToken);
            run.Tolerances[on] = tolerance;
        }

        return tolerance;
    }

    /// <summary>Something but less than the accepted quantity shipped: the only case the SAT-D5 tolerance decides (SAT-D130).</summary>
    private static bool IsShortShipped(Quantity accepted, Quantity shipped) => !shipped.IsZero && shipped.CompareTo(accepted) < 0;

    /// <summary>
    /// The open remainder: none when shipped in full (or more) or when nothing is accepted; the SAT-D5 judgement of the
    /// realization calculator when shipped short (Completed → none, SAT-D130; else accepted − shipped); all of it when nothing shipped.
    /// </summary>
    private static (Quantity Remaining, bool CompletedWithinTolerance) Remaining(Quantity accepted, Quantity shipped, ToleranceRead? tolerance)
    {
        try
        {
            if (accepted.IsZero || shipped.CompareTo(accepted) >= 0)
            {
                return (Quantity.Zero(accepted.Unit), false);
            }

            if (shipped.IsZero)
            {
                return (accepted, false);
            }

            var realization = SalesRealizationCalculator.Line(accepted, shipped, lineClosed: false, tolerance!.Tolerance);
            return realization.State == LineRealizationState.Completed
                ? (Quantity.Zero(accepted.Unit), true)
                : (accepted.Subtract(shipped), false);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }
    }

    // ---- the acceptance basis ---------------------------------------------------------------------------------------------------

    /// <summary>Per current line: the accepted quantity (open is measured on it) and what the approved, not yet accepted revision adds.</summary>
    private sealed record OpenBasis(string? AcceptedRevision, string? AwaitingRevision,
        IReadOnlyDictionary<Guid, (Quantity Accepted, Quantity Awaiting)> Lines);

    /// <summary>
    /// The owner's acceptance rule over the order's revisions. Acceptance not required (pre-Q9): the API-08 basis, nothing awaits.
    /// Required: the internally approved revision is the current one when it is approved, else the last archived (approved) one;
    /// the accepted revision is the one the customer acceptance names. Accepted = approved → that quantity, nothing awaiting.
    /// Otherwise the accepted revision's quantity (zero when none is accepted, or when the line is not in it) is open, and
    /// max(0, approved − accepted) awaits acceptance. A line held by a revision under another product or unit, or with an
    /// unreadable quantity, is not comparable: null (the order is unmeasured) — never repaired.
    /// </summary>
    private static OpenBasis? AcceptanceBasis(SalesOrderDraft draft)
    {
        if (!draft.CustomerAcceptanceRequired)
        {
            return Basis(draft) is { } basis
                ? new OpenBasis(basis.Revision, null,
                    basis.Lines.ToDictionary(entry => entry.Key, entry => (entry.Value.Quantity, Quantity.Zero(entry.Value.Quantity.Unit))))
                : null;
        }

        ApprovedRevisionSnapshot? approvedArchive = null;
        int approvedRevision;
        if (draft.ApprovalState == InternalApprovalState.Approved)
        {
            approvedRevision = draft.Revision;
        }
        else if (draft.ArchivedRevisions.Count > 0)
        {
            approvedArchive = draft.ArchivedRevisions[^1];
            approvedRevision = approvedArchive.Revision;
        }
        else
        {
            return null;
        }

        var acceptedRevision = draft.CustomerAcceptance?.Revision;
        ApprovedRevisionSnapshot? acceptedArchive = null;
        if (acceptedRevision is { } accepted && accepted != approvedRevision)
        {
            acceptedArchive = draft.ArchivedRevisions.FirstOrDefault(revision => revision.Revision == accepted);
            if (acceptedArchive is null)
            {
                return null;
            }
        }

        var lines = new Dictionary<Guid, (Quantity Accepted, Quantity Awaiting)>();
        foreach (var line in draft.Lines)
        {
            var zero = Quantity.Zero(line.Quantity.Unit);
            if (!TryRevisionQuantity(line, approvedArchive, out var approved))
            {
                return null;
            }

            Quantity acceptedQuantity;
            if (acceptedRevision == approvedRevision)
            {
                acceptedQuantity = approved ?? zero;
            }
            else if (acceptedArchive is null)
            {
                acceptedQuantity = zero;
            }
            else if (!TryRevisionQuantity(line, acceptedArchive, out var fromAccepted))
            {
                return null;
            }
            else
            {
                acceptedQuantity = fromAccepted ?? zero;
            }

            var approvedQuantity = approved ?? zero;
            var awaiting = acceptedRevision == approvedRevision || !approvedQuantity.IsGreaterThan(acceptedQuantity)
                ? zero
                : approvedQuantity.Subtract(acceptedQuantity);
            lines[line.Id] = (acceptedQuantity, awaiting);
        }

        return new OpenBasis(
            acceptedRevision is { } revision ? SalesDraftApiV1.RevisionText(revision) : null,
            acceptedRevision == approvedRevision ? null : SalesDraftApiV1.RevisionText(approvedRevision),
            lines);
    }

    /// <summary>
    /// The line's quantity in <paramref name="archive"/> (or the current quantity when it is null): null when the revision does not
    /// hold the line; false when it holds it under another product or unit or with an unreadable quantity (not comparable).
    /// </summary>
    private static bool TryRevisionQuantity(SalesOrderDraftLine line, ApprovedRevisionSnapshot? archive, out Quantity? quantity)
    {
        quantity = null;
        if (archive is null)
        {
            quantity = line.Quantity;
            return true;
        }

        var archived = archive.Lines.FirstOrDefault(candidate => candidate.LineId == line.Id);
        if (archived is null)
        {
            return true;
        }

        if (archived.ProductId != line.Product.ProductId || archived.OrderUnitId != line.OrderUnit.UnitId
            || !decimal.TryParse(archived.QuantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        try
        {
            quantity = Quantity.Of(value, line.Quantity.Unit);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---- views ------------------------------------------------------------------------------------------------------------------

    private sealed record OpenLine(SalesOrderDraft Order, SalesOrderDraftLine Line, string? AcceptedRevision, Quantity Accepted,
        Quantity Shipped, Quantity Remaining, bool CompletedWithinTolerance, Quantity Awaiting, string? AwaitingRevision);

    private sealed record GroupResult(SalesOpenOrderGroupKey Key, int ScopeOrderCount, SalesOpenOrderGroupStateView State,
        SalesOrderDraftLine? Sample, IReadOnlyList<OpenLine> Lines, IReadOnlyList<SalesOpenOrderUnmeasuredLineView> Unmeasured)
    {
        public bool HasAnything => State == SalesOpenOrderGroupStateView.TooManyOrders || Lines.Count > 0 || Unmeasured.Count > 0;

        /// <summary>
        /// The group's view: exact additions in its single unit (one unit id is one Settings unit of one dimension, checked again
        /// by <see cref="Quantity.Add"/>); a too-large group carries no sums. The product and unit texts are the order line's snapshot.
        /// </summary>
        public SalesOpenOrderGroupView View(SalesReportCustomerView customer)
        {
            var sample = Sample ?? throw new SalesDraftServiceException(503, "service_unavailable");
            string? approved = null, shipped = null, remaining = null, awaiting = null;
            if (State == SalesOpenOrderGroupStateView.Measured)
            {
                var unit = sample.Quantity.Unit;
                var (a, s, r, w) = (Quantity.Zero(unit), Quantity.Zero(unit), Quantity.Zero(unit), Quantity.Zero(unit));
                try
                {
                    foreach (var line in Lines)
                    {
                        a = a.Add(line.Accepted);
                        s = s.Add(line.Shipped);
                        r = r.Add(line.Remaining);
                        w = w.Add(line.Awaiting);
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
                {
                    // Another dimension under the same unit id, or a sum beyond the exact range: no number rather than a wrong one.
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
                }

                (approved, shipped, remaining, awaiting) = (QuantityText(a), QuantityText(s), QuantityText(r), QuantityText(w));
            }

            return new SalesOpenOrderGroupView(
                customer,
                Key.ProductId,
                sample.Product.Code,
                sample.Product.Name,
                Key.UnitId,
                sample.OrderUnit.UnitCode,
                State,
                ScopeOrderCount,
                Lines.Select(line => line.Order.Id).Distinct().Count(),
                approved,
                shipped,
                remaining,
                awaiting,
                Complete: State == SalesOpenOrderGroupStateView.Measured && Unmeasured.Count == 0,
                [.. Lines
                    .OrderBy(line => line.Order.Header.BusinessDate).ThenBy(line => line.Order.DocumentNo, StringComparer.Ordinal)
                    .ThenBy(line => line.Line.LineNo)
                    .Select(line => new SalesOpenOrderLineView(line.Order.Id, line.Order.DocumentNo, line.Order.Header.BusinessDate,
                        line.Line.RequestedDeliveryDate ?? line.Order.Header.RequestedDeliveryDate, line.AcceptedRevision, line.Line.Id,
                        line.Line.LineNo, QuantityText(line.Accepted), QuantityText(line.Shipped), QuantityText(line.Remaining),
                        line.CompletedWithinTolerance, QuantityText(line.Awaiting), line.AwaitingRevision))],
                Unmeasured);
        }
    }

    // ---- cursor -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The API-41 cursor: base64url JSON of the last scanned group key, bound to the company and a digest of the filter. Malformed,
    /// another company's or another filter's → 400 <c>invalid_cursor</c>, never read as a position.
    /// </summary>
    private static class OpenOrderCursor
    {
        private const string Ordering = "reports.open-orders.v2: companyAccountId, partyId, productId, unitId";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string Encode(Guid companyId, SalesOpenOrderReportFilter filter, SalesOpenOrderGroupKey after) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new Payload(companyId, Binding(filter), after.CompanyAccountId, after.PartyId, after.ProductId, after.UnitId), Json)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static SalesOpenOrderGroupKey Decode(string cursor, Guid companyId, SalesOpenOrderReportFilter filter)
        {
            Payload? payload;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
            {
                throw new SalesDraftServiceException(400, SalesDraftProblemCodesV1.InvalidCursor);
            }

            if (payload is null || payload.Company != companyId || !string.Equals(payload.Binding, Binding(filter), StringComparison.Ordinal)
                || payload.CompanyAccountId == Guid.Empty || payload.PartyId == Guid.Empty || payload.ProductId == Guid.Empty
                || payload.UnitId == Guid.Empty)
            {
                throw new SalesDraftServiceException(400, SalesDraftProblemCodesV1.InvalidCursor);
            }

            return new SalesOpenOrderGroupKey(payload.CompanyAccountId, payload.PartyId, payload.ProductId, payload.UnitId);
        }

        private static string Binding(SalesOpenOrderReportFilter filter) => Ordering + "|" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', Day(filter.From), Day(filter.To), filter.CompanyAccountId?.ToString("D") ?? "-"))));

        private static string Day(DateOnly? day) => day?.ToString("O", CultureInfo.InvariantCulture) ?? "-";

        private sealed record Payload(Guid Company, string Binding, Guid CompanyAccountId, Guid PartyId, Guid ProductId, Guid UnitId);
    }
}
