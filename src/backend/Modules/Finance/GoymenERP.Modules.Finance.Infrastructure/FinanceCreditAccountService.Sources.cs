using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditAccountService
{
    public const string PlanMoneyOperation = "finance.credit-plan";

    private sealed record SourceRead(CreditAccountCommandResult? Refusal, MoneyProfileV1? MoneyProfile,
        FinanceCreditLenderAnswerV1? Lender);

    private async Task<SourceRead> SourcesAsync(FinanceContext context, FinanceAction action,
        CreditAccountProposeRequest request, CancellationToken cancellationToken)
    {
        var invalid = FinanceCreditAccountProfilePolicy.Validate(request);
        if (invalid.Count > 0) return new(Refused(context, CommandOutcome.ValidationFailed, [.. invalid]), null, null);
        var profileRead = await MoneyProfileAsync(context, action, request.CurrencyId, request.StartDate, cancellationToken);
        if (profileRead.Refusal is not null) return new(profileRead.Refusal, null, null);
        var input = request.Profile!;
        if (input.Cash?.IndexCurrencyId is { } indexCurrency
            && await CurrencyRefusalAsync(context, action, indexCurrency, cancellationToken) is { } indexedRefusal)
            return new(indexedRefusal with
            {
                Issues = indexedRefusal.Issues.Select(issue => issue with { Field = "profile.cash.indexCurrencyId" }).ToArray()
            }, null, null);
        if (lenders is null) return new(Refused(context, CommandOutcome.Unavailable, Issue("profile.lender", "credit_lender_source_unavailable")), null, null);
        FinanceCreditLenderAnswerV1? lender;
        try { lender = await lenders.ReadAsync(context.CompanyId, input.LenderPartyId, input.LenderCompanyPartyAccountId, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { lender = null; }
        if (await FenceAsync(context, action, cancellationToken) is { } fenced) return new(fenced, null, null);
        if (lender is null || lender.CompanyId != context.CompanyId || lender.PartyId != input.LenderPartyId
            || lender.CompanyPartyAccountId != input.LenderCompanyPartyAccountId
            || !Enum.IsDefined(lender.Status) || lender.Status == FinanceCreditLenderStatusV1.Unavailable)
            return new(Refused(context, CommandOutcome.Unavailable, Issue("profile.lender", "credit_lender_source_unavailable")), null, null);
        if (lender.Status != FinanceCreditLenderStatusV1.Available)
            return new(Refused(context, CommandOutcome.ValidationFailed, Issue("profile.lender", "credit_lender_not_active")), null, null);
        if (lender.PartyVersion is not > 0 || lender.CompanyAccountVersion is not > 0
            || string.IsNullOrWhiteSpace(lender.DisplayName) || lender.DisplayName.Length > LenderNameMaximumLength)
            return new(Refused(context, CommandOutcome.Unavailable, Issue("profile.lender", "credit_lender_source_unavailable")), null, null);

        if (input.InstrumentAndFactoring is { InstrumentIds.Count: > 0 } backed)
        {
            var count = await dbContext.CustomerInstruments.AsNoTracking().CountAsync(x => backed.InstrumentIds.Contains(x.Id), cancellationToken);
            if (await FenceAsync(context, action, cancellationToken) is { } after) return new(after, null, null);
            if (count != backed.InstrumentIds.Count)
                return new(Refused(context, CommandOutcome.ValidationFailed, Issue("profile.instrumentAndFactoring.instrumentIds", "credit_instrument_not_found")), null, null);
        }
        return new(null, profileRead.Profile, lender);
    }

    private async Task<(CreditAccountCommandResult? Refusal, MoneyProfileV1? Profile)> MoneyProfileAsync(
        FinanceContext context, FinanceAction action, Guid currencyId, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        MoneyProfileV1? profile = null;
        if (settings is not null)
        {
            try { profile = await settings.GetMoneyProfileAsync(currencyId, PlanMoneyOperation, effectiveOn, cancellationToken); }
            catch (SettingsPrerequisiteException exception)
            {
                if (await FenceAsync(context, action, cancellationToken) is { } denied) return (denied, null);
                return (Refused(context, exception.Code == "settings_access_denied" ? CommandOutcome.Denied : CommandOutcome.Unavailable,
                    Issue("profile", exception.Code)), null);
            }
        }
        if (await FenceAsync(context, action, cancellationToken) is { } fenced) return (fenced, null);
        if (profile is null || profile.CompanyId != context.CompanyId || profile.CurrencyId != currencyId
            || profile.Operation != PlanMoneyOperation || !profile.IsActive || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version < 1 || profile.Scale is < 0 or > 28 || profile.EffectiveFrom > effectiveOn
            || profile.EffectiveThrough is { } through && through < effectiveOn)
            return (Refused(context, CommandOutcome.Unavailable, Issue("profile", "credit_plan_profile_unavailable")), null);
        return (null, profile);
    }

    private static CreditAccountProfileV1? Profile(CreditAccountRow row) => row.SourceProfileJson is null ? null
        : JsonSerializer.Deserialize<CreditAccountProfileV1>(row.SourceProfileJson, Json)
            ?? throw new InvalidOperationException("Kredi kaynak profili okunamadı.");

    private static IReadOnlyList<CreditPlanChargeInputV1>? Charges(CreditAccountInstallmentRow row) => row.ChargesJson is null ? null
        : JsonSerializer.Deserialize<IReadOnlyList<CreditPlanChargeInputV1>>(row.ChargesJson, Json)
            ?? throw new InvalidOperationException("Banka masraf belgesi okunamadı.");

    private static CreditPlanToleranceSnapshotV1 Snapshot(MoneyProfileV1 profile) =>
        new(profile.Profile.ProfileId, profile.Profile.Version, profile.Scale, new FinanceExactDecimalText(1, profile.Scale).Text);

    private static CreditPlanToleranceSnapshotV1? Snapshot(CreditAccountRow row) =>
        row.MoneyProfileId is { } id && row.MoneyProfileVersion is { } version && row.CurrencyScale is { } scale
            ? new(id, version, scale, new FinanceExactDecimalText(1, scale).Text) : null;
}
