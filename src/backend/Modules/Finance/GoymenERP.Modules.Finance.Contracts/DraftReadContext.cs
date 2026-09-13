using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

public sealed record DraftReadContextRequest(Guid TreasuryAccountId, Guid PartyId,
    Guid CompanyAccountId, DateOnly OccurredOn);
public sealed record DraftPartyLabel(Guid PartyId, Guid CompanyAccountId, string Code,
    string LegalName, string? TradeName, string PartyVersion, string CompanyAccountVersion);
public sealed record DraftEffectiveProfile(ProfileReference Reference, int Scale,
    DateOnly EffectiveFrom, DateOnly? EffectiveThrough);
public sealed record DraftCurrencyContext(Guid CurrencyId, string DisplayCode, DraftEffectiveProfile Profile);
// A preparation read response has no amount, balance, write permission or source identity.
public sealed record DraftReadContextView(Guid CompanyId, DateOnly OccurredOn,
    BankAccountView BankAccount, DraftCurrencyContext Currency, DraftPartyLabel Party);
public enum DraftReadOutcome { Found, InvalidRequest, Denied, NotFound, Unavailable }
public sealed record DraftReadContextResult(DraftReadOutcome Outcome, DraftReadContextView? Resource, string? Code);

/// <summary>
/// Composes owner reads only. Host supplies an accepted operation mapping; null remains unavailable.
/// The authenticated company and actor are derived from server context, never request data.
/// Read permission does not authorize DraftSave, Record or Reverse.
/// </summary>
public sealed class DraftReadContextReader(ICompanyContext companyContext, ICurrentUser currentUser,
    IFinancePermissionPort permissions, IFinanceBankAccountReader banks,
    IFinancePartyAccountPort parties, ISettingsPrerequisiteProvider settings)
{
    public async Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request,
        string? acceptedMoneyOperation, string correlationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasCompanyAccess()) return Fail(DraftReadOutcome.Denied);
        var context = new FinanceContext(companyContext.CompanyId, currentUser.UserId!, correlationId, null);
        var permission = await permissions.CheckAsync(context, FinanceAction.ReadBankAccounts, null, cancellationToken);
        if (permission != PermissionDecision.Allowed) return PermissionFailure(permission);
        if (request.TreasuryAccountId == Guid.Empty || request.PartyId == Guid.Empty ||
            request.CompanyAccountId == Guid.Empty)
            return Fail(DraftReadOutcome.InvalidRequest, "finance_draft_context_invalid");
        if (string.IsNullOrWhiteSpace(acceptedMoneyOperation))
            return Fail(DraftReadOutcome.Unavailable, "finance_operation_mapping_unavailable");

        var bankResult = await banks.GetAuthorizedAsync(context, request.TreasuryAccountId, cancellationToken);
        if (bankResult.Outcome == ReadOutcome.Denied) return Fail(DraftReadOutcome.Denied);
        if (bankResult.Outcome == ReadOutcome.NotFound) return Fail(DraftReadOutcome.NotFound);
        if (bankResult.Outcome != ReadOutcome.Found || bankResult.Resource is not { } bank)
            return Fail(DraftReadOutcome.Unavailable, "finance_bank_source_unavailable");
        if (bank.Id != request.TreasuryAccountId || bank.CompanyId != context.CompanyId ||
            bank.CurrencyId == Guid.Empty || !bank.IsActive || !bank.IsUsableForReceipt)
            return Fail(DraftReadOutcome.NotFound);
        if (string.IsNullOrWhiteSpace(bank.DisplayName) || string.IsNullOrWhiteSpace(bank.Version))
            return Fail(DraftReadOutcome.Unavailable, "finance_bank_source_invalid");

        var partyResult = await parties.GetAuthorizedAsync(context, request.PartyId, request.CompanyAccountId, cancellationToken);
        if (partyResult.Outcome == ReadOutcome.Denied) return Fail(DraftReadOutcome.Denied);
        if (partyResult.Outcome == ReadOutcome.NotFound) return Fail(DraftReadOutcome.NotFound);
        if (partyResult.Outcome != ReadOutcome.Found || partyResult.Resource is not { } party)
            return Fail(DraftReadOutcome.Unavailable, "finance_party_source_unavailable");
        // No role-bit requirement: the owner adapter may return an active roles0 account.
        if (party.PartyId != request.PartyId || party.CompanyAccountId != request.CompanyAccountId ||
            party.CompanyId != context.CompanyId || !party.IsActive || !party.IsPartyActive)
            return Fail(DraftReadOutcome.NotFound);
        if (party.Label is not { } label || label.PartyId != party.PartyId || label.CompanyAccountId != party.CompanyAccountId ||
            string.IsNullOrWhiteSpace(label.Code) || string.IsNullOrWhiteSpace(label.LegalName) ||
            string.IsNullOrWhiteSpace(label.PartyVersion) || string.IsNullOrWhiteSpace(label.CompanyAccountVersion))
            return Fail(DraftReadOutcome.Unavailable, "finance_party_source_invalid");

        MoneyProfileV1 profile;
        try
        {
            profile = await settings.GetMoneyProfileAsync(bank.CurrencyId, acceptedMoneyOperation,
                request.OccurredOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return exception.Code == "settings_access_denied"
                ? Fail(DraftReadOutcome.Denied)
                : Fail(DraftReadOutcome.Unavailable, exception.Code);
        }
        if (profile is null || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty ||
            profile.Profile.Version <= 0 || profile.CompanyId != context.CompanyId ||
            profile.CurrencyId != bank.CurrencyId || profile.Operation != acceptedMoneyOperation ||
            !profile.IsActive || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.CurrencyCode) ||
            request.OccurredOn < profile.EffectiveFrom ||
            (profile.EffectiveThrough is { } through && request.OccurredOn > through))
            return Fail(DraftReadOutcome.Unavailable, "settings_profile_invalid");

        if (!SameContext(context)) return Fail(DraftReadOutcome.Denied);
        permission = await permissions.CheckAsync(context, FinanceAction.ReadBankAccounts, null, cancellationToken);
        if (permission != PermissionDecision.Allowed) return PermissionFailure(permission);
        if (!SameContext(context)) return Fail(DraftReadOutcome.Denied);
        return new(DraftReadOutcome.Found, new(context.CompanyId, request.OccurredOn, bank,
            new(bank.CurrencyId, profile.CurrencyCode,
                new(new(profile.Profile.ProfileId.ToString("D"), profile.Profile.Version.ToString(CultureInfo.InvariantCulture)),
                    profile.Scale, profile.EffectiveFrom, profile.EffectiveThrough)),
            label), null);
    }

    private bool HasCompanyAccess() => currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) &&
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty && currentUser.CanAccessCompany(companyContext.CompanyId);
    private bool SameContext(FinanceContext context) => HasCompanyAccess() &&
        companyContext.CompanyId == context.CompanyId && currentUser.UserId == context.ActorId;
    private static DraftReadContextResult PermissionFailure(PermissionDecision permission) => permission == PermissionDecision.Denied
        ? Fail(DraftReadOutcome.Denied)
        : Fail(DraftReadOutcome.Unavailable, "finance_read_mapping_unavailable");
    private static DraftReadContextResult Fail(DraftReadOutcome outcome, string? code = null) => new(outcome, null, code);
}

// Concrete unavailable adapters never provide a fabricated account or grant.
public sealed class UnavailableFinancePermissionPort : IFinancePermissionPort
{
    public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PermissionDecision.Unavailable);
    }
}

public sealed class UnavailableFinanceBankAccountReader(ICompanyContext companyContext, ICurrentUser currentUser)
    : IFinanceBankAccountReader
{
    public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) =>
        Unavailable<BankAccountPage>(context, cancellationToken);
    public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId, CancellationToken cancellationToken) =>
        Unavailable<BankAccountView>(context, cancellationToken);
    private Task<ReadResult<T>> Unavailable<T>(FinanceContext context, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId) &&
            currentUser.UserId == context.ActorId && companyContext.IsResolved &&
            companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId &&
            currentUser.CanAccessCompany(context.CompanyId);
        return Task.FromResult(new ReadResult<T>(allowed ? ReadOutcome.Unavailable : ReadOutcome.Denied, null));
    }
}
