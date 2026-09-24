using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The real <see cref="IFinancePermissionPort"/> (FIN-T04b): every check is a FRESH measurement
/// through <see cref="IErpAuthorityRevalidationService"/> (#276), so a grant revoked while a request
/// is in flight is seen at the services' next fence, replay included.
/// <list type="bullet">
/// <item><b>Allowed</b> only for the server-resolved company and actor of the request, a granted and
/// company-wide scope (the names are CompanyWideOnly: Finance records have no site dimension).</item>
/// <item><b>Denied</b> when the grant is missing or narrower, or the context is not the request's.</item>
/// <item><b>Unavailable</b> when the action has no name (only the unpublished legacy direct Reverse; FIN-T06
/// proposal/approval actions have distinct declared names) or
/// the name is not registered centrally. Record left this branch when
/// <c>finance.manual-receipt.record</c> was registered: its name is now known, so it is MEASURED, and an
/// actor without the grant is Denied. The branch itself remains for Reverse and for any future name the
/// registry does not carry yet — an unmapped action is not reported as a refusal: "I cannot tell" must not
/// look like "you may not".</item>
/// </list>
/// The role codes of each action's latest allowed measurement are kept for the ADR-009 audit
/// envelope (<see cref="RoleCodesOf"/>): a save is audited with the roles that granted the save.
/// </summary>
public sealed class FinancePermissionPort(
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : IFinancePermissionPort
{
    private readonly Dictionary<FinanceAction, IReadOnlyCollection<string>> _roleCodes =
        new Dictionary<FinanceAction, IReadOnlyCollection<string>>();

    /// <summary>
    /// A live view of the role codes behind the latest allowed decision for <paramref name="action"/>
    /// in this request; empty until one is allowed. Read when the audit event is written, after the
    /// services' fences.
    /// </summary>
    public IReadOnlyCollection<string> RoleCodesOf(FinanceAction action) => new RoleCodesView(this, action);

    public static string? NameOf(FinanceAction action) => action switch
    {
        FinanceAction.SaveDraft => FinancePermissionNames.DraftSave,
        FinanceAction.ReadSources => FinancePermissionNames.DraftRead,
        FinanceAction.ReadAmount => FinancePermissionNames.AmountRead,
        FinanceAction.Record => FinancePermissionNames.Record,
        FinanceAction.ProposeReversal => FinancePermissionNames.ReversePropose,
        FinanceAction.ApproveReversal => FinancePermissionNames.ReverseApprove,
        FinanceAction.WriteAllocation => FinancePermissionNames.AllocationWrite,
        FinanceAction.ReadBankAccounts => ErpPermissions.FinanceBankAccountRead,
        FinanceAction.ProposeBankAccount => ErpPermissions.FinanceBankAccountPropose,
        FinanceAction.ApproveBankAccount => ErpPermissions.FinanceBankAccountApprove,
        // D4: the same Ö4 names, deliberately. The IBAN change re-enters the EXISTING dual control, so
        // whoever may propose a card may propose its IBAN change and whoever may approve one may
        // approve the other. A new name here would be a grant nobody decided.
        FinanceAction.ProposeBankAccountIbanChange => ErpPermissions.FinanceBankAccountPropose,
        FinanceAction.ApproveBankAccountIbanChange => ErpPermissions.FinanceBankAccountApprove,
        FinanceAction.SaveManualBankPaymentDraft => FinancePermissionNames.ManualBankPaymentDraftSave,
        FinanceAction.ReadManualBankPayments => FinancePermissionNames.ManualBankPaymentDraftRead,
        FinanceAction.ApproveManualBankPayment => FinancePermissionNames.ManualBankPaymentApprove,
        FinanceAction.RecordManualBankPayment => FinancePermissionNames.ManualBankPaymentRecord,
        _ => null,
    };

    public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRequestContext(context))
        {
            return PermissionDecision.Denied;
        }

        if (NameOf(action) is not { } name || !ErpPermissions.IsKnown(name))
        {
            return PermissionDecision.Unavailable;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(name, context.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !IsRequestContext(context))
        {
            return PermissionDecision.Denied;
        }

        _roleCodes[action] = [.. scope.RoleCodes.Order(StringComparer.Ordinal)];
        return PermissionDecision.Allowed;
    }

    private IReadOnlyCollection<string> Current(FinanceAction action) =>
        _roleCodes.TryGetValue(action, out var codes) ? codes : [];

    private sealed class RoleCodesView(FinancePermissionPort port, FinanceAction action) : IReadOnlyCollection<string>
    {
        public int Count => port.Current(action).Count;

        public IEnumerator<string> GetEnumerator() => port.Current(action).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private bool IsRequestContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);
}
