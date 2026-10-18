using System.Globalization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// Purchasing-owned commercial commitment for one subcontract service supplier. This contract is
/// not a stock movement, process plan, invoice or payment. Physical execution is addressed through
/// immutable source identities and remains with its owning modules (D-I08-SUBCONTRACT-01=A).
/// </summary>
public sealed record PurchasingSubcontractOrderV1
{
    public PurchasingSubcontractOrderV1(
        Guid orderId,
        Guid companyId,
        Guid supplierPartyId,
        Guid companyAccountId,
        string expectedVersion,
        IReadOnlyList<PurchasingSubcontractOrderLineV1> lines)
    {
        OrderId = PurchasingSubcontractContractGuardV1.Identity(orderId, nameof(orderId));
        CompanyId = PurchasingSubcontractContractGuardV1.Identity(companyId, nameof(companyId));
        SupplierPartyId = PurchasingSubcontractContractGuardV1.Identity(supplierPartyId, nameof(supplierPartyId));
        CompanyAccountId = PurchasingSubcontractContractGuardV1.Identity(companyAccountId, nameof(companyAccountId));
        ExpectedVersion = PurchasingSubcontractContractGuardV1.Token(expectedVersion, nameof(expectedVersion));
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(line => line is null))
        {
            throw new ArgumentException("At least one subcontract service line is required.", nameof(lines));
        }

        if (lines.Select(line => line.LineId).Distinct().Count() != lines.Count)
        {
            throw new ArgumentException("Subcontract service line identities must be unique.", nameof(lines));
        }

        Lines = lines.ToArray();
    }

    public Guid OrderId { get; }
    public Guid CompanyId { get; }
    public Guid SupplierPartyId { get; }
    public Guid CompanyAccountId { get; }
    public string ExpectedVersion { get; }
    public IReadOnlyList<PurchasingSubcontractOrderLineV1> Lines { get; }
}

/// <summary>
/// One Service commitment line. Service identity, quantity and unit are separate facts; there are
/// no parallel nullable Product/Service/Consumable ids. The physical request is a reference only
/// and neither this line nor service acceptance creates an Inventory quantity.
/// </summary>
public sealed record PurchasingSubcontractOrderLineV1
{
    public PurchasingSubcontractOrderLineV1(
        Guid lineId,
        Guid serviceId,
        string expectedServiceVersion,
        Guid serviceUnitId,
        string serviceQuantity,
        Guid sourcePhysicalRequestId,
        string expectedSourcePhysicalRequestVersion)
    {
        LineId = PurchasingSubcontractContractGuardV1.Identity(lineId, nameof(lineId));
        ServiceId = PurchasingSubcontractContractGuardV1.Identity(serviceId, nameof(serviceId));
        ExpectedServiceVersion = PurchasingSubcontractContractGuardV1.Token(
            expectedServiceVersion,
            nameof(expectedServiceVersion));
        ServiceUnitId = PurchasingSubcontractContractGuardV1.Identity(serviceUnitId, nameof(serviceUnitId));
        ServiceQuantity = PurchasingSubcontractContractGuardV1.PositiveDecimalText(serviceQuantity, nameof(serviceQuantity));
        SourcePhysicalRequestId = PurchasingSubcontractContractGuardV1.Identity(
            sourcePhysicalRequestId,
            nameof(sourcePhysicalRequestId));
        ExpectedSourcePhysicalRequestVersion = PurchasingSubcontractContractGuardV1.Token(
            expectedSourcePhysicalRequestVersion,
            nameof(expectedSourcePhysicalRequestVersion));
    }

    public Guid LineId { get; }
    public Guid ServiceId { get; }
    public string ExpectedServiceVersion { get; }
    public Guid ServiceUnitId { get; }
    public string ServiceQuantity { get; }
    public Guid SourcePhysicalRequestId { get; }
    public string ExpectedSourcePhysicalRequestVersion { get; }
}

internal static class PurchasingSubcontractContractGuardV1
{
    public static Guid Identity(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Identity is required.", parameterName) : value;

    public static string Token(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A canonical version token is required.", parameterName);
        }

        return value;
    }

    public static string PositiveDecimalText(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl)
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0m)
        {
            throw new ArgumentException("A positive exact service quantity is required.", parameterName);
        }

        return value;
    }
}
