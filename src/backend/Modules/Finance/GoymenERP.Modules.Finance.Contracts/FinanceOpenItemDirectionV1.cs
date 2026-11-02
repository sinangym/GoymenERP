using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// Which way an open item runs, from the company's view (MOD-15, FNS-D7): a <see cref="Receivable"/> is the party's
/// debt to the company (a sales invoice installment; Tekdüzen 120 Alıcılar), a <see cref="Payable"/> is the company's
/// debt to the party (a purchase invoice installment; 320 Satıcılar). The direction is never chosen by a caller: it
/// follows the source kind (<see cref="FinanceOpenItemDirections.FromSourceKind"/>). The same party can hold both;
/// they are never netted — an offset is its own Finance operation (ALS-D42).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceOpenItemDirectionV1>))]
public enum FinanceOpenItemDirectionV1
{
    Receivable = 1,
    Payable = 2,
}

/// <summary>The single mapping from an open item's source kind to its direction.</summary>
public static class FinanceOpenItemDirections
{
    /// <summary>The direction of a known source kind, or <c>null</c> for a kind Finance does not know.</summary>
    public static FinanceOpenItemDirectionV1? FromSourceKind(string? sourceKind) => sourceKind switch
    {
        FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment => FinanceOpenItemDirectionV1.Receivable,
        FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment => FinanceOpenItemDirectionV1.Payable,
        _ => null,
    };
}
