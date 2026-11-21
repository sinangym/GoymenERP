using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The deterministic value rules of the company policy registry (B-13). The tax default shape is the same as the tax master's
/// item type default (SAT-D381 reuses it): VAT above 0 up to 100 without an exemption code, or no VAT (optional exemption code,
/// no withholding); withholding above 0 up to 100 with an optional code, or neither. Percents use
/// <see cref="TaxMasterCalculatorV1.TryParsePercent"/>, codes <see cref="TaxMasterCalculatorV1.TryNormalizeListCode"/>.
/// </summary>
public static class CompanyPolicyRulesV1
{
    /// <summary>URT-D96: whole working days, 0 or more (the start value is 0; no upper bound is decided here).</summary>
    public static bool IsValidDueDateBuffer(int workingDays) => workingDays >= 0;

    /// <summary>Reads a requested tax default; <paramref name="detail"/> is the Turkish refusal when it is not valid.</summary>
    public static bool TryReadTaxDefault(bool vatApplies, string? vatPercentText, string? exemptionCodeText,
        bool withholdingApplies, string? withholdingPercentText, string? withholdingCodeText,
        out SettingsSubcontractTaxDefaultV1 value, out string detail)
    {
        value = new SettingsSubcontractTaxDefaultV1(false, null, null, false, null, null);
        detail = string.Empty;
        decimal? vatPercent = null;
        string? exemptionCode = null;
        if (vatApplies)
        {
            if (!TaxMasterCalculatorV1.TryParsePercent(vatPercentText, allowZero: false, out var parsed) || exemptionCodeText is not null)
            {
                detail = "KDV uygulanıyorsa 0'dan büyük, en fazla 100, en çok 4 ondalıklı oran girilir; istisna kodu girilmez. "
                    + "Sıfır KDV yalnız \"KDV yok (istisna)\" ile girilir.";
                return false;
            }

            vatPercent = parsed;
        }
        else
        {
            if (vatPercentText is not null || withholdingApplies)
            {
                detail = "KDV uygulanmıyorsa oran ve tevkifat girilmez; istisna kodu isteğe bağlıdır.";
                return false;
            }

            if (exemptionCodeText is not null)
            {
                if (!TaxMasterCalculatorV1.TryNormalizeListCode(exemptionCodeText, out var code))
                {
                    detail = "İstisna kodu yalnız büyük harf ve rakam (en çok 16) olabilir.";
                    return false;
                }

                exemptionCode = code;
            }
        }

        decimal? withholdingPercent = null;
        string? withholdingCode = null;
        if (withholdingApplies)
        {
            if (!TaxMasterCalculatorV1.TryParsePercent(withholdingPercentText, allowZero: false, out var parsed))
            {
                detail = "Tevkifat uygulanıyorsa 0'dan büyük, en fazla 100, en çok 4 ondalıklı oran girilir.";
                return false;
            }

            withholdingPercent = parsed;
            if (withholdingCodeText is not null)
            {
                if (!TaxMasterCalculatorV1.TryNormalizeListCode(withholdingCodeText, out var code))
                {
                    detail = "Tevkifat kodu yalnız büyük harf ve rakam (en çok 16) olabilir.";
                    return false;
                }

                withholdingCode = code;
            }
        }
        else if (withholdingPercentText is not null || withholdingCodeText is not null)
        {
            detail = "Tevkifat uygulanmıyorsa oran ve kod girilmez.";
            return false;
        }

        value = new SettingsSubcontractTaxDefaultV1(vatApplies, vatPercent, exemptionCode, withholdingApplies,
            withholdingPercent, withholdingCode);
        return true;
    }

    /// <summary>Whether a stored value has the shape <see cref="TryReadTaxDefault"/> writes.</summary>
    public static bool IsValidTaxDefault(SettingsSubcontractTaxDefaultV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var vatOk = value.VatApplies
            ? value.VatPercent is > TaxMasterCalculatorV1.MinimumPercent and <= TaxMasterCalculatorV1.MaximumPercent
                && value.ExemptionCode is null
            : value.VatPercent is null && !value.WithholdingApplies && IsCodeOrNull(value.ExemptionCode);
        var withholdingOk = value.WithholdingApplies
            ? value.WithholdingPercent is > TaxMasterCalculatorV1.MinimumPercent and <= TaxMasterCalculatorV1.MaximumPercent
                && IsCodeOrNull(value.WithholdingCode)
            : value.WithholdingPercent is null && value.WithholdingCode is null;
        return vatOk && withholdingOk;
    }

    private static bool IsCodeOrNull(string? code) =>
        code is null || (TaxMasterCalculatorV1.TryNormalizeListCode(code, out var normalized)
            && string.Equals(normalized, code, StringComparison.Ordinal));
}
