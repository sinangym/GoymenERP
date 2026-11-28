namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>KIM-D59 selected-pair content binding. No additional request, approval, or permission is created.</summary>
public interface ICounterpartyPartyMergePurposeV1
{
    Task<CounterpartyPartyMergePurposePreviewV1> PreviewPurposeAsync(Guid survivorPartyId,
        PreviewCounterpartyPartyMergeV1 command, Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default);
    Task<CounterpartyPartyMergePurposeResultV1> ApprovePurposeAsync(Guid survivorPartyId,
        ApproveCounterpartyPartyMerge command, string seenImpactDigest, CounterpartyCommandMetadata metadata,
        Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default);
}

public sealed record CounterpartyPartyMergePurposePreviewV1(CounterpartyPartyMergePreviewResultV1 NativePreview, string? SeenImpactDigest);
public enum CounterpartyPartyMergePurposeStatusV1 { Executed, Changed, Unavailable }
public sealed record CounterpartyPartyMergePurposeResultV1(CounterpartyPartyMergePurposeStatusV1 Status, CounterpartyCommandExecution? Execution);
