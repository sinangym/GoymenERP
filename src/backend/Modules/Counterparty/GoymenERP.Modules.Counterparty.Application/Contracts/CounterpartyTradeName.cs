using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// F-027 / C-02 Cari card name edit, the direct part. The trade name (ticari ad) is a basic card field: it is changed with
/// the company-wide <c>counterparty.parties.update.basic</c> permission (SAT-D192: "cari kartı açma/değiştirme") plus
/// <c>counterparty.parties.read</c>, fenced on the PARTY version, one Idempotency-Key, and one audit event per real change.
/// The official legal name (resmi unvan) is shown read-only here and never travels through this command: it has its own
/// one-step command under a separate permission (ALS-D95, <see cref="ICounterpartyLegalNameService"/>).
/// </summary>
public interface ICounterpartyTradeNameService
{
    /// <summary>The card's names and the party version; null when the active company holds no account for the party.</summary>
    Task<CounterpartyTradeNameView?> GetTradeNameAsync(Guid partyId, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> UpdateTradeNameAsync(
        Guid partyId, UpdateCounterpartyTradeName command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>
/// <c>expectedVersion</c> is the PARTY version (decimal text) from the read the form opened on. <c>tradeName</c> is
/// required as a member; null or blank clears the trade name. A save of the stored value is a no-op (204, no new
/// version, no audit event).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCounterpartyTradeName(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string? TradeName);

/// <summary>What the reader may do here, measured by the server for this actor and company at read time.</summary>
public sealed record CounterpartyTradeNameViewer(bool CanUpdateTradeName);

public sealed record CounterpartyTradeNameView(
    Guid PartyId,
    Guid CompanyId,
    string Code,
    string Version,
    string LegalName,
    string? TradeName,
    CounterpartyTradeNameViewer Viewer);
