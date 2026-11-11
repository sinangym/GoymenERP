using System.Globalization;
using System.Numerics;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal sealed record FinanceAllocationMutation(
    string SourceUnallocated,
    string OpenItemOutstanding,
    int OpenItemStatus);

internal sealed record FinanceSourceReversalBarrier(
    string OriginalAmount,
    string UnallocatedAmount,
    long Version);

/// <summary>
/// Pure F-03 invariants. It performs arbitrary-precision decimal arithmetic over canonical text;
/// no CLR/database decimal range, floating point or rounding participates in an allocation.
/// </summary>
internal static class FinanceAllocationPolicy
{
    public static FinanceSourceReversalBarrier PrepareReversalBarrier(
        ReceiptDraftRow source,
        FinanceSourceAllocationStateRow? sourceState)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Stage != ReceiptStages.Recorded
            || !FinanceExactDecimalText.TryParsePositive(source.AmountText, out var sourceAmount))
        {
            throw Failure("sourceId", "finance_allocation_state_invalid");
        }

        if (sourceState is not null
            && (sourceState.CompanyId != source.CompanyId || sourceState.SourceId != source.Id
                || sourceState.CurrencyId != source.CurrencyId || sourceState.Version < 1
                || !FinanceExactDecimalText.Equal(sourceState.OriginalAmountText, source.AmountText)
                || !FinanceExactDecimalText.Equal(sourceState.UnallocatedAmountText, source.AmountText)))
        {
            throw Failure("sourceId", "finance_allocation_state_invalid");
        }

        return new FinanceSourceReversalBarrier(
            source.AmountText,
            sourceAmount.Subtract(sourceAmount).Text,
            sourceState is null ? 1 : checked(sourceState.Version + 1));
    }

    public static FinanceAllocationMutation Create(
        ReceiptDraftRow source,
        FinanceOpenItemRow openItem,
        FinanceSourceAllocationStateRow? sourceState,
        CreateFinanceAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentity(request.AllocationId, request.SourceId, request.OpenItemId);
        RequireVersion(request.ExpectedSourceVersion, source.Version);
        RequireVersion(request.ExpectedOpenItemVersion, openItem.Version);
        RequireReason(request.Reason);

        if (source.Id != request.SourceId || openItem.Id != request.OpenItemId
            || source.CompanyId != openItem.CompanyId
            || source.CompanyAccountId != openItem.CompanyAccountId
            || source.CurrencyId != openItem.CurrencyId
            || source.Stage != ReceiptStages.Recorded
            || openItem.Status != FinanceOpenItemStatuses.Open)
        {
            throw Failure("request", "finance_allocation_reference_invalid");
        }
        // MOD-15 / ALS-D42: a receipt (money in) closes only a receivable. Closing a payable with a receipt would be an
        // offset, which is its own approved Finance operation, never a side effect of an allocation.
        if (openItem.Direction != FinanceOpenItemDirectionCodes.Receivable)
        {
            throw Failure("openItemId", "finance_allocation_direction_mismatch");
        }

        if (sourceState is not null
            && (sourceState.CompanyId != source.CompanyId || sourceState.SourceId != source.Id
                || sourceState.CurrencyId != source.CurrencyId
                || !FinanceExactDecimalText.Equal(sourceState.OriginalAmountText, source.AmountText)))
        {
            throw Failure("sourceId", "finance_allocation_state_invalid");
        }

        var available = sourceState?.UnallocatedAmountText ?? source.AmountText;
        if (!FinanceExactDecimalText.TryParsePositive(request.Amount, out var amount)
            || !FinanceExactDecimalText.TryParseNonNegative(available, out var sourceAvailable)
            || !FinanceExactDecimalText.TryParseNonNegative(openItem.OutstandingAmountText, out var itemOutstanding))
        {
            throw Failure("amount", "finance_allocation_amount_invalid");
        }
        if (amount.CompareTo(sourceAvailable) > 0)
        {
            throw Failure("amount", "finance_allocation_exceeds_source");
        }
        if (amount.CompareTo(itemOutstanding) > 0)
        {
            throw Failure("amount", "finance_allocation_exceeds_open_item");
        }

        var sourceAfter = sourceAvailable.Subtract(amount);
        var itemAfter = itemOutstanding.Subtract(amount);
        return new FinanceAllocationMutation(
            sourceAfter.Text,
            itemAfter.Text,
            itemAfter.IsZero ? FinanceOpenItemStatuses.Closed : FinanceOpenItemStatuses.Open);
    }

    public static FinanceAllocationMutation Release(
        FinanceAllocationRow allocation,
        FinanceOpenItemRow openItem,
        FinanceSourceAllocationStateRow sourceState,
        ReleaseFinanceAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(sourceState);
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentity(request.AllocationId);
        RequireVersion(request.ExpectedAllocationVersion, allocation.Version);
        RequireVersion(request.ExpectedOpenItemVersion, openItem.Version);
        RequireReason(request.Reason);

        if (allocation.Id != request.AllocationId
            || allocation.Status != FinanceAllocationStatuses.Active
            || allocation.CompanyId != openItem.CompanyId || allocation.CompanyId != sourceState.CompanyId
            || allocation.OpenItemId != openItem.Id || allocation.SourceId != sourceState.SourceId
            || allocation.CurrencyId != openItem.CurrencyId || allocation.CurrencyId != sourceState.CurrencyId)
        {
            throw Failure("allocationId", "finance_allocation_state_invalid");
        }

        if (!FinanceExactDecimalText.TryParsePositive(allocation.AmountText, out var amount)
            || !FinanceExactDecimalText.TryParseNonNegative(sourceState.UnallocatedAmountText, out var sourceAvailable)
            || !FinanceExactDecimalText.TryParseNonNegative(openItem.OutstandingAmountText, out var itemOutstanding)
            || !FinanceExactDecimalText.TryParsePositive(openItem.OriginalAmountText, out var itemOriginal))
        {
            throw Failure("amount", "finance_allocation_amount_invalid");
        }

        var sourceAfter = sourceAvailable.Add(amount);
        var itemAfter = itemOutstanding.Add(amount);
        if (itemAfter.CompareTo(itemOriginal) > 0)
        {
            throw Failure("openItemId", "finance_open_item_balance_invalid");
        }

        return new FinanceAllocationMutation(
            sourceAfter.Text,
            itemAfter.Text,
            FinanceOpenItemStatuses.Open);
    }

    /// <summary>
    /// F3a / API-20 (DEC-FIN-006): a RECORDED manual bank payment (money out) applied to an open PAYABLE item of the
    /// same company, account and currency. A receivable with a payment would be an offset — its own approved Finance
    /// operation — so it is refused like a receipt on a payable. The arithmetic is the receipt allocation's.
    /// </summary>
    public static FinanceAllocationMutation CreatePayment(
        ManualBankPaymentRow payment,
        FinanceOpenItemRow openItem,
        FinancePaymentAllocationStateRow? paymentState,
        CreateFinancePaymentAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentity(request.AllocationId, request.PaymentId, request.OpenItemId);
        RequireVersion(request.ExpectedPaymentVersion, payment.Version);
        RequireVersion(request.ExpectedOpenItemVersion, openItem.Version);
        RequireReason(request.Reason);

        if (payment.Id != request.PaymentId || openItem.Id != request.OpenItemId
            || payment.CompanyId != openItem.CompanyId
            || payment.CompanyAccountId != openItem.CompanyAccountId
            || payment.CurrencyId != openItem.CurrencyId
            || payment.Status != ManualBankPaymentStatuses.Recorded
            || openItem.Status != FinanceOpenItemStatuses.Open)
        {
            throw Failure("request", "finance_allocation_reference_invalid");
        }
        if (openItem.Direction != FinanceOpenItemDirectionCodes.Payable)
        {
            throw Failure("openItemId", "finance_allocation_direction_mismatch");
        }

        if (paymentState is not null
            && (paymentState.CompanyId != payment.CompanyId || paymentState.PaymentId != payment.Id
                || paymentState.CurrencyId != payment.CurrencyId
                || !FinanceExactDecimalText.Equal(paymentState.OriginalAmountText, payment.AmountText)))
        {
            throw Failure("paymentId", "finance_allocation_state_invalid");
        }

        return Apply(paymentState?.UnallocatedAmountText ?? payment.AmountText, openItem.OutstandingAmountText,
            request.Amount);
    }

    public static FinanceAllocationMutation ReleasePayment(
        FinancePaymentAllocationRow allocation,
        FinanceOpenItemRow openItem,
        FinancePaymentAllocationStateRow paymentState,
        ReleaseFinanceAllocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(openItem);
        ArgumentNullException.ThrowIfNull(paymentState);
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentity(request.AllocationId);
        RequireVersion(request.ExpectedAllocationVersion, allocation.Version);
        RequireVersion(request.ExpectedOpenItemVersion, openItem.Version);
        RequireReason(request.Reason);

        if (allocation.Id != request.AllocationId
            || allocation.Status != FinanceAllocationStatuses.Active
            || allocation.CompanyId != openItem.CompanyId || allocation.CompanyId != paymentState.CompanyId
            || allocation.OpenItemId != openItem.Id || allocation.PaymentId != paymentState.PaymentId
            || allocation.CurrencyId != openItem.CurrencyId || allocation.CurrencyId != paymentState.CurrencyId
            || openItem.Direction != FinanceOpenItemDirectionCodes.Payable
            || openItem.Status == FinanceOpenItemStatuses.Cancelled)
        {
            throw Failure("allocationId", "finance_allocation_state_invalid");
        }

        return Restore(allocation.AmountText, paymentState.UnallocatedAmountText, paymentState.OriginalAmountText,
            openItem.OutstandingAmountText, openItem.OriginalAmountText);
    }

    private static FinanceAllocationMutation Apply(string available, string outstanding, string requested)
    {
        if (!FinanceExactDecimalText.TryParsePositive(requested, out var amount)
            || !FinanceExactDecimalText.TryParseNonNegative(available, out var sourceAvailable)
            || !FinanceExactDecimalText.TryParseNonNegative(outstanding, out var itemOutstanding))
        {
            throw Failure("amount", "finance_allocation_amount_invalid");
        }
        if (amount.CompareTo(sourceAvailable) > 0)
        {
            throw Failure("amount", "finance_allocation_exceeds_source");
        }
        if (amount.CompareTo(itemOutstanding) > 0)
        {
            throw Failure("amount", "finance_allocation_exceeds_open_item");
        }
        // The stored balances keep the scale their own facts were recorded with: an allocation can never add decimal
        // places that neither the payment's balance nor the item's outstanding amount already carries.
        if (amount.Scale > Math.Max(sourceAvailable.Scale, itemOutstanding.Scale))
        {
            throw Failure("amount", "finance_allocation_amount_invalid");
        }

        var sourceAfter = sourceAvailable.Subtract(amount);
        var itemAfter = itemOutstanding.Subtract(amount);
        return new FinanceAllocationMutation(
            sourceAfter.Text,
            itemAfter.Text,
            itemAfter.IsZero ? FinanceOpenItemStatuses.Closed : FinanceOpenItemStatuses.Open);
    }

    private static FinanceAllocationMutation Restore(
        string allocated, string sourceUnallocated, string sourceOriginal, string itemOutstanding, string itemOriginal)
    {
        if (!FinanceExactDecimalText.TryParsePositive(allocated, out var amount)
            || !FinanceExactDecimalText.TryParseNonNegative(sourceUnallocated, out var sourceAvailable)
            || !FinanceExactDecimalText.TryParsePositive(sourceOriginal, out var sourceTotal)
            || !FinanceExactDecimalText.TryParseNonNegative(itemOutstanding, out var outstanding)
            || !FinanceExactDecimalText.TryParsePositive(itemOriginal, out var original))
        {
            throw Failure("amount", "finance_allocation_amount_invalid");
        }

        var sourceAfter = sourceAvailable.Add(amount);
        var itemAfter = outstanding.Add(amount);
        if (sourceAfter.CompareTo(sourceTotal) > 0)
        {
            throw Failure("paymentId", "finance_allocation_state_invalid");
        }
        if (itemAfter.CompareTo(original) > 0)
        {
            throw Failure("openItemId", "finance_open_item_balance_invalid");
        }

        return new FinanceAllocationMutation(sourceAfter.Text, itemAfter.Text, FinanceOpenItemStatuses.Open);
    }

    private static void RequireIdentity(params Guid[] ids)
    {
        if (ids.Any(id => id == Guid.Empty))
        {
            throw Failure("identity", "identity_required");
        }
    }

    private static void RequireVersion(string value, long actual)
    {
        if (string.IsNullOrEmpty(value) || value.Any(character => !char.IsAsciiDigit(character))
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected <= 0 || expected != actual)
        {
            throw Failure("expectedVersion", "version_conflict");
        }
    }

    private static void RequireReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000)
        {
            throw Failure("reason", "validation_failed");
        }
    }

    private static FinanceAllocationPolicyException Failure(string field, string code) => new(field, code);
}

internal sealed class FinanceAllocationPolicyException(string field, string code) : Exception(code)
{
    public string Field { get; } = field;
    public string Code { get; } = code;
}

internal readonly record struct FinanceExactDecimalText(BigInteger Unscaled, int Scale)
{
    public bool IsZero => Unscaled.IsZero;

    public string Text => Format(Unscaled, Scale);

    public int CompareTo(FinanceExactDecimalText other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return Align(Unscaled, Scale, scale).CompareTo(Align(other.Unscaled, other.Scale, scale));
    }

    public FinanceExactDecimalText Add(FinanceExactDecimalText other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return new FinanceExactDecimalText(
            Align(Unscaled, Scale, scale) + Align(other.Unscaled, other.Scale, scale), scale);
    }

    public FinanceExactDecimalText Subtract(FinanceExactDecimalText other)
    {
        var scale = Math.Max(Scale, other.Scale);
        var result = Align(Unscaled, Scale, scale) - Align(other.Unscaled, other.Scale, scale);
        if (result.Sign < 0)
        {
            throw new InvalidOperationException("Finance amount subtraction cannot become negative.");
        }
        return new FinanceExactDecimalText(result, scale);
    }

    public static bool Equal(string left, string right) =>
        TryParseNonNegative(left, out var a) && TryParseNonNegative(right, out var b) && a.CompareTo(b) == 0;

    public static bool TryParsePositive(string? value, out FinanceExactDecimalText parsed) =>
        TryParse(value, requirePositive: true, out parsed);

    public static bool TryParseNonNegative(string? value, out FinanceExactDecimalText parsed) =>
        TryParse(value, requirePositive: false, out parsed);

    private static bool TryParse(string? value, bool requirePositive, out FinanceExactDecimalText parsed)
    {
        parsed = default;
        if (string.IsNullOrEmpty(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Count(character => character == '.') > 1
            || value.Any(character => !char.IsAsciiDigit(character) && character != '.'))
        {
            return false;
        }

        var separator = value.IndexOf('.');
        if (separator == 0 || separator == value.Length - 1)
        {
            return false;
        }
        var scale = separator < 0 ? 0 : value.Length - separator - 1;
        var digits = separator < 0 ? value : value.Remove(separator, 1);
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var unscaled)
            || unscaled.Sign < 0 || requirePositive && unscaled.IsZero)
        {
            return false;
        }
        parsed = new FinanceExactDecimalText(unscaled, scale);
        return true;
    }

    private static BigInteger Align(BigInteger value, int fromScale, int toScale) =>
        value * BigInteger.Pow(10, toScale - fromScale);

    private static string Format(BigInteger value, int scale)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        if (scale == 0)
        {
            return digits;
        }
        if (digits.Length <= scale)
        {
            digits = digits.PadLeft(scale + 1, '0');
        }
        return digits.Insert(digits.Length - scale, ".");
    }
}
