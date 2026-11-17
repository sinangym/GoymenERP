using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>KLN-D38 evidence pinned to an already authorized, clean document of either exact legal party.</summary>
public sealed class DocumentsFinancePartyBalanceTransferEvidencePortV1(IDocumentService documents,
    ICompanyContext company, ICurrentUser user) : IFinancePartyBalanceTransferEvidencePortV1
{
    public async Task<ReadOutcome> CheckAsync(FinanceContext context, FinancePartyBalanceTransferOwnerProofV1 owner,
        FinancePartyBalanceTransferEvidenceV1 evidence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Same()) return ReadOutcome.Denied;
        if (owner.CompanyId != context.CompanyId || evidence.DocumentId == Guid.Empty || evidence.VersionNo is < 1 or int.MaxValue)
            return ReadOutcome.NotFound;
        try
        {
            var result = await documents.ListVersionsAsync(evidence.DocumentId, 1,
                checked(evidence.VersionNo + 1).ToString(CultureInfo.InvariantCulture), cancellationToken);
            if (!Same()) return ReadOutcome.Denied;
            if (result.DocumentId != evidence.DocumentId || result.Versions is not { Count: 1 }) return ReadOutcome.NotFound;
            var version = result.Versions[0];
            return version.Id == evidence.DocumentId && version.CompanyId == context.CompanyId
                && version.VersionNo == evidence.VersionNo && version.Downloadable
                && string.Equals(version.Sha256, evidence.Sha256, StringComparison.OrdinalIgnoreCase)
                && version.OwnerKind == DocumentOwnerLinkKindsV1.CounterpartyParty
                && (version.OwnerId == owner.PredecessorPartyId || version.OwnerId == owner.SuccessorPartyId)
                ? ReadOutcome.Found : ReadOutcome.NotFound;
        }
        catch (DocumentServiceException exception)
        { return exception.Status switch { 401 or 403 => ReadOutcome.Denied, 404 => ReadOutcome.NotFound, _ => ReadOutcome.Unavailable }; }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return ReadOutcome.Unavailable; }

        bool Same() => company.IsResolved && company.CompanyId == context.CompanyId && user.IsAuthenticated
            && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
    }
}
