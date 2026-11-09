using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-37 — Kontrol Merkezi (Satış hazırlık planı v1 §3.3; dispatch/invoice design spec §3.5). K2, read-only: no table, command,
/// attempt or audit; no money. Six checks over what main can see, each stated in the answer (never a silent empty list):
/// <list type="bullet">
/// <item><b>Uninvoiced shipment overdue</b> (SAT-D95): the Logistics owner's approved shares (<see cref="ISalesInvoiceableShipmentReadPortV1"/>)
/// that are on no active invoice line and whose Europe/Istanbul shipment day is more than the grace days (SAT-D95: 7 from the
/// Sales-owned <see cref="SalesControlCenterPolicy"/>) before today. A share on a Draft, PendingApproval or Approved invoice is being
/// invoiced and is not listed. An owner that is not composed, refuses, fails or answers malformed leaves the check
/// <c>Unavailable</c> (the other checks still answer).</item>
/// <item><b>Invoice needs review</b> (EK42): active invoices with a line flagged by the TL calculator.</item>
/// <item><b>Approved invoice awaiting the official document</b> (SAT-D7): Approved invoices with no API-35 match record.</item>
/// <item><b>Official document deviation</b> (SAT-D229): matched invoices whose recorded TL difference is not zero, with the tolerance
/// class. The due-date and rate comparison (SAT-D86) has no recorded source and is not claimed.</item>
/// <item><b>Finalisation blocked</b> (API-36, NEEDS-SINAN #3): Approved invoices whose recorded match carries a difference.</item>
/// <item><b>Finance admission waiting</b> (API-36, SAT-D208): Finalized invoices Finance has not admitted; a recorded refusal is Critical.</item>
/// </list> The name <c>sales.invoices.read</c> is measured fresh before anything is read, after every
/// awaited owner or store read and before the answer is built; the owner ports carry no authority.
/// </summary>
public sealed class SalesControlCenterService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ISalesInvoiceStore invoices,
    SalesControlCenterPolicy policy,
    TimeProvider timeProvider,
    ISalesInvoiceableShipmentReadPortV1? shipments = null) : ISalesControlCenterService
{
    private const string ReadPermission = SalesPermissionManifestV1.InvoicesRead;

    public async Task<SalesControlCenterResponse> GetControlCenterAsync(CancellationToken cancellationToken = default)
    {
        var scope = await RequireScopeAsync(cancellationToken);
        var today = Day(timeProvider.GetUtcNow());

        var (invoiceList, invoicesTruncated) = await ReadActiveInvoicesAsync(scope, cancellationToken);
        var (overdue, overdueState) = await ReadOverdueShipmentsAsync(scope, today, cancellationToken);
        await RequireStillAsync(scope, cancellationToken);

        var items = new List<SalesControlCenterItemView>(overdue);
        foreach (var invoice in invoiceList)
        {
            var age = Math.Max(0, today.DayNumber - invoice.Header.BusinessDate.DayNumber);
            var flagged = invoice.Lines.Count(line => line.Functional is { NeedsReview: true });
            if (flagged > 0)
            {
                items.Add(new SalesControlCenterItemView(SalesControlCenterCheckKindView.InvoiceNeedsReview,
                    SalesControlCenterSeverityView.ApprovalRequired, invoice.Header.PartyId, invoice.Header.CompanyAccountId,
                    invoice.DocumentNo, invoice.Header.BusinessDate, age, flagged, invoice.Id, null, null, null));
            }

            if (invoice.Match is { Difference: > 0m } match)
            {
                // API-35 recorded a TL difference against the official document (SAT-D229): within the tolerance it is shown as such;
                // beyond it, the recorded match already carries the approving person's decision (SAT-D106), so it stays informational.
                items.Add(new SalesControlCenterItemView(SalesControlCenterCheckKindView.OfficialDocumentDeviation,
                    match.Difference <= match.Tolerance ? SalesControlCenterSeverityView.WithinTolerance : SalesControlCenterSeverityView.Info,
                    invoice.Header.PartyId, invoice.Header.CompanyAccountId, invoice.DocumentNo, invoice.Header.BusinessDate, age,
                    invoice.Lines.Count, invoice.Id, null, null, null));
            }

            if (invoice.State == SalesInvoiceState.Approved && invoice.Match is { Difference: not 0m })
            {
                // API-36 refuses this invoice (409 sales_invoice_match_difference_open) until NEEDS-SINAN #3 is decided, and there is
                // no Approved cancel path yet: without this item it would sit unseen.
                items.Add(new SalesControlCenterItemView(SalesControlCenterCheckKindView.InvoiceFinalizationBlocked,
                    SalesControlCenterSeverityView.Critical, invoice.Header.PartyId, invoice.Header.CompanyAccountId,
                    invoice.DocumentNo, invoice.Header.BusinessDate, age, invoice.Lines.Count, invoice.Id, null, null, null));
            }

            if (invoice.State == SalesInvoiceState.Finalized && invoice.Finalization is { FinanceStatus: SalesInvoiceFinanceStatus.Waiting } waiting)
            {
                // SAT-D208: Sales never calls a waiting invoice admitted. A recorded refusal has no automatic way on (only the explicit
                // re-drive), so it is Critical; an unanswered or retryable hand-off is informational.
                items.Add(new SalesControlCenterItemView(SalesControlCenterCheckKindView.InvoiceFinanceAdmissionWaiting,
                    SalesInvoiceService.IsRefusedFinanceOutcome(waiting.LastOutcome)
                        ? SalesControlCenterSeverityView.Critical
                        : SalesControlCenterSeverityView.Info,
                    invoice.Header.PartyId, invoice.Header.CompanyAccountId, invoice.DocumentNo, invoice.Header.BusinessDate, age,
                    invoice.Lines.Count, invoice.Id, null, null, null));
            }

            if (invoice.State == SalesInvoiceState.Approved && invoice.Match is null)
            {
                items.Add(new SalesControlCenterItemView(SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch,
                    SalesControlCenterSeverityView.Info, invoice.Header.PartyId, invoice.Header.CompanyAccountId,
                    invoice.DocumentNo, invoice.Header.BusinessDate, age, invoice.Lines.Count, invoice.Id, null, null, null));
            }
        }

        var ordered = items
            .OrderBy(item => item.Severity)
            .ThenByDescending(item => item.AgeDays)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Reference, StringComparer.Ordinal)
            .ThenBy(item => item.ShipmentId ?? item.InvoiceId ?? Guid.Empty)
            .ToArray();

        return new SalesControlCenterResponse(today, policy.UninvoicedDispatchGraceDays,
        [
            overdueState,
            new(SalesControlCenterCheckKindView.InvoiceNeedsReview, SalesControlCenterCheckStateView.Measured, invoicesTruncated),
            new(SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch, SalesControlCenterCheckStateView.Measured, invoicesTruncated),
            new(SalesControlCenterCheckKindView.OfficialDocumentDeviation, SalesControlCenterCheckStateView.Measured, invoicesTruncated),
            new(SalesControlCenterCheckKindView.InvoiceFinalizationBlocked, SalesControlCenterCheckStateView.Measured, invoicesTruncated),
            new(SalesControlCenterCheckKindView.InvoiceFinanceAdmissionWaiting, SalesControlCenterCheckStateView.Measured,
                invoicesTruncated),
        ], ordered);
    }

    /// <summary>The company's invoices that still hold shares (Draft, PendingApproval, Approved), newest first, within the page bound.</summary>
    private async Task<(IReadOnlyList<SalesInvoice> Invoices, bool Truncated)> ReadActiveInvoicesAsync(Scope scope,
        CancellationToken cancellationToken)
    {
        var active = new List<SalesInvoice>();
        string? cursor = null;
        var truncated = false;
        for (var page = 0; ; page++)
        {
            if (page == SalesControlCenterApiV1.MaximumInvoicePages)
            {
                truncated = true;
                break;
            }

            SalesInvoicePage answer;
            try
            {
                answer = await invoices.ListInvoicesAsync(SalesControlCenterApiV1.InvoicePageSize, cursor, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RequireStillAsync(scope, cancellationToken);
                throw new SalesDraftServiceException(503, "service_unavailable");
            }

            await RequireStillAsync(scope, cancellationToken);
            if (answer is null || answer.Items is null || answer.Items.Any(invoice => invoice is null) || answer.NextCursor is { Length: 0 })
            {
                throw new SalesDraftServiceException(503, "service_unavailable");
            }

            active.AddRange(answer.Items.Where(invoice => invoice.HoldsShares));
            if (answer.NextCursor is null)
            {
                break;
            }

            cursor = answer.NextCursor;
        }

        return (active, truncated);
    }

    private async Task<(IReadOnlyList<SalesControlCenterItemView> Items, SalesControlCenterCheckView State)> ReadOverdueShipmentsAsync(
        Scope scope, DateOnly today, CancellationToken cancellationToken)
    {
        var kind = SalesControlCenterCheckKindView.UninvoicedDispatchOverdue;
        SalesControlCenterCheckView Unavailable() => new(kind, SalesControlCenterCheckStateView.Unavailable, false);
        if (shipments is null)
        {
            return ([], Unavailable());
        }

        SalesInvoiceableShipmentAnswerV1? answer;
        try
        {
            answer = await shipments.ReadInvoiceableShipmentsAsync(
                new SalesInvoiceableShipmentQueryV1(null, SalesDispatchApiV1.MaximumCandidateShares), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, cancellationToken);
            return ([], Unavailable());
        }

        await RequireStillAsync(scope, cancellationToken);
        if (answer is { Outcome: SalesDispatchReadOutcomeV1.Denied })
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (answer is not { Outcome: SalesDispatchReadOutcomeV1.Found } || answer.CompanyId != scope.CompanyId || answer.Shares is null
            || answer.Shares.Count > SalesDispatchApiV1.MaximumCandidateShares
            || answer.Shares.Any(share => share is null || share.ShipmentId == Guid.Empty || string.IsNullOrWhiteSpace(share.SystemNumber)))
        {
            return ([], Unavailable());
        }

        IReadOnlySet<Guid> invoiced;
        try
        {
            invoiced = await invoices.FindInvoicedShipmentsAsync([.. answer.Shares.Select(share => share.ShipmentId)], cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, cancellationToken);
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        var items = new List<SalesControlCenterItemView>();
        foreach (var share in answer.Shares.Where(share => !invoiced.Contains(share.ShipmentId)))
        {
            var shipmentDay = Day(share.ShippedAtUtc);
            var age = today.DayNumber - shipmentDay.DayNumber;
            if (age > policy.UninvoicedDispatchGraceDays)
            {
                items.Add(new SalesControlCenterItemView(kind, SalesControlCenterSeverityView.Critical, share.CustomerPartyId,
                    share.CompanyAccountId, share.SystemNumber, shipmentDay, age, 1, null, share.DispatchNoteId, share.ShipmentId,
                    share.SalesOrderId));
            }
        }

        return (items, new SalesControlCenterCheckView(kind, SalesControlCenterCheckStateView.Measured, answer.HasMore));
    }

    /// <summary>SAT-D10: the Europe/Istanbul day; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateOnly Day(DateTimeOffset instant)
    {
        try
        {
            return SalesBusinessDay.DateOf(instant);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    private async Task<Scope> RequireScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(ReadPermission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(ReadPermission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId);
    }

    private async Task RequireStillAsync(Scope expected, CancellationToken cancellationToken)
    {
        if (await RequireScopeAsync(cancellationToken) != expected)
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

    private sealed record Scope(Guid CompanyId, string ActorId);
}
