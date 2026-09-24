using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal interface IManualBankPaymentDraftContextTreasuryPort
{
    Task<ReadResult<ManualBankPaymentTreasuryEligibility>> GetAsync(FinanceContext context,
        Guid treasuryAccountId, CancellationToken cancellationToken);
}

internal sealed class FinanceManualBankPaymentDraftContextTreasuryPort(FinanceDbContext dbContext)
    : IManualBankPaymentDraftContextTreasuryPort
{
    private const int ApprovedBankAccountStatus = 2;

    public async Task<ReadResult<ManualBankPaymentTreasuryEligibility>> GetAsync(FinanceContext context,
        Guid treasuryAccountId, CancellationToken cancellationToken)
    {
        var row = await dbContext.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == treasuryAccountId, cancellationToken);
        if (row is null || row.CompanyId != context.CompanyId)
            return Result(ReadOutcome.NotFound);

        var approved = row.Status == ApprovedBankAccountStatus;
        var paymentEligible = approved && row.MoneyMovementEligible == true
            && !string.IsNullOrWhiteSpace(row.Iban);
        return new ReadResult<ManualBankPaymentTreasuryEligibility>(ReadOutcome.Found,
            new ManualBankPaymentTreasuryEligibility(row.Id, row.CompanyId, row.CurrencyId, approved,
                paymentEligible, row.DisplayName, row.Version.ToString(CultureInfo.InvariantCulture)));
    }

    private static ReadResult<ManualBankPaymentTreasuryEligibility> Result(ReadOutcome outcome) => new(outcome, null);
}

/// <summary>Composes only current owner reads for an R47 outgoing-payment preparation.</summary>
internal sealed class ManualBankPaymentDraftContextReader(IFinancePermissionPort permissions,
    IManualBankPaymentDraftContextTreasuryPort treasuryAccounts, IFinancePartyAccountPort parties,
    ISettingsPrerequisiteProvider settings) : IManualBankPaymentDraftContextReader
{
    public async Task<ManualBankPaymentDraftContextResult> GetAuthorizedAsync(FinanceContext context,
        ManualBankPaymentDraftContextRequest request, CancellationToken cancellationToken)
    {
        var first = await permissions.CheckAsync(context, FinanceAction.ReadManualBankPayments, null, cancellationToken);
        if (first != PermissionDecision.Allowed) return PermissionFailure(first);
        if (request.TreasuryAccountId == Guid.Empty || request.PartyId == Guid.Empty
            || request.CompanyAccountId == Guid.Empty)
            return Fail(DraftReadOutcome.InvalidRequest, "finance_manual_payment_context_invalid");

        ReadResult<ManualBankPaymentTreasuryEligibility> treasuryResult;
        try
        {
            treasuryResult = await treasuryAccounts.GetAsync(context, request.TreasuryAccountId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await FenceAsync(context, cancellationToken) is { } fenced) return fenced;
            return Fail(DraftReadOutcome.Unavailable, "finance_manual_payment_treasury_unavailable");
        }
        if (await FenceAsync(context, cancellationToken) is { } afterTreasury) return afterTreasury;
        if (treasuryResult.Outcome == ReadOutcome.Denied) return Fail(DraftReadOutcome.Denied);
        if (treasuryResult.Outcome == ReadOutcome.NotFound) return Fail(DraftReadOutcome.NotFound);
        if (treasuryResult.Outcome != ReadOutcome.Found || treasuryResult.Resource is not { } treasury)
            return Fail(DraftReadOutcome.Unavailable, "finance_manual_payment_treasury_unavailable");
        if (treasury.Id != request.TreasuryAccountId || treasury.CompanyId != context.CompanyId
            || treasury.CurrencyId == Guid.Empty || !treasury.IsActive || !treasury.IsUsableForPayment)
            return Fail(DraftReadOutcome.NotFound);
        if (string.IsNullOrWhiteSpace(treasury.DisplayName) || !PositiveVersion(treasury.Version))
            return Fail(DraftReadOutcome.Unavailable, "finance_manual_payment_treasury_invalid");

        ReadResult<PartyAccountEligibility> partyResult;
        try
        {
            partyResult = await parties.GetAuthorizedAsync(context, request.PartyId, request.CompanyAccountId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await FenceAsync(context, cancellationToken) is { } fenced) return fenced;
            return Fail(DraftReadOutcome.Unavailable, "finance_party_source_unavailable");
        }
        if (await FenceAsync(context, cancellationToken) is { } afterParty) return afterParty;
        if (partyResult.Outcome == ReadOutcome.Denied) return Fail(DraftReadOutcome.Denied);
        if (partyResult.Outcome == ReadOutcome.NotFound) return Fail(DraftReadOutcome.NotFound);
        if (partyResult.Outcome != ReadOutcome.Found || partyResult.Resource is not { } party)
            return Fail(DraftReadOutcome.Unavailable, "finance_party_source_unavailable");
        if (party.CompanyId != context.CompanyId || party.PartyId != request.PartyId
            || party.CompanyAccountId != request.CompanyAccountId || !party.IsActive || !party.IsPartyActive)
            return Fail(DraftReadOutcome.NotFound);
        if (party.Label is not { } label || label.PartyId != party.PartyId
            || label.CompanyAccountId != party.CompanyAccountId || string.IsNullOrWhiteSpace(label.Code)
            || string.IsNullOrWhiteSpace(label.LegalName) || string.IsNullOrWhiteSpace(label.PartyVersion)
            || string.IsNullOrWhiteSpace(label.CompanyAccountVersion))
            return Fail(DraftReadOutcome.Unavailable, "finance_party_source_invalid");

        MoneyProfileV1 profile;
        try
        {
            profile = await settings.GetMoneyProfileAsync(treasury.CurrencyId,
                FinanceMoneyOperations.ManualBankPaymentV1, request.OccurredOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, cancellationToken) is { } fenced) return fenced;
            return exception.Code == "settings_access_denied"
                ? Fail(DraftReadOutcome.Denied)
                : Fail(DraftReadOutcome.Unavailable, exception.Code);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await FenceAsync(context, cancellationToken) is { } fenced) return fenced;
            return Fail(DraftReadOutcome.Unavailable, "settings_provider_unavailable");
        }
        if (await FenceAsync(context, cancellationToken) is { } afterSettings) return afterSettings;
        if (profile is null || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0 || profile.CompanyId != context.CompanyId
            || profile.CurrencyId != treasury.CurrencyId
            || profile.Operation != FinanceMoneyOperations.ManualBankPaymentV1 || !profile.IsActive
            || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.CurrencyCode)
            || request.OccurredOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && request.OccurredOn > through))
            return Fail(DraftReadOutcome.Unavailable, "settings_profile_invalid");

        return new ManualBankPaymentDraftContextResult(DraftReadOutcome.Found,
            new ManualBankPaymentDraftContextView(context.CompanyId, request.OccurredOn, treasury,
                new DraftCurrencyContext(treasury.CurrencyId, profile.CurrencyCode,
                    new DraftEffectiveProfile(
                        new ProfileReference(profile.Profile.ProfileId.ToString("D"),
                            profile.Profile.Version.ToString(CultureInfo.InvariantCulture)),
                        profile.Scale, profile.EffectiveFrom, profile.EffectiveThrough)), label), null);
    }

    private async Task<ManualBankPaymentDraftContextResult?> FenceAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        var permission = await permissions.CheckAsync(context, FinanceAction.ReadManualBankPayments, null,
            cancellationToken);
        return permission == PermissionDecision.Allowed ? null : PermissionFailure(permission);
    }

    private static bool PositiveVersion(string value) => value.Length > 0 && value.All(char.IsAsciiDigit)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

    private static ManualBankPaymentDraftContextResult PermissionFailure(PermissionDecision permission) =>
        permission == PermissionDecision.Denied ? Fail(DraftReadOutcome.Denied)
            : Fail(DraftReadOutcome.Unavailable, "finance_manual_payment_read_mapping_unavailable");

    private static ManualBankPaymentDraftContextResult Fail(DraftReadOutcome outcome, string? code = null) =>
        new(outcome, null, code);
}
