namespace GoymenERP.Modules.Finance.Contracts;

public enum BankReconciliationDirectionV1
{
    Inflow = 1,
    Outflow = 2
}

public enum BankReconciliationTopologyV1
{
    OneToOne = 1,
    OneToMany = 2,
    ManyToOne = 3,
    ManyToMany = 4
}

public sealed record BankStatementReconciliationItemV1(
    Guid StatementLineId,
    BankReconciliationDirectionV1 Direction,
    MoneyText Money);

public sealed record FinanceEventReconciliationItemV1(
    Guid MoneyEventId,
    BankReconciliationDirectionV1 Direction,
    MoneyText Money);

public sealed record BankReconciliationTotalsV1(
    MoneyText BankInflow,
    MoneyText BankOutflow,
    MoneyText FinanceInflow,
    MoneyText FinanceOutflow);

public sealed record BankReconciliationEvaluationV1(
    BankReconciliationTopologyV1 Topology,
    BankReconciliationTotalsV1 Totals,
    bool IsBalanced);
