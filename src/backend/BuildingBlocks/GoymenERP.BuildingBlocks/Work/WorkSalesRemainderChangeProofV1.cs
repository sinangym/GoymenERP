namespace GoymenERP.BuildingBlocks.Work;

// Selected native decision N is distinct from the current draft version observed during an original-key recovery.
// No quantity, price, free-text reason or full source record is copied into this private routing/binding carrier.
public sealed record WorkSalesRemainderChangeProofV1(Guid CompanyId, Guid DraftId, string DocumentNo, int ChangeNo, Guid LineId,
    int RequestRevision, string RequestedBy, DateTimeOffset RequestedAtUtc, string? DraftCreatedBy, bool ApplyPersonaRestriction,
    long SelectedExpectedVersion, long ObservedCurrentVersion, string RequestState, string SourceFactsDigest, string PreviewDigest);
