namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// Counterparty-owned material's commercial identity captured when a planned input is admitted.
/// All four values are immutable provenance: reopen and replay use these exact values rather than
/// asking Counterparty for a newer party, account or version. The two versions are opaque tokens;
/// Manufacturing never trims, parses or otherwise normalizes them.
/// </summary>
public sealed record PlanCommercialContextSnapshotV1
{
    public PlanCommercialContextSnapshotV1(
        Guid partyId,
        Guid companyPartyAccountId,
        string partyVersion,
        string accountVersion)
    {
        PartyId = RequireId(partyId, nameof(partyId));
        CompanyPartyAccountId = RequireId(companyPartyAccountId, nameof(companyPartyAccountId));
        PartyVersion = RequireOpaqueVersion(partyVersion, nameof(partyVersion));
        AccountVersion = RequireOpaqueVersion(accountVersion, nameof(accountVersion));
    }

    public Guid PartyId { get; }
    public Guid CompanyPartyAccountId { get; }
    public string PartyVersion { get; }
    public string AccountVersion { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Ticari bağlam kimliği boş olamaz.", name) : value;

    private static string RequireOpaqueVersion(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length == 0)
        {
            throw new ArgumentException("Ticari bağlam sürümü boş olamaz.", name);
        }

        return value;
    }
}
