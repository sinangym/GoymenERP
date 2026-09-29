namespace GoymenERP.BuildingBlocks.Search;

/// <summary>
/// AT-002 / ERP-000I (DEC-TEC-005): one module's answer to the shell's global search. The platform composer (the API
/// host) fans a search term out to every registered source and merges the answers into groups; it reads no module's
/// table itself. Each source is owned by the module whose records it answers and:
/// <list type="bullet">
/// <item>measures ITS OWN read permission for the server-resolved company before anything is read (DEC-TEC-005: "Yetki
/// backend'de yeniden kontrol edilir") and answers <see cref="GlobalSearchSourceOutcomeV1.NotAuthorized"/> — with no hit,
/// no count and no "more" — when it is not held, so an unauthorized kind leaks nothing (AT-002);</item>
/// <item>reads only the context company's records (its tenant filter), never another company's;</item>
/// <item>answers at most <see cref="GlobalSearchRequestV1.Limit"/> hits and says whether more exist, never a total;</item>
/// <item>is PRICE-FREE by construction: <see cref="GlobalSearchHitV1"/> has no price, amount, cost or currency member;</item>
/// <item>writes nothing: no attempt, audit, version or read-tracking effect.</item>
/// </list>
/// A source that cannot answer (its owner is unavailable or answered malformed) throws; the composer then answers the
/// whole search as unavailable rather than a partial page that would read as "no results".
/// </summary>
public interface IGlobalSearchSourceV1
{
    /// <summary>The stable kind this source answers (one of <see cref="GlobalSearchKindsV1"/>).</summary>
    string Kind { get; }

    Task<GlobalSearchSourceAnswerV1> SearchAsync(GlobalSearchRequestV1 request, CancellationToken cancellationToken = default);
}

/// <summary>The kinds the first release searches, in the order the shell groups them.</summary>
public static class GlobalSearchKindsV1
{
    /// <summary>A Cari party with an account in the context company (Counterparty owner).</summary>
    public const string CounterpartyParty = "counterparty.party";

    /// <summary>A product card of the group master (Product owner).</summary>
    public const string ProductItem = "product.item";

    /// <summary>A sales order (draft, approved or cancelled) of the context company (Sales owner).</summary>
    public const string SalesOrder = "sales.order";

    public static IReadOnlyList<string> Ordered { get; } = [CounterpartyParty, ProductItem, SalesOrder];
}

/// <summary>A normalized term (trimmed, NFC, within the composer's bounds) and the per-kind hit bound.</summary>
public sealed record GlobalSearchRequestV1(string Term, int Limit);

public enum GlobalSearchSourceOutcomeV1
{
    Answered = 1,
    NotAuthorized = 2,
}

/// <summary>One source's answer: its hits (at most the request's limit) and whether more exist. No total count.</summary>
public sealed record GlobalSearchSourceAnswerV1(GlobalSearchSourceOutcomeV1 Outcome, IReadOnlyList<GlobalSearchHitV1> Hits, bool HasMore)
{
    public static GlobalSearchSourceAnswerV1 NotAuthorized { get; } = new(GlobalSearchSourceOutcomeV1.NotAuthorized, [], false);

    public static GlobalSearchSourceAnswerV1 Answered(IReadOnlyList<GlobalSearchHitV1> hits, bool hasMore) =>
        new(GlobalSearchSourceOutcomeV1.Answered, hits, hasMore);
}

/// <summary>
/// One hit, as its owner states it: the record id (the owner screen's route id), its code or document number, its
/// name when it has one, a second name (a trade name) when it has one, the owner's state name, a business date when the
/// record has one, and — for a company-owned record — the company it belongs to (the composer refuses a hit of another
/// company). Identities and text only.
/// </summary>
public sealed record GlobalSearchHitV1(
    Guid Id,
    string Code,
    string? Name,
    string? SecondaryName,
    string State,
    DateOnly? Date,
    Guid? CompanyId);
