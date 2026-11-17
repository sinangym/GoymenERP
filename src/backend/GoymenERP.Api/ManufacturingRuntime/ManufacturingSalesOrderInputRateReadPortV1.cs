using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>FNS-D83's absent-field default, resolved by the existing SAT-D54/D170 Settings calculator.
/// The original source currency is explicit. This supplies display FX; it never revalues stamped TL input costs.</summary>
internal sealed class ManufacturingSalesOrderInputRateReadPortV1(ICompanyContext company, ICurrentUser user,
    ISalesOrderCostRateSourceReadPortV1 orders, ISettingsExchangeRateResolverV1 rates) : IManufacturingInputOrderRateReadPortV1
{
    public async Task<ManufacturingInputOrderRateV1> ReadAsync(Guid companyId, Guid orderId, Guid lineId, int revision,
        Guid sourceCurrencyId, Guid targetCurrencyId, CancellationToken ct)
    {
        var actor = user.UserId;
        if (!Scope() || sourceCurrencyId == Guid.Empty || sourceCurrencyId == targetCurrencyId
            || targetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira) return Unavailable();
        try
        {
            var source = await orders.GetAsync(companyId, orderId, lineId, revision, ct);
            if (!Scope() || !Source(source)) return Unavailable();
            var answer = await rates.ResolveAsync(source.RateDay!.Value, sourceCurrencyId, targetCurrencyId, ct);
            if (!Scope() || answer is null || answer.State != ExchangeRateResolutionStatesV1.Resolved
                || answer.Source != ExchangeRateSourcesV1.Tcmb || answer.RateDay != source.RateDay
                || answer.SourceCurrencyId != sourceCurrencyId || answer.TargetCurrencyId != targetCurrencyId
                || answer.BulletinDate is not { } bulletin || bulletin >= source.RateDay
                || answer.RecordId is not { } record || record == Guid.Empty
                || !long.TryParse(answer.RecordVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var rateVersion) || rateVersion < 1
                || answer.Provenance is not (ExchangeRateProvenancesV1.Manual or ExchangeRateProvenancesV1.Official)
                || answer.Code is not null || answer.StoppedAt is not null
                || !ExactRate(answer.SellingRate, out var rate))
                return Unavailable();
            var fresh = await orders.GetAsync(companyId, orderId, lineId, revision, ct);
            if (!Scope() || !Source(fresh) || fresh != source) return Unavailable();
            var frame = JsonSerializer.Serialize(new { source, resolution = answer });
            var stamp = "manufacturing.sales-order-input-rate.v1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(frame))).ToLowerInvariant();
            return new(ManufacturingInputOrderRateOutcomeV1.Available, companyId, orderId, lineId, revision,
                sourceCurrencyId, targetCurrencyId, rate, null, stamp);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Unavailable(); }

        bool Scope() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
        bool Source(SalesOrderCostRateSourceV1? value) => value is not null && value.Outcome == SalesOrderCostRateSourceOutcomeV1.Available
            && value.CompanyId == companyId && value.OrderId == orderId && value.LineId == lineId && value.Revision == revision
            && value.OrderVersion is > 0 && value.OrderCurrencyId is { } currency && currency != Guid.Empty
            && value.RateDay is { } day && day != default && value.RateType == "TcmbForexSelling" && !string.IsNullOrWhiteSpace(value.SourceVersion);
        ManufacturingInputOrderRateV1 Unavailable() => new(ManufacturingInputOrderRateOutcomeV1.Unavailable,
            companyId, orderId, lineId, revision, sourceCurrencyId, targetCurrencyId, null, null,
            "manufacturing.sales-order-input-rate.v1/unavailable", "order_rate_unavailable");
    }

    private static bool ExactRate(string? text, out decimal rate)
    {
        rate = 0m;
        if (text is null || text.Length is < 1 or > 29) return false;
        var pieces = text.Split('.');
        return pieces.Length is 1 or 2 && pieces[0].Length is >= 1 and <= 18
            && pieces.All(part => part.Length > 0 && part.All(char.IsAsciiDigit))
            && (pieces.Length == 1 || pieces[1].Length <= 10)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate) && rate > 0m;
    }
}
