using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Api.Shell;

/// <summary>
/// The host's adapter for <see cref="IAccountingPeriodStatusPortV1"/> (FIN-T05): the period owner's answer
/// for ONE date of the caller's company, read under the CALLER's own narrow
/// <see cref="AccountingPermissionManifestV1.PeriodsRead"/> — never under an authority of the port's own,
/// which would widen privilege for every consumer that holds none.
/// <para>
/// <b>The actor fence is here from the first commit</b> (#366's lesson, where two cross-module adapters
/// re-checked the company and the session but NOT the actor after their awaited read): the company AND the
/// actor are captured before the await and compared again after it. <c>IsCurrent()</c> alone is not enough —
/// it re-checks the CURRENT actor, so a swap to another actor with access to the same company would pass it.
/// </para>
/// <para>
/// It decides nothing about WHICH date matters. Sinan's K1 (#66 5748258299) maps the manual bank receipt to
/// its <c>occurredOn</c>; that mapping is Finance's and is deliberately not visible here, so this port never
/// becomes a general accounting rule the user did not approve.
/// </para>
/// </summary>
public sealed class AccountingPeriodStatusPort(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IAccountingPeriodStore periods) : IAccountingPeriodStatusPortV1
{
    public async Task<AccountingPeriodStatusAnswerV1> GetStatusAsync(DateOnly on, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent())
        {
            return Denied;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;

        var scope = await authority.RevalidatePermissionScopeAsync(
            AccountingPermissionManifestV1.PeriodsRead, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Still(companyId, actorId))
        {
            return Denied;
        }

        AccountingPeriodRecord? record;
        try
        {
            record = await periods.FindPeriodCoveringAsync(on, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Denied;
        }

        // The owner answered for the context captured before the await — the company AND the actor.
        if (!Still(companyId, actorId))
        {
            return Denied;
        }

        if (record is null)
        {
            return new AccountingPeriodStatusAnswerV1(AccountingPeriodStatusOutcomeV1.None, null, null);
        }

        var period = record.Period;
        if (period.CompanyId != companyId)
        {
            // The tenant filter should make this impossible; a row of another company is a stored
            // contradiction, not an answer.
            return Denied;
        }

        var outcome = period.Status == AccountingPeriodStatus.Open
            ? AccountingPeriodStatusOutcomeV1.Open
            : AccountingPeriodStatusOutcomeV1.NotOpen;
        return new AccountingPeriodStatusAnswerV1(outcome, period.Id, period.Status.ToString());
    }

    private static AccountingPeriodStatusAnswerV1 Denied { get; } =
        new AccountingPeriodStatusAnswerV1(AccountingPeriodStatusOutcomeV1.Denied, null, null);

    private bool Still(Guid companyId, string? actorId) =>
        IsCurrent() && companyContext.CompanyId == companyId
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private bool IsCurrent() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
}
