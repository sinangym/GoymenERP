using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Shell;

/// <summary>
/// The host's adapter for <see cref="IFinancePartyAccountPortV2"/> (B-17): the Counterparty owner's authorized
/// party read, narrowed to one company account of the caller's company, with that account's roles (dual role,
/// DEC-CAR-001) and its supplier IBAN view.
/// <para>
/// <b>Fences, as in the V1 adapter:</b> the company and the actor of the <see cref="FinanceContext"/> must be the
/// request's own before the owner read AND after it; if either changed while the read was pending, neither the
/// owner's data, its NotFound nor its failure is released — the answer is Denied.
/// </para>
/// <para>
/// <b>Roles are mapped flag by flag.</b> An owner role this adapter does not know makes the answer Unavailable
/// instead of being dropped or cast, so Finance never reads a narrower role set than the owner holds.
/// </para>
/// <para>
/// <b>Supplier IBAN:</b> Counterparty holds no approved supplier IBAN yet (FNS-D15: "ertelendi: Cari başlayınca"),
/// so a supplier account answers <see cref="FinanceSupplierIbanState.SourceUnavailable"/>. When the owner provides
/// its approved-IBAN read, it is classified here through <see cref="FinanceSupplierIbanRules"/>; this adapter does
/// not validate or invent anything itself.
/// </para>
/// </summary>
public sealed class CounterpartyFinancePartyAccountPortV2(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : IFinancePartyAccountPortV2
{
    private static readonly (PartyRole Owner, FinancePartyAccountRoles Finance)[] RoleMap =
    [
        (PartyRole.Customer, FinancePartyAccountRoles.Customer),
        (PartyRole.Supplier, FinancePartyAccountRoles.Supplier),
        (PartyRole.Tolling, FinancePartyAccountRoles.Tolling),
        (PartyRole.Service, FinancePartyAccountRoles.Service),
    ];

    public async Task<ReadResult<PartyAccountEligibilityV2>> GetAuthorizedAsync(FinanceContext context,
        Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        // Without a usable context or ids nothing is read (as the Purchasing party port).
        if (partyId == Guid.Empty || companyAccountId == Guid.Empty || !IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(ReadOutcome.Denied, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrent(context)) return new(ReadOutcome.Denied, null);
            throw;
        }

        // The owner answered for the context captured before the await. If company or actor changed meanwhile,
        // neither its data nor its NotFound may be released.
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId
            && account.CompanyId == context.CompanyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var account = accounts[0];
        if (!TryMapRoles(account.Roles, out var roles))
        {
            return new(ReadOutcome.Unavailable, null);
        }

        return new(ReadOutcome.Found, new(
            party.Id,
            account.Id,
            account.CompanyId,
            account.IsActive,
            party.IsActive,
            roles,
            new(party.Id, account.Id, party.Code, party.LegalName, party.TradeName, party.Version, account.Version),
            FinanceSupplierIbanRules.Classify(roles, FinanceSupplierIbanSourceAnswer.Unavailable)));
    }

    /// <summary>Maps each known owner flag; false when the owner holds a flag Finance does not know.</summary>
    internal static bool TryMapRoles(PartyRole ownerRoles, out FinancePartyAccountRoles roles)
    {
        roles = FinancePartyAccountRoles.None;
        var remaining = ownerRoles;
        foreach (var (owner, finance) in RoleMap)
        {
            if ((ownerRoles & owner) == 0)
            {
                continue;
            }

            roles |= finance;
            remaining &= ~owner;
        }

        if (remaining != PartyRole.None)
        {
            roles = FinancePartyAccountRoles.None;
            return false;
        }

        return true;
    }

    private bool IsCurrent(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == context.ActorId && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}
