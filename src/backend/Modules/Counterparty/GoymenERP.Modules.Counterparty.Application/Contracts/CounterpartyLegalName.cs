using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// ALS-D95 permission name, the Counterparty owner's spelling. The central catalogue (ErpPermissions) registers it;
/// registering grants nothing — no role, seed or user receives it (human gate; SINAN-CEVAP 19.31 puts the grant at the
/// end of the train). Company-wide only: the party is reached only through the server-resolved company's account for it,
/// and the name is measured in that company. Not Critical. It is NOT the basic Cari edit
/// (<c>counterparty.parties.update.basic</c>): 19.22 chose a separate permission, so existing basic-edit grants are not
/// silently widened to the official legal name.
/// </summary>
public static class CounterpartyLegalNamePermissionsV1
{
    /// <summary>Change the Cari card's official legal name (resmi/hukuki unvan) in one step, without approval.</summary>
    public const string Update = "counterparty.parties.legal-name.update";
}

/// <summary>
/// ALS-D95 (confirmed by SINAN-CEVAP 19.22): the official legal name (resmi/hukuki unvan) of a Cari card is changed in
/// one step, with no approval step, by the holder of the separate <c>counterparty.parties.legal-name.update</c>
/// permission (plus <c>counterparty.parties.read</c>). Fenced on the PARTY version, one Idempotency-Key (stored terminal
/// answers replay), and one audit event per real change carrying the old and the new legal name. The party is shared by
/// every Göymen company holding an account for it (DEC-CAR-001), so the change is seen by all of them; the existence
/// fence is the active company's account for the party. VKN/TCKN, MERSİS and IBAN stay in the CARI-DEC-004
/// maker-checker flow; the trade name stays on its own basic-edit command (ALS-D97).
/// </summary>
public interface ICounterpartyLegalNameService
{
    /// <summary>The card's legal name and the party version; null when the active company holds no account for the party.</summary>
    Task<CounterpartyLegalNameView?> GetLegalNameAsync(Guid partyId, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> UpdateLegalNameAsync(
        Guid partyId, UpdateCounterpartyLegalName command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>
/// <c>expectedVersion</c> is the PARTY version (decimal text) from the read the form opened on. <c>legalName</c> is
/// required and may not be blank (trimmed, at most 250 characters). A save of the stored value is a no-op (204, no new
/// version, no audit event).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCounterpartyLegalName(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string LegalName);

/// <summary>What the reader may do here, measured by the server for this actor and company at read time.</summary>
public sealed record CounterpartyLegalNameViewer(bool CanUpdateLegalName);

public sealed record CounterpartyLegalNameView(
    Guid PartyId,
    Guid CompanyId,
    string Code,
    string Version,
    string LegalName,
    CounterpartyLegalNameViewer Viewer);
