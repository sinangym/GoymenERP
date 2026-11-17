namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// FNS-D14/D24/D25, B-28/F2-OB: publication of an already recorded economic fact, not a second
/// movement or a journal instruction. Accounting owns its approved mapping and separate approval.
/// No fee/interest classification is inferred from an expense description. Currency-exchange
/// posting remains outside this v1 source until the FNS-D63 mapping is decided.
/// </summary>
public static class FinanceFinancialMovementEventsV1
{
    public const string SourceName = "finance.outbox.v1";
    public const string ExpensePaymentRecorded = "finance.expense-payment.recorded.v1";
    public const string CashTransferRecorded = "finance.cash-transfer.recorded.v1";
    public const string CashExchangeRecorded = "finance.cash-exchange.recorded.v1";
    public const string CashExchangeBookValueApproved = "finance.cash-exchange.book-value-approved.v1";
    public const int SchemaVersion = 1;
}

/// <summary>The exact owner snapshot, including preparation, distinct approval and final record provenance.</summary>
public sealed record FinanceExpensePaymentRecordedV1(Guid EventId, ExpensePaymentView Source);

/// <summary>One same-currency transfer snapshot containing both account legs; never two money events.</summary>
public sealed record FinanceCashTransferRecordedV1(Guid EventId, CashTransferView Source);

/// <summary>Actual two-leg exchange; TCMB references are not Accounting carrying values or journal instructions.</summary>
public sealed record FinanceCashExchangeRecordedV1(Guid EventId, CashTransferView Source);

/// <summary>Separately approved, document-backed source-leg carrying value; never another treasury movement.</summary>
public sealed record FinanceCashExchangeBookValueApprovedV1(Guid EventId, CashTransferView Source,
    CashExchangeBookValueViewV1 BookValue);
