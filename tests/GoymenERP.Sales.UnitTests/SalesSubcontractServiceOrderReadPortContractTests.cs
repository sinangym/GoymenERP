using System.Reflection;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S-3 (handoff) / SAT-D175 (EK25): the versioned contract of <see cref="ISalesSubcontractServiceOrderReadPortV1"/> — the Production
/// lane binds to exactly these members. A change here is a new contract version (V2), never a silent edit. Price-free by construction.
/// </summary>
public sealed class SalesSubcontractServiceOrderReadPortContractTests
{
    private static string[] Members(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public void The_read_port_v1_shape_is_pinned()
    {
        Assert.Equal("sales.subcontract-service-order.v1", ISalesSubcontractServiceOrderReadPortV1.ContractVersion);
        var method = Assert.Single(typeof(ISalesSubcontractServiceOrderReadPortV1).GetMethods());
        Assert.Equal(("GetAsync", typeof(Task<SalesSubcontractServiceOrderAnswerV1>)), (method.Name, method.ReturnType));
        Assert.Equal([typeof(Guid), typeof(Guid), typeof(CancellationToken)], method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(["companyId", "orderId", "cancellationToken"], method.GetParameters().Select(parameter => parameter.Name!));

        Assert.Equal(["ContractVersion", "Order", "Outcome", "Reason"], Members(typeof(SalesSubcontractServiceOrderAnswerV1)));
        Assert.Equal(["ApprovalState", "Basis", "BasisRevision", "CompanyAccountId", "CompanyId", "CustomerAcceptanceState", "DocumentNo",
            "IsApproved", "IsOpen", "LifecycleState", "Lines", "OrderId", "PartyId", "Revision", "Version"], Members(typeof(SalesSubcontractServiceOrderV1)));
        Assert.Equal(["ItemType", "LineId", "LineNo", "OrderUnitCode", "OrderUnitId", "ProductCode", "ProductId", "ProductName",
            "ProductVersion", "Quantity", "QuantityText"], Members(typeof(SalesSubcontractServiceLineV1)));
        Assert.Equal(["Unreadable", "NotFound", "NotSubcontractService", "Found"], Enum.GetNames<SalesSubcontractServiceOrderOutcomeV1>());
        Assert.Equal(0, (int)SalesSubcontractServiceOrderOutcomeV1.Unreadable);
        Assert.Equal(("NotRequired", "Accepted", "Pending", "Rejected"), (SalesSubcontractCustomerAcceptanceStatesV1.NotRequired,
            SalesSubcontractCustomerAcceptanceStatesV1.Accepted, SalesSubcontractCustomerAcceptanceStatesV1.Pending,
            SalesSubcontractCustomerAcceptanceStatesV1.Rejected));
        Assert.Equal(("CurrentRevision", "CustomerAcceptedRevision"), (SalesSubcontractServiceOrderBasesV1.CurrentRevision,
            SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision));
    }

    [Fact]
    public void The_answer_carries_no_price_discount_currency_tax_or_payment_member()
    {
        foreach (var type in new[] { typeof(SalesSubcontractServiceOrderAnswerV1), typeof(SalesSubcontractServiceOrderV1), typeof(SalesSubcontractServiceLineV1) })
        {
            Assert.DoesNotContain(Members(type), name => name.Contains("Price", StringComparison.Ordinal)
                || name.Contains("Discount", StringComparison.Ordinal) || name.Contains("Currency", StringComparison.Ordinal)
                || name.Contains("Tax", StringComparison.Ordinal) || name.Contains("Payment", StringComparison.Ordinal)
                || name.Contains("Amount", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_binding_hook_fails_closed_by_default()
    {
        Assert.Equal(0, (int)SalesSubcontractOrderBindingOutcomeV1.Unreadable);
        Assert.Equal(["Unreadable", "NotBound", "Bound"], Enum.GetNames<SalesSubcontractOrderBindingOutcomeV1>());
        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Unreadable, default(SalesSubcontractOrderBindingOutcomeV1));
    }
}
