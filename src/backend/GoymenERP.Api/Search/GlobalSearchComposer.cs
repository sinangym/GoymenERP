using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;

namespace GoymenERP.Api.Search;

/// <summary>GET /api/v1/search — the shell's global search (AT-002, ERP-000I, DEC-TEC-005).</summary>
public static class GlobalSearchApiV1
{
    public const string Path = "/api/v1/search";
    public const string QueryParameter = "q";

    /// <summary>Shorter terms match too broadly to be useful and are refused (400), never answered with everything.</summary>
    public const int MinimumTermLength = 2;

    /// <summary>The owners' own list searches accept up to 200 characters; the shell box needs far less.</summary>
    public const int MaximumTermLength = 100;

    /// <summary>
    /// Hits per kind. Bounded on purpose (no total, no deep paging here): "more" sends the user to the owner's list, which
    /// has its own keyset paging. A technical UX bound, not a performance SLA — OPEN-015 keeps the p95 target open.
    /// </summary>
    public const int PerKindLimit = 5;
}

/// <summary>The search answer: the normalized term and one group per kind the actor may read, in the fixed kind order.</summary>
public sealed record GlobalSearchResponseV1(string Query, IReadOnlyList<GlobalSearchGroupV1> Groups);

/// <summary>
/// One kind's hits (at most <see cref="GlobalSearchApiV1.PerKindLimit"/>) and whether its owner holds more. A kind the actor
/// may not read has NO group — not an empty one, not a count (AT-002).
/// </summary>
public sealed record GlobalSearchGroupV1(string Kind, IReadOnlyList<GlobalSearchItemV1> Items, bool HasMore);

/// <summary>One hit: the owner record's id, code/number, names, the owner's state name and a business date. Price-free.</summary>
public sealed record GlobalSearchItemV1(Guid Id, string Code, string? Name, string? SecondaryName, string State, DateOnly? Date);

/// <summary>A source could not answer, or answered against its contract: the whole search is 503, never a partial page.</summary>
public sealed class GlobalSearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The platform composer of the global search. It owns NO data: it fans the normalized term out to the module-owned
/// <see cref="IGlobalSearchSourceV1"/> sources (each measuring its own read permission and reading its own company-filtered
/// records), checks every answer against the contract, and merges them into groups in <see cref="GlobalSearchKindsV1.Ordered"/>.
/// <list type="bullet">
/// <item>Exactly one source per first-release kind must be registered; a missing, duplicate or unknown source is a setup
/// gap (503), not a silently narrower search.</item>
/// <item>A NotAuthorized answer contributes nothing — its group is absent (AT-002).</item>
/// <item>An answered hit must be well-formed, within the limit, unique by id, and — when the owner states a company — the
/// context company's; anything else is 503 (fail closed, nothing guessed onto the wire).</item>
/// <item>Sources are asked one after another: each owner uses its own scoped DbContext, and none may run concurrently with
/// another read of the same request scope.</item>
/// </list>
/// Reading writes nothing.
/// </summary>
public sealed class GlobalSearchComposer(IEnumerable<IGlobalSearchSourceV1> sources, ICompanyContext companyContext)
{
    public async Task<GlobalSearchResponseV1> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new GlobalSearchUnavailableException("Global arama yalnız çözümlenmiş şirket bağlamında çalışır.");
        }

        var companyId = companyContext.CompanyId;
        var registered = sources.ToArray();
        if (registered.Any(source => source is null || !GlobalSearchKindsV1.Ordered.Contains(source.Kind))
            || registered.GroupBy(source => source.Kind).Any(group => group.Count() != 1)
            || GlobalSearchKindsV1.Ordered.Any(kind => registered.All(source => source.Kind != kind)))
        {
            throw new GlobalSearchUnavailableException("Global arama kaynakları eksik ya da yinelenmiş.");
        }

        var request = new GlobalSearchRequestV1(term, GlobalSearchApiV1.PerKindLimit);
        var groups = new List<GlobalSearchGroupV1>(GlobalSearchKindsV1.Ordered.Count);
        foreach (var kind in GlobalSearchKindsV1.Ordered)
        {
            var source = registered.Single(candidate => candidate.Kind == kind);
            GlobalSearchSourceAnswerV1? answer;
            try
            {
                answer = await source.SearchAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GlobalSearchUnavailableException($"Global arama kaynağı yanıt vermedi: {kind}", exception);
            }

            var group = Checked(kind, answer, companyId);
            if (group is not null)
            {
                groups.Add(group);
            }
        }

        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            throw new GlobalSearchUnavailableException("Şirket bağlamı arama sırasında değişti.");
        }

        return new GlobalSearchResponseV1(term, groups);
    }

    private static GlobalSearchGroupV1? Checked(string kind, GlobalSearchSourceAnswerV1? answer, Guid companyId)
    {
        if (answer is null || answer.Hits is null)
        {
            throw new GlobalSearchUnavailableException($"Global arama kaynağı boş yanıt verdi: {kind}");
        }

        if (answer.Outcome == GlobalSearchSourceOutcomeV1.NotAuthorized)
        {
            // Not held: nothing of this kind leaves the server — no hit, no "more", no empty group (AT-002).
            if (answer.Hits.Count != 0 || answer.HasMore)
            {
                throw new GlobalSearchUnavailableException($"Yetkisiz kaynak sonuç taşıdı: {kind}");
            }

            return null;
        }

        if (answer.Outcome != GlobalSearchSourceOutcomeV1.Answered || answer.Hits.Count > GlobalSearchApiV1.PerKindLimit
            || answer.Hits.Any(hit => hit is null || hit.Id == Guid.Empty || string.IsNullOrWhiteSpace(hit.Code)
                || string.IsNullOrWhiteSpace(hit.State) || (hit.CompanyId is { } owner && owner != companyId))
            || answer.Hits.Select(hit => hit.Id).Distinct().Count() != answer.Hits.Count)
        {
            throw new GlobalSearchUnavailableException($"Global arama kaynağı sözleşme dışı yanıt verdi: {kind}");
        }

        return new GlobalSearchGroupV1(kind,
            [.. answer.Hits.Select(hit => new GlobalSearchItemV1(hit.Id, hit.Code, hit.Name, hit.SecondaryName, hit.State, hit.Date))],
            answer.HasMore);
    }
}
