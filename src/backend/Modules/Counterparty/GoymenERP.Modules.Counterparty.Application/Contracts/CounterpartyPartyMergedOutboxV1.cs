namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// F-027 / EK-18 #76: Counterparty's durable event for an approved party merge. One row is written for EVERY company that
/// holds an account for the survivor or the duplicate, in the same transaction as the merge record, so each company's
/// modules move their own Cari references from <see cref="CounterpartyPartyMergedV1.MergedPartyId"/> to
/// <see cref="CounterpartyPartyMergedV1.SurvivorPartyId"/> in their own tables (no module writes another module's
/// tables). A refusal or an idempotent replay writes nothing. There is no unmerge event (#77). Persistence of this event
/// is not publication; dispatch, retry, retention and consumers remain outside this command.
/// </summary>
public static class CounterpartyPartyMergedOutboxV1
{
    public const string EventType = "counterparty.party.merged.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = "counterparty.party.merge.approve.v1";
    public const string AggregateType = "Counterparty.PartyMerge";

    /// <summary>A merge is a single, never-repeated fact: each company's row carries sequence and aggregate version 1.</summary>
    public const long Sequence = 1;
}

/// <summary>
/// The merge as seen by one company: the receiving company, the merge record, the surviving and the merged party, the
/// merged party's version after the merge, and the company the approval was given in. The free-text reason stays in the
/// approving company's merge record and audit event; it is not copied to other companies.
/// </summary>
public sealed record CounterpartyPartyMergedV1(
    Guid CompanyId,
    Guid MergeId,
    Guid SurvivorPartyId,
    Guid MergedPartyId,
    long MergedPartyVersion,
    Guid ApprovalCompanyId);
