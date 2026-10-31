namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

/// <summary>
/// SAT-D102 (Sinan, 2026-10-28): the customer's e-belge status as checked when the Cari card is opened, held on the
/// company's account. <see cref="Unknown"/> is an explicit state — nothing is inferred from the VKN, the party kind or
/// any other field — and an invoice consumer must treat it as "not checked" (fail-closed). The GİB mükellef query that
/// would fill it automatically is a follow-up; today a user records what they checked.
/// </summary>
public enum EDocumentStatus
{
    /// <summary>Not checked yet, or reset; no check date or checker is kept.</summary>
    Unknown = 0,

    /// <summary>Registered e-Fatura taxpayer (e-Fatura mükellefi): the invoice goes as e-Fatura with a scenario.</summary>
    EInvoiceTaxpayer = 1,

    /// <summary>Not an e-Fatura taxpayer: the invoice goes as e-Arşiv.</summary>
    EArchive = 2,
}
