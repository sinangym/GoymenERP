namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// The Finance owner's technical permission names for the manual receipt (FIN-T04b; the backend
/// owner's mapping, Business #254 5654539215). A name here grants nothing: the central registry makes
/// it known, a human assigns it.
/// <para>
/// <see cref="Record"/> (FIN-T05a): Ö4 (Sinan #66 5722637822) answers the "who may record" part of OPEN-009
/// for a realized routine receipt — one narrow authorised operator, no second approval, no amount
/// threshold (card #66 5743678389). The central registry now knows the name, so the port MEASURES it: an
/// actor without it is Denied, not Unavailable. A name is still not a grant — nobody holds it until a human
/// assigns it. Legacy direct Reverse has no name; FIN-T06 uses the separately declared
/// propose/approve permissions and enforces maker≠checker (Ö4).
/// </para>
/// </summary>
public static class FinancePermissionNames
{
    public const string DraftSave = "finance.manual-receipt.draft.save";
    public const string DraftRead = "finance.manual-receipt.draft.read";
    public const string AmountRead = "finance.manual-receipt.amount.read";
    public const string Record = "finance.manual-receipt.record";

    // FIN-T06 / B1: declaration only. Action mapping and central permission
    // registration remain unavailable until the Cari-owned authority tuple lands.
    public const string ReversePropose = "finance.manual-receipt.reverse.propose";

    public const string ReverseApprove = "finance.manual-receipt.reverse.approve";

    // R47 / PLAN-48H-20260923-01: owner declarations only. They are deliberately not mapped from
    // FinanceAction until the central permission catalog and authority tuple land together. A name
    // here grants nothing and cannot make an endpoint available.
    public const string ManualBankPaymentDraftSave = "finance.manual-bank-payment.draft.save";
    public const string ManualBankPaymentDraftRead = "finance.manual-bank-payment.draft.read";
    public const string ManualBankPaymentApprove = "finance.manual-bank-payment.approve";
    public const string ManualBankPaymentRecord = "finance.manual-bank-payment.record";

    // F-03 technical declaration only. Central registration and every real grant remain separate.
    public const string AllocationWrite = "finance.allocation.write";

    // DEC-FIN-016 first release (customer cheque / note portfolio). Owner declarations: the central registry must know
    // them before the port measures them (until then every portfolio call is Unavailable, 503), and a human grants them.
    // Collect is the only one with a money effect (one recorded receipt source, one money event); it also needs the
    // caller's accounting.periods.read (the occurredOn period rule) and finance.bank-account.read (the bank read).
    public const string CustomerInstrumentRead = "finance.customer-instrument.read";
    public const string CustomerInstrumentAmountRead = "finance.customer-instrument.amount.read";
    public const string CustomerInstrumentReceive = "finance.customer-instrument.receive";
    public const string CustomerInstrumentStatusChange = "finance.customer-instrument.status.change";
    public const string CustomerInstrumentCollect = "finance.customer-instrument.collect";

    // First-release party ledger (cari hesap ekstresi): a READ only. Its own names because it shows facts no other read
    // shows together (admitted open items have no read of their own) — owner declarations: until the central registry
    // knows them the port answers Unavailable (503), and a human grants them. Every amount, total and balance of the
    // ledger needs the amount read as well.
    public const string PartyLedgerRead = "finance.party-ledger.read";
    public const string PartyLedgerAmountRead = "finance.party-ledger.amount.read";
}
