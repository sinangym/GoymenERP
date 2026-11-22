using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31): the fence every Logistics Work inbox source applies before and after its read. The server-resolved company
/// and the authenticated user must be the context's, the user must still reach the company, and the source's approver
/// permission must be granted company-wide as it stands now. Anything else is a refusal without items; an authority read
/// that fails is unavailable, never a grant.
/// </summary>
internal sealed class LogisticsWorkInboxFence(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
{
    /// <summary>The instruction detail screen (L-02-DETAIL), where the dispatch notes and their kilogram records are decided.</summary>
    public const string InstructionDetailPath = "/lojistik/yukleme-talimatlari";

    /// <summary>Null when the caller may read; otherwise the outcome to return.</summary>
    public async Task<WorkInboxSourceResultV1?> CheckAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
            return scope.IsGranted && scope.IsCompanyWide && SameContext(context) ? null : WorkInboxSourceResultV1.Denied();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return WorkInboxSourceResultV1.Unavailable();
        }
    }

    /// <summary>A failed read answers the fence's refusal when the caller lost access meanwhile, else unavailable.</summary>
    public async Task<WorkInboxSourceResultV1> UnavailableAsync(
        WorkInboxSourceContextV1 context,
        string permission,
        CancellationToken cancellationToken) =>
        await CheckAsync(context, permission, cancellationToken) ?? WorkInboxSourceResultV1.Unavailable();

    public bool SameContext(WorkInboxSourceContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    /// <summary>The identity as the Logistics screens carry it (lowercase, hyphenated).</summary>
    public static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    public static string InstructionLink(Guid instructionId) => InstructionDetailPath + "/" + Id(instructionId);

    public static WorkApprovalReferenceV1 InstructionReference(Guid instructionId) =>
        new("loading-instruction", Id(instructionId), instructionId, InstructionLink(instructionId));
}
