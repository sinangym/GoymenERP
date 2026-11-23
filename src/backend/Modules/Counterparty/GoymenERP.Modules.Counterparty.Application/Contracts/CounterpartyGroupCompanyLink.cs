using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// YOL-D31 (WP14): the party card's link to the Göymen group company the party is. Read under <c>counterparty.parties.read</c>;
/// changed under the company-wide <c>counterparty.parties.group-company-link.update</c> (registered, not granted) plus the read,
/// fenced on the PARTY version, one Idempotency-Key, one audit event per real change. The target must be a known group company
/// (Organization's company lifecycle read); a tax-number match never links anything.
/// </summary>
public interface ICounterpartyGroupCompanyLinkService
{
    /// <summary>The link and the party version; null when the active company holds no account for the party.</summary>
    Task<CounterpartyGroupCompanyLinkView?> GetGroupCompanyLinkAsync(Guid partyId, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> UpdateGroupCompanyLinkAsync(
        Guid partyId, UpdateCounterpartyGroupCompanyLink command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

/// <summary><c>groupCompanyId</c> is required as a member; null clears the link. Saving the stored value is a no-op (204).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCounterpartyGroupCompanyLink(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid? GroupCompanyId);

public sealed record CounterpartyGroupCompanyLinkViewer(bool CanUpdateGroupCompanyLink);

public sealed record CounterpartyGroupCompanyLinkView(
    Guid PartyId,
    Guid CompanyId,
    string Code,
    string Version,
    string LegalName,
    Guid? GroupCompanyId,
    CounterpartyGroupCompanyLinkViewer Viewer);

/// <summary>
/// YOL-D31 (WP14): the owner read port of every linked party (group-wide: a party and its link are shared by every company).
/// It carries no authority (the consumer measures its own), creates nothing, and answers active and merged parties alike — a
/// merged duplicate's link is history, its survivor carries its own.
/// </summary>
public interface ICounterpartyGroupCompanyLinkReadPortV1
{
    Task<IReadOnlyList<CounterpartyGroupCompanyLinkV1>> ListLinksAsync(CancellationToken cancellationToken = default);
}

public sealed record CounterpartyGroupCompanyLinkV1(Guid PartyId, Guid GroupCompanyId);
