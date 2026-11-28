using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Contracts;

public sealed record CounterpartyMergePurposePreviewResponseV1(CounterpartyPartyMergeImpactPreviewV1 Impact, string SeenImpactDigest);
