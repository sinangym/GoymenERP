using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Reads a real clean immutable Documents version, under both existing owner and occurrence authority.
/// No parsing, line/quantity/date inference, owner link, approval, stock or commercial admission is performed.</summary>
public sealed class PurchaseUnorderedAcceptanceDocumentReadV1(IDocumentService documents, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority) : IPurchaseUnorderedAcceptanceDocumentReadV1
{
    public async Task<PurchaseUnorderedAcceptanceDocumentAnswerV1> ReadAsync(Guid companyId,
        PurchaseAcceptanceDocumentEvidenceV1 evidence, Func<CancellationToken, Task> requireStill, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(requireStill);
        var actor = user.UserId;
        try
        {
            await Fence();
            if (evidence is null || evidence.DocumentId == Guid.Empty || evidence.DocumentVersion is < 1 or > int.MaxValue
                || evidence.Sha256 is not { Length: 64 } || !evidence.Sha256.All(char.IsAsciiHexDigit)) return Answer(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable);
            DocumentContentV1 content;
            try { content = await documents.GetVersionContentAsync(evidence.DocumentId, (int)evidence.DocumentVersion, token); }
            catch (OperationCanceledException) { throw; }
            catch { await Fence(); throw; }
            await Fence();
            // The owner owns/disposes any storage stream; this adapter neither returns nor retains its byte array.
            var actualHash = content?.Content is { Length: > 0 } bytes ? Convert.ToHexString(SHA256.HashData(bytes)) : null;
            if (content is null || content.DocumentId != evidence.DocumentId || actualHash is null
                || !string.Equals(content.Sha256, actualHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(evidence.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
                return Answer(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable);
            return new(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Found,
                new(companyId, content.DocumentId, evidence.DocumentVersion, actualHash.ToLowerInvariant()));
        }
        catch (OperationCanceledException) { throw; }
        catch (DocumentServiceException error)
        {
            return Answer(error.Status is 401 or 403 ? PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied
                : error.Status == 404 ? PurchaseUnorderedAcceptanceEvidenceOutcomeV1.NotFound : PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable);
        }
        catch (PurchasingServiceException error)
        {
            return Answer(error.Status is 401 or 403 ? PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Denied : PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable);
        }
        catch { return Answer(PurchaseUnorderedAcceptanceEvidenceOutcomeV1.Unavailable); }

        async Task Fence()
        {
            token.ThrowIfCancellationRequested(); Same();
            await requireStill(token); token.ThrowIfCancellationRequested(); Same();
            var permission = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.GoodsReceiptsRecord, companyId, token);
            token.ThrowIfCancellationRequested(); Same();
            if (permission is not { IsGranted: true, IsCompanyWide: true }) throw new PurchasingServiceException(403, "forbidden");
        }
        void Same()
        {
            if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
                throw new PurchasingServiceException(403, "forbidden");
        }
    }
    private static PurchaseUnorderedAcceptanceDocumentAnswerV1 Answer(PurchaseUnorderedAcceptanceEvidenceOutcomeV1 outcome) => new(outcome, null);
}
