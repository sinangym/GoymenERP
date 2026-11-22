using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure.Ai;

/// <summary>
/// WP-A3c shared pieces of the Finance READ tools: the owner context built only from the server's AI context, the
/// owner read outcome mapping, and the currency code of a stored <c>CurrencyId</c> (para birimi kodu açık).
/// </summary>
internal static class FinanceAiReadSupport
{
    public const string InvalidArguments = "arguments";

    /// <summary>The Finance owner context: company, actor and correlation from the server only, never from arguments.</summary>
    public static FinanceContext Context(AiServerContextV1 context) =>
        new(context.CompanyId, context.ActorId, context.CorrelationId, context.CausationId);

    /// <summary>A refused owner read: Denied stays Denied; every other non-Found answer releases nothing (Unavailable).</summary>
    public static AiReadResultV1 Refusal(ReadOutcome outcome) =>
        new(outcome == ReadOutcome.Denied ? AiToolOutcomeV1.Denied : AiToolOutcomeV1.Unavailable);

    /// <summary>
    /// The ISO code of each currency identity, from the Settings owner only: its money currency candidates (the owner's
    /// real codes) and its published currency identities (TRY, USD, EUR). An id neither answers stays unresolved — the
    /// tool then withholds that amount and says so; a code is never guessed. An unreachable candidate source leaves
    /// only the published identities.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> CurrencyCodesAsync(
        ISettingsMoneyCandidateReader? candidates, CancellationToken cancellationToken)
    {
        var codes = new Dictionary<Guid, string>
        {
            [SettingsCurrencyIdentitiesV1.TurkishLira] = "TRY",
            [SettingsCurrencyIdentitiesV1.UnitedStatesDollar] = "USD",
            [SettingsCurrencyIdentitiesV1.Euro] = "EUR",
        };
        if (candidates is null)
        {
            return codes;
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> listed;
        try
        {
            listed = MoneyCurrencyCandidatesV1.EnsureWellFormed(await candidates.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return codes;
        }

        foreach (var candidate in listed)
        {
            var code = candidate.Code.Trim();
            if (!ExchangeRateCurrencyIdentitiesV1.IsIsoCode(code))
            {
                continue;
            }

            // A candidate that contradicts a published identity is not trusted for either id.
            if (codes.TryGetValue(candidate.CurrencyId, out var known) && !string.Equals(known, code, StringComparison.Ordinal))
            {
                codes.Remove(candidate.CurrencyId);
                continue;
            }

            codes[candidate.CurrencyId] = code;
        }

        return codes;
    }
}
