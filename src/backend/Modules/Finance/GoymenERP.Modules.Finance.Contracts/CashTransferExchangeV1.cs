using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>An exact positive quote. The rounded display rate never replaces its numerator/denominator proof.</summary>
public sealed record CashTransferRateRatioV1(string Numerator, string Denominator);

/// <summary>FNS-D59: one actual Settings TCMB selling-rate source to TRY, or TRY's identity rate.
/// A comparison is not a treasury carrying value or recognized exchange gain/loss.</summary>
public sealed record CashTransferComparisonSourceV1(Guid CurrencyId, string SellingRate, DateOnly RateDay,
    string Source, Guid? RecordId, string? RecordVersion, DateOnly? BulletinDate, string? Provenance);

/// <summary>Q10: the exact original leg amount times its recorded comparison rate. This is a benchmark,
/// never the treasury carrying value, realized exchange profit/loss or an Accounting posting amount.</summary>
public sealed record CashTransferTlReferenceV1(Guid CurrencyId, string Amount, string SourceVersion);

/// <summary>The backend's immutable quote evidence. All amounts retain their original leg currency.
/// No accounting account, recognized gain/loss or inferred historical carrying amount is contained here.</summary>
public sealed record CashTransferExchangeSnapshotV1(Guid CompanyId, DateOnly OccurredOn,
    MoneyText SourceMoney, MoneyText TargetMoney, ProfileReference SourceProfile, ProfileReference TargetProfile,
    string TransactionRate, CashTransferRateRatioV1 ExactTransactionRate,
    string ComparisonRate, CashTransferRateRatioV1 ExactComparisonRate, string RateDeviation,
    CashTransferComparisonSourceV1 SourceComparison, CashTransferComparisonSourceV1 TargetComparison,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CashTransferTlReferenceV1? SourceReferenceTl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CashTransferTlReferenceV1? TargetReferenceTl = null);

/// <summary>FNS-D59: actual target/source and a separate TCMB benchmark; no deviation threshold or financial posting.
/// Rate display is two decimals, using the existing Money half-away-from-zero convention. BigInteger prevents an
/// intermediate decimal overflow or a rounded numerator from changing the original monetary leg amounts.</summary>
public static class CashTransferExchangeCalculatorV1
{
    public const string TlReferenceSourceVersion = "finance.cash-transfer.tl-reference.v1";
    public static CashTransferExchangeSnapshotV1 Calculate(Guid companyId, DateOnly occurredOn,
        MoneyText source, MoneyText target, ProfileReference sourceProfile, ProfileReference targetProfile,
        CashTransferComparisonSourceV1 sourceComparison, CashTransferComparisonSourceV1 targetComparison)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceComparison); ArgumentNullException.ThrowIfNull(targetComparison);
        if (companyId == Guid.Empty || occurredOn == default || source.CurrencyId == Guid.Empty || target.CurrencyId == Guid.Empty
            || source.CurrencyId == target.CurrencyId || sourceComparison.CurrencyId != source.CurrencyId
            || targetComparison.CurrencyId != target.CurrencyId || sourceComparison.RateDay != occurredOn
            || targetComparison.RateDay != occurredOn || sourceProfile is null || targetProfile is null)
            throw new ArgumentException("Bozdurma kuru aynı şirket, gün ve gerçek iki para bacağına bağlanmalıdır.");
        var actual = Ratio.Parse(target.Amount).Divide(Ratio.Parse(source.Amount));
        var comparison = Ratio.Parse(sourceComparison.SellingRate).Divide(Ratio.Parse(targetComparison.SellingRate));
        return new(companyId, occurredOn, source, target, sourceProfile, targetProfile,
            actual.Rounded(2), actual.Proof(), comparison.Rounded(2), comparison.Proof(),
            actual.Subtract(comparison).Rounded(2), sourceComparison, targetComparison,
            Reference(source, sourceComparison), Reference(target, targetComparison));
    }

    /// <summary>Absent on old snapshots means not recorded; it is never backfilled on a read or replay.</summary>
    public static bool HasValidTlReferences(CashTransferExchangeSnapshotV1 snapshot)
    {
        if (snapshot.SourceReferenceTl is null && snapshot.TargetReferenceTl is null) return true;
        try
        {
            var expected = Calculate(snapshot.CompanyId, snapshot.OccurredOn, snapshot.SourceMoney, snapshot.TargetMoney,
                snapshot.SourceProfile, snapshot.TargetProfile, snapshot.SourceComparison, snapshot.TargetComparison);
            return snapshot.SourceReferenceTl == expected.SourceReferenceTl && snapshot.TargetReferenceTl == expected.TargetReferenceTl;
        }
        catch (Exception error) when (error is ArgumentException or SettingsPrerequisiteException) { return false; }
    }

    private static CashTransferTlReferenceV1 Reference(MoneyText money, CashTransferComparisonSourceV1 comparison) =>
        new(SettingsCurrencyIdentitiesV1.TurkishLira, Ratio.Parse(money.Amount).Multiply(Ratio.Parse(comparison.SellingRate)).ExactText(),
            TlReferenceSourceVersion);

    public static bool SameRate(string supplied, CashTransferRateRatioV1 actual)
    {
        try { return Ratio.Parse(supplied).Equals(Ratio.Of(actual)); }
        catch (Exception exception) when (exception is ArgumentException or SettingsPrerequisiteException) { return false; }
    }

    private readonly record struct Ratio(BigInteger Numerator, BigInteger Denominator)
    {
        internal static Ratio Parse(string text)
        {
            _ = ExactPositiveDecimalV1.Parse(text, 28);
            var point = text.IndexOf('.');
            var scale = point < 0 ? 0 : text.Length - point - 1;
            return Normal(BigInteger.Parse(text.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture),
                BigInteger.Pow(10, scale));
        }
        internal static Ratio Of(CashTransferRateRatioV1 value)
        {
            if (!BigInteger.TryParse(value.Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
                || !BigInteger.TryParse(value.Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
                || numerator <= 0 || denominator <= 0) throw new ArgumentException("Kur kanıtı geçersiz.");
            return Normal(numerator, denominator);
        }
        private static Ratio Normal(BigInteger numerator, BigInteger denominator)
        {
            var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            return new(numerator / gcd, denominator / gcd);
        }
        internal Ratio Divide(Ratio other) => Normal(Numerator * other.Denominator, Denominator * other.Numerator);
        internal Ratio Multiply(Ratio other) => Normal(Numerator * other.Numerator, Denominator * other.Denominator);
        internal Ratio Subtract(Ratio other) => Normal(Numerator * other.Denominator - other.Numerator * Denominator,
            Denominator * other.Denominator);
        internal CashTransferRateRatioV1 Proof() => new(Numerator.ToString(CultureInfo.InvariantCulture),
            Denominator.ToString(CultureInfo.InvariantCulture));
        internal string ExactText()
        {
            // Decimal inputs have only 2/5 denominator factors. Preserve the complete finite product,
            // including precision beyond System.Decimal; no profile rounding is applied to a benchmark.
            var denominator = Denominator; var twos = 0; var fives = 0;
            while (denominator % 2 == 0) { denominator /= 2; twos++; }
            while (denominator % 5 == 0) { denominator /= 5; fives++; }
            if (denominator != 1) throw new ArgumentException("Referans TL tutarı tam ondalık kanıtı taşımıyor.");
            var scale = Math.Max(twos, fives);
            var units = Numerator * BigInteger.Pow(2, scale - twos) * BigInteger.Pow(5, scale - fives);
            var whole = BigInteger.DivRem(units, BigInteger.Pow(10, scale), out var fraction);
            var decimals = scale == 0 ? "" : fraction.ToString(CultureInfo.InvariantCulture).PadLeft(scale, '0').TrimEnd('0');
            return whole.ToString(CultureInfo.InvariantCulture) + (decimals.Length == 0 ? "" : "." + decimals);
        }
        internal string Rounded(int scale)
        {
            var power = BigInteger.Pow(10, scale);
            var absolute = BigInteger.Abs(Numerator) * power;
            var units = BigInteger.DivRem(absolute, Denominator, out var remainder);
            if (remainder * 2 >= Denominator) units++;
            var whole = BigInteger.DivRem(units, power, out var fraction);
            return (Numerator.Sign < 0 && units != 0 ? "-" : "") + whole.ToString(CultureInfo.InvariantCulture)
                + "." + fraction.ToString(CultureInfo.InvariantCulture).PadLeft(scale, '0');
        }
    }
}
