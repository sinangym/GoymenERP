namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>Retained file interpretation and actual owner profile provenance; carries no authority or mutable master copy.</summary>
public sealed record PurchaseInvoiceImportProof(Guid ImportId, string ParserVersion, string OptionsJson, string MappingJson,
    Guid MoneyProfileId, long MoneyProfileVersion, Guid FunctionalMoneyProfileId, long FunctionalMoneyProfileVersion,
    string UnitProfilesJson, string? OfficialRatePurpose);
