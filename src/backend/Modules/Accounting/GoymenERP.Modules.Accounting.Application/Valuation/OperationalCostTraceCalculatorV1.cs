using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Accounting.Application.Valuation;

/// <summary>
/// Pure operational value-share preview for D-F07=B. Historical quantity shares and current
/// values must be proven by their owning modules before invocation. Calculation uses exact
/// rational arithmetic and an explicitly supplied approved money scale; it never rounds,
/// writes, posts, revalues customer-owned material, or decides a forward accounting period.
/// </summary>
public static class OperationalCostTraceCalculatorV1
{
    public static OperationalCostTraceResultV1 Preview(OperationalCostTraceRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Copy each caller-owned collection once; all validation, fingerprinting and calculation
        // below use only these private snapshots, never a second enumeration of mutable input.
        // Caller-provided Count is deliberately ignored: it may be stale, expensive or dishonest.
        var shareSnapshot = SnapshotBounded(request.Shares,
            OperationalCostTraceSerializationLimitsV1.MaxShareCount, out var shares);
        var historySnapshot = SnapshotBounded(request.PriorApplications,
            OperationalCostTraceSerializationLimitsV1.MaxPriorApplicationCount, out var priorApplications);
        if (shareSnapshot == SnapshotStatus.TooMany || historySnapshot == SnapshotStatus.TooMany)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NumericLimitExceeded);
        }

        if (shareSnapshot != SnapshotStatus.Ready || historySnapshot != SnapshotStatus.Ready)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
        }

        var origin = request.Origin;
        if (origin is null || origin.CostOriginId == Guid.Empty || origin.CompanyId == Guid.Empty
            || origin.StockOwnerId == Guid.Empty || origin.ProductId == Guid.Empty || origin.LotId == Guid.Empty
            || origin.LabelId == Guid.Empty || origin.CurrencyId == Guid.Empty || origin.SourceFactId == Guid.Empty
            || !HasText(origin.ProductVersion) || !HasText(origin.SourceFactKind)
            || !HasText(origin.SourceFactVersion)
            || origin.AssetKind is not (OperationalCostAssetKindV1.CompanyOwned or OperationalCostAssetKindV1.CustomerOwned)
            || request.AdjustmentId == Guid.Empty || request.AdjustmentSourceFactId == Guid.Empty
            || request.SourceBusinessDate == default || !HasText(request.AdjustmentSourceKind)
            || !HasText(request.AdjustmentSourceVersion) || request.QuantityBasis is null
            || request.QuantityBasis.UnitId == Guid.Empty
            || request.QuantityBasis.ApprovedProfileId == Guid.Empty
            || !HasText(request.QuantityBasis.ApprovedProfileVersion))
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
        }

        if (origin.AssetKind == OperationalCostAssetKindV1.CustomerOwned)
        {
            return Refuse(OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.CustomerMaterial);
        }

        if (!request.SourcePeriodOpen)
        {
            return Refuse(OperationalCostTraceStatusV1.ForwardCorrectionRequired,
                OperationalCostTraceCodesV1.ForwardCorrectionRequired);
        }

        if (request.ApprovedScale is null || request.ApprovedProfileId == Guid.Empty
            || !HasText(request.ApprovedProfileVersion))
        {
            return Refuse(OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.ProfileUnavailable);
        }

        var scale = request.ApprovedScale.Value;
        if (scale < 0 || shares.Length == 0)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
        }

        if (scale > OperationalCostTraceSerializationLimitsV1.MaxApprovedMoneyScale
            || ExceedsNumericLimit(request.AdjustmentAmountText)
            || ExceedsNumericLimit(request.CurrentTotalAmountText)
            || ExceedsNumericLimit(request.TotalQuantityText)
            || ExceedsMetadataLimit(origin.ProductVersion)
            || ExceedsMetadataLimit(origin.SourceFactKind)
            || ExceedsMetadataLimit(origin.SourceFactVersion)
            || ExceedsMetadataLimit(request.AdjustmentSourceKind)
            || ExceedsMetadataLimit(request.AdjustmentSourceVersion)
            || ExceedsMetadataLimit(request.ApprovedProfileVersion)
            || ExceedsMetadataLimit(request.QuantityBasis.ApprovedProfileVersion)
            || shares.Any(share => share is not null && (ExceedsNumericLimit(share.QuantityText)
                || ExceedsNumericLimit(share.CurrentAmountText)
                || ExceedsMetadataLimit(share.ProductVersion)
                || ExceedsMetadataLimit(share.EvidenceProducer)
                || ExceedsMetadataLimit(share.EvidenceFactKind)
                || ExceedsMetadataLimit(share.EvidenceFactVersion)
                || (share.QuantityBasis is not null
                    && ExceedsMetadataLimit(share.QuantityBasis.ApprovedProfileVersion)))))
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NumericLimitExceeded);
        }

        if (request.AdjustmentAmountText is null || request.CurrentTotalAmountText is null
            || shares.Any(share => share is null || share.CurrentAmountText is null))
        {
            return Refuse(OperationalCostTraceStatusV1.Unavailable, OperationalCostTraceCodesV1.UnknownAmount);
        }

        if (!Exact.TryParse(request.AdjustmentAmountText, out var adjustment)
            || !Exact.TryParse(request.CurrentTotalAmountText, out var currentTotal)
            || !Exact.TryParse(request.TotalQuantityText, out var totalQuantity)
            || totalQuantity.Numerator <= 0)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
        }

        if (!TryMinor(adjustment, scale, out var adjustmentMinor)
            || !TryMinor(currentTotal, scale, out var currentTotalMinor))
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AmountScaleExceeded);
        }

        if (currentTotalMinor < 0)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NegativeValue);
        }

        var prepared = new List<PreparedShare>(shares.Length);
        var seenShares = new HashSet<Guid>();
        var quantitySum = Exact.Zero;
        var amountSum = BigInteger.Zero;
        foreach (var share in shares)
        {
            if (share is null || share.ShareId == Guid.Empty || !seenShares.Add(share.ShareId)
                || share.LotId == Guid.Empty || share.LabelId == Guid.Empty || share.EvidenceFactId == Guid.Empty
                || !HasText(share.EvidenceProducer) || !HasText(share.EvidenceFactKind)
                || !HasText(share.EvidenceFactVersion) || share.QuantityBasis is null
                || share.Kind is not (OperationalCostShareKindV1.OnHand or OperationalCostShareKindV1.Wip
                    or OperationalCostShareKindV1.SoldOrConsumed))
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
            }

            if (share.CostOriginId != origin.CostOriginId || share.CompanyId != origin.CompanyId
                || share.StockOwnerId != origin.StockOwnerId || share.ProductId != origin.ProductId
                || !string.Equals(share.ProductVersion, origin.ProductVersion, StringComparison.Ordinal)
                || share.CurrencyId != origin.CurrencyId)
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.SourceMismatch);
            }

            if (share.QuantityBasis != request.QuantityBasis)
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict,
                    OperationalCostTraceCodesV1.QuantityBasisMismatch);
            }

            if (!Exact.TryParse(share.QuantityText, out var quantity) || quantity.Numerator <= 0
                || !Exact.TryParse(share.CurrentAmountText, out var shareAmount))
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
            }

            if (!TryMinor(shareAmount, scale, out var shareMinor))
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AmountScaleExceeded);
            }

            if (shareMinor < 0)
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NegativeValue);
            }

            quantitySum += quantity;
            amountSum += shareMinor;
            prepared.Add(new PreparedShare(share, quantity, shareAmount, shareMinor));
        }

        if (quantitySum != totalQuantity)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.ShareTotalMismatch);
        }

        if (amountSum != currentTotalMinor)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AmountMismatch);
        }

        if (priorApplications.Any(item => item is null || item.AdjustmentId == Guid.Empty
                || !IsSha256(item.Fingerprint))
            || priorApplications.Select(item => item.AdjustmentId).Distinct().Count()
                != priorApplications.Length)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.InvalidReference);
        }

        var fingerprint = Fingerprint(request, origin, adjustment, currentTotal, totalQuantity, prepared, scale);
        var previous = priorApplications.FirstOrDefault(item => item.AdjustmentId == request.AdjustmentId);
        if (previous is not null)
        {
            return string.Equals(previous.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                ? new OperationalCostTraceResultV1(OperationalCostTraceStatusV1.Ready,
                    OperationalCostTraceCodesV1.Replayed, fingerprint, true, null, [])
                : Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AdjustmentConflict);
        }

        var nextTotalMinor = currentTotalMinor + adjustmentMinor;
        if (nextTotalMinor < 0)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NegativeValue);
        }

        var previews = new List<OperationalCostSharePreviewV1>(prepared.Count);
        var distributedMinor = BigInteger.Zero;
        foreach (var item in prepared)
        {
            var exactDelta = adjustment * item.Quantity / totalQuantity;
            if (!TryMinor(exactDelta, scale, out var deltaMinor))
            {
                return Refuse(OperationalCostTraceStatusV1.RoundingRequired,
                    OperationalCostTraceCodesV1.RoundingRequired);
            }

            var nextMinor = item.CurrentMinor + deltaMinor;
            if (nextMinor < 0)
            {
                return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.NegativeValue);
            }

            distributedMinor += deltaMinor;
            previews.Add(new OperationalCostSharePreviewV1(item.Share.ShareId, item.Share.Kind,
                FormatMinor(deltaMinor, scale), FormatMinor(nextMinor, scale)));
        }

        if (distributedMinor != adjustmentMinor)
        {
            return Refuse(OperationalCostTraceStatusV1.Conflict, OperationalCostTraceCodesV1.AmountMismatch);
        }

        return new OperationalCostTraceResultV1(OperationalCostTraceStatusV1.Ready,
            OperationalCostTraceCodesV1.Ready, fingerprint, false, FormatMinor(nextTotalMinor, scale), previews.AsReadOnly());
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool ExceedsNumericLimit(string? value) =>
        value is { Length: > OperationalCostTraceSerializationLimitsV1.MaxNumericTextLength };

    private static bool ExceedsMetadataLimit(string? value) =>
        value is { Length: > OperationalCostTraceSerializationLimitsV1.MaxFingerprintMetadataTextLength };

    private static SnapshotStatus SnapshotBounded<T>(IEnumerable<T>? source, int maxCount, out T[] snapshot)
    {
        snapshot = [];
        if (source is null)
        {
            return SnapshotStatus.Missing;
        }

        try
        {
            var items = new List<T>();
            foreach (var item in source)
            {
                if (items.Count == maxCount)
                {
                    return SnapshotStatus.TooMany;
                }

                items.Add(item);
            }

            snapshot = items.ToArray();
            return SnapshotStatus.Ready;
        }
        catch (InvalidOperationException)
        {
            return SnapshotStatus.Invalid;
        }
        catch (ArgumentException)
        {
            return SnapshotStatus.Invalid;
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static OperationalCostTraceResultV1 Refuse(OperationalCostTraceStatusV1 status, string code) =>
        new(status, code, null, false, null, []);

    private static bool TryMinor(Exact amount, int scale, out BigInteger minor)
    {
        minor = BigInteger.DivRem(amount.Numerator * BigInteger.Pow(10, scale),
            amount.Denominator, out var remainder);
        return remainder.IsZero;
    }

    private static string FormatMinor(BigInteger minor, int scale)
    {
        var sign = minor.Sign < 0 ? "-" : string.Empty;
        var whole = BigInteger.DivRem(BigInteger.Abs(minor), BigInteger.Pow(10, scale), out var fraction);
        return scale == 0
            ? sign + whole.ToString(CultureInfo.InvariantCulture)
            : sign + whole.ToString(CultureInfo.InvariantCulture) + "."
                + fraction.ToString(CultureInfo.InvariantCulture).PadLeft(scale, '0');
    }

    private static string Fingerprint(OperationalCostTraceRequestV1 request, OperationalCostOriginV1 origin,
        Exact adjustment, Exact currentTotal, Exact totalQuantity, IEnumerable<PreparedShare> shares, int scale)
    {
        var builder = new StringBuilder();
        void Add(string? text) => builder.Append(text?.Length.ToString(CultureInfo.InvariantCulture) ?? "-1")
            .Append(':').Append(text).Append('|');

        Add("operational-cost-trace.v1");
        Add(origin.CostOriginId.ToString("D"));
        Add(origin.CompanyId.ToString("D"));
        Add(origin.StockOwnerId.ToString("D"));
        Add(origin.ProductId.ToString("D"));
        Add(origin.ProductVersion);
        Add(origin.LotId.ToString("D"));
        Add(origin.LabelId.ToString("D"));
        Add(origin.CurrencyId.ToString("D"));
        Add(origin.SourceFactKind);
        Add(origin.SourceFactId.ToString("D"));
        Add(origin.SourceFactVersion);
        Add(((int)origin.AssetKind).ToString(CultureInfo.InvariantCulture));
        Add(request.AdjustmentId.ToString("D"));
        Add(request.AdjustmentSourceKind);
        Add(request.AdjustmentSourceFactId.ToString("D"));
        Add(request.AdjustmentSourceVersion);
        Add(request.SourceBusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(adjustment.Canonical);
        Add(request.ApprovedProfileId.ToString("D"));
        Add(request.ApprovedProfileVersion);
        Add(scale.ToString(CultureInfo.InvariantCulture));
        Add(request.SourcePeriodOpen ? "open" : "closed");
        Add(request.QuantityBasis.UnitId.ToString("D"));
        Add(request.QuantityBasis.ApprovedProfileId.ToString("D"));
        Add(request.QuantityBasis.ApprovedProfileVersion);
        Add(totalQuantity.Canonical);
        Add(currentTotal.Canonical);
        foreach (var item in shares.OrderBy(item => item.Share.ShareId))
        {
            var share = item.Share;
            Add(share.ShareId.ToString("D"));
            Add(share.CostOriginId.ToString("D"));
            Add(share.CompanyId.ToString("D"));
            Add(share.StockOwnerId.ToString("D"));
            Add(share.ProductId.ToString("D"));
            Add(share.ProductVersion);
            Add(share.LotId.ToString("D"));
            Add(share.LabelId.ToString("D"));
            Add(share.CurrencyId.ToString("D"));
            Add(share.EvidenceProducer);
            Add(share.EvidenceFactKind);
            Add(share.EvidenceFactId.ToString("D"));
            Add(share.EvidenceFactVersion);
            Add(((int)share.Kind).ToString(CultureInfo.InvariantCulture));
            Add(share.QuantityBasis.UnitId.ToString("D"));
            Add(share.QuantityBasis.ApprovedProfileId.ToString("D"));
            Add(share.QuantityBasis.ApprovedProfileVersion);
            Add(item.Quantity.Canonical);
            Add(item.CurrentAmount.Canonical);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private sealed record PreparedShare(OperationalCostShareV1 Share, Exact Quantity,
        Exact CurrentAmount, BigInteger CurrentMinor);

    private enum SnapshotStatus
    {
        Ready,
        Missing,
        TooMany,
        Invalid,
    }

    private readonly record struct Exact(BigInteger Numerator, BigInteger Denominator)
    {
        public static Exact Zero => new(BigInteger.Zero, BigInteger.One);
        public string Canonical => Numerator.ToString(CultureInfo.InvariantCulture) + "/"
            + Denominator.ToString(CultureInfo.InvariantCulture);

        public static Exact operator +(Exact left, Exact right) => Create(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

        public static Exact operator *(Exact left, Exact right) => Create(
            left.Numerator * right.Numerator, left.Denominator * right.Denominator);

        public static Exact operator /(Exact left, Exact right) => Create(
            left.Numerator * right.Denominator, left.Denominator * right.Numerator);

        private static Exact Create(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            return new Exact(numerator / divisor, denominator / divisor);
        }

        public static bool TryParse(string? text, out Exact value)
        {
            value = default;
            if (string.IsNullOrEmpty(text)
                || text.Length > OperationalCostTraceSerializationLimitsV1.MaxNumericTextLength
                || text.Length != text.Trim().Length)
            {
                return false;
            }

            var negative = text[0] == '-';
            var body = negative ? text[1..] : text;
            var point = body.IndexOf('.');
            var whole = point < 0 ? body : body[..point];
            var fraction = point < 0 ? string.Empty : body[(point + 1)..];
            if (whole.Length == 0 || (point >= 0 && fraction.Length == 0)
                || (whole.Length > 1 && whole[0] == '0')
                || !whole.All(c => c is >= '0' and <= '9')
                || !fraction.All(c => c is >= '0' and <= '9'))
            {
                return false;
            }

            var digits = whole + fraction;
            if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var numerator))
            {
                return false;
            }

            value = Create(negative ? -numerator : numerator, BigInteger.Pow(10, fraction.Length));
            return true;
        }
    }
}
