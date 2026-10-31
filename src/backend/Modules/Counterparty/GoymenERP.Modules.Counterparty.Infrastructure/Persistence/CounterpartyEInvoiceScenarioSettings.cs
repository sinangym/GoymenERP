using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D102: Counterparty's reading of the Settings-owned e-Fatura scenario keys, through the Settings port only. The
/// mapping is fail-closed: a value is <c>Set</c> only when the owner served it for THIS company and in the published
/// shape; Settings' own "not found" codes are <c>NotSet</c> (an empty key, "kaynak yok"); anything else — denied,
/// malformed, not composed in this host, or failed — is <c>Unavailable</c>, never an empty list or a default.
/// </summary>
internal static class CounterpartyEInvoiceScenarioSettings
{
    /// <summary>SAT-D10's business day (Europe/Istanbul); the Settings windows are read for this day.</summary>
    private const string BusinessTimeZoneId = "Europe/Istanbul";

    private static readonly Lazy<TimeZoneInfo> BusinessZone = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"İş günü saat dilimi ({BusinessTimeZoneId}) bu sunucuda çözülemedi; tarih UTC'ye düşürülmez.", exception);
        }
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public static DateOnly BusinessDateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, BusinessZone.Value).DateTime);

    public static async Task<CodeListRead> ReadCodesAsync(
        ISettingsEInvoiceScenarioPolicyProviderV1? provider,
        Guid companyId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return CodeListRead.Unavailable;
        }

        try
        {
            var list = await provider.GetEInvoiceScenarioCodesAsync(effectiveOn, cancellationToken);
            if (list is null
                || list.CompanyId != companyId
                || list.Codes is null
                || list.Codes.Count == 0
                || list.Codes.Any(code => !EInvoiceScenarioCodeFormatV1.IsWellFormed(code))
                || list.Codes.Distinct(StringComparer.Ordinal).Count() != list.Codes.Count
                || list.EffectiveFrom > effectiveOn
                || (list.EffectiveThrough is DateOnly end && end < effectiveOn))
            {
                return CodeListRead.Unavailable;
            }

            return new CodeListRead(CounterpartySettingsSourceStateV1.Set, list.Codes.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SettingsPrerequisiteException exception)
            when (exception.Code == SettingsPrerequisiteCodesV1.EInvoiceScenarioCodeListNotFound)
        {
            return new CodeListRead(CounterpartySettingsSourceStateV1.NotSet, []);
        }
        catch (Exception)
        {
            return CodeListRead.Unavailable;
        }
    }

    public static async Task<DefaultRead> ReadDefaultAsync(
        ISettingsEInvoiceScenarioPolicyProviderV1? provider,
        Guid companyId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return DefaultRead.Unavailable;
        }

        try
        {
            var value = await provider.GetEInvoiceScenarioDefaultAsync(effectiveOn, cancellationToken);
            if (value is null
                || value.CompanyId != companyId
                || !EInvoiceScenarioCodeFormatV1.IsWellFormed(value.ScenarioCode)
                || value.EffectiveFrom > effectiveOn
                || (value.EffectiveThrough is DateOnly end && end < effectiveOn))
            {
                return DefaultRead.Unavailable;
            }

            return new DefaultRead(CounterpartySettingsSourceStateV1.Set, value.ScenarioCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SettingsPrerequisiteException exception)
            when (exception.Code == SettingsPrerequisiteCodesV1.EInvoiceScenarioDefaultNotFound)
        {
            return new DefaultRead(CounterpartySettingsSourceStateV1.NotSet, null);
        }
        catch (Exception)
        {
            return DefaultRead.Unavailable;
        }
    }

    /// <summary>The effective scenario for an override on a date (both keys read; the resolver decides).</summary>
    public static async Task<(CodeListRead Codes, DefaultRead Default, CounterpartyEInvoiceScenarioResolutionV1 Effective)>
        ResolveAsync(
            ISettingsEInvoiceScenarioPolicyProviderV1? provider,
            Guid companyId,
            string? overrideCode,
            DateOnly effectiveOn,
            CancellationToken cancellationToken)
    {
        var codes = await ReadCodesAsync(provider, companyId, effectiveOn, cancellationToken);
        var defaultValue = await ReadDefaultAsync(provider, companyId, effectiveOn, cancellationToken);
        var effective = CounterpartyEInvoiceScenarioResolverV1.Resolve(
            overrideCode, codes.State, codes.Codes, defaultValue.State, defaultValue.ScenarioCode);
        return (codes, defaultValue, effective);
    }

    public sealed record CodeListRead(CounterpartySettingsSourceStateV1 State, IReadOnlyList<string> Codes)
    {
        public static CodeListRead Unavailable { get; } = new(CounterpartySettingsSourceStateV1.Unavailable, []);
    }

    public sealed record DefaultRead(CounterpartySettingsSourceStateV1 State, string? ScenarioCode)
    {
        public static DefaultRead Unavailable { get; } = new(CounterpartySettingsSourceStateV1.Unavailable, null);
    }
}
