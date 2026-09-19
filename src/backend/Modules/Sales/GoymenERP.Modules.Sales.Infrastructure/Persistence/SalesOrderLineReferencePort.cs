using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// SALES-A1: the Sales-owned implementation of <see cref="ISalesOrderLineReferencePortV1"/> over the
/// company-filtered draft store (Business #66 5737989917 = B). It reads under the caller's server
/// context — the consumer (INV-AL-1, LOG-4) measures its own authority before asking — and creates
/// nothing. An approved draft's line answers its stored identity facts; any other approval state is
/// <see cref="SalesOrderLineReferenceOutcomeV1.NotApproved"/> with the state's name; another
/// company's draft is invisible under the tenant filter and answers
/// <see cref="SalesOrderLineReferenceOutcomeV1.NotFound"/>, never 403.
/// </summary>
public sealed class SalesOrderLineReferencePort(ISalesOrderDraftStore store) : ISalesOrderLineReferencePortV1
{
    public async Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (orderId == Guid.Empty || lineId == Guid.Empty)
        {
            return NotFound;
        }

        var draft = await store.FindDraftAsync(orderId, cancellationToken);
        var line = draft?.Lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (draft is null || line is null)
        {
            return NotFound;
        }

        if (draft.ApprovalState != InternalApprovalState.Approved)
        {
            return new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotApproved, null, draft.ApprovalState.ToString());
        }

        return new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.Approved, new SalesOrderLineReferenceV1(
            draft.CompanyId, draft.Id, line.Id, line.LineNo, draft.Header.PartyId, draft.Header.CompanyAccountId,
            line.Product.ProductId, line.Product.ProductVersion, line.OrderUnit.UnitId, line.QuantityText, draft.Revision, draft.Version), null);
    }

    /// <summary>
    /// LOG-4b: the APPROVED drafts' lines as a LINE page (FİNANS #358 P2): <paramref name="pageSize"/> caps the
    /// lines actually returned (1–<see cref="MaximumLinePageSize"/>), never the drafts read — so a page is
    /// bounded whatever the drafts' line counts. The drafts come newest first through the store's approval
    /// filter and its company-bound cursor; the page's own cursor is COMPOSITE (the store cursor that lists the
    /// draft where the page stopped first + that draft + the last line number answered), opaque and bound to the
    /// company, so a draft's lines are neither skipped nor repeated at a page boundary. A malformed cursor, another
    /// company's, or one whose draft is not listed under its own store cursor is <see cref="ArgumentException"/>
    /// (the consumer's 400). The last page answers a null cursor. Projected without any price, discount,
    /// currency or payment member (Sinan §4).
    /// </summary>
    public async Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > MaximumLinePageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Satır sayfası 1–{MaximumLinePageSize} olmalıdır.");
        }

        var position = cursor is null ? null : LinePosition.Decode(cursor);
        var items = new List<SalesApprovedLineReferenceV1>(pageSize);
        var draftCursor = position?.DraftCursor;
        // The draft the cursor stopped in is skipped through its last answered line wherever it is listed (still first, moved,
        // or gone): nothing of it is repeated, nothing after it is skipped.
        var skipDraft = position?.DraftId;
        var skipThroughLineNo = position?.LastLineNo ?? 0;
        while (true)
        {
            SalesDraftPage page;
            try
            {
                // Drafts are read pageSize at a time: at least one line per draft, so one draft page always fills a line page.
                page = await store.ListDraftsAsync(pageSize, draftCursor, cancellationToken, InternalApprovalState.Approved);
            }
            catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.InvalidCursor)
            {
                throw new ArgumentException("Onaylı satır imleci geçersiz.", nameof(cursor), exception);
            }

            if (position is not null && page.Items.Any(candidate => candidate.CompanyId != position.CompanyId))
            {
                // The composite cursor is bound to the company that read it (the inner store cursor is too; this covers a
                // cursor whose inner cursor is the listing's start). Another company's cursor lists nothing of that company.
                throw new ArgumentException("Onaylı satır imleci bu şirketin imleci değil.", nameof(cursor));
            }

            var previousDraftCursor = draftCursor;
            SalesOrderDraft? previous = null;
            foreach (var draft in page.Items)
            {
                if (draft.ApprovalState != InternalApprovalState.Approved)
                {
                    previous = draft;
                    continue;
                }

                // The store cursor that lists THIS draft first: the page's own cursor for its first draft, else "after the previous".
                var listsThisFirst = previous is null ? previousDraftCursor : SalesStore.ListCursor.Encode(draft.CompanyId, previous.CreatedAtUtc, previous.Id);
                var skipThrough = skipDraft == draft.Id ? skipThroughLineNo : 0;
                foreach (var line in draft.Lines.OrderBy(candidate => candidate.LineNo))
                {
                    if (line.LineNo <= skipThrough)
                    {
                        continue;
                    }

                    if (items.Count == pageSize)
                    {
                        // The cap is reached with lines of this draft still unanswered: resume in this draft after the last answered line.
                        var last = items[^1];
                        var stoppedIn = last.OrderId == draft.Id ? draft : previous!;
                        var stoppedCursor = last.OrderId == draft.Id ? listsThisFirst : ListsFirst(previousDraftCursor, page, stoppedIn);
                        return new SalesApprovedLinePageV1(items, LinePosition.Encode(stoppedIn.CompanyId, stoppedCursor, stoppedIn.Id, last.LineNo));
                    }

                    items.Add(new SalesApprovedLineReferenceV1(draft.CompanyId, draft.Id, draft.DocumentNo, line.Id, line.LineNo,
                        draft.Header.PartyId, draft.Header.CompanyAccountId, line.Product.ProductId, line.Product.ProductVersion,
                        line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, line.QuantityText,
                        line.RequestedDeliveryDate, draft.Revision));
                }

                previous = draft;
            }

            if (page.NextCursor is null)
            {
                // Every approved line of the company is behind us: the last page.
                return new SalesApprovedLinePageV1(items, null);
            }

            if (items.Count == pageSize)
            {
                // The cap is reached exactly at a draft boundary: resume with the next store page, nothing to skip.
                var last = items[^1];
                var stoppedIn = page.Items.Single(candidate => candidate.Id == last.OrderId);
                return new SalesApprovedLinePageV1(items, LinePosition.Encode(stoppedIn.CompanyId, ListsFirst(previousDraftCursor, page, stoppedIn), stoppedIn.Id, last.LineNo));
            }

            draftCursor = page.NextCursor;
        }
    }

    /// <summary>The line page cap (the consumer's endpoint validates the same 1–100).</summary>
    public const int MaximumLinePageSize = 100;

    /// <summary>The store cursor under which <paramref name="draft"/> is the first draft listed: the page's own cursor, or "after its predecessor".</summary>
    private static string? ListsFirst(string? pageCursor, SalesDraftPage page, SalesOrderDraft draft)
    {
        var index = page.Items.ToList().FindIndex(candidate => candidate.Id == draft.Id);
        if (index <= 0)
        {
            return pageCursor;
        }

        var predecessor = page.Items[index - 1];
        return SalesStore.ListCursor.Encode(draft.CompanyId, predecessor.CreatedAtUtc, predecessor.Id);
    }

    /// <summary>
    /// The composite line cursor: the company, the store cursor that lists the draft first (null = the listing's start),
    /// the draft the page stopped in and the last line number answered. Opaque base64url JSON; the client never builds one.
    /// </summary>
    private sealed record LinePosition(Guid CompanyId, string? DraftCursor, Guid DraftId, int LastLineNo)
    {
        private const string Shape = "approved-lines-v1";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string Encode(Guid companyId, string? draftCursor, Guid draftId, int lastLineNo) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new Payload(Shape, companyId, draftCursor, draftId, lastLineNo), Json))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static LinePosition Decode(string cursor)
        {
            Payload? payload;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                throw new ArgumentException("Onaylı satır imleci çözülemedi.", nameof(cursor), exception);
            }

            if (payload is null || !string.Equals(payload.Shape, Shape, StringComparison.Ordinal) || payload.CompanyId == Guid.Empty
                || payload.DraftId == Guid.Empty || payload.LastLineNo < 1 || payload.DraftCursor is { Length: 0 })
            {
                throw new ArgumentException("Onaylı satır imleci bu listelemenin imleci değil.", nameof(cursor));
            }

            return new LinePosition(payload.CompanyId, payload.DraftCursor, payload.DraftId, payload.LastLineNo);
        }

        private sealed record Payload(string Shape, Guid CompanyId, string? DraftCursor, Guid DraftId, int LastLineNo);
    }

    private static readonly SalesOrderLineReferenceAnswerV1 NotFound = new(SalesOrderLineReferenceOutcomeV1.NotFound, null, null);
}
