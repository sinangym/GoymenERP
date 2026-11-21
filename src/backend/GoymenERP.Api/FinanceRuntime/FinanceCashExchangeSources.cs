using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>FNS-D59/D62: actual owner reads for a treasury quote; no accounting mapping or posting is admitted.</summary>
public static class FinanceCashExchangeSources
{
    public static IServiceCollection AddFinanceCashExchangeSources(this IServiceCollection services) =>
        services.AddScoped<IFinanceCurrencyExchangeRateRule, SettingsFinanceCashExchangeRateRuleV1>();
}

internal sealed class SettingsFinanceCashExchangeRateRuleV1(ISettingsPrerequisiteProvider moneyProfiles,
    ISettingsExchangeRateResolverV1 rates, ICompanyContext company, ICurrentUser user, IFinancePermissionPort permissions)
    : IFinanceCurrencyExchangeRateRule
{
    public async Task<CurrencyExchangeRateAnswer> CheckAsync(FinanceContext context, MoneyText sourceMoney,
        MoneyText targetMoney, string? rate, DateOnly occurredOn, CancellationToken cancellationToken)
    {
        var actor = context.ActorId;
        bool Current() => company.IsResolved && company.CompanyId != Guid.Empty && company.CompanyId == context.CompanyId
            && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(context.CompanyId);
        async Task RequireAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current()) throw new UnauthorizedAccessException();
            var answer = await permissions.CheckAsync(context, FinanceAction.CommitCashTransfer, null, cancellationToken);
            if (!Current() || answer == PermissionDecision.Denied) throw new UnauthorizedAccessException();
            if (answer != PermissionDecision.Allowed) throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }
        try
        {
            await RequireAsync();
            var sourceProfile = await ProfileAsync(sourceMoney);
            var targetProfile = await ProfileAsync(targetMoney);
            var source = await ComparisonAsync(sourceMoney.CurrencyId);
            var target = await ComparisonAsync(targetMoney.CurrencyId);
            var snapshot = CashTransferExchangeCalculatorV1.Calculate(context.CompanyId, occurredOn, sourceMoney, targetMoney,
                Reference(sourceProfile), Reference(targetProfile), source, target);
            await RequireAsync();
            if (rate is not null && !CashTransferExchangeCalculatorV1.SameRate(rate, snapshot.ExactTransactionRate))
                return new(CurrencyExchangeRateOutcome.Inconsistent, CurrencyExchangeRateAnswer.InconsistentCode);
            return new(CurrencyExchangeRateOutcome.Consistent, null, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { return new(CurrencyExchangeRateOutcome.Denied, null); }
        catch (SettingsPrerequisiteException ex) when (ex.Code == "settings_access_denied")
        { return new(CurrencyExchangeRateOutcome.Denied, null); }
        catch
        {
            // An owner failure must not mask authority that was lost during its awaited read.
            try { await RequireAsync(); }
            catch (UnauthorizedAccessException) { return new(CurrencyExchangeRateOutcome.Denied, null); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return new(CurrencyExchangeRateOutcome.Unavailable, CurrencyExchangeRateAnswer.UnavailableCode); }
            return new(CurrencyExchangeRateOutcome.Unavailable, CurrencyExchangeRateAnswer.UnavailableCode);
        }

        async Task<MoneyProfileV1> ProfileAsync(MoneyText amount)
        {
            var profile = await moneyProfiles.GetMoneyProfileAsync(amount.CurrencyId, FinanceMoneyOperations.CashTransferV1,
                occurredOn, cancellationToken);
            await RequireAsync();
            if (profile is null || CashTransferValidator.ValidateLegProfile("profile", "amount", amount, Reference(profile),
                occurredOn, context.CompanyId, profile, FinanceMoneyOperations.CashTransferV1).Count != 0)
                throw new SettingsPrerequisiteException("settings_profile_invalid");
            return profile;
        }
        async Task<CashTransferComparisonSourceV1> ComparisonAsync(Guid currency)
        {
            await RequireAsync();
            if (currency == SettingsCurrencyIdentitiesV1.TurkishLira)
                return new(currency, "1", occurredOn, "FunctionalCurrency", null, null, null, null);
            var answer = await rates.ResolveAsync(occurredOn, currency, SettingsCurrencyIdentitiesV1.TurkishLira,
                cancellationToken);
            await RequireAsync();
            // No same-day/manual/fallback quote is substituted for the full Settings source identity.
            if (answer is null || answer.State != ExchangeRateResolutionStatesV1.Resolved || answer.Source != ExchangeRateSourcesV1.Tcmb
                || answer.RateDay != occurredOn || answer.SourceCurrencyId != currency
                || answer.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira || answer.RecordId is null || answer.RecordId == Guid.Empty
                || string.IsNullOrWhiteSpace(answer.RecordVersion) || answer.BulletinDate is null || answer.BulletinDate >= occurredOn
                || !CashTransferValidator.IsRateText(answer.SellingRate))
                throw new SettingsPrerequisiteException("settings_provider_unavailable");
            return new(currency, answer.SellingRate!, occurredOn, answer.Source, answer.RecordId, answer.RecordVersion,
                answer.BulletinDate, answer.Provenance);
        }
        static ProfileReference Reference(MoneyProfileV1 profile) => new(profile.Profile.ProfileId.ToString("D"),
            profile.Profile.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
