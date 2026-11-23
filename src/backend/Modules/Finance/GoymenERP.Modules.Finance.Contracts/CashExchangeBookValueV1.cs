using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>FIN-S14, direct decision #1189/6960684942 (GOV-D16). Existing Accounting permissions; no grant or new role.</summary>
public static class CashExchangeBookValuePermissionsV1
{
    public const string Prepare = "accounting.normal-journal.draft.create";
    public const string Approve = "accounting.normal-journal.draft.approve";
}

public sealed record CashExchangeBookValueEvidenceV1(Guid DocumentId, int VersionNo, string Sha256);

/// <summary>Document-backed actual source carrying and target functional TL values. No TCMB/FIFO/average inference.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposeCashExchangeBookValueV1(Guid TransferId, string ExpectedTransferVersion,
    string SourceBookValueTl, CashExchangeBookValueEvidenceV1 Evidence, string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetFunctionalValueTl = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideCashExchangeBookValueV1(Guid ProposalId, string ExpectedVersion, bool Approve, string? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<CashExchangeBookValueStatusV1>))]
public enum CashExchangeBookValueStatusV1 { Pending = 1, Approved = 2, Rejected = 3 }

/// <summary>Version 1 is immutable preparation; version 2 is a separate immutable decision, with its original proof.</summary>
public sealed record CashExchangeBookValueViewV1(Guid Id, Guid CompanyId, Guid TransferId, string TransferVersion,
    string TransferSourceHash, string SourceBookValueTl, CashExchangeBookValueEvidenceV1 Evidence, string Reason,
    string PreparedBy, DateTimeOffset PreparedAtUtc, CashExchangeBookValueStatusV1 Status, string Version,
    string? DecidedBy, DateTimeOffset? DecidedAtUtc, string? DecisionReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetFunctionalValueTl = null);

public sealed record CashExchangeBookValueWorkspaceV1(Guid CompanyId, CashTransferView Transfer,
    IReadOnlyList<CashExchangeBookValueViewV1> Proposals);
public sealed record CashExchangeBookValueCandidatePageV1(Guid CompanyId, IReadOnlyList<CashTransferView> Items,
    Guid? NextCursor);
public sealed record CashExchangeBookValueCommandResultV1(CommandOutcome Outcome,
    CashExchangeBookValueViewV1? Resource, bool IsReplay, string OriginalCorrelationId, string? Code);

/// <summary>Pinned, actually readable Documents version/hash; unavailable is never a successful attestation.</summary>
public interface ICashExchangeBookValueEvidencePortV1
{
    Task<ReadOutcome> CheckAsync(FinanceContext context, CashExchangeBookValueEvidenceV1 evidence, CancellationToken cancellationToken);
}

public interface ICashExchangeBookValueServiceV1
{
    Task<ReadResult<CashExchangeBookValueCandidatePageV1>> ListAsync(FinanceContext context, Guid? afterId, CancellationToken cancellationToken);
    Task<ReadResult<CashExchangeBookValueWorkspaceV1>> GetAsync(FinanceContext context, Guid transferId, CancellationToken cancellationToken);
    Task<CashExchangeBookValueCommandResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        ProposeCashExchangeBookValueV1 request, CancellationToken cancellationToken);
    Task<CashExchangeBookValueCommandResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata,
        DecideCashExchangeBookValueV1 request, CancellationToken cancellationToken);
}
