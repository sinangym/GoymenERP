using GoymenERP.Api.Ai;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>Tier 3 result. The text is external content and goes through the quarantine like any other.</summary>
public sealed record AiOcrResultV1(bool Recognized, string? Text);

/// <summary>
/// Tier 3 port: local OCR of a scanned image or an image-only PDF. No OCR engine is installed yet; the default
/// <see cref="UnavailableAiDocumentOcr"/> recognizes nothing, so a scanned document is reported unreadable instead of
/// being sent anywhere. A real engine must run locally (no cloud upload of a document, AI-D34 d) — adding one is a
/// separate deployment step.
/// </summary>
public interface IAiDocumentOcrV1
{
    bool IsAvailable { get; }

    Task<AiOcrResultV1> RecognizeAsync(ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken = default);
}

public sealed class UnavailableAiDocumentOcr : IAiDocumentOcrV1
{
    public bool IsAvailable => false;

    public Task<AiOcrResultV1> RecognizeAsync(ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiOcrResultV1(false, null));
}

/// <summary>
/// What tier 4 receives: the quarantined document text and the header field codes still open. Deliberately NO tool
/// list, no company, actor, recipient or permission: the local model can only suggest values.
/// </summary>
public sealed record AiLocalModelFieldRequestV1(AiQuarantinedTextV1 Content, IReadOnlyList<string> OpenFieldCodes);

/// <summary>
/// Untrusted tier 4 output. <see cref="ToolCalls"/> exists so a steered model's attempt is SEEN and refused: when it is
/// not empty the whole response is discarded (<see cref="AiExternalContentQuarantineV1.AdmitModelOutput"/>).
/// </summary>
public sealed record AiLocalModelFieldResponseV1(
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<AiPlannedToolCallV1> ToolCalls);

/// <summary>
/// Tier 4 port: a LOCAL model for header fields the deterministic tiers left open (B.3 "yalnız anlamlandırılması
/// gereken ya da belirsiz kalan alanlarda yerel model"). Closed until the hardware decision (OPEN-014, DALGA_PLANI_v2
/// A.7): the default <see cref="ClosedAiDocumentLocalModel"/> is never available, and the service also requires
/// <c>Ai:DocumentDrafts:LocalModelEnabled</c>. A suggestion is never "verified" on its own.
/// </summary>
public interface IAiDocumentLocalModelV1
{
    bool IsAvailable { get; }

    Task<AiLocalModelFieldResponseV1> SuggestAsync(AiLocalModelFieldRequestV1 request, CancellationToken cancellationToken = default);
}

public sealed class ClosedAiDocumentLocalModel : IAiDocumentLocalModelV1
{
    public bool IsAvailable => false;

    public Task<AiLocalModelFieldResponseV1> SuggestAsync(AiLocalModelFieldRequestV1 request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Yerel model kapalı (OPEN-014).");
}

public enum AiDraftMatchOutcomeV1 { Matched = 1, NotFound = 2, Ambiguous = 3, Unavailable = 4 }

public sealed record AiCounterpartyMatchV1(AiDraftMatchOutcomeV1 Outcome, Guid? PartyId);

/// <summary>The counterparty role the draft kind needs: a customer for a sales order, a supplier otherwise.</summary>
public enum AiDraftCounterpartyRoleV1 { Customer = 1, Supplier = 2 }

/// <summary>
/// Counterparty matching by tax identity, answered by the Counterparty owner. The owner does not yet publish an exact
/// tax-identity lookup port, so the default <see cref="UnconfiguredAiDocumentCounterpartyMatcher"/> answers
/// unavailable and the counterparty stays "doğrulanmadı" (never a fuzzy name match).
/// </summary>
public interface IAiDocumentCounterpartyMatcherV1
{
    Task<AiCounterpartyMatchV1> MatchByTaxIdAsync(string taxId, AiDraftCounterpartyRoleV1 role, CancellationToken cancellationToken = default);
}

public sealed class UnconfiguredAiDocumentCounterpartyMatcher : IAiDocumentCounterpartyMatcherV1
{
    public Task<AiCounterpartyMatchV1> MatchByTaxIdAsync(string taxId, AiDraftCounterpartyRoleV1 role, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiCounterpartyMatchV1(AiDraftMatchOutcomeV1.Unavailable, null));
}

public sealed record AiProductMatchV1(AiDraftMatchOutcomeV1 Outcome, Guid? ProductId, string? ProductVersion, IReadOnlyList<string> UnitCodes)
{
    public static AiProductMatchV1 Of(AiDraftMatchOutcomeV1 outcome) => new(outcome, null, null, []);
}

/// <summary>Product matching by OUR product code, answered by the Product owner's bounded candidate ports.</summary>
public interface IAiDocumentProductMatcherV1
{
    Task<AiProductMatchV1> MatchByCodeAsync(string code, AiDraftCounterpartyRoleV1 role, DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Matches through the Product owner's operational candidate ports (<see cref="IProductSalesCandidatesV1"/> for a
/// sales order, <see cref="IProductPurchaseCandidatesV1"/> for supplier documents), which apply their own
/// operational-selection authority. Only one candidate whose code equals the document's code (ordinal, case-insensitive)
/// is a match; none or several stay unverified. The owner's save revalidates the chosen product anyway.
/// </summary>
public sealed class ProductCandidatesAiDocumentProductMatcher(
    IProductSalesCandidatesV1 salesCandidates,
    IProductPurchaseCandidatesV1 purchaseCandidates) : IAiDocumentProductMatcherV1
{
    /// <summary>The candidate window examined per code; a full window is not treated as exhaustive.</summary>
    private const int CandidateWindow = 25;

    public async Task<AiProductMatchV1> MatchByCodeAsync(string code, AiDraftCounterpartyRoleV1 role, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (role == AiDraftCounterpartyRoleV1.Customer)
            {
                var sales = await salesCandidates.SearchAsync(new ProductSalesCandidateQueryV1(code, effectiveOn, CandidateWindow), cancellationToken);
                var exact = sales.Items.Where(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)).ToArray();
                return exact.Length switch
                {
                    1 => new(AiDraftMatchOutcomeV1.Matched, exact[0].ProductId, exact[0].ProductVersion,
                        [.. exact[0].OrderUnits.Select(unit => unit.Code)]),
                    0 when sales.SearchWindowFull => AiProductMatchV1.Of(AiDraftMatchOutcomeV1.Ambiguous),
                    0 => AiProductMatchV1.Of(AiDraftMatchOutcomeV1.NotFound),
                    _ => AiProductMatchV1.Of(AiDraftMatchOutcomeV1.Ambiguous),
                };
            }

            var purchase = await purchaseCandidates.SearchAsync(new ProductPurchaseCandidateQueryV1(code, effectiveOn, CandidateWindow), cancellationToken);
            var products = purchase.Items.Where(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.ProductId).ToArray();
            return products.Length switch
            {
                1 => new(AiDraftMatchOutcomeV1.Matched, products[0].Key, products[0].First().ProductVersion,
                    [.. products[0].Select(item => item.UnitCode).Distinct(StringComparer.Ordinal)]),
                0 when purchase.SearchWindowFull => AiProductMatchV1.Of(AiDraftMatchOutcomeV1.Ambiguous),
                0 => AiProductMatchV1.Of(AiDraftMatchOutcomeV1.NotFound),
                _ => AiProductMatchV1.Of(AiDraftMatchOutcomeV1.Ambiguous),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The owner refused (no operational-selection grant) or could not answer: the line stays unverified.
            return AiProductMatchV1.Of(AiDraftMatchOutcomeV1.Unavailable);
        }
    }
}

/// <summary>
/// Stores the source document through the object-storage abstraction (AGENTS.md: S3-compatible object storage
/// abstraction; <see cref="IDocumentObjectStoreV1"/>), content-addressed per company. Nothing else is written: no
/// metadata row, no record. How long a draft's source is kept is a retention policy that is not decided yet; the store
/// itself never deletes (OPEN-018/021).
/// </summary>
public interface IAiDocumentSourceStoreV1
{
    Task<bool> TryStoreAsync(Guid companyId, ReadOnlyMemory<byte> content, string sha256, CancellationToken cancellationToken = default);
}

public sealed class ObjectStoreAiDocumentSourceStore(IDocumentObjectStoreV1 store) : IAiDocumentSourceStoreV1
{
    public async Task<bool> TryStoreAsync(Guid companyId, ReadOnlyMemory<byte> content, string sha256, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.PutAsync(DocumentObjectKeys.For(companyId, sha256), content, sha256, cancellationToken);
            return true;
        }
        catch (DocumentObjectStoreUnavailableException)
        {
            return false;
        }
    }
}
