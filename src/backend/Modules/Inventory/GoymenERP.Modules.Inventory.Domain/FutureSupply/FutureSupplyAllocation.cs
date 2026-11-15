using System.Globalization;

namespace GoymenERP.Modules.Inventory.Domain.FutureSupply;

/// <summary>
/// F-017 F017-b (STOK_F017 §4.1): the lifecycle stored for one future supply allocation. The numbers are the stored values and
/// never change meaning: Active (1) while some quantity remains; Consumed (2) when nothing remains and some part was turned into
/// a physical allocation at goods receipt (F017-c); Released (3) when nothing remains and nothing was consumed.
/// </summary>
public enum FutureSupplyAllocationStatus
{
    Active = 1,
    Consumed = 2,
    Released = 3,
}

/// <summary>
/// An exact quantity in a purchase GENERAL line's own unit (SAT-D298: Purchasing answers every amount in the line's unit). At most
/// <see cref="MaximumDecimals"/> decimals, the precision Purchasing stores its line quantities with; a value that needs more is
/// REFUSED, never rounded or cut (no silent digit cutting). Trailing zeros are not extra precision and are normalised away.
/// Which unit it is lives beside the value (the purchase line's unit id); this type only guarantees the exact arithmetic.
/// </summary>
public readonly record struct FutureSupplyQuantity : IComparable<FutureSupplyQuantity>
{
    /// <summary>The purchase order line quantity scale (decimal(18,3) in Purchasing); not a business tolerance.</summary>
    public const int MaximumDecimals = 3;

    private FutureSupplyQuantity(decimal value) => Value = value;

    public decimal Value { get; }

    public static FutureSupplyQuantity Zero { get; } = new(0m);

    /// <summary>Any non-negative quantity with at most <see cref="MaximumDecimals"/> decimals.</summary>
    public static FutureSupplyQuantity Of(decimal value)
    {
        if (value < 0m)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.QuantityNotPositive, "Gelecek tedarik miktarı negatif olamaz.");
        }

        if (decimal.Round(value, MaximumDecimals, MidpointRounding.ToZero) != value)
        {
            throw new FutureSupplyRuleException(
                FutureSupplyRuleFailure.PrecisionExceeded,
                "Gelecek tedarik miktarı en fazla 3 ondalık basamak içerebilir; fazla basamak kesilmez, reddedilir.");
        }

        return new(value / 1.000000000000000000000000000000000m);
    }

    /// <summary>A strictly positive quantity (an allocation or a release amount).</summary>
    public static FutureSupplyQuantity Positive(decimal value)
    {
        var quantity = Of(value);
        if (quantity.Value == 0m)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.QuantityNotPositive, "Gelecek tedarik miktarı sıfırdan büyük olmalıdır.");
        }

        return quantity;
    }

    public static FutureSupplyQuantity operator +(FutureSupplyQuantity left, FutureSupplyQuantity right) => new(left.Value + right.Value);

    /// <summary>Exact difference; a negative result is refused (a quantity never goes below zero).</summary>
    public static FutureSupplyQuantity operator -(FutureSupplyQuantity left, FutureSupplyQuantity right) => Of(left.Value - right.Value);

    public int CompareTo(FutureSupplyQuantity other) => Value.CompareTo(other.Value);

    public static bool operator <(FutureSupplyQuantity left, FutureSupplyQuantity right) => left.Value < right.Value;

    public static bool operator >(FutureSupplyQuantity left, FutureSupplyQuantity right) => left.Value > right.Value;

    public static bool operator <=(FutureSupplyQuantity left, FutureSupplyQuantity right) => left.Value <= right.Value;

    public static bool operator >=(FutureSupplyQuantity left, FutureSupplyQuantity right) => left.Value >= right.Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Why a future supply rule refused; the application maps each to its stable problem code, never the message text.</summary>
public enum FutureSupplyRuleFailure
{
    QuantityNotPositive = 1,
    PrecisionExceeded = 2,
    InsufficientFutureSupply = 3,
    VersionConflict = 4,
    StateConflict = 5,
    ReleaseExceedsRemaining = 6,
    InvalidReason = 7,
}

/// <summary>A future supply rule refused the operation; nothing is partly applied when this is thrown.</summary>
public sealed class FutureSupplyRuleException(FutureSupplyRuleFailure failure, string message) : Exception(message)
{
    public FutureSupplyRuleFailure Failure { get; } = failure;
}

/// <summary>
/// F-017 F017-b (STOK_F017 §3.2-§3.4, §4.1; KLN-D26): one reservation of quantity on an approved, open purchase GENERAL line for
/// an approved sales order line. It is NOT a physical movement and never mixes with free stock (DEC-STK-003). Quantities are in
/// the purchase line's unit. <c>consumed = allocated − remaining − released</c>; this slice never consumes (goods receipt
/// conversion is F017-c), so here a full release always ends Released, while a row restored with a consumed part ends Consumed.
/// A release keeps the id, the creation time and the manual sequence and steps the version by exactly one (§3.4: a reduction is a
/// partial release, never release + re-create). Nothing releases by itself or by time (DEC-STK-005).
/// </summary>
public sealed class FutureSupplyAllocation
{
    /// <summary>The reason texts' stored maximum (the column width); a longer text is refused, never cut.</summary>
    public const int MaximumReasonLength = 1000;

    private FutureSupplyAllocation(
        Guid id,
        Guid companyId,
        FutureSupplyQuantity allocated,
        FutureSupplyQuantity remaining,
        FutureSupplyQuantity released,
        FutureSupplyAllocationStatus status,
        long version)
    {
        if (id == Guid.Empty || companyId == Guid.Empty || version < 1 || !Enum.IsDefined(status)
            || allocated.Value <= 0m || remaining.Value + released.Value > allocated.Value
            || !StatusMatches(status, allocated, remaining, released))
        {
            throw new ArgumentException("Stored future supply allocation is inconsistent.");
        }

        Id = id;
        CompanyId = companyId;
        Allocated = allocated;
        Remaining = remaining;
        Released = released;
        Status = status;
        Version = version;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public FutureSupplyQuantity Allocated { get; }

    public FutureSupplyQuantity Remaining { get; private set; }

    public FutureSupplyQuantity Released { get; private set; }

    /// <summary>What goods receipt turned into a physical allocation: allocated − remaining − released.</summary>
    public FutureSupplyQuantity Consumed => Allocated - Remaining - Released;

    public FutureSupplyAllocationStatus Status { get; private set; }

    public long Version { get; private set; }

    /// <summary>A new allocation: Active, version 1, the whole quantity remaining.</summary>
    public static FutureSupplyAllocation Create(Guid id, Guid companyId, FutureSupplyQuantity quantity)
    {
        if (quantity.Value <= 0m)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.QuantityNotPositive, "Gelecek tedarik ayırma miktarı sıfırdan büyük olmalıdır.");
        }

        return new FutureSupplyAllocation(id, companyId, quantity, quantity, FutureSupplyQuantity.Zero,
            FutureSupplyAllocationStatus.Active, 1);
    }

    /// <summary>Rebuilds a stored allocation; a stored row that breaks the status rule is refused.</summary>
    public static FutureSupplyAllocation Restore(
        Guid id,
        Guid companyId,
        FutureSupplyQuantity allocated,
        FutureSupplyQuantity remaining,
        FutureSupplyQuantity released,
        FutureSupplyAllocationStatus status,
        long version) =>
        new(id, companyId, allocated, remaining, released, status, version);

    /// <summary>
    /// Releases <paramref name="quantity"/> (null = all that remains) against the caller's expected version. Only an Active
    /// allocation releases (else <see cref="FutureSupplyRuleFailure.StateConflict"/>); a stale version is
    /// <see cref="FutureSupplyRuleFailure.VersionConflict"/>; more than remains is refused, never clipped. Returns the quantity
    /// actually released. The status follows §4.1: remaining &gt; 0 → Active; remaining 0 and consumed &gt; 0 → Consumed;
    /// otherwise Released.
    /// </summary>
    public FutureSupplyQuantity Release(long expectedVersion, FutureSupplyQuantity? quantity)
    {
        if (Status != FutureSupplyAllocationStatus.Active)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.StateConflict, "Yalnız aktif gelecek tedarik ayırması bırakılır.");
        }

        if (expectedVersion != Version)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.VersionConflict, "Gelecek tedarik ayırması başka bir işlemle değişti.");
        }

        var amount = quantity ?? Remaining;
        if (amount.Value <= 0m)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.QuantityNotPositive, "Bırakılan miktar sıfırdan büyük olmalıdır.");
        }

        if (amount > Remaining)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.ReleaseExceedsRemaining, "Bırakılan miktar kalan ayırmayı aşamaz.");
        }

        Remaining -= amount;
        Released += amount;
        Status = Remaining.Value > 0m
            ? FutureSupplyAllocationStatus.Active
            : Consumed.Value > 0m ? FutureSupplyAllocationStatus.Consumed : FutureSupplyAllocationStatus.Released;
        Version = checked(Version + 1);
        return amount;
    }

    /// <summary>A reason text: required, trimmed-meaningful, at most <see cref="MaximumReasonLength"/> characters, never cut.</summary>
    public static string RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumReasonLength)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.InvalidReason, "Gerekçe zorunludur ve en fazla 1000 karakterdir.");
        }

        return reason;
    }

    private static bool StatusMatches(
        FutureSupplyAllocationStatus status,
        FutureSupplyQuantity allocated,
        FutureSupplyQuantity remaining,
        FutureSupplyQuantity released)
    {
        var consumed = allocated.Value - remaining.Value - released.Value;
        return status switch
        {
            FutureSupplyAllocationStatus.Active => remaining.Value > 0m,
            FutureSupplyAllocationStatus.Consumed => remaining.Value == 0m && consumed > 0m,
            FutureSupplyAllocationStatus.Released => remaining.Value == 0m && consumed == 0m,
            _ => false,
        };
    }
}

/// <summary>
/// F-017 F017-b (STOK_F017 §4.1/§4.2): Inventory's ledger row of one purchase GENERAL line — the active allocated total and the
/// last allocatable amount read under the line lock. The invariant <c>0 ≤ active allocated ≤ allocatable snapshot</c> is checked
/// here (in code, with the value read under the lock) and again by the table check. The snapshot is written only after the check
/// passes; a release lowers the active total and never touches the snapshot. Every change steps the version (the concurrency
/// token), so two writers that both missed the lock still cannot both win.
/// </summary>
public sealed class FutureSupplyLedger
{
    private FutureSupplyLedger(Guid companyId, Guid purchaseLineId, FutureSupplyQuantity allocatableSnapshot,
        FutureSupplyQuantity activeAllocated, long version)
    {
        if (companyId == Guid.Empty || purchaseLineId == Guid.Empty || version < 0 || activeAllocated > allocatableSnapshot)
        {
            throw new ArgumentException("Stored future supply ledger is inconsistent.");
        }

        CompanyId = companyId;
        PurchaseLineId = purchaseLineId;
        AllocatableSnapshot = allocatableSnapshot;
        ActiveAllocated = activeAllocated;
        Version = version;
    }

    public Guid CompanyId { get; }

    public Guid PurchaseLineId { get; }

    public FutureSupplyQuantity AllocatableSnapshot { get; private set; }

    public FutureSupplyQuantity ActiveAllocated { get; private set; }

    /// <summary>0 for a ledger that has never been written; 1 after its first write.</summary>
    public long Version { get; private set; }

    /// <summary>A ledger that has no stored row yet (nothing was ever allocated on the line).</summary>
    public static FutureSupplyLedger Empty(Guid companyId, Guid purchaseLineId) =>
        new(companyId, purchaseLineId, FutureSupplyQuantity.Zero, FutureSupplyQuantity.Zero, 0);

    public static FutureSupplyLedger Restore(Guid companyId, Guid purchaseLineId, FutureSupplyQuantity allocatableSnapshot,
        FutureSupplyQuantity activeAllocated, long version)
    {
        if (version < 1)
        {
            throw new ArgumentException("Stored future supply ledger is inconsistent.");
        }

        return new(companyId, purchaseLineId, allocatableSnapshot, activeAllocated, version);
    }

    /// <summary>
    /// Step 4: <c>active allocated + quantity ≤ allocatable</c> with the allocatable amount read under the lock, else
    /// <see cref="FutureSupplyRuleFailure.InsufficientFutureSupply"/> and nothing changes. Only then is the snapshot written.
    /// </summary>
    public void Reserve(FutureSupplyQuantity allocatable, FutureSupplyQuantity quantity)
    {
        if (quantity.Value <= 0m)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.QuantityNotPositive, "Ayırma miktarı sıfırdan büyük olmalıdır.");
        }

        if (ActiveAllocated + quantity > allocatable)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.InsufficientFutureSupply, "Satınalma satırında yeterli ayrılabilir gelecek tedarik yok.");
        }

        ActiveAllocated += quantity;
        AllocatableSnapshot = allocatable;
        Version = checked(Version + 1);
    }

    /// <summary>A release returns <paramref name="quantity"/> to the line; the snapshot is not touched.</summary>
    public void Return(FutureSupplyQuantity quantity)
    {
        if (Version < 1 || quantity.Value <= 0m || quantity > ActiveAllocated)
        {
            throw new FutureSupplyRuleException(FutureSupplyRuleFailure.StateConflict, "Defterde bırakılacak kadar aktif ayırma yok.");
        }

        ActiveAllocated -= quantity;
        Version = checked(Version + 1);
    }
}
